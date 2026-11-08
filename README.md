# honest-csharp

C# that behaves like Go, Rust, and Zig: cost and control flow are visible at the call site, nothing hides behind clean syntax. The rules fail the build, not code review.

This is the enforcement for the C# rules in [build-method](https://github.com/utilitydelta/build-method) (`coding-rules/references/dotnet.md`). It does not invent analyzer tech; it bundles the four bans nobody ships and wires the existing analyzers for the rest.

## The four custom rules

| ID | Bans | Why |
|----|------|-----|
| HON001 | Properties (and positional-record params) | A field cannot hide cost or control flow behind field-access syntax. Public fields only; a computation is a method. Ships a code-fix (property to `public readonly` field). |
| HON002 | `throw` | Errors are values. Expected failures return `Result<T,E>`; unrecoverable ones call `Panic`, which aborts via `Environment.FailFast` and never throws. |
| HON003 | `implicit operator` | A hidden call on what looks like an assignment. `explicit` is allowed; the cast is visible. |
| HON004 | `dynamic` | Moves type errors to runtime for no gain. |

Reused off the shelf for the rest: `Meziantou.Analyzer` (MA0053 sealed-by-default), `Apex.Analyzers` (`[Immutable]`), `ErrorProne.NET`, `Microsoft.CodeAnalysis.BannedApiAnalyzers` (reflection, service-locator).

## Run it

```bash
./check.sh
```

Builds the solution (the analyzer breaks the demo build on any violation) and runs the analyzer's own tests. One command, one exit code. That is what CI runs.

## Consume it in a repo

Three things, all inherited from `Directory.Build.props`:

```xml
<PackageReference Include="UtilityDelta.Honest.Analyzers" Version="1.0.0" PrivateAssets="all" />
<PackageReference Include="UtilityDelta.Honest.Runtime" Version="1.0.0" />
<PackageReference Include="Microsoft.CodeAnalysis.BannedApiAnalyzers" Version="3.3.4" PrivateAssets="all" />
<AdditionalFiles Include="BannedSymbols.txt" />
```

Both packages are on nuget.org: [UtilityDelta.Honest.Analyzers](https://www.nuget.org/packages/UtilityDelta.Honest.Analyzers), [UtilityDelta.Honest.Runtime](https://www.nuget.org/packages/UtilityDelta.Honest.Runtime).

Then escalate to error in `.editorconfig` (see `samples/Demo/.editorconfig`):

```ini
dotnet_diagnostic.HON001.severity = error
dotnet_diagnostic.HON002.severity = error
dotnet_diagnostic.HON003.severity = error
dotnet_diagnostic.HON004.severity = error
dotnet_diagnostic.CS8509.severity = error
```

The four rules ship at `warning` by default so the package is safe to add before a repo is clean; the `.editorconfig` is what makes them bite.

## Auto-fix

```bash
dotnet format analyzers
```

applies the HON001 property-to-field fix across the tree.

## Layout

- `AGENTS.md` — setup and the build/analyse/fix loop for agents. `PRIOR-ART.md` — where these rules align with and deliberately invert mainstream .NET.
- `src/Honest.Analyzers` — the four rules + the code-fix (netstandard2.0).
- `src/Honest.Runtime` — `Result<T,E>` and `Panic`.
- `tests/Honest.Analyzers.Tests` — a hand-rolled Roslyn harness, no flaky test-framework dependency.
- `samples/Demo` — the honest style, building green with every rule at error.

## Release

Push a `vX.Y.Z` tag. CI runs `check.sh`, packs both packages at that version and pushes them to nuget.org via trusted publishing.

## Later

Native `union` (C# 15 / .NET 11) replaces the `Result<T,E>` struct and the `_ => Panic(...)` arm with real closed-set exhaustiveness. Migrate when it reaches GA; do not gate this on the preview SDK.
