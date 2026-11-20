using System.Collections.Generic;

namespace M3scalinum.Text.SharpTypst.Documents;

internal sealed class ListNode(IReadOnlyList<TypstContent> items, bool ordered) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
    {
        if (items.Count == 0)
        {
            return;
        }

        writer.BlankLine();

        foreach (var item in items)
        {
            writer.WriteVerbatim(ordered ? "+ " : "- ");
            item.Write(writer);
            writer.NewLine();
        }
    }
}