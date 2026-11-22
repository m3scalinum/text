using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace M3scalinum.Text.Xlsx.Tests;

/// <summary>One cell of a workbook the tests write.</summary>
internal abstract record TestCell(int Row, int Column);

/// <summary>A string, through the shared string table.</summary>
internal sealed record TestText(int Row, int Column, string Text) : TestCell(Row, Column);

/// <summary>A number.</summary>
internal sealed record TestNumber(int Row, int Column, double Number) : TestCell(Row, Column);

/// <summary>A boolean.</summary>
internal sealed record TestBoolean(int Row, int Column, bool Value) : TestCell(Row, Column);

/// <summary>A serial date, formatted as a date.</summary>
internal sealed record TestDate(int Row, int Column, double Serial) : TestCell(Row, Column);

/// <summary>A serial duration, in days, formatted as <c>[h]:mm:ss</c>.</summary>
internal sealed record TestDuration(int Row, int Column, double Days) : TestCell(Row, Column);

/// <summary>An error value.</summary>
internal sealed record TestError(int Row, int Column, string Error) : TestCell(Row, Column);

/// <summary>A formula and, when the workbook computed it, its cached result.</summary>
internal sealed record TestFormula(int Row, int Column, string Text, double? Cached)
    : TestCell(Row, Column);

/// <summary>
/// A minimal but real <c>.xlsx</c> writer: the parts Excel writes, in the layout
/// <c>calamine</c> requires, assembled in memory.
/// </summary>
internal sealed class XlsxFixture
{
    private readonly List<(string Name, List<TestCell> Cells)> _sheets = [];

    /// <summary>Every text cell of every sheet, in sheet order: the shared string table.</summary>
    private readonly List<string> _strings = [];

    /// <summary>A workbook with one sheet.</summary>
    internal static byte[] Build(string sheet, params TestCell[] cells)
        => new XlsxFixture().Sheet(sheet, cells).Build();

    /// <summary>A workbook with no sheet at all.</summary>
    internal static byte[] Empty() => new XlsxFixture().Build();

    /// <summary>Adds a sheet with the given cells.</summary>
    internal XlsxFixture Sheet(string name, params TestCell[] cells)
    {
        _sheets.Add((name, [.. cells]));
        return this;
    }

    /// <summary>Adds a sheet with no cell at all.</summary>
    internal XlsxFixture EmptySheet(string name)
    {
        _sheets.Add((name, []));
        return this;
    }

    /// <summary>Writes the package.</summary>
    internal byte[] Build()
    {
        // The shared string table is written before the sheets, so it is filled from the cells here
        // rather than as a side effect of writing them.
        foreach (var (_, cells) in _sheets)
        {
            foreach (var cell in cells)
            {
                if (cell is TestText text && !_strings.Contains(text.Text))
                {
                    _strings.Add(text.Text);
                }
            }
        }

        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "[Content_Types].xml", ContentTypes());
            Write(archive, "_rels/.rels", PackageRelationships());
            Write(archive, "xl/workbook.xml", WorkbookXml());
            Write(archive, "xl/_rels/workbook.xml.rels", WorkbookRelationships());
            Write(archive, "xl/styles.xml", Styles());
            Write(archive, "xl/sharedStrings.xml", SharedStrings());

            for (var index = 0; index < _sheets.Count; index++)
            {
                Write(
                    archive,
                    $"xl/worksheets/sheet{index + 1}.xml",
                    SheetXml(_sheets[index].Cells));
            }
        }

        return buffer.ToArray();
    }

    private static void Write(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);

        using var stream = entry.Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
    }

    private string ContentTypes()
    {
        var worksheets = new StringBuilder();

        for (var index = 1; index <= _sheets.Count; index++)
        {
            worksheets.Append(
                $"<Override PartName=\"/xl/worksheets/sheet{index}.xml\" " +
                "ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
        }

        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
            + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
            + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
            + "<Override PartName=\"/xl/workbook.xml\" "
            + "ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>"
            + "<Override PartName=\"/xl/styles.xml\" "
            + "ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>"
            + "<Override PartName=\"/xl/sharedStrings.xml\" "
            + "ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/>"
            + worksheets
            + "</Types>";
    }

    private static string PackageRelationships()
        => "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
            + "<Relationship Id=\"rId1\" "
            + "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" "
            + "Target=\"xl/workbook.xml\"/>"
            + "</Relationships>";

    private string WorkbookXml()
    {
        var sheets = new StringBuilder();

        for (var index = 0; index < _sheets.Count; index++)
        {
            sheets.Append(
                $"<sheet name=\"{Escape(_sheets[index].Name)}\" sheetId=\"{index + 1}\" r:id=\"rId{index + 1}\"/>");
        }

        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            + "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" "
            + "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">"
            + "<sheets>" + sheets + "</sheets></workbook>";
    }

    private string WorkbookRelationships()
    {
        var relationships = new StringBuilder();

        for (var index = 1; index <= _sheets.Count; index++)
        {
            relationships.Append(
                $"<Relationship Id=\"rId{index}\" "
                + "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" "
                + $"Target=\"worksheets/sheet{index}.xml\"/>");
        }

        relationships.Append(
            $"<Relationship Id=\"rId{_sheets.Count + 1}\" "
            + "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" "
            + "Target=\"styles.xml\"/>");

        relationships.Append(
            $"<Relationship Id=\"rId{_sheets.Count + 2}\" "
            + "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings\" "
            + "Target=\"sharedStrings.xml\"/>");

        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
            + relationships
            + "</Relationships>";
    }

    /// <summary>
    /// Three cell formats: the default, a date (<c>numFmtId</c> 14, the built-in <c>mm-dd-yy</c>) and
    /// one custom duration format, which is how <c>calamine</c> tells an elapsed time from a
    /// timestamp.
    /// </summary>
    private static string Styles()
        => "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            + "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
            + "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"[h]:mm:ss\"/></numFmts>"
            + "<fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>"
            + "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill>"
            + "<fill><patternFill patternType=\"gray125\"/></fill></fills>"
            + "<borders count=\"1\"><border/></borders>"
            + "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>"
            + "<cellXfs count=\"3\">"
            + "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>"
            + "<xf numFmtId=\"14\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>"
            + "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>"
            + "</cellXfs></styleSheet>";

    private string SharedStrings()
    {
        var items = new StringBuilder();

        foreach (var text in _strings)
        {
            items.Append($"<si><t xml:space=\"preserve\">{Escape(text)}</t></si>");
        }

        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            + "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" "
            + $"count=\"{_strings.Count}\" uniqueCount=\"{_strings.Count}\">{items}</sst>";
    }

    private string SheetXml(List<TestCell> cells)
    {
        cells.Sort((left, right) => left.Row != right.Row
            ? left.Row.CompareTo(right.Row)
            : left.Column.CompareTo(right.Column));

        var dimension = Dimension(cells);
        var rows = new StringBuilder();
        var currentRow = cells.Count > 0 ? cells[0].Row : -1;
        var row = new StringBuilder();

        foreach (var cell in cells)
        {
            if (cell.Row != currentRow)
            {
                if (currentRow >= 0)
                {
                    rows.Append($"<row r=\"{currentRow + 1}\">{row}</row>");
                }

                currentRow = cell.Row;
                row.Clear();
            }

            row.Append(CellXml(cell));
        }

        if (currentRow >= 0)
        {
            rows.Append($"<row r=\"{currentRow + 1}\">{row}</row>");
        }

        var dimensionXml = dimension is null ? string.Empty : $"<dimension ref=\"{dimension}\"/>";

        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            + "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
            + dimensionXml
            + "<sheetData>" + rows + "</sheetData></worksheet>";
    }

    private static string? Dimension(List<TestCell> cells)
    {
        if (cells.Count == 0)
        {
            return null;
        }

        var minRow = int.MaxValue;
        var minColumn = int.MaxValue;
        var maxRow = 0;
        var maxColumn = 0;

        foreach (var cell in cells)
        {
            minRow = Math.Min(minRow, cell.Row);
            minColumn = Math.Min(minColumn, cell.Column);
            maxRow = Math.Max(maxRow, cell.Row);
            maxColumn = Math.Max(maxColumn, cell.Column);
        }

        var start = Reference(minRow, minColumn);
        var end = Reference(maxRow, maxColumn);

        return start == end ? start : $"{start}:{end}";
    }

    private string CellXml(TestCell cell)
    {
        var at = Reference(cell.Row, cell.Column);

        return cell switch
        {
            TestText text => TextCell(at, text.Text),
            TestNumber number => $"<c r=\"{at}\"><v>{Invariant(number.Number)}</v></c>",
            TestBoolean boolean => $"<c r=\"{at}\" t=\"b\"><v>{(boolean.Value ? 1 : 0)}</v></c>",
            TestDate date => $"<c r=\"{at}\" s=\"1\"><v>{Invariant(date.Serial)}</v></c>",
            TestDuration duration => $"<c r=\"{at}\" s=\"2\"><v>{Invariant(duration.Days)}</v></c>",
            TestError error => $"<c r=\"{at}\" t=\"e\"><v>{Escape(error.Error)}</v></c>",
            TestFormula { Cached: { } cached } formula
                => $"<c r=\"{at}\"><f>{Escape(formula.Text)}</f><v>{Invariant(cached)}</v></c>",
            TestFormula formula => $"<c r=\"{at}\"><f>{Escape(formula.Text)}</f></c>",
            _ => throw new ArgumentOutOfRangeException(nameof(cell), cell, "unknown cell shape"),
        };
    }

    private string TextCell(string at, string text)
    {
        var index = _strings.FindIndex(entry => string.Equals(entry, text, StringComparison.Ordinal));

        if (index < 0)
        {
            _strings.Add(text);
            index = _strings.Count - 1;
        }

        return $"<c r=\"{at}\" t=\"s\"><v>{index}</v></c>";
    }

    private static string Invariant(double value) => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>An <c>A1</c>-style reference for a zero-based row and column.</summary>
    private static string Reference(int row, int column)
    {
        var letters = new StringBuilder();
        var value = column + 1;

        while (value > 0)
        {
            var remainder = (value - 1) % 26;
            letters.Insert(0, (char)('A' + remainder));
            value = (value - 1) / 26;
        }

        return $"{letters}{row + 1}";
    }

    private static string Escape(string text)
        => text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
}
