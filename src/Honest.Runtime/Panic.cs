using System.Diagnostics.CodeAnalysis;

namespace UtilityDelta.Honest;

/// <summary>
/// The <c>panic!()</c> carve-out: an unrecoverable invariant violation aborts the process via
/// <see cref="Environment.FailFast(string)"/>. No unwinding, nothing catches it, and it never
/// throws. It also kills a test host, so test the invariant check, not the panic.
/// </summary>
public static class Panic
{
    /// <summary>Abort the process with <paramref name="why"/>. Never returns.</summary>
    [DoesNotReturn]
    public static void Now(string why) => Environment.FailFast(why);

    /// <summary>
    /// Abort the process with <paramref name="why"/>, in expression position, e.g. the
    /// <c>_ =&gt;</c> arm of a switch over a closed hierarchy. Never returns.
    /// </summary>
    [DoesNotReturn]
    public static T Value<T>(string why)
    {
        Environment.FailFast(why);
        return default!; // unreachable; FailFast does not return
    }
}
