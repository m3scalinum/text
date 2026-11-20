namespace M3scalinum.Text.SharpTypst.Documents;

internal sealed class TextNode(string text) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer) => writer.WriteText(text);
}