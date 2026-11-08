using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UtilityDelta.Honest.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BanPropertiesAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rules.NoProperties);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeProperty, SyntaxKind.PropertyDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeIndexer, SyntaxKind.IndexerDeclaration);
        context.RegisterSyntaxNodeAction(
            AnalyzeRecord, SyntaxKind.RecordDeclaration, SyntaxKind.RecordStructDeclaration);
    }

    private static void AnalyzeProperty(SyntaxNodeAnalysisContext context)
    {
        var prop = (PropertyDeclarationSyntax)context.Node;
        if (IsForcedByExternalContract(context, prop))
        {
            return;
        }

        context.ReportDiagnostic(
            Diagnostic.Create(Rules.NoProperties, prop.Identifier.GetLocation(), Advice(context, prop)));
    }

    // The fix differs by shape, so the message names the one that applies.
    private static string Advice(SyntaxNodeAnalysisContext context, PropertyDeclarationSyntax prop)
    {
        var name = prop.Identifier.Text;
        if (prop.Parent is InterfaceDeclarationSyntax)
        {
            return $"Replace interface property '{name}' with a method; an interface cannot declare a field";
        }

        // External contracts are exempt before this point, so any interface left is our own.
        if (ImplementsInterfaceMember(context, prop))
        {
            return $"Replace property '{name}' with a method, together with the interface property it implements";
        }

        var accessors = prop.AccessorList?.Accessors;
        var computed = prop.ExpressionBody is not null
            || (accessors?.Any(a => a.Body is not null || a.ExpressionBody is not null) ?? false);
        if (computed)
        {
            return $"Replace computed property '{name}' with a method";
        }

        if (accessors?.Any(a => a.Modifiers.Count > 0) ?? false)
        {
            return $"Replace property '{name}' with a field; a field has one access level, so choose it by hand";
        }

        return $"Replace property '{name}' with a public field";
    }

    // An indexer hides code behind `obj[i]`, which reads like array access. Same lie as a property.
    private static void AnalyzeIndexer(SyntaxNodeAnalysisContext context)
    {
        var indexer = (IndexerDeclarationSyntax)context.Node;
        if (IsForcedByExternalContract(context, indexer))
        {
            return;
        }

        context.ReportDiagnostic(
            Diagnostic.Create(
                Rules.NoProperties,
                indexer.ThisKeyword.GetLocation(),
                "Replace the indexer with a named method such as At(i)"));
    }

    // A property that implements an interface or overrides a member declared in another
    // assembly (IEnumerator<T>.Current, IReadOnlyList<T>[i], Stream.Length) cannot be a
    // field: the contract is not ours to change. Contracts declared in our own code are.
    private static bool IsForcedByExternalContract(SyntaxNodeAnalysisContext context, SyntaxNode node)
    {
        if (context.SemanticModel.GetDeclaredSymbol(node, context.CancellationToken) is not IPropertySymbol symbol)
        {
            return false;
        }

        var own = context.Compilation.Assembly;
        if (symbol.OverriddenProperty is { } overridden && !SymbolEqualityComparer.Default.Equals(overridden.ContainingAssembly, own))
        {
            return true;
        }

        if (symbol.ExplicitInterfaceImplementations.Any(i => !SymbolEqualityComparer.Default.Equals(i.ContainingAssembly, own)))
        {
            return true;
        }

        return symbol.ContainingType.AllInterfaces
            .Where(i => !SymbolEqualityComparer.Default.Equals(i.ContainingAssembly, own))
            .SelectMany(i => i.GetMembers().OfType<IPropertySymbol>())
            .Any(m => SymbolEqualityComparer.Default.Equals(symbol.ContainingType.FindImplementationForInterfaceMember(m), symbol));
    }

    private static bool ImplementsInterfaceMember(SyntaxNodeAnalysisContext context, PropertyDeclarationSyntax prop)
    {
        if (context.SemanticModel.GetDeclaredSymbol(prop, context.CancellationToken) is not IPropertySymbol symbol)
        {
            return false;
        }

        return symbol.ExplicitInterfaceImplementations.Length > 0
            || symbol.ContainingType.AllInterfaces
                .SelectMany(i => i.GetMembers().OfType<IPropertySymbol>())
                .Any(m => SymbolEqualityComparer.Default.Equals(symbol.ContainingType.FindImplementationForInterfaceMember(m), symbol));
    }

    private static void AnalyzeRecord(SyntaxNodeAnalysisContext context)
    {
        var record = (RecordDeclarationSyntax)context.Node;
        if (record.ParameterList is { Parameters.Count: > 0 })
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rules.NoProperties,
                record.ParameterList.GetLocation(),
                $"Replace the positional parameters of record '{record.Identifier.Text}' with fields and a constructor"));
        }
    }
}
