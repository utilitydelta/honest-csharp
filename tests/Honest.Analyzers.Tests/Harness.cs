using System.Collections.Immutable;
using Basic.Reference.Assemblies;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UtilityDelta.Honest.Analyzers.Tests;

internal static class Harness
{
    public static Diagnostic[] Analyze(DiagnosticAnalyzer analyzer, string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            "TestAsm",
            new[] { tree },
            Net80.References.All,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        return compilation
            .WithAnalyzers(ImmutableArray.Create(analyzer))
            .GetAnalyzerDiagnosticsAsync()
            .GetAwaiter().GetResult()
            .OrderBy(d => d.Location.SourceSpan.Start)
            .ToArray();
    }

    public static string TextOf(this Diagnostic diagnostic, string source) =>
        source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length);

    public static async Task<int> FixCount(
        DiagnosticAnalyzer analyzer, CodeFixProvider fixer, string source)
    {
        var (document, diagnostic) = await Setup(analyzer, fixer, source);
        var actions = new List<CodeAction>();
        await fixer.RegisterCodeFixesAsync(new CodeFixContext(
            document, diagnostic, (a, _) => actions.Add(a), CancellationToken.None));
        return actions.Count;
    }

    private static async Task<(Document, Diagnostic)> Setup(
        DiagnosticAnalyzer analyzer, CodeFixProvider fixer, string source)
    {
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution
            .AddProject(projectId, "Test", "Test", LanguageNames.CSharp)
            .AddMetadataReferences(projectId, Net80.References.All)
            .WithProjectCompilationOptions(projectId,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithProjectParseOptions(projectId,
                new CSharpParseOptions(LanguageVersion.Latest))
            .AddDocument(documentId, "Test.cs", source);
        var document = solution.GetDocument(documentId)!;
        var compilation = (await document.Project.GetCompilationAsync())!;
        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create(analyzer))
            .GetAnalyzerDiagnosticsAsync();
        return (document, diagnostics.First(d => fixer.FixableDiagnosticIds.Contains(d.Id)));
    }

    public static async Task<string> ApplyFix(
        DiagnosticAnalyzer analyzer, CodeFixProvider fixer, string source)
    {
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);

        using var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution
            .AddProject(projectId, "Test", "Test", LanguageNames.CSharp)
            .AddMetadataReferences(projectId, Net80.References.All)
            .WithProjectCompilationOptions(projectId,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithProjectParseOptions(projectId,
                new CSharpParseOptions(LanguageVersion.Latest))
            .AddDocument(documentId, "Test.cs", source);

        var document = solution.GetDocument(documentId)!;
        var compilation = (await document.Project.GetCompilationAsync())!;
        var diagnostic = compilation
            .WithAnalyzers(ImmutableArray.Create(analyzer))
            .GetAnalyzerDiagnosticsAsync()
            .GetAwaiter().GetResult()
            .First(d => fixer.FixableDiagnosticIds.Contains(d.Id));

        var actions = new List<CodeAction>();
        var context = new CodeFixContext(
            document, diagnostic, (a, _) => actions.Add(a), CancellationToken.None);
        await fixer.RegisterCodeFixesAsync(context);

        var operations = await actions.Single().GetOperationsAsync(CancellationToken.None);
        var changed = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var text = await changed.GetDocument(documentId)!.GetTextAsync();
        return text.ToString();
    }
}
