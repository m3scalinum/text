using System.IO.Compression;
using System.Text;
using M3scalinum.Text.Xlsx;

// Reads a spreadsheet through the native reader the package ships. The workbook is built here in
// memory, so the example depends on no file: the package is the only thing that parses it.

var workbook = XlsxReader.Read(BuildWorkbook());
var sheet = workbook.Sheets[0];

Console.WriteLine("M3scalinum.Text.Xlsx");
Console.WriteLine($"  sheets       {workbook.Sheets.Count} ({string.Join(", ", workbook.Sheets.Select(s => s.Name))})");
Console.WriteLine($"  region       {sheet.RowCount} rows x {sheet.ColumnCount} columns, from {XlsxSheet.Reference(sheet.FirstRow, sheet.FirstColumn)}");
Console.WriteLine($"  cells        {sheet.Cells.Count}");

foreach (var cell in sheet.Cells)
{
    Console.WriteLine($"    {cell.Reference(sheet),-4} {cell.DisplayText}");
}

if (workbook.Sheets.Count == 0 || sheet.Cells.Count == 0)
{
    throw new InvalidOperationException("the reader returned a workbook with no cell");
}

var amount = sheet.Find(1, 1);

if (amount is null || amount.DisplayText != "1200.5")
{
    throw new InvalidOperationException($"the amount cell is wrong: '{amount?.DisplayText}'");
}

var region = sheet.Find(1, 0);

if (region?.Value is not XlsxTextValue { Text: "Ile-de-France" } textCell)
{
    throw new InvalidOperationException($"the region cell is not a string: '{region?.DisplayText}'");
}

Console.WriteLine($"  typed read   {textCell.Text} = {amount.DisplayText}");

// The sheet to Typst conversion: the model path a caller renders with M3scalinum.Text.Typst.
var markup = sheet.ToTypstDocument().ToMarkup();

if (markup.Length == 0)
{
    throw new InvalidOperationException("the sheet converted to no Typst markup");
}

Console.WriteLine($"  typst        {markup.Length} chars of markup");
Console.WriteLine();
Console.WriteLine("M3scalinum.Text.Xlsx: the native reader loaded from the package and read the workbook.");

// A two-row workbook: inline strings and a number, in the parts Excel writes.
static byte[] BuildWorkbook()
{
    using var buffer = new MemoryStream();

    using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
    {
        Write(archive, "[Content_Types].xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
              <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
            </Types>
            """);

        Write(archive, "_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """);

        Write(archive, "xl/workbook.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets><sheet name="Regions" sheetId="1" r:id="rId1"/></sheets>
            </workbook>
            """);

        Write(archive, "xl/_rels/workbook.xml.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
              <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
            </Relationships>
            """);

        Write(archive, "xl/styles.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <fonts count="1"><font><sz val="11"/><name val="Calibri"/></font></fonts>
              <fills count="1"><fill><patternFill patternType="none"/></fill></fills>
              <borders count="1"><border/></borders>
              <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
              <cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
            </styleSheet>
            """);

        Write(archive, "xl/worksheets/sheet1.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1">
                  <c r="A1" t="inlineStr"><is><t>Region</t></is></c>
                  <c r="B1" t="inlineStr"><is><t>Amount</t></is></c>
                </row>
                <row r="2">
                  <c r="A2" t="inlineStr"><is><t>Ile-de-France</t></is></c>
                  <c r="B2"><v>1200.5</v></c>
                </row>
              </sheetData>
            </worksheet>
            """);
    }

    return buffer.ToArray();
}

static void Write(ZipArchive archive, string name, string content)
{
    var entry = archive.CreateEntry(name, CompressionLevel.Fastest);

    using var stream = entry.Open();
    stream.Write(Encoding.UTF8.GetBytes(content.Trim()));
}
