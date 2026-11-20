namespace M3scalinum.Text.SharpTypst.Documents;

/// <summary>A block-level Typst call without content: <c>#v(1em)</c>.</summary>
internal sealed class BareCallNode(string call) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
    {
        writer.BlankLine();
        writer.WriteVerbatim(call);
        writer.NewLine();
    }
}