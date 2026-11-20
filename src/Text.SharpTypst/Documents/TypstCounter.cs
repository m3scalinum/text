using System;
using System.Globalization;

namespace M3scalinum.Text.SharpTypst.Documents;

/// <summary>A counter: <c>page</c>, <c>heading</c>, or a named one.</summary>
public sealed class TypstCounter
{
    private readonly string _constructor;

    private TypstCounter(string constructor) => _constructor = constructor;

    /// <summary>The page counter.</summary>
    public static TypstCounter Page => new("counter(page)");

    /// <summary>The heading counter.</summary>
    public static TypstCounter Heading => new("counter(heading)");

    /// <summary>The figure counter.</summary>
    public static TypstCounter Figure => new("counter(figure)");

    /// <summary>The footnote counter.</summary>
    public static TypstCounter Footnote => new("counter(footnote)");

    /// <summary>The equation counter.</summary>
    public static TypstCounter Equation => new("counter(math.equation)");

    /// <summary>A named counter.</summary>
    public static TypstCounter Of(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new TypstCounter($"counter({LinkNode.Quote(name)})");
    }

    /// <summary>Displays the counter, optionally with a numbering pattern.</summary>
    public TypstExpression Display(string? numbering = null)
        => TypstExpression.Raw(
            numbering is null
                ? $"{_constructor}.display()"
                : $"{_constructor}.display({LinkNode.Quote(numbering)})");

    /// <summary>Reads the counter at the current location.</summary>
    public TypstExpression Get() => TypstExpression.Raw($"{_constructor}.get()");

    /// <summary>Reads the counter at a location.</summary>
    public TypstExpression At(TypstExpression location)
    {
        ArgumentNullException.ThrowIfNull(location);
        return TypstExpression.Raw($"{_constructor}.at({location.ToTypst()})");
    }

    /// <summary>Advances the counter.</summary>
    public TypstExpression Step(int by = 1)
        => TypstExpression.Raw($"{_constructor}.step({by.ToString(CultureInfo.InvariantCulture)})");

    /// <summary>Sets the counter.</summary>
    public TypstExpression Update(int value)
        => TypstExpression.Raw($"{_constructor}.update({value.ToString(CultureInfo.InvariantCulture)})");

    /// <summary>Reads the counter at the end of the document.</summary>
    public TypstExpression Final() => TypstExpression.Raw($"{_constructor}.final()");
}