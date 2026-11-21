using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Typst.Templates;

/// <summary>The parsed <c>template.toml</c> of a template.</summary>
public sealed class TypstTemplateManifest
{
    internal TypstTemplateManifest(
        string name,
        string version,
        string entry,
        string? description,
        IReadOnlyList<string> authors,
        IReadOnlyDictionary<string, TypstTemplateParameter> parameters,
        bool allowExtraParameters,
        string? minTypst)
    {
        Name = name;
        Version = version;
        Entry = entry;
        Description = description;
        Authors = authors;
        Parameters = parameters;
        AllowExtraParameters = allowExtraParameters;
        MinTypst = minTypst;
    }

    /// <summary>Template name, for diagnostics.</summary>
    public string Name { get; }

    /// <summary>Template version, independent of the package version.</summary>
    public string Version { get; }

    /// <summary>Virtual path of the entry file, relative to the template root.</summary>
    public string Entry { get; }

    /// <summary>Human description.</summary>
    public string? Description { get; }

    /// <summary>Template authors.</summary>
    public IReadOnlyList<string> Authors { get; }

    /// <summary>Declared parameters, keyed by name.</summary>
    public IReadOnlyDictionary<string, TypstTemplateParameter> Parameters { get; }

    /// <summary>Whether a parameter absent from the manifest is accepted instead of rejected.</summary>
    public bool AllowExtraParameters { get; }

    /// <summary>Minimum Typst version the template requires, if declared.</summary>
    public string? MinTypst { get; }

    /// <summary>Parses a manifest from TOML text.</summary>
    /// <exception cref="TypstTemplateException">The manifest is malformed.</exception>
    public static TypstTemplateManifest Parse(string toml, string? origin = null)
    {
        ArgumentNullException.ThrowIfNull(toml);

        Internal.TomlTable root;
        try
        {
            root = Internal.Toml.Parse(toml);
        }
        catch (FormatException exception)
        {
            throw new TypstTemplateException(
                $"invalid template manifest{(origin is null ? string.Empty : $" '{origin}'")}: {exception.Message}",
                templateName: origin,
                parameterName: null,
                innerException: exception);
        }

        var name = RequireString(root, "name", origin);
        var version = RequireString(root, "version", origin);
        var entry = RequireString(root, "entry", origin);

        var parameters = new Dictionary<string, TypstTemplateParameter>(StringComparer.Ordinal);
        if (root["parameters"] is Internal.TomlTable table)
        {
            foreach (var (key, value) in table.Values)
            {
                parameters[key] = ParseParameter(key, value, origin);
            }
        }

        return new TypstTemplateManifest(
            name,
            version,
            entry,
            root["description"] as string,
            ReadStringArray(root["authors"]),
            parameters,
            root["allow_extra_parameters"] is true,
            root["min_typst"] as string);
    }

    private static TypstTemplateParameter ParseParameter(string name, object? value, string? origin)
    {
        if (value is not Internal.TomlTable table)
        {
            throw Invalid(origin, name, "expected an inline table such as { type = \"string\" }");
        }

        var typeName = table["type"] as string
            ?? throw Invalid(origin, name, "missing 'type'");

        var type = ParseType(typeName, origin, name);
        TypstParameterType? itemType = null;

        if (type == TypstParameterType.Array)
        {
            var of = table["of"] as string
                ?? throw Invalid(origin, name, "an array parameter needs 'of'");
            itemType = ParseType(of, origin, name);
        }

        return new TypstTemplateParameter(
            name,
            type,
            table["required"] is true,
            table["default"],
            itemType);
    }

    private static TypstParameterType ParseType(string name, string? origin, string parameter)
        => name switch
        {
            "string" => TypstParameterType.String,
            "number" => TypstParameterType.Number,
            "boolean" => TypstParameterType.Boolean,
            "date" => TypstParameterType.Date,
            "asset" => TypstParameterType.Asset,
            "array" => TypstParameterType.Array,
            "object" => TypstParameterType.Object,
            _ => throw Invalid(origin, parameter, $"unknown type '{name}'"),
        };

    private static string RequireString(Internal.TomlTable table, string key, string? origin)
        => table[key] as string
           ?? throw new TypstTemplateException(
               $"invalid template manifest{(origin is null ? string.Empty : $" '{origin}'")}: missing '{key}'",
               templateName: origin,
               parameterName: null);

    private static List<string> ReadStringArray(object? value)
    {
        if (value is not List<object?> items)
        {
            return [];
        }

        var result = new List<string>(items.Count);
        foreach (var item in items)
        {
            if (item is string text)
            {
                result.Add(text);
            }
        }

        return result;
    }

    private static TypstTemplateException Invalid(string? origin, string parameter, string message)
        => new(
            $"invalid template manifest{(origin is null ? string.Empty : $" '{origin}'")}: parameter '{parameter}': {message}",
            templateName: origin,
            parameterName: parameter);
}
