using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Xlsx;

/// <summary>A workbook, read into typed sheets.</summary>
/// <remarks>
/// The sheets are in the order the workbook declares them, which is the order the tabs appear in.
/// Everything is decided from the package: no Excel, no Open XML SDK, no network.
/// </remarks>
public sealed partial class XlsxWorkbook
{
    internal XlsxWorkbook(IReadOnlyList<XlsxSheet> sheets) => Sheets = sheets;

    /// <summary>The sheets, in workbook order.</summary>
    public IReadOnlyList<XlsxSheet> Sheets { get; }

    /// <summary>The sheet with the given tab name, or <see langword="null"/> when the workbook has none.</summary>
    /// <param name="name">The tab name, compared ordinally.</param>
    /// <returns>The sheet, or <see langword="null"/>.</returns>
    public XlsxSheet? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        foreach (var sheet in Sheets)
        {
            if (string.Equals(sheet.Name, name, StringComparison.Ordinal))
            {
                return sheet;
            }
        }

        return null;
    }
}
