using System;
using M3scalinum.Text.Documents;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

public sealed class MarkupWriterTests
{
    private static string Render(Action<TypstContent> build)
    {
        var content = new TypstContent();
        build(content);
        return TypstDocument.Create().Paragraph(content).ToMarkup();
    }

    [Theory]
    [InlineData("a#b", "a\\#b")]
    [InlineData("a$b", "a\\$b")]
    [InlineData("a*b", "a\\*b")]
    [InlineData("a_b", "a\\_b")]
    [InlineData("a`b", "a\\`b")]
    [InlineData("a<b", "a\\<b")]
    [InlineData("a>b", "a\\>b")]
    [InlineData("a@b", "a\\@b")]
    [InlineData("a~b", "a\\~b")]
    [InlineData("a\\b", "a\\\\b")]
    public void Escapes_markup_special_characters(string input, string expected)
    {
        Assert.Equal(expected + "\n", Render(content => content.Text(input)));
    }

    [Theory]
    [InlineData("- item", "\\- item")]
    [InlineData("+ item", "\\+ item")]
    [InlineData("/ term", "\\/ term")]
    [InlineData("= heading", "\\= heading")]
    [InlineData("1. item", "1\\. item")]
    [InlineData("42. item", "42\\. item")]
    public void Escapes_line_start_markers(string input, string expected)
    {
        Assert.Equal(expected + "\n", Render(content => content.Text(input)));
    }

    [Theory]
    [InlineData("1.5 is a number")]
    [InlineData("version 1.2.3")]
    [InlineData("a1. b")]
    public void Leaves_a_dot_that_is_not_a_list_marker_alone(string input)
    {
        // Only a digit run followed by `.` at the very start of a line is a list marker.
        Assert.Equal(input + "\n", Render(content => content.Text(input)));
    }

    [Fact]
    public void Escapes_comment_openers()
    {
        Assert.Equal("a\\//b\n", Render(content => content.Text("a//b")));

        // The slash is escaped, and the star is escaped too: `\/*` would leave an
        // unbalanced emphasis marker.
        Assert.Equal("a\\/\\*b\n", Render(content => content.Text("a/*b")));
    }

    [Fact]
    public void Does_not_escape_characters_that_are_not_special()
    {
        const string text = "Total: 1 234 EUR (net) - 12.5%";

        Assert.Equal(text + "\n", Render(content => content.Text(text)));
    }

    [Fact]
    public void Escapes_brackets_inside_a_content_block()
    {
        var markup = TypstDocument.Create()
            .Align(TypstAlignment.Center, new TypstContent().Text("a [b] c"))
            .ToMarkup();

        Assert.Contains("a \\[b\\] c", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_no_trailing_whitespace_and_one_final_newline()
    {
        var markup = TypstDocument.Create()
            .Heading(1, "Title")
            .Paragraph("Body")
            .ToMarkup();

        Assert.EndsWith("\n", markup, StringComparison.Ordinal);
        Assert.DoesNotContain(" \n", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("\n\n\n", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Is_stable_across_calls()
    {
        var document = TypstDocument.Create().Heading(1, "Title").Paragraph("Body");

        Assert.Equal(document.ToMarkup(), document.ToMarkup());
    }
}