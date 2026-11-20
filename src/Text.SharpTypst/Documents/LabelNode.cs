namespace M3scalinum.Text.SharpTypst.Documents;

internal sealed class LabelNode(string name) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
        => writer.WriteVerbatim("<" + name + ">");
}