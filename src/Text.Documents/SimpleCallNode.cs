namespace M3scalinum.Text.Documents;

internal sealed class SimpleCallNode(string call) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
    {
        writer.BlankLine();
        writer.WriteVerbatim(call);
        writer.NewLine();
    }
}