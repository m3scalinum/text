namespace M3scalinum.Text.Documents;

internal sealed class ReferenceNode(string label) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
        => writer.WriteVerbatim("@" + label);
}