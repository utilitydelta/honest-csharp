using System.Buffers;
using System.Collections.Concurrent;
using System.Threading.Channels;
using UtilityDelta.Honest;

// Reference patterns for concurrency, performance and error handling in the honest style.
// Each compiles clean under every rule; the comments say what each one demonstrates.

// Concurrency rule 2: message-pass over shared-state-plus-lock. A Channel hands work
// from producers to a single consumer with no lock anywhere.
public static class WorkQueue
{
    public static Channel<OrderLine> Create() =>
        Channel.CreateBounded<OrderLine>(new BoundedChannelOptions(1024)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
        });

    // Async all the way to the boundary, cancellation threaded through. No .Result, no
    // lock held across the await.
    public static async Task Drain(ChannelReader<OrderLine> reader, decimal cap, CancellationToken ct)
    {
        await foreach (var line in reader.ReadAllAsync(ct))
        {
            var message = Pricing.Price(line, cap).Match(
                ok => $"priced {ok.Amount:C}",
                err => Pricing.Describe(err));
            Console.WriteLine(message);
        }
    }
}

// Concurrency rule 3: lock-free for the simple cases. Interlocked, no lock.
// Read() is a method, not a property: it runs code, and the parens say so.
public sealed class Counter
{
    private long _count;

    public long Increment() => Interlocked.Increment(ref _count);

    public long Read() => Interlocked.Read(ref _count);
}

// Concurrency rule 3 + performance: a lock-free shared cache, and ValueTask on a path
// that usually hits and completes synchronously, avoiding the Task allocation.
// Caveat: GetOrAdd does not lock the bucket during the factory call, so under contention
// `factory` may run more than once per key and all-but-one result is discarded. Keep it
// pure and cheap, or do the work outside and pass the value.
public static class PriceCache
{
    private static readonly ConcurrentDictionary<string, Money> Prices = new();

    public static ValueTask<Money> GetOrAdd(string sku, Func<string, Money> factory) =>
        Prices.TryGetValue(sku, out var hit)
            ? new ValueTask<Money>(hit)
            : new ValueTask<Money>(Prices.GetOrAdd(sku, factory));
}

// Concurrency rule 4: when state genuinely must be shared and mutable off the hot path,
// the C# 13 Lock type, held briefly, never across an await, never around I/O.
public sealed class Ledger
{
    private readonly Lock _gate = new();
    private decimal _total;

    public void Add(decimal amount)
    {
        lock (_gate)
        {
            _total += amount;
        }
    }

    public decimal Snapshot()
    {
        lock (_gate)
        {
            return _total;
        }
    }
}

// The sanctioned bridge from the throwing outside world to the no-throw interior:
// catch at the boundary and convert to a Result. `catch` is allowed; `throw` is not.
public static class Boundary
{
    public static Result<string, IoError> ReadText(string path)
    {
        try
        {
            return Result<string, IoError>.Ok(File.ReadAllText(path));
        }
        catch (IOException e)
        {
            return Result<string, IoError>.Err(new IoError.ReadFailed(path, e.Message));
        }
        catch (UnauthorizedAccessException e)
        {
            return Result<string, IoError>.Err(new IoError.Denied(path, e.Message));
        }
        // Banning `throw` does not stop the BCL throwing. Catch every exception the member
        // documents: File.ReadAllText also throws ArgumentException (empty or invalid path)
        // and NotSupportedException (bad path format).
        catch (ArgumentException e)
        {
            return Result<string, IoError>.Err(new IoError.ReadFailed(path, e.Message));
        }
        catch (NotSupportedException e)
        {
            return Result<string, IoError>.Err(new IoError.ReadFailed(path, e.Message));
        }
    }

    // Result composes with async: Task<Result<...>>, cancellation threaded through.
    public static async Task<Result<string, IoError>> ReadTextAsync(string path, CancellationToken ct)
    {
        try
        {
            return Result<string, IoError>.Ok(await File.ReadAllTextAsync(path, ct));
        }
        catch (IOException e)
        {
            return Result<string, IoError>.Err(new IoError.ReadFailed(path, e.Message));
        }
        catch (UnauthorizedAccessException e)
        {
            return Result<string, IoError>.Err(new IoError.Denied(path, e.Message));
        }
        catch (ArgumentException e)
        {
            return Result<string, IoError>.Err(new IoError.ReadFailed(path, e.Message));
        }
        catch (NotSupportedException e)
        {
            return Result<string, IoError>.Err(new IoError.ReadFailed(path, e.Message));
        }
    }

    // A pipeline: MapErr brings each stage onto one error type, Bind chains stages that can
    // fail, Map transforms the value. The first Err short-circuits the rest.
    public static Result<int, string> CountLines(string path) =>
        ReadText(path)
            .MapErr(Describe)
            .Bind(NonEmpty)
            .Map(text => text.Split('\n').Length);

    private static Result<string, string> NonEmpty(string text) =>
        text.Length == 0 ? Result<string, string>.Err("file is empty") : Result<string, string>.Ok(text);

    // The same pipeline without lambdas: TryGet and an early return, Go style.
    public static Result<int, string> CountLinesGoStyle(string path)
    {
        if (!ReadText(path).TryGet(out var text, out var error))
        {
            return Result<int, string>.Err(Describe(error));
        }

        if (text.Length == 0)
        {
            return Result<int, string>.Err("file is empty");
        }

        return Result<int, string>.Ok(text.Split('\n').Length);
    }

    // Success with nothing to return is Result<Unit, E>.
    public static Result<Unit, IoError> WriteText(string path, string text)
    {
        try
        {
            File.WriteAllText(path, text);
            return Result<Unit, IoError>.Ok(Unit.Value);
        }
        catch (IOException e)
        {
            return Result<Unit, IoError>.Err(new IoError.ReadFailed(path, e.Message));
        }
        catch (UnauthorizedAccessException e)
        {
            return Result<Unit, IoError>.Err(new IoError.Denied(path, e.Message));
        }
        catch (ArgumentException e)
        {
            return Result<Unit, IoError>.Err(new IoError.ReadFailed(path, e.Message));
        }
        catch (NotSupportedException e)
        {
            return Result<Unit, IoError>.Err(new IoError.ReadFailed(path, e.Message));
        }
    }

    public static string Describe(IoError error) => error switch
    {
        IoError.ReadFailed f => $"cannot read {f.Path}: {f.Why}",
        IoError.Denied d => $"access denied to {d.Path}: {d.Why}",
        _ => Panic.Value<string>($"unhandled {error}"),
    };

    // Panic.Now is [DoesNotReturn], so the compiler knows `s` is non-null after the guard.
    public static int LengthOrPanic(string? s)
    {
        if (s is null)
        {
            Panic.Now("LengthOrPanic: caller broke the non-null invariant");
        }

        return s.Length;
    }
}

public abstract record IoError
{
    private IoError() { }

    public sealed record ReadFailed : IoError
    {
        public readonly string Path;
        public readonly string Why;

        public ReadFailed(string path, string why)
        {
            Path = path;
            Why = why;
        }
    }

    public sealed record Denied : IoError
    {
        public readonly string Path;
        public readonly string Why;

        public Denied(string path, string why)
        {
            Path = path;
            Why = why;
        }
    }
}

// Performance: zero-allocation hot path. `ref readonly` iteration touches each struct
// element in place, no per-element copy. The no-copy guarantee comes from OrderLine
// being a readonly struct, not from the span alone.
public static class Hot
{
    public static decimal Total(ReadOnlySpan<OrderLine> lines)
    {
        decimal total = 0;
        foreach (ref readonly var line in lines)
        {
            total += line.LineTotal();
        }

        return total;
    }
}

// Performance: produce a buffer without heap allocation. stackalloc for small fixed
// sizes; ArrayPool for larger transient buffers, always returned in a finally.
public static class Scratch
{
    public static int CountDigits(ReadOnlySpan<char> text)
    {
        Span<int> histogram = stackalloc int[10];
        foreach (var c in text)
        {
            if (c is >= '0' and <= '9')
            {
                histogram[c - '0']++;
            }
        }

        var total = 0;
        foreach (var n in histogram)
        {
            total += n;
        }

        return total;
    }

    public static decimal SumPooled(ReadOnlySpan<OrderLine> lines)
    {
        var rented = ArrayPool<decimal>.Shared.Rent(lines.Length);
        try
        {
            for (var i = 0; i < lines.Length; i++)
            {
                rented[i] = lines[i].LineTotal();
            }

            decimal total = 0;
            for (var i = 0; i < lines.Length; i++)
            {
                total += rented[i];
            }

            return total;
        }
        finally
        {
            ArrayPool<decimal>.Shared.Return(rented);
        }
    }
}
