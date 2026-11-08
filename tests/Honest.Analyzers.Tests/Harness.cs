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

    // Compiler errors only, no analyzers: proves a code fix left the source compiling.
    public static string[] CompileErrors(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        return CSharpCompilation.Create(
                "TestAsm",
                new[] { tree },
                Net80.References.All,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable))
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
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

    // Fix-all exactly as dotnet format's SolutionCodeFixApplier runs it: register fixes for
    // the first diagnostic only, take that action's equivalence key (null when the first
    // diagnostic is one the fixer declines), then fix-all at solution scope.
    public static async Task<string> ApplyFixAll(
        DiagnosticAnalyzer analyzer, CodeFixProvider fixer, string source)
    {
        var (document, first) = await Setup(analyzer, fixer, source);
        var actions = new List<CodeAction>();
        await fixer.RegisterCodeFixesAsync(new CodeFixContext(
            document, first, (a, _) => actions.Add(a), CancellationToken.None));

        var compilation = (await document.Project.GetCompilationAsync())!;
        var all = (await compilation.WithAnalyzers(ImmutableArray.Create(analyzer)).GetAnalyzerDiagnosticsAsync())
            .Where(d => fixer.FixableDiagnosticIds.Contains(d.Id))
            .ToImmutableArray();

        var context = new FixAllContext(
            document, fixer, FixAllScope.Solution, actions.FirstOrDefault()?.EquivalenceKey!,
            fixer.FixableDiagnosticIds, new FixedDiagnostics(all), CancellationToken.None);
        var fix = await fixer.GetFixAllProvider()!.GetFixAsync(context);
        if (fix is null)
        {
            return source; // what dotnet format does: log and change nothing
        }

        var operations = await fix.GetOperationsAsync(CancellationToken.None);
        var changed = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        return (await changed.GetDocument(document.Id)!.GetTextAsync()).ToString();
    }

    private sealed class FixedDiagnostics : FixAllContext.DiagnosticProvider
    {
        private readonly ImmutableArray<Diagnostic> _all;

        public FixedDiagnostics(ImmutableArray<Diagnostic> all) => _all = all;

        public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken ct) =>
            Task.FromResult<IEnumerable<Diagnostic>>(_all);

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken ct) =>
            Task.FromResult<IEnumerable<Diagnostic>>(_all);

        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken ct) =>
            Task.FromResult<IEnumerable<Diagnostic>>(_all);
    }
}
