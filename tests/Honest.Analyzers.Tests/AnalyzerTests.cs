using Xunit;

namespace UtilityDelta.Honest.Analyzers.Tests;

public sealed class AnalyzerTests
{
    [Fact]
    public void Property_is_flagged_at_its_name()
    {
        const string src = """
            public sealed class C
            {
                public int Value { get; init; }
            }
            """;
        var d = Assert.Single(Harness.Analyze(new BanPropertiesAnalyzer(), src));
        Assert.Equal("HON001", d.Id);
        Assert.Equal("Value", d.TextOf(src));
    }

    [Fact]
    public void Computed_property_is_flagged()
    {
        const string src = """
            public sealed class C
            {
                public int X;
                public int Doubled => X * 2;
            }
            """;
        var d = Assert.Single(Harness.Analyze(new BanPropertiesAnalyzer(), src));
        Assert.Equal("Doubled", d.TextOf(src));
    }

    [Fact]
    public void Positional_record_parameters_are_flagged()
    {
        const string src = "public sealed record Money(decimal Amount, string Currency);";
        var d = Assert.Single(Harness.Analyze(new BanPropertiesAnalyzer(), src));
        Assert.Equal("HON001", d.Id);
        Assert.Equal("(decimal Amount, string Currency)", d.TextOf(src));
    }

    [Fact]
    public void Indexer_is_flagged()
    {
        const string src = """
            public sealed class C
            {
                private readonly int[] _data = new int[4];
                public int this[int i] => _data[i];
            }
            """;
        var d = Assert.Single(Harness.Analyze(new BanPropertiesAnalyzer(), src));
        Assert.Equal("HON001", d.Id);
        Assert.Equal("this", d.TextOf(src));
    }

    [Fact]
    public void Public_field_is_allowed()
    {
        const string src = """
            public sealed class C
            {
                public readonly int Value;
                public C(int value) => Value = value;
            }
            """;
        Assert.Empty(Harness.Analyze(new BanPropertiesAnalyzer(), src));
    }

    [Fact]
    public void Throw_statement_is_flagged()
    {
        const string src = """
            public sealed class C
            {
                public void M(bool b)
                {
                    if (b) throw new System.InvalidOperationException();
                }
            }
            """;
        var d = Assert.Single(Harness.Analyze(new BanThrowAnalyzer(), src));
        Assert.Equal("HON002", d.Id);
    }

    [Fact]
    public void Throw_expression_is_flagged()
    {
        const string src = """
            public sealed class C
            {
                public string M(string? s) => s ?? throw new System.ArgumentNullException();
            }
            """;
        var d = Assert.Single(Harness.Analyze(new BanThrowAnalyzer(), src));
        Assert.Equal("HON002", d.Id);
    }

    [Fact]
    public void Implicit_operator_is_flagged()
    {
        const string src = """
            public sealed class Celsius
            {
                public readonly double Value;
                public Celsius(double value) => Value = value;
                public static implicit operator double(Celsius c) => c.Value;
            }
            """;
        var d = Assert.Single(Harness.Analyze(new BanImplicitOperatorAnalyzer(), src));
        Assert.Equal("HON003", d.Id);
        Assert.Equal("implicit", d.TextOf(src));
    }

    [Fact]
    public void Explicit_operator_is_allowed()
    {
        const string src = """
            public sealed class Celsius
            {
                public readonly double Value;
                public Celsius(double value) => Value = value;
                public static explicit operator double(Celsius c) => c.Value;
            }
            """;
        Assert.Empty(Harness.Analyze(new BanImplicitOperatorAnalyzer(), src));
    }

    [Fact]
    public void Dynamic_is_flagged()
    {
        const string src = """
            public sealed class C
            {
                public void M()
                {
                    dynamic x = 1;
                    System.Console.WriteLine(x);
                }
            }
            """;
        var d = Assert.Single(Harness.Analyze(new BanDynamicAnalyzer(), src));
        Assert.Equal("HON004", d.Id);
        Assert.Equal("dynamic", d.TextOf(src));
    }

    [Fact]
    public void Implicit_bcl_interface_property_is_allowed()
    {
        const string src = """
            using System.Collections;
            using System.Collections.Generic;
            public sealed class Counter : IEnumerator<int>
            {
                private int _i;
                public int Current => _i;
                object IEnumerator.Current => _i;
                public bool MoveNext() => ++_i < 10;
                public void Reset() => _i = 0;
                public void Dispose() { }
            }
            """;
        Assert.Empty(Harness.Analyze(new BanPropertiesAnalyzer(), src));
    }

    [Fact]
    public void Bcl_override_property_is_allowed()
    {
        const string src = """
            public sealed class Oops : System.Exception
            {
                public override string Message => "oops";
            }
            """;
        Assert.Empty(Harness.Analyze(new BanPropertiesAnalyzer(), src));
    }

    [Fact]
    public void Own_interface_property_is_still_flagged_on_both_sides()
    {
        const string src = """
            public interface IHasValue { int Value { get; } }
            public sealed class C : IHasValue
            {
                public int Value => 1;
            }
            """;
        var ds = Harness.Analyze(new BanPropertiesAnalyzer(), src);
        Assert.Equal(2, ds.Length);
        Assert.All(ds, d => Assert.Equal("HON001", d.Id));
    }

    [Fact]
    public void Positional_record_struct_is_flagged()
    {
        const string src = "public readonly record struct Point(int X, int Y);";
        var d = Assert.Single(Harness.Analyze(new BanPropertiesAnalyzer(), src));
        Assert.Equal("HON001", d.Id);
    }

    [Fact]
    public void Bare_rethrow_is_flagged()
    {
        const string src = """
            public sealed class C
            {
                public void M()
                {
                    try { System.Console.WriteLine(); }
                    catch { throw; }
                }
            }
            """;
        var d = Assert.Single(Harness.Analyze(new BanThrowAnalyzer(), src));
        Assert.Equal("HON002", d.Id);
    }
}
