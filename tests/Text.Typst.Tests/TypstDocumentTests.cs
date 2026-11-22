using System;
using M3scalinum.Text.Documents;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

public sealed class TypstDocumentTests
{
    [Fact]
    public void Emits_metadata_and_page_setup()
    {
        var markup = TypstDocument.Create()
            .Metadata(m => m.Title("Q3 report").Author("Finance").Keywords("q3", "revenue").Language("en"))
            .Page(p => p.Paper("a4").Margin(TypstLength.Cm(2)).Numbering("1 / 1"))
            .Heading(1, "Revenue")
            .ToMarkup();

        Assert.Contains("#set document(title: \"Q3 report\", author: \"Finance\", keywords: (\"q3\", \"revenue\"))", markup, StringComparison.Ordinal);
        Assert.Contains("#set page(paper: \"a4\", margin: 2cm, numbering: \"1 / 1\")", markup, StringComparison.Ordinal);
        Assert.Contains("#set text(lang: \"en\")", markup, StringComparison.Ordinal);
        Assert.Contains("= Revenue", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_headings_at_the_requested_level()
    {
        var markup = TypstDocument.Create().Heading(3, "Deep").ToMarkup();

        Assert.Contains("=== Deep", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_heading_level_outside_one_to_six()
    {
        var document = TypstDocument.Create();

        Assert.Throws<ArgumentOutOfRangeException>(() => document.Heading(0, "x"));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.Heading(7, "x"));
    }

    [Fact]
    public void Emits_lists()
    {
        var markup = TypstDocument.Create()
            .BulletList("first", "second")
            .NumberedList("one", "two")
            .ToMarkup();

        Assert.Contains("- first", markup, StringComparison.Ordinal);
        Assert.Contains("- second", markup, StringComparison.Ordinal);
        Assert.Contains("+ one", markup, StringComparison.Ordinal);
        Assert.Contains("+ two", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_a_table_with_a_header()
    {
        var markup = TypstDocument.Create()
            .Table(t => t
                .Columns(TypstLength.Fr(2), TypstLength.Fr(1))
                .HeaderRow("Item", "Amount")
                .Row("Hosting", "1 200")
                .Row("Support", "800"))
            .ToMarkup();

        Assert.Contains("#table(", markup, StringComparison.Ordinal);
        Assert.Contains("columns: (2fr, 1fr),", markup, StringComparison.Ordinal);
        Assert.Contains("table.header(repeat: true, [Item], [Amount]),", markup, StringComparison.Ordinal);
        Assert.Contains("[Hosting], [1 200],", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_a_figure_with_a_caption_and_a_label()
    {
        var markup = TypstDocument.Create()
            .Figure(f => f
                .Image("charts/revenue.png", TypstLength.Percent(80))
                .WithCaption("Revenue by month")
                .WithLabel("fig:revenue"))
            .ToMarkup();

        Assert.Contains("image(\"charts/revenue.png\", width: 80%)", markup, StringComparison.Ordinal);
        Assert.Contains("caption: [Revenue by month]", markup, StringComparison.Ordinal);
        Assert.Contains(") <fig:revenue>", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_a_code_block_with_its_language()
    {
        var markup = TypstDocument.Create().Code("rust", "fn main() {}").ToMarkup();

        Assert.Contains("```rust\nfn main() {}\n```", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_inline_emphasis_links_and_math()
    {
        var markup = TypstDocument.Create()
            .Paragraph(c => c
                .Text("Total: ")
                .Bold("1 234")
                .Text(" see ")
                .Link("https://example.com", "the site")
                .Text(" and ")
                .Math("x^2"))
            .ToMarkup();

        Assert.Contains("Total: *1 234* see #link(\"https://example.com\")[the site] and $x^2$", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_a_display_equation()
    {
        var markup = TypstDocument.Create().Math("sum_(k=1)^n k").ToMarkup();

        Assert.Contains("$ sum_(k=1)^n k $", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_layout_wrappers()
    {
        var markup = TypstDocument.Create()
            .Align(TypstAlignment.Center, "centred")
            .Columns(2, "two columns")
            .VSpace(TypstLength.Em(1))
            .ToMarkup();

        Assert.Contains("#align(center)[centred]", markup, StringComparison.Ordinal);
        Assert.Contains("#columns(2)[two columns]", markup, StringComparison.Ordinal);
        Assert.Contains("#v(1em)", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_raw_markup_untouched()
    {
        var markup = TypstDocument.Create().Raw("#outline()").ToMarkup();

        Assert.Contains("#outline()", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_document_emits_nothing()
    {
        Assert.Equal(string.Empty, TypstDocument.Create().ToMarkup());
    }
}