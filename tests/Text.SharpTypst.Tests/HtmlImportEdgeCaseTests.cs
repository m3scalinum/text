using System;
using M3scalinum.Text.SharpTypst.Documents;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Tests;

/// <summary>The corners of the HTML importer: tokenizer edges, tree repair and cell spans.</summary>
public sealed class HtmlImportEdgeCaseTests
{
    private static TypstHtmlImportResult Import(string html, TypstHtmlImportOptions? options = null)
        => new TypstHtmlImporter(options).Import(html);

    [Fact]
    public void Lower_cases_tag_and_attribute_names()
    {
        var markup = Import("<P CLASS=lead>text</P><A HREF=\"https://example.com\">link</A>").Document.ToMarkup();

        Assert.Contains("text", markup, StringComparison.Ordinal);
        Assert.Contains("#link(\"https://example.com\")[link]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Accepts_self_closing_void_elements()
    {
        var markup = Import("<p>a<br />b</p><hr /><p>c</p>").Document.ToMarkup();

        Assert.Contains("a\\\nb", markup, StringComparison.Ordinal);
        Assert.Contains("#divider()", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Keeps_the_first_of_a_duplicated_attribute()
    {
        var markup = Import("<a href=\"one\" href=\"two\">x</a>").Document.ToMarkup();

        Assert.Contains("#link(\"one\")[x]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Skips_a_comment_and_keeps_a_cdata_section_as_text()
    {
        var markup = Import("<p>a<!-- hidden -->b<![CDATA[c]]></p>").Document.ToMarkup();

        Assert.Equal("abc\n", markup);
    }

    [Fact]
    public void Treats_a_lone_angle_bracket_as_text()
    {
        Assert.Equal("5 \\< 6\n", Import("<p>5 < 6</p>").Document.ToMarkup());
    }

    [Fact]
    public void Leaves_invalid_numeric_references_verbatim()
    {
        var markup = Import("<p>&#xZZ; &#0; &#xD800;</p>").Document.ToMarkup();

        Assert.Contains("&\\#xZZ; &\\#0; &\\#xD800;", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Named_references_are_case_sensitive()
    {
        Assert.Equal("&AMP;\n", Import("<p>&AMP;</p>").Document.ToMarkup());
    }

    [Fact]
    public void Decodes_a_hexadecimal_reference_outside_the_basic_plane()
    {
        Assert.Equal("\U0001F600\n", Import("<p>&#x1F600;</p>").Document.ToMarkup());
    }

    [Fact]
    public void Only_the_first_thead_row_is_a_typst_header()
    {
        var markup = Import(
            "<table><thead><tr><th>A</th></tr><tr><th>B</th></tr></thead>" +
            "<tbody><tr><td>c</td></tr></tbody><tfoot><tr><td>f</td></tr></tfoot></table>")
            .Document.ToMarkup();

        Assert.Contains("table.header([A])", markup, StringComparison.Ordinal);
        Assert.Contains("[B],\n  [c],\n  [f],", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_description_without_a_term_gets_an_empty_term()
    {
        Assert.Equal("/ : no term\n", Import("<dl><dd>no term</dd></dl>").Document.ToMarkup());
    }

    [Fact]
    public void A_term_without_a_description_emits_nothing()
    {
        Assert.Equal(string.Empty, Import("<dl><dt>only term</dt></dl>").Document.ToMarkup());
    }

    [Fact]
    public void A_table_without_cells_emits_nothing()
    {
        Assert.Equal(string.Empty, Import("<table><tr></tr></table>").Document.ToMarkup());
    }

    [Fact]
    public void A_table_cell_flattens_its_paragraphs_to_a_line_break()
    {
        var markup = Import("<table><tr><td><p>a</p><p>b</p></td></tr></table>").Document.ToMarkup();

        Assert.Contains("a\\\n  b", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_nested_table_in_a_cell_is_flattened_to_its_text()
    {
        var markup = Import("<table><tr><td><table><tr><td>inner</td></tr></table></td></tr></table>")
            .Document.ToMarkup();

        Assert.Contains("[inner]", markup, StringComparison.Ordinal);
        Assert.Equal(1, markup.Split("#table(").Length - 1);
    }

    [Fact]
    public void A_blockquote_keeps_a_list_inside_it()
    {
        var markup = Import("<blockquote><p>intro</p><ul><li>one</li></ul></blockquote>").Document.ToMarkup();

        Assert.Contains("#quote(block: true)[", markup, StringComparison.Ordinal);
        Assert.Contains("- one", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Unwraps_an_unknown_block_element_and_counts_it()
    {
        var result = Import("<custom><p>kept</p></custom>");

        Assert.Equal("kept\n", result.Document.ToMarkup());
        Assert.Contains(result.DroppedElements, element => element.ElementName == "custom" && element.Count == 1);
    }

    [Fact]
    public void Throws_on_an_unknown_block_element_when_asked()
    {
        var options = new TypstHtmlImportOptions { UnknownElements = TypstHtmlUnknownElementPolicy.Throw };

        var exception = Assert.Throws<TypstHtmlImportException>(() => Import("<custom>x</custom>", options));

        Assert.Equal("custom", exception.ElementName);
    }

    [Fact]
    public void Counts_an_image_without_a_source()
    {
        var result = Import("<img alt=\"no source\">");

        Assert.Equal(string.Empty, result.Document.ToMarkup());
        Assert.Contains(result.DroppedElements, element => element.ElementName == "img" && element.Count == 1);
    }

    [Fact]
    public void Rejects_a_resolver_that_returns_white_space()
    {
        var options = new TypstHtmlImportOptions { AssetResolver = _ => "   " };

        Assert.Throws<TypstHtmlImportException>(() => Import("<img src=\"a.png\">", options));
    }

    [Fact]
    public void Reduces_an_inline_image_to_its_alt_text()
    {
        var options = new TypstHtmlImportOptions { AssetResolver = _ => "pixel.png" };

        Assert.Equal("before alt after\n", Import("<p>before <img src=\"a.png\" alt=\"alt\"> after</p>", options).Document.ToMarkup());
    }

    [Fact]
    public void Reads_an_image_inside_a_picture_element()
    {
        var options = new TypstHtmlImportOptions { AssetResolver = _ => "pixel.png" };

        Assert.Contains("image(\"pixel.png\")", Import("<picture><img src=\"a.png\"></picture>", options).Document.ToMarkup(), StringComparison.Ordinal);
    }

    [Fact]
    public void Ignores_a_block_level_style_even_when_inline_styles_are_honoured()
    {
        var options = new TypstHtmlImportOptions { HonourInlineStyles = true };

        Assert.Equal("styled\n", Import("<p style=\"font-weight: bold\">styled</p>", options).Document.ToMarkup());
    }

    [Fact]
    public void A_weight_below_six_hundred_is_not_bold()
    {
        var options = new TypstHtmlImportOptions { HonourInlineStyles = true };

        Assert.Equal("plain\n", Import("<p><span style=\"font-weight: 500\">plain</span></p>", options).Document.ToMarkup());
    }

    [Fact]
    public void A_text_decoration_of_none_is_not_underlined()
    {
        var options = new TypstHtmlImportOptions { HonourInlineStyles = true };

        Assert.Equal("plain\n", Import("<p><span style=\"text-decoration: none\">plain</span></p>", options).Document.ToMarkup());
    }

    [Fact]
    public void Keeps_text_that_sits_directly_in_a_list()
    {
        Assert.Equal("- loose text\n- item\n", Import("<ul>loose text<li>item</li></ul>").Document.ToMarkup());
    }

    [Fact]
    public void Ignores_an_invisible_element_inside_a_list_item()
    {
        var result = Import("<ul><li>one<script>var x = 1;</script></li></ul>");

        Assert.Equal("- one\n", result.Document.ToMarkup());
        Assert.Empty(result.DroppedElements);
    }

    [Fact]
    public void A_horizontal_rule_inside_a_paragraph_ends_the_paragraph()
    {
        var markup = Import("<p>a<hr>b</p>").Document.ToMarkup();

        Assert.Equal("a\n\n#divider()\n\nb\n", markup);
    }

    [Fact]
    public void Renders_table_cells_with_spans_through_the_public_model()
    {
        var markup = TypstDocument.Create()
            .Table(table => table
                .Columns(TypstLength.Fr(1), TypstLength.Fr(1))
                .HeaderRow(new TypstTableCell("Both", colspan: 2))
                .Row(new TypstTableCell("Tall", rowspan: 3), new TypstTableCell("x"))
                .Row("y"))
            .ToMarkup();

        Assert.Contains("table.header(table.cell(colspan: 2)[Both])", markup, StringComparison.Ordinal);
        Assert.Contains("table.cell(rowspan: 3)[Tall], [x],", markup, StringComparison.Ordinal);
        Assert.Contains("[y],", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_cell_span_below_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TypstTableCell("x", colspan: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TypstTableCell("x", rowspan: 0));
    }

    [Fact]
    public void Rejects_a_cell_without_content()
    {
        Assert.Throws<ArgumentNullException>(() => new TypstTableCell(null!));
    }

    [Fact]
    public void Decorations_accept_nested_content()
    {
        var markup = TypstDocument.Create()
            .Paragraph(content => content
                .Superscript(new TypstContent().Bold("2"))
                .Text(" ")
                .Subscript(new TypstContent().Emphasis("n"))
                .Text(" ")
                .Underline(new TypstContent().Text("u"))
                .Text(" ")
                .Strikethrough(new TypstContent().Text("s")))
            .ToMarkup();

        Assert.Contains("#super[*2*]", markup, StringComparison.Ordinal);
        Assert.Contains("#sub[_n_]", markup, StringComparison.Ordinal);
        Assert.Contains("#underline[u]", markup, StringComparison.Ordinal);
        Assert.Contains("#strike[s]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Handles_a_script_that_contains_markup_lookalikes()
    {
        var result = Import("<p>before<script>if (a < b) { document.write('</p>'); }</script>after</p>");

        Assert.Contains("before", result.Document.ToMarkup(), StringComparison.Ordinal);
        Assert.Contains("after", result.Document.ToMarkup(), StringComparison.Ordinal);
        Assert.Empty(result.DroppedElements);
    }

    [Fact]
    public void Reads_an_attribute_value_that_contains_an_angle_bracket_in_quotes()
    {
        var markup = Import("<p title=\"a > b\">text</p>").Document.ToMarkup();

        Assert.Equal("text\n", markup);
    }

    [Fact]
    public void Accepts_a_body_element_as_the_root()
    {
        Assert.Equal("text\n", Import("<body><p>text</p></body>").Document.ToMarkup());
    }

    [Fact]
    public void Accepts_an_html_element_without_a_body()
    {
        Assert.Equal("text\n", Import("<html><p>text</p></html>").Document.ToMarkup());
    }

    [Fact]
    public void A_sublist_directly_inside_a_list_hangs_off_the_last_item()
    {
        Assert.Equal("- first\n  - second\n", Import("<ul><li>first</li><ul><li>second</li></ul></ul>").Document.ToMarkup());
    }

    [Fact]
    public void A_blockquote_inside_a_list_item_becomes_a_child_block()
    {
        var markup = Import("<ul><li>outer<blockquote><p>quoted</p></blockquote></li></ul>").Document.ToMarkup();

        Assert.Contains("- outer", markup, StringComparison.Ordinal);
        Assert.Contains("#quote(block: true)[", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Counts_colgroup_as_dropped()
    {
        var result = Import("<table><colgroup><col span=\"2\"></colgroup><tr><td>a</td></tr></table>");

        Assert.Contains(result.DroppedElements, element => element.ElementName == "colgroup" && element.Count == 1);
        Assert.DoesNotContain(result.DroppedElements, element => element.ElementName == "col");
    }

    [Fact]
    public void An_invalid_colspan_falls_back_to_one_column()
    {
        var markup = Import("<table><tr><td colspan=\"0\">a</td><td colspan=\"abc\">b</td></tr></table>")
            .Document.ToMarkup();

        Assert.Contains("columns: (1fr, 1fr)", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("table.cell", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Reads_the_lang_prefix_for_a_code_language()
    {
        Assert.Contains("```rust", Import("<pre><code class=\"lang-rust\">fn main() {}</code></pre>").Document.ToMarkup(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_term_may_contain_inline_markup()
    {
        Assert.Equal("/ A term: definition\n", Import("<dl><dt>A <strong>term</strong></dt><dd>definition</dd></dl>").Document.ToMarkup());
    }

    [Fact]
    public void Honours_bolder_and_oblique()
    {
        var options = new TypstHtmlImportOptions { HonourInlineStyles = true };
        var markup = Import(
            "<p><span style=\"font-weight: bolder\">b</span> <span style=\"font-style: oblique\">o</span></p>",
            options).Document.ToMarkup();

        Assert.Contains("*b*", markup, StringComparison.Ordinal);
        Assert.Contains("_o_", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Counts_an_unknown_element_inside_a_table_cell()
    {
        var result = Import("<table><tr><td><mark>a</mark></td></tr></table>");

        Assert.Contains(result.DroppedElements, element => element.ElementName == "mark");
        Assert.Contains("a", result.Document.ToMarkup(), StringComparison.Ordinal);
    }

    [Fact]
    public void Ignores_an_invisible_element_inside_a_table_row_without_counting_it()
    {
        var result = Import("<table><tr><td>a</td><script>x()</script></tr></table>");

        Assert.Empty(result.DroppedElements);
    }

    [Fact]
    public void A_self_closing_div_does_not_swallow_what_follows()
    {
        Assert.Equal("before\n\nafter\n", Import("<div/>before<p>after</p>").Document.ToMarkup());
    }

    [Fact]
    public void Grows_the_code_fence_past_a_backtick_run()
    {
        var markup = Import("<pre><code>a ``` b</code></pre>").Document.ToMarkup();

        Assert.Contains("````\na ``` b\n````", markup, StringComparison.Ordinal);
    }
}
