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

    [Fact]
    public async Task Property_with_one_bodied_accessor_gets_no_fix()
    {
        const string src = "public sealed class C { public int V { get; set => field = value; } }";
        Assert.Equal(0, await Harness.FixCount(
            new BanPropertiesAnalyzer(), new PropertyToFieldCodeFix(), src));
    }

    [Fact]
    public async Task Required_property_gets_no_fix()
    {
        const string src = "public sealed class C { public required int Value { get; init; } }";
        Assert.Equal(0, await Harness.FixCount(
            new BanPropertiesAnalyzer(), new PropertyToFieldCodeFix(), src));
    }

    [Theory]
    [InlineData("var c = new C { Value = 1 };")]
    [InlineData("var c = new C() with { Value = 1 };")]
    public async Task Init_property_set_by_a_caller_gets_no_fix(string caller)
    {
        // A readonly field cannot be set from an object initializer or a `with`.
        var src = "public sealed record C { public int Value { get; init; } } public static class Use { public static void M() { " + caller + " } }";
        Assert.Equal(0, await Harness.FixCount(
            new BanPropertiesAnalyzer(), new PropertyToFieldCodeFix(), src));
    }

    [Fact]
    public async Task Settable_property_set_by_an_initializer_still_converts()
    {
        const string src = "public sealed class C { public int Value { get; set; } } public static class Use { public static C M() => new C { Value = 1 }; }";
        var fixedSource = await Harness.ApplyFix(new BanPropertiesAnalyzer(), new PropertyToFieldCodeFix(), src);
        Assert.Contains("public int Value;", fixedSource);
        Assert.Empty(Harness.CompileErrors(fixedSource));
    }

    [Fact]
    public async Task Positional_record_becomes_fields_and_constructor()
    {
        const string src = """
            public sealed record Pos(int Line, string Id);
            public static class Use { public static Pos M() => new Pos(Line: 1, Id: "a"); }
            """;
        var fixedSource = await Harness.ApplyFix(new BanPropertiesAnalyzer(), new PropertyToFieldCodeFix(), src);

        Assert.Contains("public readonly int Line;", fixedSource);
        Assert.Contains("public readonly string Id;", fixedSource);
        Assert.Contains("public Pos(int Line, string Id)", fixedSource);
        Assert.Contains("this.Line = Line;", fixedSource);
        Assert.Contains("this.Id = Id;", fixedSource);
        Assert.DoesNotContain("Deconstruct", fixedSource);
        Assert.Empty(Harness.CompileErrors(fixedSource));
        Assert.Empty(Harness.Analyze(new BanPropertiesAnalyzer(), fixedSource));
    }

    [Fact]
    public async Task Positional_record_keeps_its_body_and_defaults()
    {
        const string src = """
            public sealed record Pos(int Line, string Id = "x")
            {
                public string Describe() => Id + Line;
            }
            """;
        var fixedSource = await Harness.ApplyFix(new BanPropertiesAnalyzer(), new PropertyToFieldCodeFix(), src);

        Assert.Contains("public Pos(int Line, string Id = \"x\")", fixedSource);
        Assert.Contains("public string Describe()", fixedSource);
        Assert.Empty(Harness.CompileErrors(fixedSource));
    }

    [Fact]
    public async Task Deconstructed_record_keeps_a_deconstruct_method()
    {
        const string src = """
            public sealed record Pos(int Line, string Id);
            public static class Use
            {
                public static int M(Pos p) { var (line, _) = p; return line; }
            }
            """;
        var fixedSource = await Harness.ApplyFix(new BanPropertiesAnalyzer(), new PropertyToFieldCodeFix(), src);

        Assert.Contains("public void Deconstruct(out int Line, out string Id)", fixedSource);
        Assert.Empty(Harness.CompileErrors(fixedSource));
    }

    [Theory]
    [InlineData("public readonly record struct P(int X);", "public readonly int X;")]
    [InlineData("public record struct P(int X);", "public int X;")]
    public async Task Record_struct_field_mutability_matches_the_generated_property(string src, string expected)
    {
        var fixedSource = await Harness.ApplyFix(new BanPropertiesAnalyzer(), new PropertyToFieldCodeFix(), src);
        Assert.Contains(expected, fixedSource);
        Assert.Empty(Harness.CompileErrors(fixedSource));
    }

    [Theory]
    [InlineData("public sealed record Pos(int Line) { public Pos With() => this with { Line = 2 }; }")]
    [InlineData("public abstract record B { protected B(int x) { } } public sealed record D(int X) : B(X);")]
    [InlineData("public sealed record Pos(int Line) { public readonly int Twice = Line * 2; }")]
    [InlineData("public sealed record Pos(int Id, [property: System.Obsolete] int Line);")]
    [InlineData("public interface IMarker { } public abstract record B { protected B(int x) { } } public sealed record D(int X) : B(X), IMarker;")]
    [InlineData("public sealed record Pos(int Line) { public readonly int Other, Line; }")]
    public async Task Positional_record_that_cannot_convert_cleanly_gets_no_fix(string src)
    {
        Assert.Equal(0, await Harness.FixCount(
            new BanPropertiesAnalyzer(), new PropertyToFieldCodeFix(), src));
    }

    [Fact]
    public async Task Fix_all_converts_properties_and_records_in_one_pass()
    {
        // dotnet format applies only actions sharing the first action's equivalence key.
        const string src = """
            public sealed class C { public int V { get; init; } }
            public sealed record Pos(int Line, string Id);
            """;
        var fixedSource = await Harness.ApplyFixAll(new BanPropertiesAnalyzer(), new PropertyToFieldCodeFix(), src);

        Assert.Contains("public readonly int V;", fixedSource);
        Assert.Contains("public Pos(int Line, string Id)", fixedSource);
        Assert.Empty(Harness.Analyze(new BanPropertiesAnalyzer(), fixedSource));
    }

    [Theory]
    [InlineData("public int Doubled => 2;")]
    [InlineData("public int Owner { get; private set; }")]
    [InlineData("public int this[int i] => i;")]
    public async Task Fix_all_still_converts_when_the_first_diagnostic_is_declined(string declinedFirst)
    {
        // dotnet format seeds fix-all from the first diagnostic. When the fixer declines it,
        // the stock BatchFixer gets a null key and changed nothing in the whole file.
        var src = "public sealed class C { " + declinedFirst + " public int Width { get; } }";
        var fixedSource = await Harness.ApplyFixAll(new BanPropertiesAnalyzer(), new PropertyToFieldCodeFix(), src);

        Assert.Contains("public readonly int Width;", fixedSource);
        Assert.Contains(declinedFirst, fixedSource);
    }

    [Fact]
    public async Task Fix_all_on_a_mixed_legacy_file_converts_the_safe_members_and_compiles()
    {
        const string src = """
            public interface IHasName { string Name { get; } }
            public sealed record Point(int X, int Y);
            public sealed record Job(string Id, int Cost);
            public sealed class Box : IHasName
            {
                public string Name => "box";
                public int Doubled => Width * 2;
                public int Width { get; }
                public int Height { get; set; }
                public string Owner { get; private set; } = "";
                public string Label { get; init; } = "";
                public int this[int i] => i;
                public Box(int width) => Width = width;
            }
            public static class Use
            {
                public static Job Cheaper(Job j) => j with { Cost = 1 };
                public static Box Make() => new Box(3) { Height = 4, Label = "x" };
                public static int Sum(Point p) { var (x, y) = p; return x + y; }
            }
            """;
        var fixedSource = await Harness.ApplyFixAll(new BanPropertiesAnalyzer(), new PropertyToFieldCodeFix(), src);

        Assert.Contains("public Point(int X, int Y)", fixedSource);
        Assert.Contains("public void Deconstruct(out int X, out int Y)", fixedSource);
        Assert.Contains("public readonly int Width;", fixedSource);
        Assert.Contains("public int Height;", fixedSource);
        Assert.Contains("public sealed record Job(string Id, int Cost);", fixedSource);   // `with` caller
        Assert.Contains("public string Label { get; init; }", fixedSource);              // initializer caller
        Assert.Contains("public string Owner { get; private set; }", fixedSource);       // two access levels
        Assert.Contains("public int Doubled => Width * 2;", fixedSource);                // computed
        Assert.Empty(Harness.CompileErrors(fixedSource));
    }
}
