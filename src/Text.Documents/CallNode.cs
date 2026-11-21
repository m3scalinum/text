namespace M3scalinum.Text.Documents;

/// <summary>A block-level Typst call that wraps content: <c>#align(center)[…]</c>.</summary>
internal sealed class CallNode(string call, TypstContent content) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
    {
        writer.BlankLine();
        writer.WriteVerbatim(call);

        using (writer.ContentBlock())
        {
            content.Write(writer);
        }

        writer.NewLine();
    }
}