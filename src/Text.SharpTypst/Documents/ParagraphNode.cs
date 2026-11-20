namespace M3scalinum.Text.SharpTypst.Documents;

internal sealed class ParagraphNode(TypstContent content) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
    {
        if (content.IsEmpty)
        {
            return;
        }

        writer.BlankLine();
        content.Write(writer);
        writer.NewLine();
    }
}