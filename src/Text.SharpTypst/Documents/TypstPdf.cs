using System;

namespace M3scalinum.Text.SharpTypst.Documents;

/// <summary>PDF-specific functions. They only take effect when exporting to PDF.</summary>
public static class TypstPdf
{
    /// <summary>Attaches a file to the PDF.</summary>
    public static TypstExpression Attach(string path, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return description is null
            ? TypstExpression.Raw($"pdf.attach({LinkNode.Quote(path)})")
            : TypstExpression.Raw(
                $"pdf.attach({LinkNode.Quote(path)}, description: {LinkNode.Quote(description)})");
    }

    /// <summary>Marks content as an artifact, hidden from assistive technology.</summary>
    public static TypstExpression Artifact(TypstContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return TypstExpression.Raw("pdf.artifact" + TypstExpression.Content(content).ToTypst());
    }

    /// <summary>Marks a table cell as data, for accessibility tagging.</summary>
    public static TypstExpression DataCell(TypstContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return TypstExpression.Raw("pdf.data-cell" + TypstExpression.Content(content).ToTypst());
    }

    /// <summary>Marks a table cell as a header, for accessibility tagging.</summary>
    public static TypstExpression HeaderCell(TypstContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return TypstExpression.Raw("pdf.header-cell" + TypstExpression.Content(content).ToTypst());
    }

    /// <summary>Attaches a summary to a table, for accessibility tagging.</summary>
    public static TypstExpression TableSummary(string summary, TypstContent table)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(table);

        return TypstExpression.Raw(
            $"pdf.table-summary(summary: {LinkNode.Quote(summary)}, {TypstExpression.Content(table).ToTypst()})");
    }
}