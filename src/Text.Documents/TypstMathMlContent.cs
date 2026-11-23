using System;

namespace M3scalinum.Text.Documents;

/// <summary>Inline math imported from MathML.</summary>
public sealed partial class TypstContent
{
    /// <summary>Appends inline math imported from MathML.</summary>
    /// <param name="mathml">
    /// A <c>&lt;math&gt;</c> element. Imported under the policy described in
    /// <c>docs/specifications/44-mathml.md</c>; an element outside the subset is refused by name.
    /// </param>
    /// <returns>This run, for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="mathml"/> is blank.</exception>
    /// <exception cref="TypstMathMlException">The formula is refused; see
    /// <see cref="TypstMathMlExpression.Import(string)"/>.</exception>
    public TypstContent MathMl(string mathml)
    {
        ArgumentNullException.ThrowIfNull(mathml);
        return MathMl(TypstMathMlExpression.Import(mathml));
    }

    /// <summary>Appends inline math from a formula already imported.</summary>
    /// <param name="expression">The imported formula.</param>
    /// <returns>This run, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="expression"/> is null.</exception>
    public TypstContent MathMl(TypstMathMlExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        _nodes.Add(new MathNode(expression.ToTypst(), block: false, expression));
        return this;
    }
}
