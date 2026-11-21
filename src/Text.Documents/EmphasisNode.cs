namespace M3scalinum.Text.Documents;

internal sealed class EmphasisNode(TypstContent content) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
    {
        writer.WriteVerbatim("_");
        content.Write(writer);
        writer.WriteVerbatim("_");
    }
}