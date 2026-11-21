namespace M3scalinum.Text.Documents;

internal sealed class FootnoteNode(TypstContent content) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
    {
        writer.WriteVerbatim("#footnote");

        using (writer.ContentBlock())
        {
            content.Write(writer);
        }
    }
}