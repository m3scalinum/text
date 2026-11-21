using System;
using System.Linq;
using M3scalinum.Text.SharpTypst.Documents;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Tests;

/// <summary>
/// The fidelity contract of the HTML importer: one fixture per supported element, plus the traps —
/// malformed markup, entities, escaping and images that do not resolve.
/// </summary>
public sealed class HtmlImportTests
{
    private static string Import(string html, TypstHtmlImportOptions? options = null)
        => new TypstHtmlImporter(options).Import(html).Document.ToMarkup();

    [Fact]
    public void Imports_a_paragraph()
    {
        var markup = Import("<p>Hello world.</p>");

        Assert.Equal("Hello world.\n", markup);
    }

    [Theory]
    [InlineData("h1", "= Heading")]
    [InlineData("h2", "== Heading")]
    [InlineData("h3", "=== Heading")]
    [InlineData("h4", "==== Heading")]
    [InlineData("h5", "===== Heading")]
    [InlineData("h6", "====== Heading")]
    public void Imports_every_heading_level(string tag, string expected)
    {
        Assert.Contains(expected, Import($"<{tag}>Heading</{tag}>"), StringComparison.Ordinal);
    }

    [Fact]
    public void Imports_strong_and_emphasis_in_both_spellings()
    {
        var markup = Import("<p><strong>a</strong> <b>b</b> <em>c</em> <i>d</i></p>");

        Assert.Equal("*a* *b* _c_ _d_\n", markup);
    }

    [Fact]
    public void Imports_inline_code_superscript_and_subscript()
    {
        var markup = Import("<p>H<sub>2</sub>O and <code>a * b</code> and x<sup>2</sup></p>");

        Assert.Contains("H#sub[2]O", markup, StringComparison.Ordinal);
        Assert.Contains("`a * b`", markup, StringComparison.Ordinal);
        Assert.Contains("x#super[2]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Imports_a_link_with_its_text_and_without_one()
    {
        var markup = Import("<p><a href=\"https://example.com\">the site</a> <a href=\"https://example.com/x\"></a></p>");

        Assert.Contains("#link(\"https://example.com\")[the site]", markup, StringComparison.Ordinal);
        Assert.Contains("#link(\"https://example.com/x\")[]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Imports_a_link_without_an_href_as_its_text()
    {
        Assert.Equal("plain\n", Import("<p><a>plain</a></p>"));
    }

    [Fact]
    public void Imports_nested_unordered_lists()
    {
        var markup = Import("<ul><li>one<ul><li>one-a</li><li>one-b</li></ul></li><li>two</li></ul>");

        Assert.Equal("- one\n  - one-a\n  - one-b\n- two\n", markup);
    }

    [Fact]
    public void Imports_nested_ordered_lists_inside_unordered_ones()
    {
        var markup = Import("<ul><li>step<ol><li>first</li></ol></li></ul>");

        Assert.Equal("- step\n  + first\n", markup);
    }

    [Fact]
    public void Imports_a_table_with_a_header_and_spans()
    {
        var markup = Import(
            "<table><thead><tr><th>Item</th><th>Amount</th></tr></thead>" +
            "<tbody><tr><td colspan=\"2\">Total</td></tr>" +
            "<tr><td rowspan=\"2\">North</td><td>1</td></tr><tr><td>2</td></tr></tbody></table>");

        Assert.Contains("columns: (1fr, 1fr)", markup, StringComparison.Ordinal);
        Assert.Contains("table.header([Item], [Amount])", markup, StringComparison.Ordinal);
        Assert.Contains("table.cell(colspan: 2)[Total]", markup, StringComparison.Ordinal);
        Assert.Contains("table.cell(rowspan: 2)[North], [1]", markup, StringComparison.Ordinal);
        Assert.Contains("[2],", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Imports_a_table_without_a_thead()
    {
        var markup = Import("<table><tr><td>a</td><td>b</td></tr></table>");

        Assert.Contains("columns: (1fr, 1fr)", markup, StringComparison.Ordinal);
        Assert.Contains("[a], [b],", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("table.header", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Counts_a_table_caption_as_dropped()
    {
        var result = new TypstHtmlImporter().Import("<table><caption>Title</caption><tr><td>a</td></tr></table>");

        Assert.Contains(result.DroppedElements, element => element.ElementName == "caption" && element.Count == 1);
    }

    [Fact]
    public void Imports_a_code_block_with_its_language()
    {
        var markup = Import("<pre><code class=\"language-rust\">fn main() {\n  let x = 1 &lt; 2;\n}</code></pre>");

        Assert.Contains("```rust", markup, StringComparison.Ordinal);
        Assert.Contains("fn main() {\n  let x = 1 < 2;\n}", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Keeps_preformatted_whitespace()
    {
        var markup = Import("<pre>  indented\n    deeper</pre>");

        Assert.Contains("  indented\n    deeper", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Drops_the_first_newline_of_a_pre_block()
    {
        var markup = Import("<pre>\nline one\nline two</pre>");

        Assert.Contains("```\nline one\nline two", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Imports_a_blockquote()
    {
        var markup = Import("<blockquote><p>first</p><p>second</p></blockquote>");

        Assert.Contains("#quote(block: true)[", markup, StringComparison.Ordinal);
        Assert.Contains("first", markup, StringComparison.Ordinal);
        Assert.Contains("second", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Imports_a_description_list()
    {
        var markup = Import("<dl><dt>Term</dt><dd>A definition.</dd></dl>");

        Assert.Equal("/ Term: A definition.\n", markup);
    }

    [Fact]
    public void Imports_a_thematic_break_and_a_line_break()
    {
        var markup = Import("<p>one<br>two</p><hr>");

        Assert.Contains("one\\\ntwo", markup, StringComparison.Ordinal);
        Assert.Contains("#divider()", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Imports_a_registered_image_as_a_figure()
    {
        var options = new TypstHtmlImportOptions { AssetResolver = source => source == "logo.png" ? "assets/logo.png" : null };
        var markup = Import("<img src=\"logo.png\" alt=\"A logo\">", options);

        Assert.Contains("#figure(", markup, StringComparison.Ordinal);
        Assert.Contains("image(\"assets/logo.png\")", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Passes_the_raw_source_to_the_resolver()
    {
        string? seen = null;
        var options = new TypstHtmlImportOptions
        {
            AssetResolver = source =>
            {
                seen = source;
                return "assets/pixel.png";
            },
        };

        Import("<img src=\"/media/2026/pixel.png?v=2\">", options);

        Assert.Equal("/media/2026/pixel.png?v=2", seen);
    }

    [Fact]
    public void Rejects_an_image_that_does_not_resolve()
    {
        var exception = Assert.Throws<TypstHtmlImportException>(
            () => Import("<img src=\"charts/revenue.png\">"));

        Assert.Equal("img", exception.ElementName);
        Assert.Contains("charts/revenue.png", exception.Message, StringComparison.Ordinal);
        Assert.Contains("AssetResolver", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_an_inline_image_that_does_not_resolve()
    {
        var exception = Assert.Throws<TypstHtmlImportException>(
            () => Import("<p>before <img src=\"missing.png\"> after</p>"));

        Assert.Equal("img", exception.ElementName);
    }

    [Fact]
    public void Never_fetches_an_image_without_a_resolver()
    {
        Assert.Throws<TypstHtmlImportException>(() => Import("<img src=\"https://example.com/logo.png\">"));
    }

    [Fact]
    public void Repairs_unclosed_tags_and_stray_end_tags()
    {
        var markup = Import("<div><p>first<p>second</p></p><p>third<ul><li>one<li>two</ul></div>");

        Assert.Contains("first", markup, StringComparison.Ordinal);
        Assert.Contains("second", markup, StringComparison.Ordinal);
        Assert.Contains("third", markup, StringComparison.Ordinal);
        Assert.Contains("- one\n- two", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Repairs_an_unclosed_table_row_and_cell()
    {
        var markup = Import("<table><tr><td>a<td>b<tr><td>c</table>");

        Assert.Contains("[a], [b],", markup, StringComparison.Ordinal);
        Assert.Contains("[c],", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Reads_attributes_with_double_single_and_no_quotes()
    {
        var markup = Import(
            "<p class=lead id=\"one\" data-x='two' hidden>text</p>" +
            "<a href=https://example.com/no-quotes>link</a>");

        Assert.Contains("text", markup, StringComparison.Ordinal);
        Assert.Contains("#link(\"https://example.com/no-quotes\")[link]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Consumes_the_rest_of_the_input_for_an_unterminated_attribute_quote()
    {
        // HTML reads a quoted attribute value up to its closing quote or the end of the input; this
        // importer does the same rather than guessing where the value ends.
        Assert.Equal(string.Empty, Import("<p class=\"lead>text</p>"));
    }

    [Fact]
    public void Decodes_character_references()
    {
        var markup = Import("<p>&amp; &lt; &gt; &quot; &apos; &#65; &#x42; &hellip; &nbsp;</p>");

        Assert.Contains("& \\< \\> \" ' A B \u2026", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Leaves_an_unknown_reference_verbatim()
    {
        var markup = Import("<p>&unknown; &amp no semicolon</p>");

        Assert.Contains("&unknown; &amp no semicolon", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Escapes_typst_significant_characters_in_text()
    {
        var markup = Import("<p>* _ # $ &lt; &gt; @ [ ] ` ~ \\</p>");

        Assert.Contains("\\* \\_ \\# \\$ \\< \\> \\@ [ ] \\` \\~ \\\\", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Escapes_typst_significant_characters_in_an_attribute()
    {
        var markup = Import("<a href=\"https://example.com/a?x=*_#$&amp;q=[1]&quot;z&quot;\">link</a>");

        Assert.Contains(
            "#link(\"https://example.com/a?x=*_#$&q=[1]\\\"z\\\"\")[link]",
            markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Counts_unknown_elements_and_keeps_their_text()
    {
        var result = new TypstHtmlImporter().Import("<p>a <mark>highlighted</mark> b <mark>again</mark></p>");

        Assert.Equal("a highlighted b again\n", result.Document.ToMarkup());
        Assert.Contains(result.DroppedElements, element => element.ElementName == "mark" && element.Count == 2);
        Assert.Equal(2, result.DroppedElementCount);
    }

    [Fact]
    public void Throws_on_an_unknown_element_when_asked()
    {
        var options = new TypstHtmlImportOptions { UnknownElements = TypstHtmlUnknownElementPolicy.Throw };
        var exception = Assert.Throws<TypstHtmlImportException>(() => Import("<section><mark>x</mark></section>", options));

        Assert.Equal("mark", exception.ElementName);
    }

    [Fact]
    public void Skips_the_subtree_of_an_element_that_has_no_prose()
    {
        var result = new TypstHtmlImporter().Import("<p>kept</p><iframe src=\"https://example.com\">ignored</iframe>");

        Assert.Equal("kept\n", result.Document.ToMarkup());
        Assert.Contains(result.DroppedElements, element => element.ElementName == "iframe" && element.Count == 1);
    }

    [Fact]
    public void Ignores_the_head_without_counting_it()
    {
        var result = new TypstHtmlImporter().Import(
            "<html><head><title>T</title><style>p{color:red}</style></head><body><p>body</p></body></html>");

        Assert.Equal("body\n", result.Document.ToMarkup());
        Assert.Empty(result.DroppedElements);
    }

    [Fact]
    public void Ignores_inline_styles_by_default()
    {
        var markup = Import("<p><span style=\"font-weight: bold; font-style: italic\">text</span></p>");

        Assert.Equal("text\n", markup);
    }

    [Fact]
    public void Honours_the_three_supported_inline_style_declarations_when_enabled()
    {
        var options = new TypstHtmlImportOptions { HonourInlineStyles = true };
        var markup = Import(
            "<p><span style=\"font-weight: 700; color: red\">bold</span> " +
            "<span style=\"font-style: italic\">italic</span> " +
            "<span style=\"text-decoration: underline line-through\">both</span></p>",
            options);

        Assert.Contains("*bold*", markup, StringComparison.Ordinal);
        Assert.Contains("_italic_", markup, StringComparison.Ordinal);
        Assert.Contains("#strike[#underline[both]]", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("color", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Keeps_a_space_that_spans_a_styled_span()
    {
        var options = new TypstHtmlImportOptions { HonourInlineStyles = true };
        var markup = Import("<p>a <span>spaced </span>b</p>", options);

        Assert.Equal("a spaced b\n", markup);
    }

    [Fact]
    public void Collapses_whitespace_between_inline_elements()
    {
        Assert.Equal("one two three\n", Import("<p>one\n    two\t\tthree</p>"));
    }

    [Fact]
    public void Preserves_a_non_breaking_space()
    {
        Assert.Equal("a\u00A0b\n", Import("<p>a&nbsp;b</p>"));
    }

    [Fact]
    public void Imports_a_full_document_and_ignores_the_doctype_and_comments()
    {
        var markup = Import(
            "<!DOCTYPE html><!-- a comment --><html><head><title>x</title></head>" +
            "<body><h1>Title</h1><p>Body</p></body></html>");

        Assert.Equal("= Title\n\nBody\n", markup);
    }

    [Fact]
    public void Imports_a_fragment_without_html_or_body()
    {
        Assert.Equal("just text\n", Import("just text"));
    }

    [Fact]
    public void Treats_structural_elements_as_transparent()
    {
        var result = new TypstHtmlImporter().Import("<div><section><p>inside</p></section></div>");

        Assert.Equal("inside\n", result.Document.ToMarkup());
        Assert.Empty(result.DroppedElements);
    }

    [Fact]
    public void FromHtml_matches_the_importer()
    {
        const string html = "<h2>Title</h2><p>Body</p>";

        Assert.Equal(
            new TypstHtmlImporter().Import(html).Document.ToMarkup(),
            TypstDocument.FromHtml(html).ToMarkup());
    }

    [Fact]
    public void FromHtml_rejects_a_null_fragment()
    {
        Assert.Throws<ArgumentNullException>(() => TypstDocument.FromHtml(null!));
    }

    [Fact]
    public void Import_rejects_a_null_fragment()
    {
        Assert.Throws<ArgumentNullException>(() => new TypstHtmlImporter().Import(null!));
    }

    [Fact]
    public void Exposes_its_options()
    {
        var options = new TypstHtmlImportOptions { HonourInlineStyles = true };

        Assert.Same(options, new TypstHtmlImporter(options).Options);
        Assert.NotNull(new TypstHtmlImporter().Options);
    }

    [Fact]
    public void Importing_an_empty_string_yields_an_empty_document()
    {
        Assert.Equal(string.Empty, Import(string.Empty));
    }

    [Fact]
    public void A_list_item_keeps_paragraphs_and_nested_lists_in_order()
    {
        var markup = Import("<ul><li><p>first</p><p>second</p><ul><li>nested</li></ul></li></ul>");

        Assert.Contains("- first\\\nsecond", markup, StringComparison.Ordinal);
        Assert.Contains("  - nested", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_dropped_elements_are_sorted_by_name()
    {
        var result = new TypstHtmlImporter().Import("<p><mark>a</mark> <mark>b</mark> <video></video></p>");

        var names = result.DroppedElements.Select(element => element.ElementName).ToArray();

        Assert.Equal(["mark", "video"], names);
    }

    [Fact]
    public void Reports_the_total_dropped_count()
    {
        var result = new TypstHtmlImporter().Import("<p><mark>a</mark> <mark>b</mark> <video></video></p>");

        Assert.Equal(3, result.DroppedElementCount);
    }

    [Fact]
    public void Does_not_emit_a_paragraph_for_whitespace_only_text()
    {
        Assert.Equal("text\n", Import("<p>   </p><p>text</p>"));
    }

    [Fact]
    public void Ignores_an_empty_heading()
    {
        Assert.Equal(string.Empty, Import("<h2></h2>"));
    }
}
