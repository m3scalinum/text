using System;
using System.Diagnostics;
using System.Text;
using M3scalinum.Text.SharpTypst.Documents;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Tests;

/// <summary>
/// The traps of a Markdown import: nested structures, malformed input that must not throw, CRLF,
/// escaping, and a document big enough that a quadratic parser would show.
/// </summary>
public sealed class MarkdownImportEdgeCaseTests
{
    private static string Import(string markdown, TypstMarkdownImportOptions? options = null)
        => new MarkdownImporter(options).Import(markdown).Document.ToMarkup();

    [Fact]
    public void A_table_inside_a_list_item_inside_a_block_quote_stays_nested()
    {
        const string markdown = """
            > 1. item
            >
            >    | a | b |
            >    | - | - |
            >    | 1 | 2 |
            """;

        var markup = Import(markdown);

        Assert.StartsWith("#quote(block: true)[", markup, StringComparison.Ordinal);
        Assert.Contains("+ item", markup, StringComparison.Ordinal);
        Assert.Contains("#table(", markup, StringComparison.Ordinal);
        Assert.Contains("table.header([a], [b])", markup, StringComparison.Ordinal);
        Assert.Contains("[1], [2],", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("#quote(block: true)[\n\n+ item\n\n#table", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_quote_inside_a_list_item_keeps_the_quote()
    {
        var markup = Import("- item\n  > quoted\n");

        Assert.Contains("- item", markup, StringComparison.Ordinal);
        Assert.Contains("#quote(block: true)[", markup, StringComparison.Ordinal);
        Assert.Contains("quoted", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unclosed_fence_runs_to_the_end_of_the_document()
    {
        var result = new MarkdownImporter().Import("```\ncode\nmore code\n");

        Assert.Equal("```\ncode\nmore code\n```\n", result.Document.ToMarkup());
        Assert.Empty(result.DroppedConstructs);
    }

    [Fact]
    public void A_fence_shorter_than_its_opening_does_not_close_it()
    {
        var markup = Import("````\n```\nstill code\n````\n");

        Assert.Contains("````\n```\nstill code\n````", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_table_row_with_a_stray_pipe_drops_the_extra_cell_and_counts_it()
    {
        const string markdown = "| a | b |\n| --- | --- |\n| 1 | 2 | 3 |\n";

        var result = new MarkdownImporter().Import(markdown);

        Assert.Equal(1, result.CountOf("table-cell"));
        Assert.Contains("[1], [2],", result.Document.ToMarkup(), StringComparison.Ordinal);
        Assert.DoesNotContain("[3]", result.Document.ToMarkup(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_short_table_row_is_padded_with_empty_cells()
    {
        var markup = Import("| a | b |\n| --- | --- |\n| 1 |\n");

        Assert.Contains("[1], [],", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void An_escaped_pipe_stays_inside_its_cell()
    {
        var markup = Import("| a | b |\n| --- | --- |\n| x \\| y | z |\n");

        Assert.Contains("x | y", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_reference_that_is_never_defined_is_kept_as_text()
    {
        var result = new MarkdownImporter().Import("[text][nope] and [also][missing]\n");

        Assert.Equal("[text][nope] and [also][missing]\n", result.Document.ToMarkup());
        Assert.Empty(result.DroppedConstructs);
    }

    [Fact]
    public void A_reference_link_may_be_defined_after_it_is_used()
    {
        var markup = Import("See [the docs][docs].\n\n[docs]: https://example.com/docs\n");

        Assert.Contains("#link(\"https://example.com/docs\")[the docs]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_crlf_document_imports_exactly_like_an_lf_one()
    {
        const string lf = "# Title\n\nA paragraph with *emphasis* and `code`.\n\n- one\n- two\n\n```rust\nfn main() {}\n```\n";
        var crlf = lf.Replace("\n", "\r\n", StringComparison.Ordinal);

        Assert.Equal(Import(lf), Import(crlf));
    }

    [Fact]
    public void A_bare_carriage_return_document_imports_exactly_like_an_lf_one()
    {
        const string lf = "# Title\n\nBody.\n";
        var cr = lf.Replace("\n", "\r", StringComparison.Ordinal);

        Assert.Equal(Import(lf), Import(cr));
    }

    [Fact]
    public void A_byte_order_mark_is_ignored()
    {
        Assert.Equal("= Title\n", Import("\uFEFF# Title\n"));
    }

    [Fact]
    public void Escapes_typst_significant_characters_in_text_a_link_title_and_alt_text()
    {
        var options = new TypstMarkdownImportOptions { AssetResolver = _ => "assets/pixel.png" };
        const string markdown =
            "chars *_#$<>@[] end\n\n" +
            "[t](https://example.com/x \"a*_#title\")\n\n" +
            "![a*_#alt](pixel.png)";

        var markup = Import(markdown, options);

        Assert.Contains("chars \\*\\_\\#\\$\\<\\>\\@[] end", markup, StringComparison.Ordinal);
        Assert.Contains("#link(\"https://example.com/x\")[t]", markup, StringComparison.Ordinal);
        Assert.Contains("caption: [a\\*\\_\\#alt]", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("a*_#title", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Escapes_typst_significant_characters_in_a_reference_link_text()
    {
        var markup = Import("[*_#<>@[]](https://example.com)\n");

        Assert.Contains("#link(\"https://example.com\")[\\*\\_\\#\\<\\>\\@\\[\\]]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hundred_kilobytes_parse_in_under_a_second()
    {
        var builder = new StringBuilder(120_000);

        for (var index = 0; index < 400; index++)
        {
            builder.Append("## Section ").Append(index).Append("\n\n");
            builder.Append("A paragraph with *emphasis*, `code`, a [link](https://example.com/").Append(index)
                .Append(") and a <https://example.com/auto").Append(index).Append(">.\n\n");
            builder.Append("- first item\n- second item with **strong** text\n\n");
            builder.Append("| a | b |\n| --- | ---: |\n| 1 | 2 |\n\n");
            builder.Append("> a quoted line\n\n");
            builder.Append("```text\nlet x = ").Append(index).Append(";\n```\n\n");
        }

        var markdown = builder.ToString();

        Assert.True(markdown.Length > 100_000, $"the fixture is {markdown.Length} characters");

        var stopwatch = Stopwatch.StartNew();
        var result = new MarkdownImporter().Import(markdown);
        stopwatch.Stop();

        Assert.True(result.Document.ToMarkup().Length > markdown.Length / 2);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(1),
            $"the import took {stopwatch.Elapsed.TotalMilliseconds:0} ms for {markdown.Length} characters");
    }

    [Fact]
    public void Deeply_nested_lists_stop_at_a_sane_depth_without_throwing()
    {
        var builder = new StringBuilder();

        for (var depth = 0; depth < 40; depth++)
        {
            builder.Append(' ', depth * 2).Append("- level ").Append(depth).Append('\n');
        }

        var markup = Import(builder.ToString());

        Assert.Contains("level 0", markup, StringComparison.Ordinal);
        Assert.Contains("level 39", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unterminated_link_is_kept_as_text()
    {
        Assert.Equal("[unclosed\n", Import("[unclosed\n"));
    }

    [Fact]
    public void A_table_delimiter_without_a_header_pipe_is_a_setext_heading()
    {
        Assert.Equal("== a\n", Import("a\n---\n"));
    }
}
