using System;

namespace M3scalinum.Text.Documents;

/// <summary>A table cell, with the spans HTML's <c>colspan</c> and <c>rowspan</c> describe.</summary>
/// <remarks>
/// Spans are emitted as <c>table.cell(colspan: …, rowspan: …)</c>. A row that follows a row span
/// provides fewer cells — none, when the span covers the row whole, which is what
/// <see cref="TypstTable.Row()"/> writes — and Typst places them after the spanning cell, exactly as
/// HTML does.
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

    /// <summary>
    /// Optional alignment of the cell, emitted as <c>table.cell(align: …)</c>. Null lets the table
    /// default apply. GFM's header delimiter sets it.
    /// </summary>
    public TypstAlignment? Align { get; init; }

    /// <summary>
    /// Makes the cell a header cell and states what it labels, emitted as
    /// <c>pdf.header-cell(scope: …)</c>. Null keeps the cell a data cell, except in a header row,
    /// where Typst marks it a column header on its own.
    /// </summary>
    /// <remarks>
    /// A body row needs this to declare a header column (<see cref="TypstHeaderScope.Row"/>), and a
    /// header row needs it to declare a corner cell (<see cref="TypstHeaderScope.Both"/>). Typst
    /// requires the <c>a11y-extras</c> feature for the function; this suite's compiler enables
    /// every feature, and Typst gates it on PDF 1.5 and later.
    /// </remarks>
    public TypstHeaderScope? HeaderScope { get; init; }

    /// <summary>How many columns the cell covers.</summary>
    public int Colspan { get; }

    /// <summary>How many rows the cell covers.</summary>
    public int Rowspan { get; }
}
