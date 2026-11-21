using System;
using M3scalinum.Text.Documents;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

/// <summary>
/// The fidelity contract of the Markdown importer: one fixture per supported construct, plus the
/// traps — malformed input, escaping, and images that do not resolve.
/// </summary>
public sealed class MarkdownImportTests
{
    private static string Import(string markdown, TypstMarkdownImportOptions? options = null)
        => new MarkdownImporter(options).Import(markdown).Document.ToMarkup();

    [Fact]
    public void Imports_a_paragraph()
    {
        Assert.Equal("Hello world.\n", Import("Hello world."));
    }

    [Theory]
    [InlineData("# Heading", "= Heading")]
    [InlineData("## Heading", "== Heading")]
    [InlineData("### Heading", "=== Heading")]
    [InlineData("#### Heading", "==== Heading")]
    [InlineData("##### Heading", "===== Heading")]
    [InlineData("###### Heading", "====== Heading")]
    public void Imports_every_atx_heading_level(string markdown, string expected)
    {
        Assert.Contains(expected, Import(markdown), StringComparison.Ordinal);
    }

    [Fact]
    public void Imports_a_setext_heading_at_both_levels()
    {
        var markup = Import("Title\n=====\n\nSubtitle\n--------\n");

        Assert.Equal("= Title\n\n== Subtitle\n", markup);
    }

    [Fact]
    public void Strips_a_closing_run_of_hashes_from_an_atx_heading()
    {
        Assert.Equal("== Closed\n", Import("## Closed ##"));
    }

    [Fact]
    public void Imports_emphasis_strong_and_strikethrough()
    {
        var markup = Import("*a* **b** _c_ __d__ ~~e~~");

        Assert.Equal("_a_ *b* _c_ *d* #strike[e]\n", markup);
    }

    [Fact]
    public void Imports_triple_emphasis_as_strong_inside_emphasis()
    {
        Assert.Equal("_*both*_\n", Import("***both***"));
    }

    [Fact]
    public void Imports_inline_code_with_its_backticks_grown()
    {
        var markup = Import("Use `a * b` and ``a ` b``.");

        Assert.Contains("`a * b`", markup, StringComparison.Ordinal);
        Assert.Contains("``a ` b``", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Imports_an_inline_link_with_its_text()
    {
        var markup = Import("[the site](https://example.com \"Home\")");

        Assert.Equal("#link(\"https://example.com\")[the site]\n", markup);
    }

    [Fact]
    public void Imports_a_reference_link_and_its_definition()
    {
        var markup = Import("See [the docs][docs] and [shortcut].\n\n[docs]: https://example.com/docs\n[shortcut]: https://example.com/short\n");

        Assert.Contains("#link(\"https://example.com/docs\")[the docs]", markup, StringComparison.Ordinal);
        Assert.Contains("#link(\"https://example.com/short\")[shortcut]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Imports_a_collapsed_reference_link()
    {
        var markup = Import("[docs][]\n\n[docs]: https://example.com/docs\n");

        Assert.Contains("#link(\"https://example.com/docs\")[docs]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Counts_a_definition_that_nothing_refers_to()
    {
        var result = new MarkdownImporter().Import("[unused]: https://example.com\n");

        Assert.Equal(1, result.CountOf("reference-definition"));
        Assert.Equal(string.Empty, result.Document.ToMarkup());
    }

    [Fact]
    public void Imports_an_autolink()
    {
        var markup = Import("<https://example.com/x> and <mail@example.com>");

        Assert.Contains("#link(\"https://example.com/x\")", markup, StringComparison.Ordinal);
        Assert.Contains("#link(\"mailto:mail@example.com\")", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Imports_a_footnote_reference_and_its_definition()
    {
        var markup = Import("Text with a note.[^1]\n\n[^1]: The note.\n");

        Assert.Contains("Text with a note.#footnote[The note.]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_second_reference_to_one_footnote_is_counted_not_duplicated()
    {
        var result = new MarkdownImporter().Import("One[^a] and two[^a].\n\n[^a]: Note.\n");
        var markup = result.Document.ToMarkup();

        Assert.Equal(1, result.CountOf("footnote-reference"));
        Assert.Contains("two[^a]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Counts_a_footnote_definition_that_nothing_refers_to()
    {
        var result = new MarkdownImporter().Import("[^orphan]: Never used.\n");

        Assert.Equal(1, result.CountOf("footnote-definition"));
    }

    [Fact]
    public void Imports_a_thematic_break()
    {
        Assert.Contains("#divider()", Import("---\n"), StringComparison.Ordinal);
        Assert.Contains("#divider()", Import("***\n"), StringComparison.Ordinal);
        Assert.Contains("#divider()", Import("___\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void Imports_a_fenced_code_block_with_its_language()
    {
        var markup = Import("```rust\nfn main() {\n  let x = 1 < 2;\n}\n```\n");

        Assert.Contains("```rust\nfn main() {\n  let x = 1 < 2;\n}\n```", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Imports_a_tilde_fenced_code_block()
    {
        var markup = Import("~~~csharp\nvar x = 1;\n~~~\n");

        Assert.Contains("```csharp\nvar x = 1;\n```", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Imports_an_indented_code_block()
    {
        var markup = Import("Text.\n\n    indented\n      deeper\n");

        Assert.Contains("```\nindented\n  deeper\n```", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Imports_a_block_quote_with_two_paragraphs()
    {
        var markup = Import("> first\n>\n> second\n");

        Assert.Contains("#quote(block: true)[", markup, StringComparison.Ordinal);
        Assert.Contains("first", markup, StringComparison.Ordinal);
        Assert.Contains("second", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Imports_an_unordered_list()
    {
        Assert.Equal("- one\n- two\n", Import("- one\n- two\n"));
    }

    [Fact]
    public void Imports_an_ordered_list_as_an_enumeration()
    {
        Assert.Equal("+ one\n+ two\n", Import("1. one\n2. two\n"));
    }

    [Fact]
    public void Imports_a_nested_list_two_spaces_deeper()
    {
        var markup = Import("- one\n  - one-a\n  - one-b\n- two\n");

        Assert.Equal("- one\n  - one-a\n  - one-b\n- two\n", markup);
    }

    [Fact]
    public void Imports_an_ordered_list_nested_in_an_unordered_one()
    {
        Assert.Equal("- step\n  + first\n", Import("- step\n  1. first\n"));
    }

    [Fact]
    public void Separates_the_items_of_a_loose_list_with_a_blank_line()
    {
        var markup = Import("- one\n\n- two\n");

        Assert.Equal("- one\n\n- two\n", markup);
    }

    [Fact]
    public void Keeps_a_paragraph_and_a_nested_list_in_one_item()
    {
        var markup = Import("- first\n  continued\n  - nested\n");

        Assert.Contains("- first continued", markup, StringComparison.Ordinal);
        Assert.Contains("  - nested", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Imports_a_table_with_alignment()
    {
        var markup = Import("| Item | Amount |\n| :--- | ---: |\n| A | 1 |\n");

        Assert.Contains("columns: (1fr, 1fr)", markup, StringComparison.Ordinal);
        Assert.Contains("table.header(table.cell(align: left)[Item], table.cell(align: right)[Amount])", markup, StringComparison.Ordinal);
        Assert.Contains("table.cell(align: left)[A], table.cell(align: right)[1]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Imports_a_centred_column()
    {
        var markup = Import("| a |\n| :-: |\n| b |\n");

        Assert.Contains("table.cell(align: center)[b]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Imports_a_table_without_alignment()
    {
        var markup = Import("a | b\n--- | ---\n1 | 2\n");

        Assert.Contains("table.header([a], [b])", markup, StringComparison.Ordinal);
        Assert.Contains("[1], [2],", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("table.cell", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Imports_a_hard_line_break()
    {
        var markup = Import("one  \ntwo\\\nthree\n");

        Assert.Contains("one\\\ntwo\\\nthree", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Imports_a_registered_image_as_a_figure_with_its_alt_as_caption()
    {
        var options = new TypstMarkdownImportOptions
        {
            AssetResolver = source => source == "logo.png" ? "assets/logo.png" : null,
        };

        var markup = Import("![A logo](logo.png)", options);

        Assert.Contains("#figure(", markup, StringComparison.Ordinal);
        Assert.Contains("image(\"assets/logo.png\")", markup, StringComparison.Ordinal);
        Assert.Contains("caption: [A logo]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Passes_the_raw_destination_to_the_resolver()
    {
        string? seen = null;
        var options = new TypstMarkdownImportOptions
        {
            AssetResolver = source =>
            {
                seen = source;
                return "assets/pixel.png";
            },
        };

        Import("![alt](/media/2026/pixel.png?v=2)", options);

        Assert.Equal("/media/2026/pixel.png?v=2", seen);
    }

    [Fact]
    public void Rejects_an_image_that_does_not_resolve()
    {
        var exception = Assert.Throws<TypstMarkdownImportException>(
            () => Import("![chart](charts/revenue.png)"));

        Assert.Equal("image", exception.ConstructName);
        Assert.Contains("charts/revenue.png", exception.Message, StringComparison.Ordinal);
        Assert.Contains("AssetResolver", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Never_fetches_an_image_without_a_resolver()
    {
        Assert.Throws<TypstMarkdownImportException>(() => Import("![x](https://example.com/logo.png)"));
    }

    [Fact]
    public void Reduces_an_inline_image_to_its_alt_text()
    {
        var options = new TypstMarkdownImportOptions { AssetResolver = _ => "assets/pixel.png" };
        var markup = Import("before ![the alt](pixel.png) after", options);

        Assert.Contains("before the alt after", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Counts_a_raw_html_block_without_passing_it_through()
    {
        var result = new MarkdownImporter().Import("<div class=\"x\">\nraw\n</div>\n\nAfter.\n");

        Assert.Equal("After.\n", result.Document.ToMarkup());
        Assert.Equal(1, result.CountOf("html-block"));
    }

    [Fact]
    public void Counts_inline_raw_html_and_keeps_the_text()
    {
        var result = new MarkdownImporter().Import("a <span>b</span> c\n");

        Assert.Equal("a b c\n", result.Document.ToMarkup());
        Assert.Equal(2, result.CountOf("html-inline"));
    }

    [Fact]
    public void Throws_on_raw_html_when_asked()
    {
        var options = new TypstMarkdownImportOptions { RawHtml = TypstMarkdownRawHtmlPolicy.Throw };
        var exception = Assert.Throws<TypstMarkdownImportException>(
            () => Import("<div>\nx\n</div>\n", options));

        Assert.Equal("html-block", exception.ConstructName);
    }

    [Fact]
    public void FromMarkdown_matches_the_importer()
    {
        const string markdown = "## Title\n\nBody with *emphasis*.\n";

        Assert.Equal(
            new MarkdownImporter().Import(markdown).Document.ToMarkup(),
            TypstDocument.FromMarkdown(markdown).ToMarkup());
    }

    [Fact]
    public void FromMarkdown_rejects_a_null_document()
    {
        Assert.Throws<ArgumentNullException>(() => TypstDocument.FromMarkdown(null!));
    }

    [Fact]
    public void Import_rejects_a_null_document()
    {
        Assert.Throws<ArgumentNullException>(() => new MarkdownImporter().Import(null!));
    }

    [Fact]
    public void Exposes_its_options()
    {
        var options = new TypstMarkdownImportOptions { RawHtml = TypstMarkdownRawHtmlPolicy.Throw };

        Assert.Same(options, new MarkdownImporter(options).Options);
        Assert.NotNull(new MarkdownImporter().Options);
    }

    [Fact]
    public void Importing_an_empty_string_yields_an_empty_document()
    {
        Assert.Equal(string.Empty, Import(string.Empty));
        Assert.Empty(new MarkdownImporter().Import(string.Empty).DroppedConstructs);
    }

    [Fact]
    public void Importing_whitespace_only_yields_an_empty_document()
    {
        Assert.Equal(string.Empty, Import("\n\n   \n\t\n"));
    }

    [Fact]
    public void The_dropped_constructs_are_sorted_by_name()
    {
        var result = new MarkdownImporter().Import("<div>\nx\n</div>\n\n[unused]: https://example.com\n");

        Assert.Equal(["html-block", "reference-definition"], [result.DroppedConstructs[0].ConstructName, result.DroppedConstructs[1].ConstructName]);
    }
}
