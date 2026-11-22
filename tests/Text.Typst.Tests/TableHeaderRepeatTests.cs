using System;
using M3scalinum.Text.Documents;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

/// <summary>How a table crosses a page: the repeated header, and the row that must not be cut.</summary>
/// <remarks>
/// Typst repeats a <c>table.header</c> on every page a table spans and keeps a row whole when one of
/// its cells is <c>breakable: false</c>. The tests pin the markup that turns each of those on, and
/// the markup a table that asks for neither still emits.
/// </remarks>
public sealed class TableHeaderRepeatTests
{
    [Fact]
    public void A_header_row_repeats_on_every_page_by_default()
    {
        var markup = TypstDocument.Create()
            .Table(t => t
                .Columns(TypstLength.Fr(1), TypstLength.Fr(1))
                .HeaderRow("Item", "Amount")
                .Row("Hosting", "1 200 EUR"))
            .ToMarkup();

        Assert.Contains("table.header(repeat: true, [Item], [Amount]),", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_header_row_can_be_kept_on_the_first_page()
    {
        var markup = TypstDocument.Create()
            .Table(t => t
                .Columns(TypstLength.Fr(1), TypstLength.Fr(1))
                .RepeatHeader(repeat: false)
                .HeaderRow("Item", "Amount")
                .Row("Hosting", "1 200 EUR"))
            .ToMarkup();

        Assert.Contains("table.header(repeat: false, [Item], [Amount]),", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Repeating_a_header_leaves_its_corner_cell_scoped()
    {
        var markup = TypstDocument.Create()
            .Table(t => t
                .Columns(TypstLength.Fr(1), TypstLength.Fr(1))
                .HeaderRow(new TypstTableCell("Quarter") { HeaderScope = TypstHeaderScope.Both }, new TypstTableCell("Revenue"))
                .Row("Q1", "1 200"))
            .ToMarkup();

        Assert.Contains(
            "table.header(repeat: true, pdf.header-cell(scope: \"both\")[Quarter], [Revenue]),",
            markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Repeating_a_header_leaves_a_merged_cell_scoped()
    {
        var markup = TypstDocument.Create()
            .Table(t => t
                .Columns(TypstLength.Fr(1), TypstLength.Fr(1))
                .HeaderRow(
                    new TypstTableCell("both", colspan: 2) { HeaderScope = TypstHeaderScope.Both })
                .Row("a", "b"))
            .ToMarkup();

        Assert.Contains(
            "table.header(repeat: true, pdf.header-cell(table.cell(colspan: 2)[both], scope: \"both\")),",
            markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_table_with_no_header_emits_no_header_and_ignores_the_repeat_setting()
    {
        var markup = TypstDocument.Create()
            .Table(t => t
                .Columns(TypstLength.Fr(1), TypstLength.Fr(1))
                .RepeatHeader()
                .Row("Hosting", "1 200 EUR"))
            .ToMarkup();

        Assert.DoesNotContain("table.header", markup, StringComparison.Ordinal);
        Assert.Equal(
            "#table(\n" +
            "  columns: (1fr, 1fr),\n" +
            "  [Hosting], [1 200 EUR],\n" +
            ")\n",
            markup);
    }

    [Fact]
    public void A_table_that_fits_on_one_page_keeps_the_markup_it_always_had()
    {
        var markup = TypstDocument.Create()
            .Table(t => t
                .Columns(TypstLength.Fr(2), TypstLength.Fr(1))
                .Row(new TypstTableCell("wide", colspan: 2))
                .Row("a", "b"))
            .ToMarkup();

        Assert.Equal(
            "#table(\n" +
            "  columns: (2fr, 1fr),\n" +
            "  table.cell(colspan: 2)[wide],\n" +
            "  [a], [b],\n" +
            ")\n",
            markup);
    }

    [Fact]
    public void A_cell_that_must_not_be_cut_emits_breakable_false()
    {
        var markup = TypstDocument.Create()
            .Table(t => t
                .Columns(TypstLength.Fr(1), TypstLength.Fr(1))
                .Row(
                    new TypstTableCell("A row that must stay in one piece") { Breakable = false },
                    new TypstTableCell("1 200 EUR") { Breakable = false }))
            .ToMarkup();

        Assert.Contains(
            "table.cell(breakable: false)[A row that must stay in one piece], " +
            "table.cell(breakable: false)[1 200 EUR],",
            markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_cell_can_allow_the_split_with_breakable_true()
    {
        var markup = TypstDocument.Create()
            .Table(t => t
                .Columns(TypstLength.Fr(1))
                .Row(new TypstTableCell("A long narrative cell") { Breakable = true }))
            .ToMarkup();

        Assert.Contains("table.cell(breakable: true)[A long narrative cell]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_cell_that_leaves_breakability_alone_emits_no_breakable_argument()
    {
        var markup = TypstDocument.Create()
            .Table(t => t
                .Columns(TypstLength.Fr(1), TypstLength.Fr(1))
                .Row("Hosting", "1 200 EUR"))
            .ToMarkup();

        Assert.DoesNotContain("breakable", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_span_and_a_breakability_share_one_cell_call()
    {
        var markup = TypstDocument.Create()
            .Table(t => t
                .Columns(TypstLength.Fr(1), TypstLength.Fr(1))
                .Row(new TypstTableCell("kept whole", colspan: 2) { Breakable = false }))
            .ToMarkup();

        Assert.Contains("table.cell(colspan: 2, breakable: false)[kept whole]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_header_cell_can_be_kept_whole_beside_its_scope()
    {
        var markup = TypstDocument.Create()
            .Table(t => t
                .Columns(TypstLength.Fr(1), TypstLength.Fr(1))
                .HeaderRow(
                    new TypstTableCell("Item") { HeaderScope = TypstHeaderScope.Column, Breakable = false },
                    new TypstTableCell("Amount") { Breakable = false })
                .Row("Hosting", "1 200 EUR"))
            .ToMarkup();

        Assert.Contains(
            "table.header(repeat: true, " +
            "pdf.header-cell(table.cell(breakable: false)[Item], scope: \"column\"), " +
            "table.cell(breakable: false)[Amount]),",
            markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_repeat_setting_can_be_turned_back_on_after_it_was_turned_off()
    {
        var markup = TypstDocument.Create()
            .Table(t => t
                .Columns(TypstLength.Fr(1))
                .RepeatHeader(repeat: false)
                .RepeatHeader()
                .HeaderRow("Item")
                .Row("Hosting"))
            .ToMarkup();

        Assert.Contains("table.header(repeat: true, [Item]),", markup, StringComparison.Ordinal);
    }
}
