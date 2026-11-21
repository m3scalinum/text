using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace M3scalinum.Text.SharpTypst.Documents;

internal sealed class TableNode(TypstTable table) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
    {
        writer.BlankLine();
        writer.WriteVerbatim("#table(");

        using (writer.Indent())
        {
            writer.NewLine();
            writer.WriteVerbatim("columns: (");
            writer.WriteVerbatim(string.Join(", ", table.ColumnWidths.Select(width => width.ToString())));
            writer.WriteVerbatim("),");

            if (table.Header.Count > 0)
            {
                writer.NewLine();
                writer.WriteVerbatim("table.header(");
                WriteCells(writer, table.Header);
                writer.WriteVerbatim("),");
            }

            foreach (var row in table.Rows)
            {
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

            var cell = cells[index];

            if (cell.Colspan > 1 || cell.Rowspan > 1)
            {
                writer.WriteVerbatim("table.cell(");
                WriteSpan(writer, "colspan", cell.Colspan, first: true);
                WriteSpan(writer, "rowspan", cell.Rowspan, first: cell.Colspan == 1);
                writer.WriteVerbatim(")");
            }

            using (writer.ContentBlock())
            {
                cell.Content.Write(writer);
            }
        }
    }

    private static void WriteSpan(TypstMarkupWriter writer, string name, int value, bool first)
    {
        if (value == 1)
        {
            return;
        }

        if (!first)
        {
            writer.WriteVerbatim(", ");
        }

        writer.WriteVerbatim(name + ": " + value.ToString(CultureInfo.InvariantCulture));
    }
}