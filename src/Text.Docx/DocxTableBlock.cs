using System.Collections.Generic;

namespace M3scalinum.Text.Docx;

/// <summary>A table: the columns its grid declares, and its rows.</summary>
public sealed class DocxTableBlock : DocxBlock
{
    internal DocxTableBlock(int columns, IReadOnlyList<DocxTableRow> rows)
    {
        Columns = columns;
        Rows = rows;
    }

    /// <summary>The number of columns the table's grid declares.</summary>
    public int Columns { get; }

    /// <summary>The rows, top to bottom.</summary>
    public IReadOnlyList<DocxTableRow> Rows { get; }
}

/// <summary>A table row.</summary>
/// <remarks>
/// A cell a row above spans into this row is not repeated here, exactly as Typst's <c>table</c>
/// places the cells that follow a row span.
/// </remarks>
public sealed class DocxTableRow
{
    internal DocxTableRow(IReadOnlyList<DocxTableCell> cells) => Cells = cells;

    /// <summary>The cells, left to right.</summary>
    public IReadOnlyList<DocxTableCell> Cells { get; }
}

/// <summary>A table cell.</summary>
public sealed class DocxTableCell
{
    internal DocxTableCell(int colspan, int rowspan, IReadOnlyList<DocxBlock> blocks)
    {
        Colspan = colspan;
        Rowspan = rowspan;
        Blocks = blocks;
    }

    /// <summary>How many columns the cell covers. Word writes it as <c>w:gridSpan</c>.</summary>
    public int Colspan { get; }

    /// <summary>
    /// How many rows the cell covers. Word writes a vertical merge as a cell that restarts it plus
    /// one continuation cell per row below; the continuations are counted here and dropped.
    /// </summary>
    public int Rowspan { get; }

    /// <summary>The cell body. A cell always holds at least one paragraph.</summary>
    public IReadOnlyList<DocxBlock> Blocks { get; }
}
