namespace M3scalinum.Text.SharpTypst.Documents;

internal sealed class LoremNode(int words) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
        => writer.WriteVerbatim($"#lorem({words})");
}