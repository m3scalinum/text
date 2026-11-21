using System;

namespace M3scalinum.Text.Documents;

/// <summary>A WebAssembly plugin.</summary>
public sealed class TypstPlugin
{
    private readonly string _path;

    private TypstPlugin(string path) => _path = path;

    /// <summary>Loads a plugin from a virtual asset path.</summary>
    public static TypstPlugin Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new TypstPlugin(path);
    }

    /// <summary>Calls an exported function.</summary>
    public TypstExpression Call(string function, params TypstExpression[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(function);
        ArgumentNullException.ThrowIfNull(arguments);

        var call = $"plugin({LinkNode.Quote(_path)}).call({LinkNode.Quote(function)}";

        foreach (var argument in arguments)
        {
            ArgumentNullException.ThrowIfNull(argument);
            call += ", " + argument.ToTypst();
        }

        return TypstExpression.Raw(call + ")");
    }
}