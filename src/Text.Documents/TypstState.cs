using System;

namespace M3scalinum.Text.Documents;

/// <summary>A state value, readable and writable from Typst code.</summary>
public sealed class TypstState
{
    private readonly string _constructor;

    private TypstState(string constructor) => _constructor = constructor;

    /// <summary>Declares a state with an initial value.</summary>
    public static TypstState Of(string name, TypstExpression initial)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(initial);
        return new TypstState($"state({LinkNode.Quote(name)}, {initial.ToTypst()})");
    }

    /// <summary>Reads the state at the current location.</summary>
    public TypstExpression Get() => TypstExpression.Raw($"{_constructor}.get()");

    /// <summary>Reads the state at a location.</summary>
    public TypstExpression At(TypstExpression location)
    {
        ArgumentNullException.ThrowIfNull(location);
        return TypstExpression.Raw($"{_constructor}.at({location.ToTypst()})");
    }

    /// <summary>Sets the state.</summary>
    public TypstExpression Update(TypstExpression value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return TypstExpression.Raw($"{_constructor}.update({value.ToTypst()})");
    }

    /// <summary>Reads the state at the end of the document.</summary>
    public TypstExpression Final() => TypstExpression.Raw($"{_constructor}.final()");
}