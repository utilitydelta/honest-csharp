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
}
