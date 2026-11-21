namespace M3scalinum.Text.Documents;

internal sealed class QuoteNode(TypstContent content, bool block = true) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
    {
        writer.BlankLine();
        writer.WriteVerbatim(block ? "#quote(block: true)" : "#quote");

        using (writer.ContentBlock())
        {
            content.Write(writer);
        }

        writer.NewLine();
    }
}