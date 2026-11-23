namespace M3scalinum.Text.Documents;

internal sealed class MathNode(string expression, bool block, TypstMathMlExpression? mathml = null) : TypstNode
{
    /// <summary>The MathML tree the expression came from, when it was imported rather than written.</summary>
    internal TypstMathMlExpression? MathMl => mathml;

    internal override void Write(TypstMarkupWriter writer)
    {
        writer.WriteVerbatim(block ? "$ " : "$");
        writer.WriteVerbatim(expression);
        writer.WriteVerbatim(block ? " $" : "$");
    }
}
