using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace M3scalinum.Text.Documents;

internal sealed class TableNode(TypstTable table) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
    {
        var columns = table.ColumnCount;

        // A table with no widths and no cells has no grid to draw; a row Typst would place nothing
        // in adds no markup either, which keeps a row swallowed whole by a row span out of the
        // output.
        if (columns == 0)
        {
            return;
        }

        writer.BlankLine();
        writer.WriteVerbatim("#table(");

        using (writer.Indent())
        {
            writer.NewLine();
            writer.WriteVerbatim("columns: ");

            if (table.HasColumnWidths)
            {
                writer.WriteVerbatim("(");
                writer.WriteVerbatim(string.Join(", ", table.ColumnWidths.Select(width => width.ToString())));
                writer.WriteVerbatim(")");
            }
            else
            {
                // Typst's integer form: that many equal columns, measured from the cells.
                writer.WriteVerbatim(columns.ToString(CultureInfo.InvariantCulture));
            }

            writer.WriteVerbatim(",");

            if (table.Header.Count > 0)
            {
                writer.NewLine();
                writer.WriteVerbatim("table.header(");
                WriteCells(writer, table.Header);
                writer.WriteVerbatim("),");
            }

            foreach (var row in table.Rows)
            {
                if (row.Count == 0)
                {
                    continue;
                }

                writer.NewLine();
                WriteCells(writer, row);
                writer.WriteVerbatim(",");
            }
        }

        writer.NewLine();
        writer.WriteVerbatim(")");
        writer.NewLine();
    }

    private static void WriteCells(TypstMarkupWriter writer, IReadOnlyList<TypstTableCell> cells)
    {
        for (var index = 0; index < cells.Count; index++)
        {
            if (index > 0)
            {
                writer.WriteVerbatim(", ");
            }

            WriteCell(writer, cells[index]);
        }
    }

    private static void WriteCell(TypstMarkupWriter writer, TypstTableCell cell)
    {
        var call = CellCall(cell);

        if (cell.HeaderScope is { } scope)
        {
            var quoted = LinkNode.Quote(ScopeName(scope));

            if (call is null)
            {
                // A plain cell: the content is the function's trailing block.
                writer.WriteVerbatim("pdf.header-cell(scope: " + quoted + ")");

                using (writer.ContentBlock())
                {
                    cell.Content.Write(writer);
                }

                return;
            }

            // A spanned or aligned cell: table.cell takes the content, and is the one positional
            // argument pdf.header-cell accepts.
            writer.WriteVerbatim("pdf.header-cell(" + call);

            using (writer.ContentBlock())
            {
                cell.Content.Write(writer);
            }

            writer.WriteVerbatim(", scope: " + quoted + ")");
            return;
        }

        if (call is not null)
        {
            writer.WriteVerbatim(call);
        }

        using (writer.ContentBlock())
        {
            cell.Content.Write(writer);
        }
    }

    private static string? CellCall(TypstTableCell cell)
    {
        if (cell.Colspan == 1 && cell.Rowspan == 1 && cell.Align is null)
        {
            return null;
        }

        var arguments = new List<string>();

        if (cell.Colspan > 1)
        {
            arguments.Add("colspan: " + cell.Colspan.ToString(CultureInfo.InvariantCulture));
        }

        if (cell.Rowspan > 1)
        {
            arguments.Add("rowspan: " + cell.Rowspan.ToString(CultureInfo.InvariantCulture));
        }

        if (cell.Align is not null)
        {
            arguments.Add("align: " + AlignmentName(cell.Align.Value));
        }

        return "table.cell(" + string.Join(", ", arguments) + ")";
    }

    private static string ScopeName(TypstHeaderScope scope) => scope switch
    {
        TypstHeaderScope.Row => "row",
        TypstHeaderScope.Both => "both",
        _ => "column",
    };

    private static string AlignmentName(TypstAlignment alignment) => alignment switch
    {
        TypstAlignment.Center => "center",
        TypstAlignment.Right => "right",
        _ => "left",
    };
}