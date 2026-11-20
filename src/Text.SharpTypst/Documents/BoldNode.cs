namespace M3scalinum.Text.SharpTypst.Documents;

internal sealed class BoldNode(TypstContent content) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
    {
        writer.WriteVerbatim("*");
        content.Write(writer);
        writer.WriteVerbatim("*");
    }
}