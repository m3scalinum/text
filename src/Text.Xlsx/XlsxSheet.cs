using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Xlsx;

/// <summary>One sheet of a workbook: its used region, and the cells that hold something.</summary>
/// <remarks>
/// The readers of <c>M3scalinum.Text.Xlsx</c> produce this type as a whole, so it is immutable once
/// read. A sheet keeps only the cells that hold a value or a formula, which is why
/// <see cref="Cells"/> may be shorter than <see cref="RowCount"/> times
/// <see cref="ColumnCount"/>.
/// </remarks>
public sealed partial class XlsxSheet
{
    private readonly Dictionary<(int Row, int Column), XlsxCell> _byPosition;

    internal XlsxSheet(
        string name,
        int rowCount,
        int columnCount,
        int firstRow,
        int firstColumn,
        IReadOnlyList<XlsxCell> cells)
    {
        Name = name;
        RowCount = rowCount;
        ColumnCount = columnCount;
        FirstRow = firstRow;
        FirstColumn = firstColumn;
        Cells = cells;

        _byPosition = new Dictionary<(int Row, int Column), XlsxCell>(cells.Count);

        foreach (var cell in cells)
        {
            _byPosition[(cell.Row, cell.Column)] = cell;
        }
    }

    /// <summary>The tab name, as the workbook shows it.</summary>
    public string Name { get; }

    /// <summary>Rows in the used region.</summary>
    public int RowCount { get; }

    /// <summary>Columns in the used region.</summary>
    public int ColumnCount { get; }

    /// <summary>
    /// The zero-based sheet row the used region starts at. A sheet whose data begins at <c>C5</c>
    /// reports <c>4</c> here, because Excel counts from one.
    /// </summary>
    public int FirstRow { get; }

    /// <summary>The zero-based sheet column the used region starts at.</summary>
    public int FirstColumn { get; }

    /// <summary>The cells that hold a value or a formula, row-major.</summary>
    public IReadOnlyList<XlsxCell> Cells { get; }

    /// <summary>The cell at a position inside the used region, or <see langword="null"/> when it holds nothing.</summary>
    /// <param name="row">Row within the used region.</param>
    /// <param name="column">Column within the used region.</param>
    /// <returns>The cell, or <see langword="null"/> for an empty one.</returns>
    public XlsxCell? Find(int row, int column)
        => _byPosition.GetValueOrDefault((row, column));

    /// <summary>An <c>A1</c>-style reference for a zero-based sheet row and column.</summary>
    /// <param name="row">Zero-based sheet row.</param>
    /// <param name="column">Zero-based sheet column.</param>
    /// <returns>The reference, such as <c>B7</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Either coordinate is negative.</exception>
    public static string Reference(int row, int column)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfNegative(column);

        var letters = string.Empty;
        var value = column + 1;

        while (value > 0)
        {
            var remainder = (value - 1) % 26;
            letters = (char)('A' + remainder) + letters;
            value = (value - 1) / 26;
        }

        return $"{letters}{row + 1}";
    }
}
