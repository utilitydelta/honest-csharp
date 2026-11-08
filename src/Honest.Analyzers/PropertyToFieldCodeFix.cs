using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;

namespace UtilityDelta.Honest.Analyzers;

// Converts what converts without changing behaviour, and offers nothing otherwise:
// a fix that compiles but silently changes access or breaks a caller is worse than none.
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PropertyToFieldCodeFix)), Shared]
public sealed class PropertyToFieldCodeFix : CodeFixProvider
{
    private const string EquivalenceKey = "HON001-to-fields";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(Rules.NoProperties.Id);

    // Not the stock BatchFixer. dotnet format seeds fix-all from whichever diagnostic it sees
    // first; when that one is declined (a computed property, say) the equivalence key is null
    // and BatchFixer matches nothing, so the whole run is a silent no-op. HON001 has one kind
    // of fix, so this provider ignores the key and converts every diagnostic that plans.
    public override FixAllProvider GetFixAllProvider() =>
        FixAllProvider.Create(async (context, document, diagnostics) =>
        {
            var plans = new List<Plan>();
            foreach (var diagnostic in diagnostics)
            {
                var plan = await PlanFix(document, diagnostic.Location.SourceSpan, context.CancellationToken).ConfigureAwait(false);
                if (plan is not null)
                {
                    plans.Add(plan);
                }
            }

            return plans.Count == 0 ? document : await Apply(document, plans, context.CancellationToken).ConfigureAwait(false);
        });

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var plan = await PlanFix(context.Document, context.Span, context.CancellationToken).ConfigureAwait(false);
        if (plan is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                plan.Title,
                ct => Apply(context.Document, new[] { plan }, ct),
                equivalenceKey: EquivalenceKey),
            context.Diagnostics);
    }

    // What to replace and how. Decided against the original document; Rewrite receives the
    // node after any nested plans have been applied, so a positional record inside another
    // still converts in one pass.
    private sealed class Plan
    {
        public readonly SyntaxNode Target;
        public readonly string Title;
        public readonly Func<SyntaxNode, SyntaxNode> Rewrite;

        public Plan(SyntaxNode target, string title, Func<SyntaxNode, SyntaxNode> rewrite)
        {
            Target = target;
            Title = title;
            Rewrite = rewrite;
        }
    }

    private static async Task<Document> Apply(Document document, IReadOnlyList<Plan> plans, CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        var byTarget = plans
            .GroupBy(p => p.Target)
            .ToDictionary(g => g.Key, g => g.First());
        var newRoot = root!.ReplaceNodes(byTarget.Keys, (original, rewritten) => byTarget[original].Rewrite(rewritten));
        return await Formatter.FormatAsync(document.WithSyntaxRoot(newRoot), Formatter.Annotation, cancellationToken: ct).ConfigureAwait(false);
    }

    private static async Task<Plan?> PlanFix(Document document, TextSpan span, CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        var node = root?.FindNode(span);
        if (node is null)
        {
            return null;
        }

        if (node.FirstAncestorOrSelf<ParameterListSyntax>()?.Parent is RecordDeclarationSyntax record)
        {
            return await PlanRecord(document, record, ct).ConfigureAwait(false);
        }

        var prop = node.FirstAncestorOrSelf<PropertyDeclarationSyntax>();
        return prop is null ? null : await PlanProperty(document, prop, ct).ConfigureAwait(false);
    }

    private static async Task<Plan?> PlanProperty(Document document, PropertyDeclarationSyntax prop, CancellationToken ct)
    {
        // Only auto-properties convert cleanly to a field. Computed properties stay flagged.
        if (prop.AccessorList is null || prop.Parent is InterfaceDeclarationSyntax)
        {
            return null;
        }

        if (prop.AccessorList.Accessors.Any(a => a.Body is not null || a.ExpressionBody is not null))
        {
            return null;
        }

        // `{ get; private set; }` has two access levels; a field has one. Converting would
        // either widen the setter to public or narrow the getter, so leave it to a human.
        if (prop.AccessorList.Accessors.Any(a => a.Modifiers.Count > 0))
        {
            return null;
        }

        // A required member must stay settable; a readonly field cannot be.
        if (prop.Modifiers.Any(SyntaxKind.RequiredKeyword))
        {
            return null;
        }

        // `init` is set-once, so an init-only property becomes a readonly field. A readonly
        // field cannot be set from `new C { X = 1 }` or `c with { X = 1 }`, so if any caller
        // does that, converting would break it.
        var becomesReadonly = !prop.AccessorList.Accessors.Any(a => a.IsKind(SyntaxKind.SetAccessorDeclaration));
        if (becomesReadonly)
        {
            var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
            var symbol = model?.GetDeclaredSymbol(prop, ct);
            if (symbol is null || await HasInitializerWrites(document.Project.Solution, symbol, ct).ConfigureAwait(false))
            {
                return null;
            }
        }

        return new Plan(prop, "Convert to public field", n => ConvertProperty((PropertyDeclarationSyntax)n, becomesReadonly));
    }

    private static SyntaxNode ConvertProperty(PropertyDeclarationSyntax prop, bool becomesReadonly)
    {
        var modifiers = prop.Modifiers;
        if (becomesReadonly && !modifiers.Any(SyntaxKind.ReadOnlyKeyword))
        {
            modifiers = modifiers.Add(SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword));
        }

        var declarator = SyntaxFactory.VariableDeclarator(prop.Identifier.WithoutTrivia());
        if (prop.Initializer is not null)
        {
            declarator = declarator.WithInitializer(prop.Initializer);
        }

        // Carry attributes across; dropping them silently changes serialization/ORM/validation.
        var attributes = SyntaxFactory.List(prop.AttributeLists.Select(a => a.WithoutTrivia()));

        return SyntaxFactory.FieldDeclaration(
                SyntaxFactory.VariableDeclaration(prop.Type.WithoutTrivia())
                    .WithVariables(SyntaxFactory.SingletonSeparatedList(declarator)))
            .WithAttributeLists(attributes)
            .WithModifiers(modifiers)
            .NormalizeWhitespace(elasticTrivia: true)
            .WithLeadingTrivia(prop.GetLeadingTrivia())
            .WithTrailingTrivia(prop.GetTrailingTrivia());
    }

    // `record Pos(int Line, string Id);` becomes a body with one field per parameter and a
    // constructor with the same signature, so `new Pos(1, "a")` and named arguments keep
    // compiling. Deconstruct is kept only if something deconstructs the record.
    private static async Task<Plan?> PlanRecord(Document document, RecordDeclarationSyntax record, CancellationToken ct)
    {
        var parameters = record.ParameterList!.Parameters;

        // Arguments passed to a base record reference the parameters; attributes on them may
        // target the property, the field or the parameter. Both need a human.
        if (record.BaseList?.Types.Any(t => t is PrimaryConstructorBaseTypeSyntax) ?? false)
        {
            return null;
        }

        if (parameters.Any(p => p.AttributeLists.Count > 0 || p.Modifiers.Count > 0 || p.Type is null))
        {
            return null;
        }

        var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        if (model?.GetDeclaredSymbol(record, ct) is not INamedTypeSymbol type)
        {
            return null;
        }

        // A member that already uses a parameter name, or a body that captures a parameter
        // (`public int Twice = X * 2;`), would change meaning once the parameter is a field.
        var names = new HashSet<string>(parameters.Select(p => p.Identifier.ValueText));
        if (record.Members.Any(m => DeclaredNames(m).Any(names.Contains)))
        {
            return null;
        }

        var parameterSymbols = parameters
            .Select(p => model.GetDeclaredSymbol(p, ct))
            .ToImmutableHashSet<ISymbol?>(SymbolEqualityComparer.Default);
        var capturesParameter = record.Members
            .SelectMany(m => m.DescendantNodes().OfType<IdentifierNameSyntax>())
            .Any(id => parameterSymbols.Contains(model.GetSymbolInfo(id, ct).Symbol));
        if (capturesParameter)
        {
            return null;
        }

        // A plain `record struct` generates `{ get; set; }`, so its fields stay mutable. A
        // record class or a readonly record struct generates `{ get; init; }`: readonly fields,
        // which break any `with { X = ... }` or object initializer that sets them.
        var readonlyFields = record.ClassOrStructKeyword.IsKind(SyntaxKind.ClassKeyword)
            || record.ClassOrStructKeyword.IsKind(SyntaxKind.None)
            || record.Modifiers.Any(SyntaxKind.ReadOnlyKeyword);
        var solution = document.Project.Solution;
        if (readonlyFields)
        {
            foreach (var property in type.GetMembers().OfType<IPropertySymbol>().Where(p => names.Contains(p.Name)))
            {
                if (await HasInitializerWrites(solution, property, ct).ConfigureAwait(false))
                {
                    return null;
                }
            }
        }

        var deconstruct = type.GetMembers("Deconstruct").FirstOrDefault(m => m.IsImplicitlyDeclared);
        var keepDeconstruct = deconstruct is not null
            && (await SymbolFinder.FindReferencesAsync(deconstruct, solution, ct).ConfigureAwait(false))
                .Any(r => r.Locations.Any());

        return new Plan(
            record,
            "Convert positional parameters to fields and a constructor",
            n => ConvertRecord((RecordDeclarationSyntax)n, readonlyFields, keepDeconstruct));
    }

    private static SyntaxNode ConvertRecord(RecordDeclarationSyntax record, bool readonlyFields, bool keepDeconstruct)
    {
        var parameters = record.ParameterList!.Parameters;
        var fieldModifier = readonlyFields ? "public readonly" : "public";

        var members = new List<MemberDeclarationSyntax>();
        foreach (var p in parameters)
        {
            members.Add(Member($"{fieldModifier} {p.Type} {p.Identifier.Text};"));
        }

        // Parameter names are kept as written so named-argument call sites still compile.
        var signature = string.Join(", ", parameters.Select(p => p.WithoutTrivia().ToFullString()));
        var assignments = string.Concat(parameters.Select(p => $"this.{p.Identifier.Text} = {p.Identifier.Text}; "));
        members.Add(Member($"public {record.Identifier.Text}({signature}) {{ {assignments}}}"));

        if (keepDeconstruct)
        {
            var outs = string.Join(", ", parameters.Select(p => $"out {p.Type} {p.Identifier.Text}"));
            var copies = string.Concat(parameters.Select(p => $"{p.Identifier.Text} = this.{p.Identifier.Text}; "));
            members.Add(Member($"public void Deconstruct({outs}) {{ {copies}}}"));
        }

        var identifier = record.TypeParameterList is null
            ? record.Identifier.WithTrailingTrivia(SyntaxFactory.ElasticSpace)
            : record.Identifier;
        return record
            .WithIdentifier(identifier)
            .WithParameterList(null)
            .WithOpenBraceToken(record.OpenBraceToken.IsKind(SyntaxKind.None)
                ? SyntaxFactory.Token(SyntaxKind.OpenBraceToken)
                : record.OpenBraceToken)
            .WithCloseBraceToken(record.CloseBraceToken.IsKind(SyntaxKind.None)
                ? SyntaxFactory.Token(SyntaxKind.CloseBraceToken)
                : record.CloseBraceToken)
            .WithSemicolonToken(default)
            .WithMembers(SyntaxFactory.List(members.Concat(record.Members)))
            .WithTrailingTrivia(record.GetTrailingTrivia())
            .WithAdditionalAnnotations(Formatter.Annotation);
    }

    // Elastic trivia lets the formatter lay the member out in the document's own style.
    private static MemberDeclarationSyntax Member(string text) =>
        SyntaxFactory.ParseMemberDeclaration(text)!
            .NormalizeWhitespace(elasticTrivia: true)
            .WithLeadingTrivia(SyntaxFactory.ElasticMarker)
            .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed)
            .WithAdditionalAnnotations(Formatter.Annotation);

    private static IEnumerable<string> DeclaredNames(MemberDeclarationSyntax member) => member switch
    {
        PropertyDeclarationSyntax p => new[] { p.Identifier.ValueText },
        MethodDeclarationSyntax m => new[] { m.Identifier.ValueText },
        FieldDeclarationSyntax f => f.Declaration.Variables.Select(v => v.Identifier.ValueText),
        EventDeclarationSyntax e => new[] { e.Identifier.ValueText },
        EventFieldDeclarationSyntax e => e.Declaration.Variables.Select(v => v.Identifier.ValueText),
        BaseTypeDeclarationSyntax t => new[] { t.Identifier.ValueText },
        _ => Enumerable.Empty<string>(),
    };

    // True if any caller sets this member from an object initializer or a `with` expression.
    private static async Task<bool> HasInitializerWrites(Solution solution, ISymbol symbol, CancellationToken ct)
    {
        var references = await SymbolFinder.FindReferencesAsync(symbol, solution, ct).ConfigureAwait(false);
        foreach (var location in references.SelectMany(r => r.Locations))
        {
            var root = await location.Document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            var node = root?.FindNode(location.Location.SourceSpan, getInnermostNodeForTie: true);
            if (node?.Parent is AssignmentExpressionSyntax assignment
                && assignment.Left == node
                && assignment.Parent is InitializerExpressionSyntax initializer
                && (initializer.IsKind(SyntaxKind.ObjectInitializerExpression) || initializer.IsKind(SyntaxKind.WithInitializerExpression)))
            {
                return true;
            }
        }

        return false;
    }
}
