using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UtilityDelta.Honest.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BanThrowAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rules.NoThrow);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(
            AnalyzeThrow, SyntaxKind.ThrowStatement, SyntaxKind.ThrowExpression);
        context.RegisterCompilationStartAction(start =>
        {
            var exception = start.Compilation.GetTypeByMetadataName("System.Exception");
            var dispatchInfo = start.Compilation.GetTypeByMetadataName(
                "System.Runtime.ExceptionServices.ExceptionDispatchInfo");
            start.RegisterOperationAction(
                op => AnalyzeInvocation(op, exception, dispatchInfo), OperationKind.Invocation);
        });
    }

    private static void AnalyzeThrow(SyntaxNodeAnalysisContext context) =>
        context.ReportDiagnostic(Diagnostic.Create(Rules.NoThrow, context.Node.GetLocation(), "'throw'", ConstructorHint(context.Node)));

    // A constructor cannot return a Result, so the generic advice is a dead end there.
    private static string ConstructorHint(SyntaxNode node) =>
        node.FirstAncestorOrSelf<ConstructorDeclarationSyntax>() is null
            ? ""
            : " (a constructor cannot return one: make it private and validate in a static factory that returns Result)";

    // A throw helper is a `throw` with the keyword moved into the BCL: ThrowIfNull,
    // ThrowIfNegative, ObjectDisposedException.ThrowIf, ExceptionDispatchInfo.Throw.
    private static void AnalyzeInvocation(
        OperationAnalysisContext context, INamedTypeSymbol? exception, INamedTypeSymbol? dispatchInfo)
    {
        var method = ((IInvocationOperation)context.Operation).TargetMethod;
        if (!method.Name.StartsWith("Throw", System.StringComparison.Ordinal))
        {
            return;
        }

        var owner = method.ContainingType;
        var isHelper = SymbolEqualityComparer.Default.Equals(owner, dispatchInfo)
            || (exception is not null && DerivesFrom(owner, exception));
        if (!isHelper)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rules.NoThrow,
            context.Operation.Syntax.GetLocation(),
            $"throw helper '{owner.Name}.{method.Name}'",
            ConstructorHint(context.Operation.Syntax)));
    }

    private static bool DerivesFrom(INamedTypeSymbol? type, INamedTypeSymbol baseType)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(t, baseType))
            {
                return true;
            }
        }

        return false;
    }
}
