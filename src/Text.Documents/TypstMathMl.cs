using System;
using System.Collections.Generic;
using System.Text;

namespace M3scalinum.Text.Documents;

/// <summary>Why a MathML formula was refused.</summary>
/// <remarks>
/// The class of the refusal says what the caller is holding: a broken formula, a hostile one, one that
/// passes a bound, or one written with an element this model does not implement.
/// </remarks>
public enum TypstMathMlError
{
    /// <summary>The XML is not well formed, or an element breaks the shape its definition requires.</summary>
    Malformed = 0,

    /// <summary>The formula declares a document type or an entity; both are refused before parsing.</summary>
    XmlPolicy = 1,

    /// <summary>The formula passes a bound: characters, nesting depth or element count.</summary>
    Limit = 2,

    /// <summary>The formula uses an element outside the supported subset.</summary>
    Unsupported = 3,
}

/// <summary>A MathML formula could not be imported.</summary>
/// <remarks>
/// An element the subset does not cover is refused, never ignored: the exception names it in
/// <see cref="Exception.Message"/> and in <see cref="ElementName"/>. Silently dropping <c>munderover</c> would
/// hand back a formula that is not the one the caller sent, and the loss would only show in the render.
/// </remarks>
public sealed class TypstMathMlException : Exception
{
    /// <summary>Creates an exception without a message, classified as <see cref="TypstMathMlError.Malformed"/>.</summary>
    public TypstMathMlException()
    {
    }

    /// <summary>Creates an exception with a message, classified as <see cref="TypstMathMlError.Malformed"/>.</summary>
    /// <param name="message">What was refused, naming the rule and the value that passed it.</param>
    public TypstMathMlException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    /// <param name="message">What was refused, naming the rule and the value that passed it.</param>
    /// <param name="innerException">The parser failure that caused the refusal, when there is one.</param>
    public TypstMathMlException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception of the given class.</summary>
    /// <param name="error">Which class of refusal this is.</param>
    /// <param name="message">What was refused, naming the rule and the value that passed it.</param>
    public TypstMathMlException(TypstMathMlError error, string message)
        : base(message)
        => Error = error;

    /// <summary>Which class of refusal this is.</summary>
    public TypstMathMlError Error { get; }

    /// <summary>The MathML element that caused the failure, when the failure is about one element.</summary>
    public string? ElementName { get; init; }
}

/// <summary>A MathML element of the supported subset, with its children.</summary>
/// <remarks>
/// The tree mirrors the source: an <c>mrow</c> in the input is an <c>mrow</c> here, and an element's
/// children are exactly the elements it held. Nothing is normalised away, which is what lets
/// <see cref="ToMathMl()"/> write back the structure that was read.
/// </remarks>
public sealed class TypstMathMlElement
{
    private readonly TypstMathMlElement[] _children;

    internal TypstMathMlElement(string name, string text, TypstMathMlElement[] children)
    {
        Name = name;
        Text = text;
        _children = children;
    }

    /// <summary>The MathML element name, for example <c>mfrac</c>.</summary>
    public string Name { get; }

    /// <summary>
    /// The text of a token element — <c>mi</c>, <c>mn</c>, <c>mo</c>, <c>mtext</c> — trimmed; empty
    /// for a container.
    /// </summary>
    public string Text { get; }

    /// <summary>The child elements, in document order. Empty for a token element.</summary>
    public IReadOnlyList<TypstMathMlElement> Children => _children;

    /// <summary>Writes this element and its subtree as MathML, with no indentation.</summary>
    /// <returns>A single-line <c>&lt;mrow&gt;…&lt;/mrow&gt;</c> shaped fragment.</returns>
    public string ToMathMl()
    {
        var writer = new StringBuilder();
        Write(writer);
        return writer.ToString();
    }

    /// <summary>Writes this element as Typst math markup, without the surrounding <c>$</c>.</summary>
    /// <returns>
    /// Markup meant to sit inside <c>$ … $</c>: <c>frac(a, b)</c> for an <c>mfrac</c>, <c>x^2</c> for
    /// an <c>msup</c>, <c>mat(a, b; c, d)</c> for an <c>mtable</c>.
    /// </returns>
    public string ToTypst() => Render().Markup;

    internal void Write(StringBuilder writer)
    {
        writer.Append('<').Append(Name).Append('>');

        if (_children.Length == 0)
        {
            WriteText(writer, Text);
        }
        else
        {
            foreach (var child in _children)
            {
                child.Write(writer);
            }
        }

        writer.Append("</").Append(Name).Append('>');
    }

    internal TypstMathMlFragment Render()
    {
        switch (Name)
        {
            case "mi":
                return Identifier();
            case "mn":
                return new TypstMathMlFragment(EscapeMath(Text), Text.Length == 1);
            case "mo":
                return Operator();
            case "mtext":
                return new TypstMathMlFragment(LinkNode.Quote(Text), true);
            case "mrow":
                return Row();
            case "msup":
                return Scripted(t: true);
            case "msub":
                return Scripted(t: false);
            case "msubsup":
                return SubAndSuperscript();
            case "mfrac":
                return new TypstMathMlFragment(
                    $"frac({_children[0].Render().Markup}, {_children[1].Render().Markup})",
                    true);
            case "msqrt":
                return new TypstMathMlFragment($"sqrt({_children[0].Render().Markup})", true);
            case "mroot":
                return new TypstMathMlFragment(
                    $"root({_children[0].Render().Markup}, {_children[1].Render().Markup})",
                    true);
            case "mtable":
                return Matrix();
            case "mtr":
            case "mtd":
                throw new InvalidOperationException(
                    $"<{Name}> is only meaningful inside an <mtable> and should not have been built here");
            default:
                throw new InvalidOperationException(
                    $"<{Name}> is not in the supported subset and should not have been built");
        }
    }

    private TypstMathMlFragment Identifier()
        => Text.Length == 1
            ? new TypstMathMlFragment(EscapeMath(Text), true)
            : new TypstMathMlFragment($"upright({LinkNode.Quote(Text)})", true);

    private TypstMathMlFragment Operator()
    {
        if (TypstMathMlOperators.Named.TryGetValue(Text, out var named))
        {
            return new TypstMathMlFragment(named, IsIdentifier(named));
        }

        return new TypstMathMlFragment(EscapeMath(Text), Text.Length == 1);
    }

    private TypstMathMlFragment Row()
    {
        if (_children.Length == 1)
        {
            return _children[0].Render();
        }

        var parts = new string[_children.Length];

        for (var index = 0; index < _children.Length; index++)
        {
            parts[index] = _children[index].Render().Markup;
        }

        return new TypstMathMlFragment(string.Join(" ", parts), false);
    }

    /// <summary>An <c>msup</c> or an <c>msub</c>: a base and one script.</summary>
    /// <param name="t">True for the superscript, false for the subscript.</param>
    private TypstMathMlFragment Scripted(bool t)
    {
        var baseFragment = _children[0].Render();
        var script = _children[1].Render();

        if (baseFragment.Atomic && script.Atomic)
        {
            return new TypstMathMlFragment(
                baseFragment.Markup + (t ? "^" : "_") + script.Markup,
                true);
        }

        // A base or a script that is not a single token takes Typst's attach() rather than
        // parentheses: parentheses would print, and the MathML had none.
        return new TypstMathMlFragment(
            $"attach({baseFragment.Markup}, {(t ? "t" : "b")}: {script.Markup})",
            true);
    }

    private TypstMathMlFragment SubAndSuperscript()
    {
        var baseFragment = _children[0].Render();
        var sub = _children[1].Render();
        var sup = _children[2].Render();

        if (baseFragment.Atomic && sub.Atomic && sup.Atomic)
        {
            return new TypstMathMlFragment($"{baseFragment.Markup}_{sub.Markup}^{sup.Markup}", true);
        }

        return new TypstMathMlFragment(
            $"attach({baseFragment.Markup}, b: {sub.Markup}, t: {sup.Markup})",
            true);
    }

    private TypstMathMlFragment Matrix()
    {
        var rows = new string[_children.Length];

        for (var rowIndex = 0; rowIndex < _children.Length; rowIndex++)
        {
            var cells = _children[rowIndex]._children;
            var written = new string[cells.Length];

            for (var cellIndex = 0; cellIndex < cells.Length; cellIndex++)
            {
                written[cellIndex] = Cell(cells[cellIndex]);
            }

            rows[rowIndex] = string.Join(", ", written);
        }

        return new TypstMathMlFragment($"mat({string.Join("; ", rows)})", true);
    }

    private static string Cell(TypstMathMlElement cell)
    {
        if (cell._children.Length == 0)
        {
            return "#[]";
        }

        var parts = new string[cell._children.Length];

        for (var index = 0; index < cell._children.Length; index++)
        {
            parts[index] = cell._children[index].Render().Markup;
        }

        var markup = string.Join(" ", parts);

        // A comma or a semicolon inside the cell would be read as a cell or row separator by mat().
        // Wrapping the cell in a content block makes it one argument again.
        return markup.IndexOfAny(TypstMathMlOperators.Separators) >= 0
            ? $"#[$ {markup} $]"
            : markup;
    }

    private static bool IsIdentifier(string markup)
    {
        if (markup.Length == 0)
        {
            return false;
        }

        foreach (var character in markup)
        {
            if (!char.IsLetter(character) && character != '.')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Escapes the characters that end a text run or open code in Typst math.</summary>
    private static string EscapeMath(string text)
    {
        var builder = new StringBuilder(text.Length);

        foreach (var character in text)
        {
            if (character is '\\' or '#' or '$' or '"' or '@' or '`')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static void WriteText(StringBuilder writer, string text)
    {
        foreach (var character in text)
        {
            switch (character)
            {
                case '&':
                    writer.Append("&amp;");
                    break;
                case '<':
                    writer.Append("&lt;");
                    break;
                case '>':
                    writer.Append("&gt;");
                    break;
                default:
                    writer.Append(character);
                    break;
            }
        }
    }
}

/// <summary>A formula imported from MathML: the root's children and how it asked to be laid out.</summary>
/// <remarks>
/// <para>
/// The entry point is <see cref="Import"/>. The result renders two ways: <see cref="ToTypst()"/> writes
/// the Typst math markup a document node carries, and <see cref="ToMathMl()"/> writes the MathML an HTML
/// export carries. Both come from the same tree, so a round trip through the model keeps the
/// structure it read.
/// </para>
/// <para>
/// The supported subset is listed in <c>docs/specifications/44-mathml.md</c>; an element outside it is
/// refused by name, never dropped.
/// </para>
/// </remarks>
public sealed partial class TypstMathMlExpression
{
    private readonly TypstMathMlElement[] _children;

    internal TypstMathMlExpression(TypstMathMlElement[] children, bool display)
    {
        _children = children;
        Display = display;
    }

    /// <summary>The children of the <c>&lt;math&gt;</c> element, in document order.</summary>
    public IReadOnlyList<TypstMathMlElement> Children => _children;

    /// <summary>True when the formula asked for <c>display="block"</c>.</summary>
    public bool Display { get; }

    /// <summary>Writes the formula as Typst math markup, without the surrounding <c>$</c>.</summary>
    /// <returns>Markup meant to sit inside <c>$ … $</c>, children separated by a space.</returns>
    public string ToTypst()
    {
        var parts = new string[_children.Length];

        for (var index = 0; index < _children.Length; index++)
        {
            parts[index] = _children[index].Render().Markup;
        }

        return string.Join(" ", parts);
    }

    /// <summary>Writes the formula as MathML, keeping the display mode it was imported with.</summary>
    /// <returns>A single-line <c>&lt;math&gt;</c> element in the MathML namespace.</returns>
    public string ToMathMl() => ToMathMl(Display ? "block" : "inline");

    internal string ToMathMl(string display)
    {
        var writer = new StringBuilder();
        writer.Append("<math xmlns=\"http://www.w3.org/1998/Math/MathML\" display=\"")
            .Append(display)
            .Append("\">");

        foreach (var child in _children)
        {
            child.Write(writer);
        }

        return writer.Append("</math>").ToString();
    }
}

/// <summary>A rendered element: its markup, and whether that markup is a single Typst atom.</summary>
/// <param name="Markup">The Typst math markup.</param>
/// <param name="Atomic">
/// True when the markup is one token — a character, an identifier or a call — so a script can attach
/// to it with <c>^</c> or <c>_</c> without grouping.
/// </param>
internal readonly record struct TypstMathMlFragment(string Markup, bool Atomic);

/// <summary>The operator spellings Typst prefers, and the separators a matrix cell may not hold.</summary>
internal static class TypstMathMlOperators
{
    /// <summary>A comma or a semicolon separates matrix cells and rows; neither may reach mat() raw.</summary>
    internal static readonly char[] Separators = [',', ';'];

    /// <summary>
    /// The MathML operators with a Typst name. An operator not listed here is written verbatim, which
    /// Typst renders as the glyph itself.
    /// </summary>
    internal static readonly Dictionary<string, string> Named = new(StringComparer.Ordinal)
    {
        ["+"] = "+",
        ["-"] = "-",
        ["\u2212"] = "-",
        ["="] = "=",
        ["<"] = "<",
        [">"] = ">",
        ["\u2264"] = "<=",
        ["\u2265"] = ">=",
        ["\u2260"] = "!=",
        ["\u2248"] = "approx",
        ["\u2261"] = "equiv",
        ["\u221D"] = "prop",
        ["\u00D7"] = "times",
        ["\u22C5"] = "dot",
        ["\u00B7"] = "dot",
        ["\u00F7"] = "div",
        ["\u00B1"] = "plus.minus",
        ["\u2213"] = "minus.plus",
        ["\u221E"] = "infinity",
        ["\u2211"] = "sum",
        ["\u220F"] = "product",
        ["\u222B"] = "integral",
        ["\u2202"] = "diff",
        ["\u2207"] = "nabla",
        ["\u2192"] = "arrow.r",
        ["\u2190"] = "arrow.l",
        ["\u2194"] = "arrow.l.r",
        ["\u21D2"] = "arrow.r.double",
        ["\u21D0"] = "arrow.l.double",
        ["\u2208"] = "in",
        ["\u2209"] = "not in",
        ["\u2282"] = "subset",
        ["\u2286"] = "subset.eq",
        ["\u222A"] = "union",
        ["\u2229"] = "sect",
        ["\u2200"] = "forall",
        ["\u2203"] = "exists",
        ["\u2026"] = "...",
    };
}
