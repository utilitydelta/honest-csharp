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

    [Theory]
    [InlineData("public sealed class C { public int V { get; set; } }", "Replace property 'V' with a public field")]
    [InlineData("public sealed class C { public int V => 1; }", "Replace computed property 'V' with a method")]
    [InlineData("public sealed class C { public int V { get; private set; } }", "a field has one access level")]
    [InlineData("public interface I { int V { get; } }", "Replace interface property 'V' with a method")]
    [InlineData("public sealed class C { public int this[int i] => i; }", "Replace the indexer with a named method")]
    [InlineData("public sealed record R(int V);", "Replace the positional parameters of record 'R' with fields and a constructor")]
    public void Property_message_names_the_fix_for_its_shape(string src, string expected)
    {
        var d = Harness.Analyze(new BanPropertiesAnalyzer(), src).First();
        Assert.Contains(expected, d.GetMessage());
    }

    [Fact]
    public void Own_interface_implementation_message_says_method()
    {
        const string src = "public interface I { int V { get; } } public sealed class C : I { public int V { get; } }";
        var d = Harness.Analyze(new BanPropertiesAnalyzer(), src).Last();
        Assert.Contains("with a method, together with the interface property it implements", d.GetMessage());
    }

    [Theory]
    [InlineData("System.ArgumentNullException.ThrowIfNull(s);", "ArgumentNullException.ThrowIfNull")]
    [InlineData("System.ArgumentException.ThrowIfNullOrEmpty(s);", "ArgumentException.ThrowIfNullOrEmpty")]
    [InlineData("System.ObjectDisposedException.ThrowIf(s is null, s!);", "ObjectDisposedException.ThrowIf")]
    [InlineData("System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(new System.Exception()).Throw();", "ExceptionDispatchInfo.Throw")]
    [InlineData("System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(new System.Exception());", "ExceptionDispatchInfo.Throw")]
    public void Throw_helper_is_flagged(string call, string name)
    {
        var src = "public static class C { public static void M(string? s) { " + call + " } }";
        var d = Assert.Single(Harness.Analyze(new BanThrowAnalyzer(), src));
        Assert.Equal("HON002", d.Id);
        Assert.Contains($"throw helper '{name}'", d.GetMessage());
    }

    [Fact]
    public void Method_named_throw_on_a_non_exception_type_is_allowed()
    {
        const string src = """
            public static class Dice { public static int ThrowDice() => 4; }
            public static class C { public static int M() => Dice.ThrowDice(); }
            """;
        Assert.Empty(Harness.Analyze(new BanThrowAnalyzer(), src));
    }

    public static readonly TheoryData<string, string> ViolationPerRule = new()
    {
        { "HON001", "public sealed class C { public int X { get; set; } }" },
        { "HON002", "public sealed class C { public void M() { throw new System.Exception(); } }" },
        { "HON003", "public sealed class C { public static implicit operator int(C c) => 1; }" },
        { "HON004", "public sealed class C { public void M() { dynamic d = 1; System.Console.WriteLine(d); } }" },
    };

    private static Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer AnalyzerFor(string id) => id switch
    {
        "HON001" => new BanPropertiesAnalyzer(),
        "HON002" => new BanThrowAnalyzer(),
        "HON003" => new BanImplicitOperatorAnalyzer(),
        _ => new BanDynamicAnalyzer(),
    };

    [Theory]
    [MemberData(nameof(ViolationPerRule))]
    public void Generated_code_is_not_analyzed(string id, string violation)
    {
        Assert.NotEmpty(Harness.Analyze(AnalyzerFor(id), violation));
        Assert.Empty(Harness.Analyze(AnalyzerFor(id), "// <auto-generated/>\n" + violation));
    }

    [Theory]
    [InlineData("public sealed class C { public void M() { throw new System.Exception(); } }", "Replace 'throw' with a Result<T,E> return")]
    [InlineData("public sealed class C { public void M() { throw new System.Exception(); } }", "Where a framework contract requires the throw (JsonConverter.Read), suppress that one line with the reason")]
    [InlineData("public sealed class C { public static implicit operator int(C c) => 1; }", "Make this conversion 'explicit'")]
    [InlineData("public sealed class C { public void M() { dynamic d = 1; System.Console.WriteLine(d); } }", "Replace 'dynamic' with a static type")]
    public void Rule_message_names_the_fix(string src, string expected)
    {
        var id = src.Contains("throw") ? "HON002" : src.Contains("implicit") ? "HON003" : "HON004";
        var d = Assert.Single(Harness.Analyze(AnalyzerFor(id), src));
        Assert.Contains(expected, d.GetMessage());
    }

    [Theory]
    [InlineData("public sealed class C { public int V { get { return 1; } } }")]
    [InlineData("public sealed class C { public int V { get; set => field = value; } }")]
    public void Property_with_any_accessor_body_is_computed(string src)
    {
        var d = Assert.Single(Harness.Analyze(new BanPropertiesAnalyzer(), src));
        Assert.Contains("Replace computed property 'V' with a method", d.GetMessage());
    }

    [Fact]
    public void Explicit_implementation_of_own_interface_is_flagged()
    {
        const string src = "public interface I { int V { get; } } public sealed class C : I { int I.V => 1; }";
        Assert.Equal(2, Harness.Analyze(new BanPropertiesAnalyzer(), src).Length);
    }

    [Fact]
    public void Record_with_empty_parameter_list_is_not_flagged()
    {
        Assert.Empty(Harness.Analyze(new BanPropertiesAnalyzer(), "public sealed record R();"));
    }

    [Theory]
    [InlineData("public sealed class C { public C(string? s) { if (s is null) throw new System.ArgumentException(); } }")]
    [InlineData("public sealed class C { public C(string? s) { System.ArgumentNullException.ThrowIfNull(s); } }")]
    public void Throw_in_a_constructor_points_at_a_static_factory(string src)
    {
        var d = Assert.Single(Harness.Analyze(new BanThrowAnalyzer(), src));
        Assert.Contains("static factory that returns Result", d.GetMessage());
        Assert.Contains("Panic.Now(...)", d.GetMessage());
    }

    [Fact]
    public void Throw_outside_a_constructor_has_no_factory_hint()
    {
        var d = Assert.Single(Harness.Analyze(new BanThrowAnalyzer(), "public static class C { public static void M() { throw new System.Exception(); } }"));
        Assert.DoesNotContain("static factory", d.GetMessage());
    }

    [Theory]
    [MemberData(nameof(ViolationPerRule))]
    public void Every_rule_links_to_the_fix_table(string id, string violation)
    {
        var d = Harness.Analyze(AnalyzerFor(id), violation).First();
        Assert.Equal("https://github.com/utilitydelta/honest-csharp#fix-by-diagnostic-id", d.Descriptor.HelpLinkUri);
    }
}
