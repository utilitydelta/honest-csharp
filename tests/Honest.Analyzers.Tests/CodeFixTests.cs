using Xunit;

namespace UtilityDelta.Honest.Analyzers.Tests;

public sealed class CodeFixTests
{
    [Fact]
    public async Task Get_only_property_becomes_readonly_field()
    {
        const string src = """
            public sealed class C
            {
                public int Value { get; }
            }
            """;
        var fixedSource = await Harness.ApplyFix(
            new BanPropertiesAnalyzer(), new PropertyToFieldCodeFix(), src);

        Assert.Contains("public readonly int Value;", fixedSource);
        Assert.DoesNotContain("{ get; }", fixedSource);
    }

    [Fact]
    public async Task Settable_property_becomes_mutable_field()
    {
        const string src = """
            public sealed class C
            {
                public int Value { get; set; }
            }
            """;
        var fixedSource = await Harness.ApplyFix(
            new BanPropertiesAnalyzer(), new PropertyToFieldCodeFix(), src);

        Assert.Contains("public int Value;", fixedSource);
        Assert.DoesNotContain("readonly", fixedSource);
    }

    [Fact]
    public async Task Initializer_is_preserved()
    {
        const string src = """
            public sealed class C
            {
                public int Value { get; } = 42;
            }
            """;
        var fixedSource = await Harness.ApplyFix(
            new BanPropertiesAnalyzer(), new PropertyToFieldCodeFix(), src);

        Assert.Contains("public readonly int Value = 42;", fixedSource);
    }

    [Fact]
    public async Task Init_only_property_becomes_readonly_field()
    {
        const string src = """
            public sealed class C
            {
                public int Value { get; init; }
            }
            """;
        var fixedSource = await Harness.ApplyFix(
            new BanPropertiesAnalyzer(), new PropertyToFieldCodeFix(), src);

        Assert.Contains("public readonly int Value;", fixedSource);
    }

    [Fact]
    public async Task Attributes_are_preserved()
    {
        const string src = """
            public sealed class C
            {
                [System.Obsolete]
                public int Value { get; }
            }
            """;
        var fixedSource = await Harness.ApplyFix(
            new BanPropertiesAnalyzer(), new PropertyToFieldCodeFix(), src);

        Assert.Contains("System.Obsolete", fixedSource);
        Assert.Contains("readonly int Value;", fixedSource);
    }

    [Theory]
    [InlineData("public int Value { get; private set; }")]
    [InlineData("public int Value { get; protected init; }")]
    [InlineData("public int Value { private get; set; }")]
    public async Task Mixed_accessibility_property_gets_no_fix(string member)
    {
        // A field has one access level. Converting `{ get; private set; }` would silently
        // widen the setter to public, so the fix must not be offered.
        var src = "public class C { " + member + " }";
        Assert.Equal(0, await Harness.FixCount(
            new BanPropertiesAnalyzer(), new PropertyToFieldCodeFix(), src));
    }

    [Fact]
    public async Task Computed_property_gets_no_fix()
    {
        const string src = "public sealed class C { public int X; public int Doubled => X * 2; }";
        Assert.Equal(0, await Harness.FixCount(
            new BanPropertiesAnalyzer(), new PropertyToFieldCodeFix(), src));
    }
}
