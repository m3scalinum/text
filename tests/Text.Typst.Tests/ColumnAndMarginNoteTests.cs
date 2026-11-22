using System;
using M3scalinum.Text.Documents;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

/// <summary>Document-wide columns and margin notes: the markup they emit.</summary>
/// <remarks>
/// These tests read the markup, not the layout: whether the first column really fills before the
/// second, and whether a note really lands level with its marker, is decided by the compiler and is
/// answered in <c>Text.Typst.Integration.Tests</c>.
/// </remarks>
public sealed class ColumnAndMarginNoteTests
{
    [Fact]
    public void A_two_column_page_asks_for_columns_and_their_gutter()
    {
        var markup = TypstDocument.Create()
            .Page(page => page.Paper("a4").Columns(2, TypstLength.Em(1)))
            .Paragraph("Body")
            .ToMarkup();

        Assert.Contains("columns: 2", markup, StringComparison.Ordinal);
        Assert.Contains("#set columns(gutter: 1em)", markup, StringComparison.Ordinal);
        Assert.True(
            markup.IndexOf("columns: 2", StringComparison.Ordinal)
                < markup.IndexOf("#set columns(gutter: 1em)", StringComparison.Ordinal),
            "the page rule comes before the gutter rule:\n" + markup);
    }

    [Fact]
    public void A_column_count_without_a_gutter_leaves_the_gutter_to_typst()
    {
        var markup = TypstDocument.Create()
            .Page(page => page.Columns(3))
            .Paragraph("Body")
            .ToMarkup();

        Assert.Contains("columns: 3", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("#set columns(", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_prepared_column_layout_can_be_applied_to_the_page()
    {
        var layout = new TypstColumnLayout(2, TypstLength.Mm(6));

        var markup = TypstDocument.Create()
            .Page(page => page.Columns(layout))
            .Paragraph("Body")
            .ToMarkup();

        Assert.Contains("#set columns(gutter: 6mm)", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_flow_level_columns_block_writes_its_gutter_argument()
    {
        var markup = TypstDocument.Create()
            .Columns(new TypstColumnLayout(2, TypstLength.Pt(8)), "left")
            .ToMarkup();

        Assert.Contains("#columns(2, gutter: 8pt)[left]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_flow_level_columns_block_without_a_gutter_writes_only_the_count()
    {
        var markup = TypstDocument.Create()
            .Columns(new TypstColumnLayout(2), "left")
            .ToMarkup();

        Assert.Contains("#columns(2)[left]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_without_columns_writes_no_column_setting()
    {
        var markup = TypstDocument.Create()
            .Page(page => page.Paper("a4").Margin(TypstLength.Cm(2)))
            .Paragraph("Body")
            .ToMarkup();

        Assert.DoesNotContain("columns", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_plain_paragraph_keeps_the_markup_it_had()
    {
        var markup = TypstDocument.Create().Paragraph("x").ToMarkup();

        Assert.Equal("x\n", markup);
    }

    [Fact]
    public void A_column_layout_rejects_a_non_positive_count()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TypstColumnLayout(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => TypstDocument.Create().Page(page => page.Columns(-1)));
    }

    [Fact]
    public void A_margin_note_places_its_text_then_prints_its_marker()
    {
        var markup = TypstDocument.Create()
            .MarginNote("1", "A note.", TypstLength.Cm(15.5), TypstLength.Cm(4))
            .ToMarkup();

        Assert.Contains(
            "#box(place(start, dx: 15.5cm)[#block(width: 4cm)[A note.]])#super[1]",
            markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_margin_note_without_a_width_is_one_line()
    {
        var markup = TypstDocument.Create()
            .MarginNote("2", "One line.", TypstLength.Cm(15.5))
            .ToMarkup();

        Assert.Contains("#box(place(start, dx: 15.5cm)[One line.])#super[2]", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("#block(width:", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_margin_notes_keep_the_document_order()
    {
        var markup = TypstDocument.Create()
            .MarginNote("1", "First.", TypstLength.Cm(15.5), TypstLength.Cm(4))
            .Paragraph("Between.")
            .MarginNote("2", "Second.", TypstLength.Cm(15.5), TypstLength.Cm(4))
            .ToMarkup();

        var first = markup.IndexOf("#super[1]", StringComparison.Ordinal);
        var between = markup.IndexOf("Between.", StringComparison.Ordinal);
        var second = markup.IndexOf("#super[2]", StringComparison.Ordinal);

        Assert.True(first >= 0 && between > first && second > between, markup);
    }

    [Fact]
    public void A_margin_note_escapes_its_marker()
    {
        var markup = TypstDocument.Create()
            .MarginNote("*", "Starred.", TypstLength.Cm(15.5))
            .ToMarkup();

        Assert.Contains("#super[\\*]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_margin_note_rejects_a_blank_marker()
    {
        Assert.Throws<ArgumentException>(() => new TypstMarginNote("", "Note.", TypstLength.Cm(15)));
        Assert.Throws<ArgumentNullException>(() => new TypstMarginNote("1", null!, TypstLength.Cm(15)));
    }
}
