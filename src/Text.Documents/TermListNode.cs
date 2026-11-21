using System.Collections.Generic;

namespace M3scalinum.Text.Documents;

internal sealed class TermListNode(IReadOnlyList<(string Term, TypstContent Description)> items) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
    {
        if (items.Count == 0)
        {
            return;
        }

        writer.BlankLine();

        foreach (var (term, description) in items)
        {
            writer.WriteVerbatim("/ ");
            writer.WriteText(term);
            writer.WriteVerbatim(": ");
            description.Write(writer);
            writer.NewLine();
        }
    }
}