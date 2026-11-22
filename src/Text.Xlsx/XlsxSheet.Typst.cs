using M3scalinum.Text.Documents;

namespace M3scalinum.Text.Xlsx;

/// <summary>One sheet as the document model of <c>M3scalinum.Text.Typst</c>.</summary>
public sealed partial class XlsxSheet
{
    /// <summary>Builds the Typst document this sheet describes: one table, and nothing else.</summary>
    /// <remarks>
    /// This is the sheet alone — no heading, no workbook around it. Use
    /// <see cref="XlsxWorkbook.ToTypstDocument"/> to render the whole workbook, where each sheet is
    /// named above its table.
    /// </remarks>
    /// <returns>The document.</returns>
    public TypstDocument ToTypstDocument() => XlsxWorkbook.Render(this);
}
