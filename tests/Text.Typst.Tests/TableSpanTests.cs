using System;
using M3scalinum.Text.Documents;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

/// <summary>How the table builder carries merged cells.</summary>
/// <remarks>
/// A Word table with a merge does not arrive as a rectangle: a cell covers several columns, or
/// several rows, and the rows below a vertical merge hold only the cells that are not covered. The
/// tests pin the markup each of those shapes emits, and the column count the builder measures when
/// no widths are given.
/// </remarks>
public sealed class TableSpanTests
{
    [Fact]
    public void A_cell_spanning_two_columns_emits_one_colspan_argument()
    {
        var markup = TypstDocument.Create()
            .Table(t => t
                .Columns(TypstLength.Fr(1), TypstLength.Fr(1))
                .Row(new TypstTableCell("wide", colspan: 2)))
            .ToMarkup();

        Assert.Contains("table.cell(colspan: 2)[wide]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_cell_spanning_two_rows_emits_one_rowspan_argument()
    {
        var markup = TypstDocument.Create()
            .Table(t => t
                .Columns(TypstLength.Fr(1), TypstLength.Fr(1))
                .Row(new TypstTableCell("tall", rowspan: 2), new TypstTableCell("a"))
                .Row(new TypstTableCell("b")))
            .ToMarkup();

        Assert.Contains("table.cell(rowspan: 2)[tall]", markup, StringComparison.Ordinal);
        Assert.Contains("[a],\n", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_cell_spanning_columns_and_rows_emits_both_arguments_in_one_call()
    {
        var markup = TypstDocument.Create()
            .Table(t => t
                .Columns(TypstLength.Fr(1), TypstLength.Fr(1), TypstLength.Fr(1))
                .Row(new TypstTableCell("block", colspan: 2, rowspan: 2), new TypstTableCell("a"))
                .Row(new TypstTableCell("b")))
            .ToMarkup();

        Assert.Contains(
            "table.cell(colspan: 2, rowspan: 2)[block]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_ragged_grid_places_each_cell_after_the_ones_before_it()
    {
        // Row sums are 3 throughout: 2+1, 1+2, 1+1+1. The middle row starts with the cell the
        // first row's row span holds, so it carries the two cells that follow it.
        var markup = TypstDocument.Create()
            .Table(t => t
                .Columns(TypstLength.Fr(1), TypstLength.Fr(1), TypstLength.Fr(1))
                .Row(new TypstTableCell("left", rowspan: 2), new TypstTableCell("wide", colspan: 2))
                .Row(new TypstTableCell("a"), new TypstTableCell("b"))
                .Row(new TypstTableCell("c"), new TypstTableCell("d"), new TypstTableCell("e")))
            .ToMarkup();

        Assert.Equal(
            "#table(\n" +
            "  columns: (1fr, 1fr, 1fr),\n" +
            "  table.cell(rowspan: 2)[left], table.cell(colspan: 2)[wide],\n" +
            "  [a], [b],\n" +
            "  [c], [d], [e],\n" +
            ")\n",
            markup);
    }

    [Fact]
    public void A_merged_header_cell_keeps_its_scope()
    {
        var markup = TypstDocument.Create()
            .Table(t => t
                .Columns(TypstLength.Fr(1), TypstLength.Fr(1))
                .HeaderRow(
                    new TypstTableCell("both", colspan: 2) { HeaderScope = TypstHeaderScope.Both })
                .Row("a", "b"))
            .ToMarkup();

        Assert.Contains(
            "table.header(pdf.header-cell(table.cell(colspan: 2)[both], scope: \"both\")),",
            markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_row_the_span_above_covers_completely_emits_no_cell_list()
    {
        // Word writes a vertical merge as a restart cell plus one continuation cell per row below;
        // a row whose cells are all continuations is a row the span above already fills.
        var markup = TypstDocument.Create()
            .Table(t => t
                .Columns(TypstLength.Fr(1), TypstLength.Fr(1))
                .Row(new TypstTableCell("tall", rowspan: 3))
                .Row()
                .Row())
            .ToMarkup();

        Assert.Equal(
            "#table(\n" +
            "  columns: (1fr, 1fr),\n" +
            "  table.cell(rowspan: 3)[tall],\n" +
            ")\n",
            markup);
    }

    [Fact]
    public void A_table_without_widths_measures_its_columns_from_the_cells()
    {
        var table = new TypstTable();
        table.Row(new TypstTableCell("wide", colspan: 2), new TypstTableCell("c"));
        table.Row("a", "b", "c");

        Assert.Equal(3, table.ColumnCount);
        Assert.Equal(
            "#table(\n" +
            "  columns: 3,\n" +
            "  table.cell(colspan: 2)[wide], [c],\n" +
            "  [a], [b], [c],\n" +
            ")\n",
            TypstDocument.Create()
                .Table(t => t
                    .Row(new TypstTableCell("wide", colspan: 2), new TypstTableCell("c"))
                    .Row("a", "b", "c"))
                .ToMarkup());
    }

    [Fact]
    public void The_column_count_of_a_row_span_is_measured_where_its_row_starts()
    {
        var table = new TypstTable();
        table.Row(new TypstTableCell("left", rowspan: 2), new TypstTableCell("a"));
        table.Row(new TypstTableCell("b"));

        Assert.Equal(2, table.ColumnCount);
    }

    [Fact]
    public void A_table_with_no_cells_emits_nothing()
    {
        var markup = TypstDocument.Create().Table(t => t.Columns()).ToMarkup();

        Assert.Equal(string.Empty, markup);
    }

    [Fact]
    public void A_regular_table_emits_the_markup_it_always_did()
    {
        var markup = TypstDocument.Create()
            .Table(t => t
                .Columns(TypstLength.Fr(1), TypstLength.Fr(1))
                .HeaderRow("Item", "Amount")
                .Row("Hosting", "1 200 EUR"))
            .ToMarkup();

        Assert.Equal(
            "#table(\n" +
            "  columns: (1fr, 1fr),\n" +
            "  table.header([Item], [Amount]),\n" +
            "  [Hosting], [1 200 EUR],\n" +
            ")\n",
            markup);
    }
}
