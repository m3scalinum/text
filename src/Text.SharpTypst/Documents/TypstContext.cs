using System;
using System.Collections.Generic;
using System.Text;

namespace M3scalinum.Text.SharpTypst.Documents;

/// <summary>
/// A context block. Contextual functions — counters, states, queries, locations, measurement —
/// only work inside one.
/// </summary>
public sealed class TypstContext
{
    private readonly List<string> _statements = [];

    /// <summary>Adds an expression statement.</summary>
    public TypstContext Statement(TypstExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        _statements.Add(expression.ToTypst());
        return this;
    }

    /// <summary>Adds literal text.</summary>
    public TypstContext Text(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _statements.Add(LinkNode.Quote(text));
        return this;
    }

    /// <summary>Binds a name.</summary>
    public TypstContext Let(string name, TypstExpression value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        _statements.Add($"let {name} = {value.ToTypst()}");
        return this;
    }

    /// <summary>Measures content.</summary>
    public TypstContext Measure(TypstContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _statements.Add($"measure({TypstExpression.Content(content).ToTypst()})");
        return this;
    }

    /// <summary>Adds Typst code, emitted untouched.</summary>
    public TypstContext Raw(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        _statements.Add(code);
        return this;
    }

    internal string ToCode()
    {
        var builder = new StringBuilder();

        for (var index = 0; index < _statements.Count; index++)
        {
            if (index > 0)
            {
                builder.Append("; ");
            }

            builder.Append(_statements[index]);
        }

        return builder.ToString();
    }
}