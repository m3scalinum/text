using System;
using System.Collections.Generic;

namespace M3scalinum.Text.SharpTypst.Documents;

/// <summary>A table built by <see cref="TypstDocument.Table"/>.</summary>
public sealed class TypstTable
{
    private readonly List<TypstLength> _columns = [];
    private readonly List<TypstTableCell> _header = [];
    private readonly List<IReadOnlyList<TypstTableCell>> _rows = [];

    internal IReadOnlyList<TypstLength> ColumnWidths => _columns;

    internal IReadOnlyList<TypstTableCell> Header => _header;

    internal IReadOnlyList<IReadOnlyList<TypstTableCell>> Rows => _rows;

    /// <summary>Sets the column widths.</summary>
    public TypstTable Columns(params TypstLength[] widths)
    {
        ArgumentNullException.ThrowIfNull(widths);
        _columns.Clear();
        _columns.AddRange(widths);
        return this;
    }

    /// <summary>Adds the header row.</summary>
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

    /// <summary>Adds a header row whose cells can span columns or rows.</summary>
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

    /// <summary>Adds a body row whose cells can span columns or rows.</summary>
    public TypstTable Row(params TypstTableCell[] cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        _rows.Add(cells);
        return this;
    }
}
