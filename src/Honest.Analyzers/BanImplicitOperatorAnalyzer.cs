using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UtilityDelta.Honest.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BanImplicitOperatorAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rules.NoImplicitOperator);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.ConversionOperatorDeclaration);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var op = (ConversionOperatorDeclarationSyntax)context.Node;
        if (op.ImplicitOrExplicitKeyword.IsKind(SyntaxKind.ImplicitKeyword))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(Rules.NoImplicitOperator, op.ImplicitOrExplicitKeyword.GetLocation()));
        }
    }
}
