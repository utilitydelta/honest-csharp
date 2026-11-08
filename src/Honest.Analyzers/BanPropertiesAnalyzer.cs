using System.Collections.Immutable;
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
        context.ReportDiagnostic(
            Diagnostic.Create(Rules.NoProperties, prop.Identifier.GetLocation(), prop.Identifier.Text));
    }

    // An indexer hides code behind `obj[i]`, which reads like array access. Same lie as a property.
    private static void AnalyzeIndexer(SyntaxNodeAnalysisContext context)
    {
        var indexer = (IndexerDeclarationSyntax)context.Node;
        context.ReportDiagnostic(
            Diagnostic.Create(Rules.NoProperties, indexer.ThisKeyword.GetLocation(), "this[] indexer"));
    }

    private static void AnalyzeRecord(SyntaxNodeAnalysisContext context)
    {
        var record = (RecordDeclarationSyntax)context.Node;
        if (record.ParameterList is { Parameters.Count: > 0 })
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rules.NoProperties,
                record.ParameterList.GetLocation(),
                record.Identifier.Text + " positional parameters (they generate properties)"));
        }
    }
}
