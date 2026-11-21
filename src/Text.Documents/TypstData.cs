using System;

namespace M3scalinum.Text.Documents;

/// <summary>The data-loading functions. The file must be registered as an asset in the world.</summary>
public static class TypstData
{
    /// <summary>Reads a file as text.</summary>
    public static TypstExpression Read(string path) => Call("read", path);

    /// <summary>Parses a JSON file.</summary>
    public static TypstExpression Json(string path) => Call("json", path);

    /// <summary>Parses a YAML file.</summary>
    public static TypstExpression Yaml(string path) => Call("yaml", path);

    /// <summary>Parses a TOML file.</summary>
    public static TypstExpression Toml(string path) => Call("toml", path);

    /// <summary>Parses a CSV file.</summary>
    public static TypstExpression Csv(string path) => Call("csv", path);

    /// <summary>Parses a CBOR file.</summary>
    public static TypstExpression Cbor(string path) => Call("cbor", path);

    /// <summary>Parses an XML file.</summary>
    public static TypstExpression Xml(string path) => Call("xml", path);

    private static TypstExpression Call(string function, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return TypstExpression.Raw($"{function}({LinkNode.Quote(path)})");
    }
}