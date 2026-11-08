namespace UtilityDelta.Honest;

// Errors are values. A readonly struct so it does not allocate; Match forces the
// caller to handle both arms. Native `union` (C# 15 / .NET 11) replaces this later.
public readonly struct Result<T, E>
{
    private readonly T _value;
    private readonly E _error;
    public readonly bool IsOk;

    private Result(T value, E error, bool isOk)
    {
        _value = value;
        _error = error;
        IsOk = isOk;
    }

    public static Result<T, E> Ok(T value) => new(value, default!, true);

    public static Result<T, E> Err(E error) => new(default!, error, false);

    public R Match<R>(Func<T, R> ok, Func<E, R> err) => IsOk ? ok(_value) : err(_error);

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
}
