using System;

namespace M3scalinum.Text.SharpTypst.Documents;

/// <summary>A table cell, with the spans HTML's <c>colspan</c> and <c>rowspan</c> describe.</summary>
/// <remarks>
/// Spans are emitted as <c>table.cell(colspan: …, rowspan: …)</c>. A row that follows a row span
/// simply provides fewer cells: Typst places them after the spanning cell, exactly as HTML does.
/// </remarks>
public sealed class TypstTableCell
{
    /// <summary>Creates a cell around content.</summary>
    /// <param name="content">The cell content.</param>
    /// <param name="colspan">How many columns the cell covers; at least 1.</param>
    /// <param name="rowspan">How many rows the cell covers; at least 1.</param>
    public TypstTableCell(TypstContent content, int colspan = 1, int rowspan = 1)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfLessThan(colspan, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rowspan, 1);

        Content = content;
        Colspan = colspan;
        Rowspan = rowspan;
    }

    /// <summary>The cell content.</summary>
    public TypstContent Content { get; }

    /// <summary>How many columns the cell covers.</summary>
    public int Colspan { get; }

    /// <summary>How many rows the cell covers.</summary>
    public int Rowspan { get; }
}
