using CsCheck;
using Xunit;

namespace UtilityDelta.Honest.Tests;

// The laws Map, MapErr and Bind must keep for every value, checked over generated inputs.
// A failure shrinks to the smallest counterexample and prints the seed to replay it.
public sealed class ResultLawTests
{
    private static readonly Gen<Result<int, string>> AnyResult =
        Gen.Bool.SelectMany(ok => ok
            ? Gen.Int.Select(Result<int, string>.Ok)
            : Gen.String.Select(Result<int, string>.Err));

    private static string Show<T, E>(Result<T, E> r) => r.Match(v => $"ok:{v}", e => $"err:{e}");

    private static Result<int, string> Half(int v) =>
        v % 2 == 0 ? Result<int, string>.Ok(v / 2) : Result<int, string>.Err($"odd {v}");

    private static Result<int, string> Dec(int v) =>
        v == int.MinValue ? Result<int, string>.Err("underflow") : Result<int, string>.Ok(v - 1);

    [Fact]
    public void Map_identity_changes_nothing() =>
        AnyResult.Sample(r => Show(r.Map(v => v)) == Show(r));

    [Fact]
    public void Map_composes() =>
        AnyResult.Sample(r => Show(r.Map(v => v ^ 3).Map(v => v / 7)) == Show(r.Map(v => (v ^ 3) / 7)));

    [Fact]
    public void MapErr_composes() =>
        AnyResult.Sample(r => Show(r.MapErr(e => e + "!").MapErr(e => e.Length)) == Show(r.MapErr(e => (e + "!").Length)));

    [Fact]
    public void Bind_left_identity() =>
        Gen.Int.Sample(v => Show(Result<int, string>.Ok(v).Bind(Half)) == Show(Half(v)));

    [Fact]
    public void Bind_right_identity() =>
        AnyResult.Sample(r => Show(r.Bind(Result<int, string>.Ok)) == Show(r));

    [Fact]
    public void Bind_is_associative() =>
        AnyResult.Sample(r => Show(r.Bind(Half).Bind(Dec)) == Show(r.Bind(v => Half(v).Bind(Dec))));

    [Fact]
    public void TryGet_agrees_with_Match() =>
        AnyResult.Sample(r =>
        {
            var ok = r.TryGet(out var value, out var error);
            return Show(r) == (ok ? $"ok:{value}" : $"err:{error}");
        });

    [Fact]
    public void Switch_runs_exactly_the_matching_arm() =>
        AnyResult.Sample(r =>
        {
            var seen = "";
            r.Switch(v => seen += $"ok:{v}", e => seen += $"err:{e}");
            return seen == Show(r);
        });
}
