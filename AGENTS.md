# Agent runbook

How an agent sets up `honest-csharp` on a .NET project and runs the build/analyse/fix loop. Deterministic. One gate command, machine-readable diagnostics, an auto-fix for the common one.

## Set up a new (or existing) project

Three pieces, all from nuget.org. All verified to break the build on a violation.

1. `Directory.Build.props` at the repo root, so every project inherits the rules. `EnforceCodeStyleInBuild` is not optional: without it the `IDE*` struct rules only fire in the editor, never at `dotnet build`.

```xml
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="UtilityDelta.Honest.Analyzers" Version="1.0.0" PrivateAssets="all" />
    <PackageReference Include="UtilityDelta.Honest.Runtime" Version="1.0.0" />
    <PackageReference Include="Microsoft.CodeAnalysis.BannedApiAnalyzers" Version="3.3.4" PrivateAssets="all" />
    <AdditionalFiles Include="BannedSymbols.txt" />
  </ItemGroup>
</Project>
```

2. `BannedSymbols.txt` at the repo root (referenced above). Start with the reflection and service-locator bans; grow it as repos teach you:

```text
T:System.Reflection.Assembly;Reflection hides what runs; keep dispatch static
M:System.IServiceProvider.GetService(System.Type);No service locator; inject through the constructor
```

3. `.editorconfig` at the repo root, escalating the rules to build-breaking:

```ini
root = true
[*.cs]
dotnet_diagnostic.HON001.severity = error
dotnet_diagnostic.HON002.severity = error
dotnet_diagnostic.HON003.severity = error
dotnet_diagnostic.HON004.severity = error
dotnet_diagnostic.CS8509.severity = error
dotnet_diagnostic.IDE0250.severity = error
dotnet_diagnostic.IDE0251.severity = error
dotnet_diagnostic.CA1815.severity = error
dotnet_diagnostic.IDE0044.severity = error
dotnet_diagnostic.RS0030.severity = error
# Disable the platform rules that contradict the bans (they fire under AnalysisMode=All):
# CA1051 wants properties over public fields, CA1034 forbids the nested sum-type records,
# CA1062's fix is a throw.
dotnet_diagnostic.CA1051.severity = none
dotnet_diagnostic.CA1034.severity = none
dotnet_diagnostic.CA1062.severity = none
```

The package ships the rules at `warning` so it is safe to add to a dirty repo. The `.editorconfig` is what makes them bite. Set the new rules to `warning` first if you want to migrate a legacy repo gradually.

## The build / analyse / fix loop

```bash
dotnet build
```

Read the diagnostics. Each is `ID: imperative message` at `file(line,col)`. Act by ID:

| ID | Fix |
|----|-----|
| HON001 | Run `dotnet format analyzers` to auto-convert auto-properties to `public readonly` fields. A computed property (`=> expr`) is not auto-fixable: turn it into a method with parens. |
| HON002 | Replace `throw` with a `Result<T,E>` return for an expected failure, or `Panic.Now(...)` / `Panic.Value<T>(...)` for an unrecoverable invariant. At a framework boundary, `catch` the platform exception and convert it to a `Result` (see `samples/Demo/Patterns.cs` `Boundary`). The ban includes bare `throw;` rethrow: convert or `Panic`, never re-propagate. |
| HON003 | Change `implicit` to `explicit` on the conversion operator, or delete the operator and use a named method. |
| HON004 | Replace `dynamic` with a concrete static type. |
| RS0030 | A banned API (reflection, service-locator). Inject the dependency or call a static equivalent. |
| CS8509 | A `switch` expression is missing a case. Add it. Only add `_ => Panic.Value<T>(...)` on a sealed-record hierarchy that the compiler cannot prove closed. |

Then:

```bash
dotnet build   # repeat until 0 errors
dotnet test    # if the repo has tests
```

## When it fucks up

- **The fix re-introduces another ban.** Converting a throwing method to a `Result` may expose a caller that also threw. Walk the call chain up to the boundary; convert each, do not suppress.
- **A framework forces a property** (an interface or attribute from a library demands one). This is the "argued otherwise" case. Suppress that single line with a written reason, never blanket-disable:

  ```csharp
  #pragma warning disable HON001 // EF Core requires a settable property for the key
  public int Id { get; set; }
  #pragma warning restore HON001
  ```

- **Do not** make the build pass by lowering severity in `.editorconfig`, deleting the analyzer reference, or wrapping the file in `#pragma` to silence everything. Those defeat the gate. A suppression is one line, with a reason, or it is wrong.
- **The analyzer itself looks wrong** (false positive, bad fix). Reproduce it as a failing test in `tests/Honest.Analyzers.Tests`, fix the rule, re-run `./check.sh`. To try the fix in a consumer repo before release, `./pack.sh` builds both packages into `~/work/.honest-feed`; add it with `dotnet nuget add source`. The analyzer is code under test, not a fixed oracle.

## Verify the setup actually works

After wiring a repo, prove the gate is live before trusting it:

```bash
# add a property to any file, build, expect HON001, then revert
```

If the build does not fail, the `.editorconfig` severities or the package reference did not take. Check `dotnet build -v n` for whether the analyzer loaded.
