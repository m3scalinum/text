using System;
using System.Collections.Generic;

namespace M3scalinum.Text.SharpTypst.Documents;

/// <summary>A table built by <see cref="TypstDocument.Table"/>.</summary>
public sealed class TypstTable
{
    private readonly List<TypstLength> _columns = [];
    private readonly List<TypstContent> _header = [];
    private readonly List<IReadOnlyList<TypstContent>> _rows = [];

    internal IReadOnlyList<TypstLength> ColumnWidths => _columns;

    internal IReadOnlyList<TypstContent> Header => _header;

    internal IReadOnlyList<IReadOnlyList<TypstContent>> Rows => _rows;

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
        _header.AddRange(cells);
        return this;
    }

    /// <summary>Adds a body row.</summary>
    public TypstTable Row(params TypstContent[] cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        _rows.Add(cells);
        return this;
    }
}