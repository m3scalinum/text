using System;
using System.Collections.Generic;
using System.Globalization;

namespace M3scalinum.Text.Xlsx.Internal;

/// <summary>Turns the native AST into the public tree.</summary>
internal static class XlsxModel
{
    internal static XlsxWorkbook Build(XlsxWorkbookDto workbook)
    {
        var sheets = new List<XlsxSheet>(workbook.Sheets.Count);

        foreach (var sheet in workbook.Sheets)
        {
            sheets.Add(Sheet(sheet));
        }

        return new XlsxWorkbook(sheets);
    }

    private static XlsxSheet Sheet(XlsxSheetDto sheet)
    {
        var cells = new List<XlsxCell>(sheet.Cells.Count);

        foreach (var cell in sheet.Cells)
        {
            cells.Add(new XlsxCell(cell.Row, cell.Column, Value(cell), cell.Formula));
        }

        return new XlsxSheet(
            sheet.Name, sheet.Rows, sheet.Columns, sheet.RowOffset, sheet.ColumnOffset, cells);
    }

    /// <summary>
    /// One cell's value. A type this build does not know is a reader that is newer than this package,
    /// and the cell is reported as blank rather than failing the whole workbook on it.
    /// </summary>
    private static XlsxCellValue Value(XlsxCellDto cell) => cell.Type switch
    {
        "text" => new XlsxTextValue(cell.Text ?? string.Empty),
        "number" => new XlsxNumberValue(cell.Number ?? 0),
        "boolean" => new XlsxBooleanValue(cell.Value ?? false),
        "date" => Date(cell.Iso, cell.DateOnly),
        "duration" => new XlsxDurationValue(TimeSpan.FromSeconds(cell.Seconds ?? 0)),
        "error" => new XlsxErrorValue(cell.Error ?? "#N/A"),
        _ => XlsxBlankValue.Instance,
    };

    /// <summary>
    /// A date from the ISO 8601 text the native side writes. The text is produced by this suite, so a
    /// value it cannot parse is a broken contract; the cell keeps the text rather than throwing.
    /// </summary>
    private static XlsxCellValue Date(string? iso, bool dateOnly)
    {
        if (iso is not null
            && DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
        {
            return new XlsxDateValue(value, dateOnly);
        }

        return new XlsxTextValue(iso ?? string.Empty);
    }
}
