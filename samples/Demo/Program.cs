using UtilityDelta.Honest;
using PriceResult = UtilityDelta.Honest.Result<Money, OrderError>;

// Data carriers: public readonly fields, real constructors, no properties.

// Value object needing value equality: record in body-with-fields form.
public sealed record Money
{
    public readonly decimal Amount;
    public readonly string Currency;

    public Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }
}

// Hot, small value: a readonly record struct in body-field form. readonly satisfies
// IDE0250/IDE0251; record gives value equality (CA1815); body fields dodge the property
// ban. A computation is a method (parens), never a property.
public readonly record struct OrderLine
{
    public readonly string Sku;
    public readonly int Qty;
    public readonly decimal UnitPrice;

    public OrderLine(string sku, int qty, decimal unitPrice)
    {
        Sku = sku;
        Qty = qty;
        UnitPrice = unitPrice;
    }

    public decimal LineTotal() => Qty * UnitPrice;
}

// Multi-shape failure: a closed sealed-record hierarchy, matched exhaustively.
public abstract record OrderError
{
    private OrderError() { }

    public sealed record OverLimit : OrderError
    {
        public readonly decimal Cap;
        public OverLimit(decimal cap) => Cap = cap;
    }

    public sealed record EmptyLine : OrderError
    {
        public readonly string Sku;
        public EmptyLine(string sku) => Sku = sku;
    }
}

public static class Pricing
{
    // Errors are values: a Result, never a thrown exception. The `using PriceResult = ...`
    // alias at the top keeps the long type out of every return.
    public static PriceResult Price(OrderLine line, decimal cap)
    {
        if (line.Qty <= 0)
        {
            return PriceResult.Err(new OrderError.EmptyLine(line.Sku));
        }

        var total = line.LineTotal();
        if (total > cap)
        {
            return PriceResult.Err(new OrderError.OverLimit(cap));
        }

        return PriceResult.Ok(new Money(total, "AUD"));
    }

    public static string Describe(OrderError error) => error switch
    {
        OrderError.OverLimit l => $"over limit of {l.Cap:C}",
        OrderError.EmptyLine e => $"empty line for {e.Sku}",
        // Unreachable by construction; .NET 10 cannot prove the hierarchy closed.
        _ => Panic.Value<string>($"unhandled {error}"),
    };
}

public static class Program
{
    public static void Main()
    {
        var line = new OrderLine(sku: "ABC", qty: 3, unitPrice: 9.5m);
        var message = Pricing.Price(line, cap: 100m).Match(
            ok => $"priced {ok.Amount:C} in {ok.Currency}",
            err => Pricing.Describe(err));

        Console.WriteLine(message);
    }
}
