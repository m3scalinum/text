namespace M3scalinum.Text.Xlsx;

/// <summary>One cell: where it is, what it holds, and how it got there.</summary>
/// <remarks>
/// <see cref="Row"/> and <see cref="Column"/> are zero-based and relative to the used region of the
/// sheet, so the first cell that holds anything is not necessarily at <c>(0, 0)</c> in sheet terms:
/// <see cref="XlsxSheet.FirstRow"/> and <see cref="XlsxSheet.FirstColumn"/> say where the region
/// starts on the sheet.
/// </remarks>
public sealed class XlsxCell
{
    internal XlsxCell(int row, int column, XlsxCellValue value, string? formula)
    {
        Row = row;
        Column = column;
        Value = value;
        Formula = formula;
    }

    /// <summary>Row within the used region.</summary>
    public int Row { get; }

    /// <summary>Column within the used region.</summary>
    public int Column { get; }

    /// <summary>What the cell holds.</summary>
    public XlsxCellValue Value { get; }

    /// <summary>
    /// The formula with its leading <c>=</c>, when the cell holds one; otherwise
    /// <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="Value"/> is the result the workbook cached for the formula. When it cached none,
    /// the value is <see cref="XlsxBlankValue"/> and this property is the only thing the cell says.
    /// </remarks>
    public string? Formula { get; }

    /// <summary>
    /// The cell as a table prints it: <see cref="XlsxCellValue.DisplayText"/>, or the formula when the
    /// cell holds one whose result was never cached.
    /// </summary>
    public string DisplayText => Value is XlsxBlankValue && Formula is not null ? Formula : Value.DisplayText;

    /// <summary>The absolute cell reference, such as <c>B7</c>, that this cell would have in Excel.</summary>
    /// <param name="sheet">The sheet the cell belongs to, for its offsets.</param>
    /// <returns>The reference, in A1 notation.</returns>
    public string Reference(XlsxSheet sheet)
    {
        System.ArgumentNullException.ThrowIfNull(sheet);
        return XlsxSheet.Reference(sheet.FirstRow + Row, sheet.FirstColumn + Column);
    }
}
