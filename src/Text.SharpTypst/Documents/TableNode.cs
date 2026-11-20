using System.Collections.Generic;
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

    private static void WriteCells(TypstMarkupWriter writer, IReadOnlyList<TypstContent> cells)
    {
        for (var index = 0; index < cells.Count; index++)
        {
            if (index > 0)
            {
                writer.WriteVerbatim(", ");
            }

            using (writer.ContentBlock())
            {
                cells[index].Write(writer);
            }
        }
    }
}