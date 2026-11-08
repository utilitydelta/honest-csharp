# honest-csharp

C# that behaves like Go, Rust, and Zig: cost and control flow are visible at the call site, nothing hides behind clean syntax. The rules fail the build, not code review.

Two packages on nuget.org:

- [UtilityDelta.Honest.Analyzers](https://www.nuget.org/packages/UtilityDelta.Honest.Analyzers): four Roslyn rules nobody else ships, plus a code fix.
- [UtilityDelta.Honest.Runtime](https://www.nuget.org/packages/UtilityDelta.Honest.Runtime): `Result<T,E>` and `Panic`, the replacements for `throw`.

This README is the whole consumer guide; nothing below depends on files outside the package. Source and the full sample: https://github.com/utilitydelta/honest-csharp

## The four rules

| ID | Bans | Why |
|----|------|-----|
| HON001 | Properties, indexers, positional record parameters | A field cannot hide cost or control flow behind field-access syntax. Public fields only; a computation is a method. Your own interfaces declare methods, since an interface cannot hold a field. Members that implement an interface or override a member from another assembly (`IEnumerator<T>.Current`, `Exception.Message`) are exempt: that contract is not yours to change. |
| HON002 | `throw`, bare `throw;`, and throw helpers (`ArgumentNullException.ThrowIfNull`, `ObjectDisposedException.ThrowIf`, `ExceptionDispatchInfo.Throw`) | Errors are values. Expected failures return `Result<T,E>`; unrecoverable ones call `Panic`, which aborts via `Environment.FailFast` and never throws. A throw helper is a `throw` with the keyword moved into the BCL. |
| HON003 | `implicit operator` | A hidden call on what looks like an assignment. `explicit` is allowed; the cast is visible. |
| HON004 | `dynamic` | Moves type errors to runtime for no gain. |

The rules ship at `warning` so the package is safe to add to a dirty repo. The `.editorconfig` below is what makes them bite.

## Set up a repo

Three files at the repo root. Every project under it inherits them. Add the package references by editing `Directory.Build.props`, not with `dotnet add package`, which writes them into a single `.csproj`.

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
    <PackageReference Include="UtilityDelta.Honest.Analyzers" Version="2.0.0" PrivateAssets="all" />
    <PackageReference Include="UtilityDelta.Honest.Runtime" Version="2.0.0" />
    <PackageReference Include="Microsoft.CodeAnalysis.BannedApiAnalyzers" Version="3.3.4" PrivateAssets="all" />
    <!-- $(MSBuildThisFileDirectory): a bare relative path resolves per project and breaks nested layouts. -->
    <AdditionalFiles Include="$(MSBuildThisFileDirectory)BannedSymbols.txt" />
    <!-- Result, Unit and Panic in every file. -->
    <Using Include="UtilityDelta.Honest" />
  </ItemGroup>
</Project>
```

**`BannedSymbols.txt`**: start with reflection and service-locator, grow it as the repo teaches you. Throw helpers need no entry; HON002 already catches them.

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
# CA1815 fires on a plain struct without equality; write `readonly record struct` with body fields.
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
| HON001 | `dotnet format analyzers --diagnostics HON001` converts what converts without changing behaviour. `{ get; }` and `{ get; init; }` become `public readonly` fields, `{ get; set; }` a mutable field. A positional record becomes a body with one field per parameter and a constructor of the same signature, plus a `Deconstruct` method if anything deconstructs it. The diagnostic message names the fix by hand for computed properties and your own interface properties (methods), indexers (a named method such as `At(i)`) and mixed access like `{ get; private set; }` (pick the one access level). One case the message cannot see: if the fix skips a plain `{ get; init; }` or a positional record, some caller sets it through `new C { X = 1 }` or `c with { X = 1 }`. Change those callers to the constructor and run the fix again. |
| HON002 | `Result<T,E>` for an expected failure, `Panic` for a broken invariant. A constructor cannot return a `Result`: make it private and validate in a static factory that returns one. At a framework boundary, `catch` what the platform throws and convert it to a `Result`. Rethrow is banned too: convert or `Panic`. A framework contract that requires a throw, such as `JsonConverter<T>.Read` throwing `JsonException`, is the one-line suppression below. |
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
    public R Match<R>(Func<T, R> ok, Func<E, R> err);        // forces both arms
    public void Switch(Action<T> ok, Action<E> err);
    public bool TryGet(out T value, out E error);              // Go-style early return
    public Result<U, E> Map<U>(Func<T, U> f);                  // transform Ok
    public Result<T, F> MapErr<F>(Func<E, F> f);               // transform Err
    public Result<U, E> Bind<U>(Func<T, Result<U, E>> f);      // chain a stage that can fail
}

public readonly record struct Unit { public static readonly Unit Value; }  // Result<Unit, E>

public static class Panic
{
    [DoesNotReturn] public static void Now(string why);       // Environment.FailFast
    [DoesNotReturn] public static T Value<T>(string why);     // for expression positions
}
```

`default(Result<T,E>)` is an `Err` holding `default(E)`. Always construct through `Ok` or `Err`. The runtime package ships XML docs, so the editor shows all of this on hover.

Long type arguments go behind a `using` alias (C# 12), once per file or as `global using` in one file. An alias resolves outside your namespace, so fully qualify every type in it:

```csharp
using OrderResult = UtilityDelta.Honest.Result<Shop.Order, Shop.OrderError>;

return OrderResult.Ok(order);
```

There is no type-inferring `Result.Ok(x)`: one argument cannot infer both `T` and `E`, and the usual workaround is an implicit conversion, which HON003 bans for good reason.

Stages compose two ways. Pick per call site.

```csharp
// Chained: MapErr brings each stage onto one error type; the first Err short-circuits.
Result<Receipt, CheckoutError> Checkout(string json) =>
    Orders.Parse(json).MapErr(CheckoutError.From)
        .Bind(order => Stock.Reserve(order).MapErr(CheckoutError.From))
        .Bind(reservation => Billing.Charge(reservation).MapErr(CheckoutError.From))
        .Map(Receipt.From);

// Go style: no lambdas, early return. TryGet is nullable-annotated, so reading the
// wrong out variable on a branch warns (CS8602).
if (!Orders.Parse(json).TryGet(out var order, out var parseError))
{
    return Result<Receipt, CheckoutError>.Err(CheckoutError.From(parseError));
}
```

`MapErr` infers its error type from the lambda, so `e => new CheckoutError.Parse(e)` gives `Result<T, CheckoutError.Parse>` and the next `Bind` fails with CS0029. Name the base type: `MapErr(CheckoutError (e) => new CheckoutError.Parse(e))`, or use a method that returns the base, like `CheckoutError.From` above.

`Bind` hands the next stage only the previous value. To keep an earlier one in scope, nest the last step inside its lambda: `.Bind(order => Stock.Reserve(order).Map(r => new Summary(order, r)))`.

One `Result`, one error. Several problems ride inside that error as a field; the error type is never a bare list.

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

Banning `throw` does nothing about exceptions the BCL throws. Inside your own code, prefer the `Try` form (`TryDequeue`, `TryGetValue`, `TryParse`) over the throwing one (`Dequeue`, an indexer, `First`), and call `Panic` on the branch that an invariant says cannot happen: the failure then names the broken invariant instead of surfacing as `InvalidOperationException: Queue empty`. At the edge, where the platform throws on bad input, wrap each throwing call and catch every exception its docs list, not only the obvious one:

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

### System.Text.Json

HON001 moves DTOs onto fields, and System.Text.Json skips public fields by default. Nothing errors: a field DTO deserializes with every field left at its default. Turn fields on, and make missing or null values fail instead of slipping through as `null`:

```csharp
static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
{
    IncludeFields = true,
    RespectRequiredConstructorParameters = true,  // missing "amount" is a JsonException, not null
    RespectNullableAnnotations = true,            // "amount": null into a string is a JsonException
};

public sealed class ExpenseDto
{
    public readonly string Id;
    public readonly string Amount;
    public ExpenseDto(string id, string amount) { Id = id; Amount = amount; }
}
```

A single public constructor binds by parameter name; no attribute needed. `JsonSerializer.Deserialize` throws on bad input, so it is a boundary call like `ReadText` above: catch `JsonException` and `NotSupportedException` and return a `Result`.

- **The deserializer stops at the first mismatch.** To report every problem in a document, walk a `JsonDocument` and return one error that holds them all, such as `LoadError.Invalid(ImmutableArray<Problem>)`.
- **`JsonElement.GetString()` throws** on a lone surrogate such as `"\uD800"`, which `JsonDocument.Parse` accepts. So does reading a property name containing one. Wrap both.
- **A custom `JsonConverter<T>.Read` must throw `JsonException`** to report bad input; the serializer has no other channel. That throw is a legitimate one-line HON002 suppression.

## Testing

- **`Panic` kills the test host.** `FailFast` is not an exception; the whole run aborts with no summary. Keep invariant checks as pure functions that return `bool` or a `Result`, test those, and let the `Panic` call site stay a one-liner. To prove a path really panics, run it in a child process and assert on the exit code. The `_ => Panic.Value<T>(...)` arm on a closed hierarchy is unreachable by construction; leave it untested.
- **Unwrapping in tests.** Write one helper per test project. `TryGet` is nullable-annotated and xUnit's `Assert.True`/`Assert.False` tell the compiler they do not return on failure, so no `!` is needed:

  ```csharp
  static T Ok<T, E>(Result<T, E> r)
  {
      Assert.True(r.TryGet(out var value, out var error), $"expected Ok, got Err: {error}");
      return value;
  }

  static E Err<T, E>(Result<T, E> r)
  {
      Assert.False(r.TryGet(out var value, out var error), $"expected Err, got Ok: {value}");
      return error;
  }
  ```

- **xUnit `MemberData`** works with a static method or field; the usual static property trips HON001.
- **Collection fields.** A `readonly` array field still lets callers change its elements; `ImmutableArray<T>` does not. Both compare by reference: record equality on a collection field compares the list reference, and xUnit's `Assert.Equal` treats `ImmutableArray<T>` as a value with reference equality. Assert on contents: `Assert.Equal(expected, actual.ToArray())`.

## Later

Native `union` (C# 15 / .NET 11) replaces `Result<T,E>` and the `_ => Panic(...)` arm with real closed-set exhaustiveness. Migrate when it reaches GA.

Maintaining the packages: see `AGENTS.md` and `PRIOR-ART.md` in the GitHub repo.
