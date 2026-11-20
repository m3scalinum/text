using System;
using System.Collections.Generic;

namespace M3scalinum.Text.SharpTypst.Documents;

/// <summary>A function definition, emitted as <c>#let name(…) = body</c>.</summary>
public sealed class TypstFunction
{
    private readonly string _name;
    private readonly List<string> _parameters = [];
    private string? _rest;
    private string _body = "none";

    private TypstFunction(string name) => _name = name;

    /// <summary>Starts a definition.</summary>
    public static TypstFunction Define(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new TypstFunction(name);
    }

    /// <summary>Adds a positional parameter.</summary>
    public TypstFunction Parameter(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _parameters.Add(name);
        return this;
    }

    /// <summary>Adds a rest parameter, which collects the remaining arguments.</summary>
    public TypstFunction Rest(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _rest = ".." + name;
        return this;
    }

    /// <summary>Sets the body, written in Typst.</summary>
    public TypstFunction Body(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        _body = code;
        return this;
    }

    /// <summary>Emits the definition.</summary>
    public string ToTypst()
    {
        var parameters = new List<string>(_parameters);

        if (_rest is not null)
        {
            parameters.Add(_rest);
        }

        return $"#let {_name}({string.Join(", ", parameters)}) = {_body}";
    }

    /// <inheritdoc />
    public override string ToString() => ToTypst();
}
