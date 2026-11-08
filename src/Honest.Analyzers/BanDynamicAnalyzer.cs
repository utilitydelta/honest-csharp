using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UtilityDelta.Honest.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BanDynamicAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rules.NoDynamic);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.IdentifierName);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var id = (IdentifierNameSyntax)context.Node;
        if (id.Identifier.ValueText != "dynamic")
        {
            return;
        }

        var type = context.SemanticModel.GetTypeInfo(id, context.CancellationToken).Type;
        if (type is { TypeKind: TypeKind.Dynamic })
        {
            context.ReportDiagnostic(Diagnostic.Create(Rules.NoDynamic, id.GetLocation()));
        }
    }
}
