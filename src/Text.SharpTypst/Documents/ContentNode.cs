namespace M3scalinum.Text.SharpTypst.Documents;

/// <summary>Content emitted as its own block, for shapes and other inline constructs.</summary>
internal sealed class ContentNode(TypstContent content) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
    {
        writer.BlankLine();
        content.Write(writer);
        writer.NewLine();
    }
}