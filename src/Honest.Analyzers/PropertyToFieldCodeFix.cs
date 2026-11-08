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

namespace UtilityDelta.Honest.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PropertyToFieldCodeFix)), Shared]
public sealed class PropertyToFieldCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(Rules.NoProperties.Id);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var prop = root?.FindNode(context.Span).FirstAncestorOrSelf<PropertyDeclarationSyntax>();

        // Only auto-properties convert cleanly to a field. Computed properties stay flagged.
        if (prop?.AccessorList is null)
        {
            return;
        }

        if (prop.AccessorList.Accessors.Any(a => a.Body is not null || a.ExpressionBody is not null))
        {
            return;
        }

        // `{ get; private set; }` has two access levels; a field has one. Converting would
        // either widen the setter to public or narrow the getter, so leave it to a human.
        if (prop.AccessorList.Accessors.Any(a => a.Modifiers.Count > 0))
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                "Convert to public field",
                ct => ConvertAsync(context.Document, prop, ct),
                equivalenceKey: "HON001-property-to-field"),
            context.Diagnostics);
    }

    private static async Task<Document> ConvertAsync(
        Document document, PropertyDeclarationSyntax prop, CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);

        // `init` is set-once, so an init-only property is immutable: it becomes a readonly
        // field. Only a real `set` keeps the field mutable.
        var hasSetter = prop.AccessorList!.Accessors.Any(a => a.IsKind(SyntaxKind.SetAccessorDeclaration));

        var modifiers = prop.Modifiers;
        if (!hasSetter && !modifiers.Any(SyntaxKind.ReadOnlyKeyword))
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

        var field = SyntaxFactory.FieldDeclaration(
                SyntaxFactory.VariableDeclaration(prop.Type.WithoutTrivia())
                    .WithVariables(SyntaxFactory.SingletonSeparatedList(declarator)))
            .WithAttributeLists(attributes)
            .WithModifiers(modifiers)
            .NormalizeWhitespace(elasticTrivia: true)
            .WithLeadingTrivia(prop.GetLeadingTrivia())
            .WithTrailingTrivia(prop.GetTrailingTrivia());

        var newRoot = root!.ReplaceNode(prop, field);
        return document.WithSyntaxRoot(newRoot);
    }
}
