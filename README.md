# honest-csharp

C# that behaves like Go, Rust, and Zig: cost and control flow are visible at the call site, nothing hides behind clean syntax. The rules fail the build, not code review.

Two packages on nuget.org:

- [UtilityDelta.Honest.Analyzers](https://www.nuget.org/packages/UtilityDelta.Honest.Analyzers): four Roslyn rules nobody else ships, plus a code fix.
- [UtilityDelta.Honest.Runtime](https://www.nuget.org/packages/UtilityDelta.Honest.Runtime): `Result<T,E>` and `Panic`, the replacements for `throw`.

This README is the whole consumer guide; nothing below depends on files outside the package. Source and the full sample: https://github.com/utilitydelta/honest-csharp

## The four rules

| ID | Bans | Why |
|----|------|-----|
| HON001 | Properties, indexers, positional record parameters | A field cannot hide cost or control flow behind field-access syntax. Public fields only; a computation is a method. Members that implement an interface or override a member from another assembly (`IEnumerator<T>.Current`, `Exception.Message`) are exempt: that contract is not yours to change. |
| HON002 | `throw`, including bare `throw;` | Errors are values. Expected failures return `Result<T,E>`; unrecoverable ones call `Panic`, which aborts via `Environment.FailFast` and never throws. |
| HON003 | `implicit operator` | A hidden call on what looks like an assignment. `explicit` is allowed; the cast is visible. |
| HON004 | `dynamic` | Moves type errors to runtime for no gain. |

The rules ship at `warning` so the package is safe to add to a dirty repo. The `.editorconfig` below is what makes them bite.

## Set up a repo

Three files at the repo root. Every project under it inherits them.

**`Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <!-- Not optional: without it the IDE* rules fire in the editor but never at dotnet build. -->
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="UtilityDelta.Honest.Analyzers" Version="1.0.1" PrivateAssets="all" />
    <PackageReference Include="UtilityDelta.Honest.Runtime" Version="1.0.1" />
    <PackageReference Include="Microsoft.CodeAnalysis.BannedApiAnalyzers" Version="3.3.4" PrivateAssets="all" />
    <!-- $(MSBuildThisFileDirectory): a bare relative path resolves per project and breaks nested layouts. -->
    <AdditionalFiles Include="$(MSBuildThisFileDirectory)BannedSymbols.txt" />
  </ItemGroup>
</Project>
```

**`BannedSymbols.txt`**: start with reflection and service-locator, grow it as the repo teaches you.

```text
T:System.Reflection.Assembly;Reflection hides what runs; keep dispatch static
M:System.IServiceProvider.GetService(System.Type);No service locator; inject through the constructor
```

**`.editorconfig`**: the complete set.

```ini
root = true

[*.cs]
# The four bans, build-breaking.
dotnet_diagnostic.HON001.severity = error
dotnet_diagnostic.HON002.severity = error
dotnet_diagnostic.HON003.severity = error
dotnet_diagnostic.HON004.severity = error

# A switch expression missing a case is an error.
dotnet_diagnostic.CS8509.severity = error

# Struct hygiene: readonly structs, readonly members, value-type equality.
dotnet_diagnostic.IDE0250.severity = error
dotnet_diagnostic.IDE0251.severity = error
dotnet_diagnostic.CA1815.severity = error

# Readonly-by-default fields.
dotnet_diagnostic.IDE0044.severity = error

# Banned APIs from BannedSymbols.txt.
dotnet_diagnostic.RS0030.severity = error

# Platform rules that contradict the bans. They only fire under a raised AnalysisMode,
# but switch them off so they never can: CA1051 wants properties (vs HON001), CA1034
# forbids the nested records closed error types use, CA1062's fix is a throw (vs HON002).
dotnet_diagnostic.CA1051.severity = none
dotnet_diagnostic.CA1034.severity = none
dotnet_diagnostic.CA1062.severity = none
```

Migrating a legacy repo? Set the HON rules to `warning` first and ratchet up.

**Prove the gate is live** before trusting it: add `public int X { get; set; }` to any class, build, expect `error HON001`, revert. No error means the package or the `.editorconfig` did not take; `dotnet build -v n` shows whether the analyzer loaded.

Optional and not wired by these packages, but aligned with the style: `Meziantou.Analyzer` (MA0053, seal classes), `Microsoft.VisualStudio.Threading.Analyzers` (sync-over-async, `async void`).

## Fix by diagnostic ID

| ID | Fix |
|----|-----|
| HON001 | `dotnet format analyzers` converts auto-properties to fields: `{ get; }` and `{ get; init; }` become `public readonly`, `{ get; set; }` a mutable field. No fix is offered for computed properties (make them a method with parens) or mixed access like `{ get; private set; }` (decide by hand: a field has one access level). Positional records become a body with `public readonly` fields and a constructor. |
| HON002 | `Result<T,E>` for an expected failure, `Panic` for a broken invariant. At a framework boundary, `catch` what the platform throws and convert it to a `Result`. Rethrow is banned too: convert or `Panic`. |
| HON003 | `implicit` to `explicit`, or a named method. |
| HON004 | A concrete static type. |
| RS0030 | A banned API. Inject the dependency or call a static equivalent. |
| CS8509 | Add the missing case. Only a closed sealed-record hierarchy gets a `_ => Panic.Value<T>(...)` arm, because the compiler cannot prove it closed. |

Suppress only where a library forces your hand, one line, with the reason:

```csharp
#pragma warning disable HON001 // EF Core requires a settable property for the key
public int Id { get; set; }
#pragma warning restore HON001
```

Lowering a severity, removing the package or wrapping a whole file in `#pragma` defeats the gate.

## Runtime API

```csharp
namespace UtilityDelta.Honest;

public readonly struct Result<T, E>
{
    public readonly bool IsOk;
    public static Result<T, E> Ok(T value);
    public static Result<T, E> Err(E error);
    public R Match<R>(Func<T, R> ok, Func<E, R> err);   // forces both arms
    public void Switch(Action<T> ok, Action<E> err);
}

public static class Panic
{
    [DoesNotReturn] public static void Now(string why);       // Environment.FailFast
    [DoesNotReturn] public static T Value<T>(string why);     // for expression positions
}
```

`default(Result<T,E>)` is an `Err` holding `default(E)`. Always construct through `Ok` or `Err`.

Multi-shape errors are a closed sealed-record hierarchy (private base constructor, body fields), matched with a switch expression:

```csharp
public abstract record ParseError
{
    private ParseError() { }
    public sealed record BadField : ParseError
    {
        public readonly int Line;
        public readonly string Field;
        public BadField(int line, string field) { Line = line; Field = field; }
    }
}
```

### Boundaries

Banning `throw` does nothing about exceptions the BCL throws. Wrap each throwing call at the edge and catch every exception its docs list, not only the obvious one:

```csharp
public static Result<string, IoError> ReadText(string path)
{
    try { return Result<string, IoError>.Ok(File.ReadAllText(path)); }
    catch (IOException e)                 { return Result<string, IoError>.Err(new IoError.ReadFailed(path, e.Message)); }
    catch (UnauthorizedAccessException e) { return Result<string, IoError>.Err(new IoError.Denied(path, e.Message)); }
    catch (ArgumentException e)           { return Result<string, IoError>.Err(new IoError.ReadFailed(path, e.Message)); } // empty or invalid path
    catch (NotSupportedException e)       { return Result<string, IoError>.Err(new IoError.ReadFailed(path, e.Message)); }
}
```

## Testing

- **`Panic` kills the test host.** `FailFast` is not an exception; the whole run aborts with no summary. Keep invariant checks as pure functions that return `bool` or a `Result`, test those, and let the `Panic` call site stay a one-liner. To prove a path really panics, run it in a child process and assert on the exit code.
- **Unwrapping in tests.** Write one helper per test project:

  ```csharp
  static T Ok<T, E>(Result<T, E> r) => r.Match(v => v, e => Fail<T>($"expected Ok, got {e}"));
  static E Err<T, E>(Result<T, E> r) => r.Match(v => Fail<E>($"expected Err, got {v}"), e => e);
  static X Fail<X>(string why) { Assert.Fail(why); return default!; }
  ```

- **xUnit `MemberData`** works with a static method or field; the usual static property trips HON001.
- **Record equality on collection fields** compares the list by reference. Assert on the contents, not on record equality.

## Later

Native `union` (C# 15 / .NET 11) replaces `Result<T,E>` and the `_ => Panic(...)` arm with real closed-set exhaustiveness. Migrate when it reaches GA.

## Working on this repo

- `./check.sh` is the gate: builds the solution (the Demo breaks on any violation) and runs the analyzer tests. CI runs it on every push and PR.
- `AGENTS.md`: the maintainer loop. `PRIOR-ART.md`: where these rules align with and deliberately invert mainstream .NET.
- Release: push a `vX.Y.Z` tag. CI packs both packages at that version and pushes them to nuget.org via trusted publishing.
