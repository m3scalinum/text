using M3scalinum.Text.Typst.Diagnostics;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

/// <summary>The source snippet rendered under a diagnostic.</summary>
public sealed class DiagnosticSnippetTests
{
    private static TypstDiagnostic Error(int line, int column, int endColumn, string sourceLine)
        => new(
            TypstSeverity.Error,
            "unknown variable: `totl`",
            [],
            [],
            new TypstLocation("main.typ", line, column, line, endColumn, true),
            sourceLine);

    [Fact]
    public void The_snippet_shows_the_line_and_a_caret_under_the_span()
    {
        var text = Error(12, 8, 12, "#let x = totl").ToDisplayString();

        Assert.Contains("12 | #let x = totl", text, System.StringComparison.Ordinal);
        Assert.Contains("   |        ^^^^", text, System.StringComparison.Ordinal);
    }

    [Fact]
    public void The_gutter_grows_with_the_line_number()
    {
        var text = Error(7, 1, 2, "= Title").ToDisplayString();

        Assert.Contains("7 | = Title", text, System.StringComparison.Ordinal);
        Assert.Contains("  | ^", text, System.StringComparison.Ordinal);
    }

    [Fact]
    public void A_span_that_crosses_lines_underlines_to_the_end_of_the_line()
    {
        var diagnostic = new TypstDiagnostic(
            TypstSeverity.Error,
            "unclosed delimiter",
            [],
            [],
            new TypstLocation("main.typ", 3, 5, 5, 2, true),
            "#let x = (1");

        var text = diagnostic.ToDisplayString();

        // Column 5 to the end of "#let x = (1" is 4 characters.
        Assert.Contains("  |     ^^^^", text, System.StringComparison.Ordinal);
    }

    [Fact]
    public void A_single_character_span_still_gets_one_caret()
    {
        var text = Error(1, 1, 1, "x").ToDisplayString();

        Assert.Contains("1 | x", text, System.StringComparison.Ordinal);
        Assert.Contains("  | ^", text, System.StringComparison.Ordinal);
    }

    [Fact]
    public void The_snippet_can_be_turned_off()
    {
        var text = Error(12, 8, 12, "#let x = totl").ToDisplayString(includeSnippet: false);

        Assert.DoesNotContain("|", text, System.StringComparison.Ordinal);
        Assert.Contains("main.typ:12:8: error: unknown variable", text, System.StringComparison.Ordinal);
    }

    [Fact]
    public void A_diagnostic_without_a_span_renders_no_snippet()
    {
        var diagnostic = new TypstDiagnostic(
            TypstSeverity.Error,
            "export failed",
            [],
            [],
            null);

        var text = diagnostic.ToDisplayString();

        Assert.Equal("error: export failed", text);
    }

    [Fact]
    public void Hints_follow_the_snippet()
    {
        var diagnostic = new TypstDiagnostic(
            TypstSeverity.Error,
            "unknown variable: `totl`",
            ["did you mean `total`?"],
            [],
            new TypstLocation("main.typ", 1, 10, 1, 14, true),
            "#let x = totl");

        var text = diagnostic.ToDisplayString();

        Assert.Contains("  hint: did you mean `total`?", text, System.StringComparison.Ordinal);
        Assert.True(
            text.IndexOf('^') < text.IndexOf("hint:", System.StringComparison.Ordinal));
    }
}
