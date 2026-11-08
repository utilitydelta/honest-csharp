namespace UtilityDelta.Honest;

// The `panic!()` carve-out. An unrecoverable invariant violation aborts the process
// via Environment.FailFast: no unwinding, nothing catches it. This is the one
// sanctioned alternative to `throw`, and it never throws.
public static class Panic
{
    public static void Now(string why) => Environment.FailFast(why);

    public static T Value<T>(string why)
    {
        Environment.FailFast(why);
        return default!; // unreachable; FailFast does not return
    }
}
