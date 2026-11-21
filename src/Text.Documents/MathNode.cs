namespace M3scalinum.Text.Documents;

internal sealed class MathNode(string expression, bool block) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
    {
        writer.WriteVerbatim(block ? "$ " : "$");
        writer.WriteVerbatim(expression);
        writer.WriteVerbatim(block ? " $" : "$");
    }
}