using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Documents;

/// <summary>A table built by <see cref="TypstDocument.Table"/>.</summary>
public sealed class TypstTable
{
    private readonly List<TypstLength> _columns = [];
    private readonly List<TypstTableCell> _header = [];
    private readonly List<IReadOnlyList<TypstTableCell>> _rows = [];
    private bool _repeatsHeader = true;

    internal IReadOnlyList<TypstLength> ColumnWidths => _columns;

    internal IReadOnlyList<TypstTableCell> Header => _header;

    internal IReadOnlyList<IReadOnlyList<TypstTableCell>> Rows => _rows;

    internal bool HasColumnWidths => _columns.Count > 0;

    /// <summary>Whether the header row repeats on every page the table spans; true unless turned off.</summary>
    internal bool RepeatsHeader => _repeatsHeader;

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
    /// Adds the header row. It is emitted as <c>table.header(repeat: true, …)</c>, which repeats on
    /// every page a table spans and which Typst tags as header cells scoped to their column.
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

    /// <summary>
    /// Decides whether the header row is repeated on every page the table spans, emitted as
    /// <c>table.header(repeat: …)</c>. It repeats by default, which is what a table crossing a page
    /// break must do for a reader to know what a column holds.
    /// </summary>
    /// <param name="repeat">
    /// <see langword="true"/> to repeat the header; <see langword="false"/> to draw it on the first
    /// page only.
    /// </param>
    /// <remarks>
    /// A table with no header row ignores the setting: there is nothing to repeat. Repetition does
    /// not cost a test its accessibility either way — the header stays a <c>table.header</c>, so
    /// Typst keeps tagging its cells as column headers, including any
    /// <c>pdf.header-cell(scope: …)</c> inside it.
    /// </remarks>
    public TypstTable RepeatHeader(bool repeat = true)
    {
        _repeatsHeader = repeat;
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

    /// <summary>The label the table carries, null when it has none.</summary>
    internal string? LabelName { get; private set; }

    /// <summary>The caption the numbered figure around the table carries, null when it has none.</summary>
    internal TypstContent? Caption { get; private set; }

    /// <summary>
    /// Numbers the table and makes it referenceable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Typst counts tables only when they are figures, so a labelled table is emitted as
    /// <c>#figure(kind: table, supplement: [Table])[#table(…)] &lt;label&gt;</c> and a
    /// <c>#ref(&lt;label&gt;)</c> to it prints <c>Table 3</c>. An unlabelled table keeps emitting a
    /// bare <c>#table(…)</c>.
    /// </para>
    /// <para>
    /// The name is emitted as given, except when the document already holds that label: the writer
    /// then appends <c>-2</c>, <c>-3</c>, … so the document never names one label twice.
    /// </para>
    /// </remarks>
    public TypstTable WithLabel(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        LabelName = label;
        return this;
    }

    /// <summary>Sets the caption of the numbered figure a labelled table is emitted as.</summary>
    /// <remarks>
    /// Ignored while the table has no label, because an unlabelled table is not a figure. The
    /// caption is plain content: build it with <see cref="TypstContent"/> like any other.
    /// </remarks>
    public TypstTable WithCaption(TypstContent caption)
    {
        ArgumentNullException.ThrowIfNull(caption);
        Caption = caption;
        return this;
    }
}
