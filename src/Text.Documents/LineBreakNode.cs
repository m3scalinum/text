namespace M3scalinum.Text.Documents;

internal sealed class LineBreakNode : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
    {
        writer.WriteVerbatim("\\");
        writer.NewLine();
    }
}