# Agent runbook

Maintaining the analyzers and runtime. Setting up a consumer repo, the fix-by-ID table and the testing notes are all in `README.md`: it ships inside both packages, so it must stay self-contained. Never point it at a repo-relative file.

## The loop

```bash
./check.sh
```

The one gate. Builds the solution (the `samples/Demo` build breaks on any rule violation, so the Demo must stay in the honest style) and runs `tests/Honest.Analyzers.Tests`. Exit 0 means in policy. CI runs exactly this.

## Changing a rule

1. Reproduce the false positive, false negative or bad fix as a failing test in `tests/Honest.Analyzers.Tests`. Watch it fail.
2. Fix the rule or the code fix. Re-run `./check.sh`.
3. Runtime changes (`Result`, `Panic`) have no test project; prove them through the Demo, which builds with every rule at error and `TreatWarningsAsErrors`. `Patterns.LengthOrPanic` is the proof that `[DoesNotReturn]` holds.
4. Update `README.md` if consumers see the change: the rule table, the fix table, the runtime API block.

The analyzer is code under test, not a fixed oracle.

## Trying a change in another repo before release

```bash
./pack.sh            # builds both packages into ~/work/.honest-feed (or pass a path)
dotnet nuget add source ~/work/.honest-feed --name honest-local
```

## Release

Bump `<Version>` in `Directory.Build.props` and the versions in the README setup block, commit, then push a `vX.Y.Z` tag. CI packs at the tag's version and pushes to nuget.org via trusted publishing (policy: owner `utilitydelta`, workflow `ci.yml`, environment `nuget`). A pushed version can be unlisted, never deleted.
