using Xunit;

namespace UtilityDelta.Honest.Tests;

public sealed class ResultTests
{
    private static Result<int, string> Ok(int v) => Result<int, string>.Ok(v);

    private static Result<int, string> Err(string e) => Result<int, string>.Err(e);

    private static string Show<T, E>(Result<T, E> r) => r.Match(v => $"ok:{v}", e => $"err:{e}");

    [Fact]
    public void Map_transforms_ok_and_passes_err_through()
    {
        Assert.Equal("ok:4", Show(Ok(2).Map(v => v * 2)));
        Assert.Equal("err:bad", Show(Err("bad").Map(v => v * 2)));
    }

    [Fact]
    public void MapErr_transforms_err_and_passes_ok_through()
    {
        Assert.Equal("err:3", Show(Err("bad").MapErr(e => e.Length)));
        Assert.Equal("ok:2", Show(Ok(2).MapErr(e => e.Length)));
    }

    [Fact]
    public void Bind_short_circuits_on_the_first_err()
    {
        var calls = 0;
        Result<int, string> Next(int v)
        {
            calls++;
            return Ok(v + 1);
        }

        Assert.Equal("ok:3", Show(Ok(1).Bind(Next).Bind(Next)));
        Assert.Equal(2, calls);
        Assert.Equal("err:bad", Show(Err("bad").Bind(Next).Bind(Next)));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void TryGet_returns_exactly_one_side()
    {
        Assert.True(Ok(7).TryGet(out var value, out _));
        Assert.Equal(7, value);
        Assert.False(Err("bad").TryGet(out _, out var error));
        Assert.Equal("bad", error);
    }

    [Fact]
    public void Default_is_err()
    {
        Assert.False(default(Result<int, string>).IsOk);
    }

    [Fact]
    public void Unit_values_are_equal()
    {
        Assert.Equal(Unit.Value, default(Unit));
    }
}
