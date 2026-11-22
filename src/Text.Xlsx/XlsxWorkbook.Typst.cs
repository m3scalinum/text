using System.Collections.Generic;
using M3scalinum.Text.Documents;

namespace M3scalinum.Text.Xlsx;

/// <summary>Turns a read workbook, or one sheet of it, into the document model of <c>M3scalinum.Text.Typst</c>.</summary>
public sealed partial class XlsxWorkbook
{
    /// <summary>
    /// Builds the Typst document this workbook describes: every sheet, in workbook order, as a
    /// heading and the table its used region covers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The result is a <see cref="TypstDocument"/> of the suite's own model — the same one every other
    /// producer builds — so it compiles through
    /// <see cref="M3scalinum.Text.Typst.Compilation.TypstCompiler"/> and exports like any other
    /// document.
    /// </para>
    /// <para>
    /// A table cell holds the text <see cref="XlsxCell.DisplayText"/> gives: the number as written,
    /// the date as ISO 8601, the error as Excel prints it. A cell that holds nothing is an empty
    /// cell, so the columns stay aligned with the sheet.
    /// </para>
    /// </remarks>
    /// <returns>The document.</returns>
    public TypstDocument ToTypstDocument()
    {
        var document = TypstDocument.Create();

        foreach (var sheet in Sheets)
        {
            AppendSheet(document, sheet, withHeading: true);
        }

        return document;
    }

    /// <summary>One sheet of a workbook, as a table and nothing else.</summary>
    /// <param name="sheet">The sheet to render.</param>
    /// <returns>The document.</returns>
    internal static TypstDocument Render(XlsxSheet sheet)
    {
        var document = TypstDocument.Create();
        AppendSheet(document, sheet, withHeading: false);
        return document;
    }

    /// <summary>
    /// Emits one sheet. A workbook's sheet is named above its table, because a document holding two
    /// sheets and no headings would be two tables nobody could tell apart.
    /// </summary>
    private static void AppendSheet(TypstDocument document, XlsxSheet sheet, bool withHeading)
    {
        if (sheet.RowCount <= 0 || sheet.ColumnCount <= 0)
        {
            return;
        }

        if (withHeading)
        {
            var heading = new TypstContent();
            heading.Text(sheet.Name);
            document.Heading(1, heading);
        }

        var cells = new Dictionary<(int Row, int Column), XlsxCell>(sheet.Cells.Count);

        foreach (var cell in sheet.Cells)
        {
            cells[(cell.Row, cell.Column)] = cell;
        }

        document.Table(builder =>
        {
            var widths = new TypstLength[sheet.ColumnCount];

            for (var index = 0; index < widths.Length; index++)
            {
                widths[index] = TypstLength.Fr(1);
            }

            builder.Columns(widths);

            for (var row = 0; row < sheet.RowCount; row++)
            {
                var texts = new TypstContent[sheet.ColumnCount];

                for (var column = 0; column < sheet.ColumnCount; column++)
                {
                    var content = new TypstContent();

                    if (cells.TryGetValue((row, column), out var cell) && cell.DisplayText.Length > 0)
                    {
                        content.Text(cell.DisplayText);
                    }

                    texts[column] = content;
                }

                builder.Row(texts);
            }
        });
    }
}
