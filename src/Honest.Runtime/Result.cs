using System.Diagnostics.CodeAnalysis;

namespace UtilityDelta.Honest;

/// <summary>
/// An expected failure as a value: either Ok holding a <typeparamref name="T"/> or Err holding
/// an <typeparamref name="E"/>. A readonly struct, so it does not allocate. Construct through
/// <see cref="Ok"/> or <see cref="Err"/>; <c>default</c> is an Err holding <c>default(E)</c>.
/// </summary>
/// <remarks>
/// Long type arguments read better behind an alias:
/// <c>using ParseResult = UtilityDelta.Honest.Result&lt;Config, ParseError&gt;;</c>
/// then <c>ParseResult.Ok(config)</c>.
/// </remarks>
public readonly struct Result<T, E>
{
    private readonly T _value;
    private readonly E _error;

    /// <summary>True for Ok, false for Err.</summary>
    public readonly bool IsOk;

    private Result(T value, E error, bool isOk)
    {
        _value = value;
        _error = error;
        IsOk = isOk;
    }

    /// <summary>A successful result holding <paramref name="value"/>.</summary>
    public static Result<T, E> Ok(T value) => new(value, default!, true);

    /// <summary>A failed result holding <paramref name="error"/>.</summary>
    public static Result<T, E> Err(E error) => new(default!, error, false);

    /// <summary>Run the arm that matches and return its value. Both arms are required.</summary>
    public R Match<R>(Func<T, R> ok, Func<E, R> err) => IsOk ? ok(_value) : err(_error);

    /// <summary>Run the arm that matches. Both arms are required.</summary>
    public void Switch(Action<T> ok, Action<E> err)
    {
        if (IsOk)
        {
            ok(_value);
        }
        else
        {
            err(_error);
        }
    }

    /// <summary>
    /// Go-style access without lambdas. Returns <see cref="IsOk"/>; exactly one of
    /// <paramref name="value"/> and <paramref name="error"/> is meaningful.
    /// <code>
    /// if (!parsed.TryGet(out var config, out var error))
    /// {
    ///     return Result&lt;Plan, ParseError&gt;.Err(error);
    /// }
    /// </code>
    /// </summary>
    public bool TryGet([MaybeNullWhen(false)] out T value, [MaybeNullWhen(true)] out E error)
    {
        value = _value;
        error = _error;
        return IsOk;
    }

    /// <summary>Transform the Ok value. An Err passes through untouched.</summary>
    public Result<U, E> Map<U>(Func<T, U> f) =>
        IsOk ? Result<U, E>.Ok(f(_value)) : Result<U, E>.Err(_error);

    /// <summary>
    /// Transform the Err value. Use it to bring stages with different error types onto one
    /// before chaining them with <see cref="Bind"/>.
    /// </summary>
    public Result<T, F> MapErr<F>(Func<E, F> f) =>
        IsOk ? Result<T, F>.Ok(_value) : Result<T, F>.Err(f(_error));

    /// <summary>Chain a stage that can itself fail. The first Err short-circuits the rest.</summary>
    public Result<U, E> Bind<U>(Func<T, Result<U, E>> f) =>
        IsOk ? f(_value) : Result<U, E>.Err(_error);
}

/// <summary>The Ok type of an operation that succeeds with nothing to return: <c>Result&lt;Unit, E&gt;</c>.</summary>
public readonly record struct Unit
{
    /// <summary>The only value.</summary>
    public static readonly Unit Value = default;
}
