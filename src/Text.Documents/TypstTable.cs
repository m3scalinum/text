using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Documents;

/// <summary>A table built by <see cref="TypstDocument.Table"/>.</summary>
public sealed class TypstTable
{
    private readonly List<TypstLength> _columns = [];
    private readonly List<TypstTableCell> _header = [];
    private readonly List<IReadOnlyList<TypstTableCell>> _rows = [];

    internal IReadOnlyList<TypstLength> ColumnWidths => _columns;

    internal IReadOnlyList<TypstTableCell> Header => _header;

    internal IReadOnlyList<IReadOnlyList<TypstTableCell>> Rows => _rows;

    internal bool HasColumnWidths => _columns.Count > 0;

    /// <summary>
    /// How many columns the table has: as many as <see cref="Columns(TypstLength[])"/> set, or, when
    /// it was never called, as many as the cells need.
    /// </summary>
    /// <remarks>
    /// The cells are laid out as Typst lays them out: left to right, each one after the last, and a
    /// column a cell above still spans into is not filled again. A row the spans above cover
    /// completely therefore holds no cell and adds no column, which is exactly the shape a Word
    /// table with a vertical merge arrives in.
    /// </remarks>
    public int ColumnCount => HasColumnWidths ? _columns.Count : MeasuredColumnCount();

    /// <summary>Sets the column widths.</summary>
    public TypstTable Columns(params TypstLength[] widths)
    {
        ArgumentNullException.ThrowIfNull(widths);
        _columns.Clear();
        _columns.AddRange(widths);
        return this;
    }

    private int MeasuredColumnCount()
    {
        // Per column, the first row the column is free again: a cell with a row span holds its
        // columns for that many rows, so the next row starts after them.
        var freeFromRow = new List<int>();
        var columns = 0;
        var row = 0;

        foreach (var cells in AllRows())
        {
            var column = 0;

            foreach (var cell in cells)
            {
                while (column < freeFromRow.Count && freeFromRow[column] > row)
                {
                    column++;
                }

                var end = column + cell.Colspan;

                while (freeFromRow.Count < end)
                {
                    freeFromRow.Add(0);
                }

                for (var covered = column; covered < end; covered++)
                {
                    freeFromRow[covered] = Math.Max(freeFromRow[covered], row + cell.Rowspan);
                }

                column = end;
                columns = Math.Max(columns, end);
            }

            row++;
        }

        return columns;
    }

    private IEnumerable<IReadOnlyList<TypstTableCell>> AllRows()
    {
        if (_header.Count > 0)
        {
            yield return _header;
        }

        foreach (var row in _rows)
        {
            yield return row;
        }
    }

    /// <summary>
    /// Adds the header row. It is emitted as <c>table.header(…)</c>, which repeats on every page a
    /// table spans and which Typst tags as header cells scoped to their column.
    /// </summary>
    public TypstTable HeaderRow(params TypstContent[] cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        _header.Clear();

        foreach (var cell in cells)
        {
            _header.Add(new TypstTableCell(cell));
        }

        return this;
    }

    /// <summary>
    /// Adds a header row whose cells can span columns or rows, and whose
    /// <see cref="TypstTableCell.HeaderScope"/> marks a corner cell as belonging to both.
    /// </summary>
    public TypstTable HeaderRow(params TypstTableCell[] cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        _header.Clear();
        _header.AddRange(cells);
        return this;
    }

    /// <summary>Adds a body row.</summary>
    public TypstTable Row(params TypstContent[] cells)
    {
        ArgumentNullException.ThrowIfNull(cells);

        var row = new TypstTableCell[cells.Length];

        for (var index = 0; index < cells.Length; index++)
        {
            row[index] = new TypstTableCell(cells[index]);
        }

        _rows.Add(row);
        return this;
    }

    /// <summary>
    /// Adds a row that holds no cell, which is the row a vertical merge above it fills completely.
    /// </summary>
    /// <remarks>
    /// Typst places such a row on its own: the spanning cell occupies it, and the row emits nothing.
    /// The reader hands this shape over for a Word row whose cells were all vertical-merge
    /// continuations.
    /// </remarks>
    public TypstTable Row()
    {
        _rows.Add([]);
        return this;
    }

    /// <summary>Adds a body row whose cells can span columns or rows.</summary>
    public TypstTable Row(params TypstTableCell[] cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        _rows.Add(cells);
        return this;
    }
}
