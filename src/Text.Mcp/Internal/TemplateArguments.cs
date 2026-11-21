using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using M3scalinum.Text.SharpTypst.Templates;

namespace M3scalinum.Text.Mcp.Internal;

/// <summary>
/// Turns the JSON <c>parameters</c> object of a call into the values a layout's manifest declares.
/// </summary>
/// <remarks>
/// <see cref="TypstTemplate.Render"/> validates values against the manifest and refuses the wrong
/// kinds, but it can only say "expected a number, got String": this layer knows the manifest, so it
/// can convert a JSON string to a <see cref="DateOnly"/> for a <c>date</c> parameter — the one type
/// JSON has no counterpart for — and name the parameter in the error when it cannot.
/// </remarks>
internal static class TemplateArguments
{
    /// <summary>Converts the argument object.</summary>
    /// <exception cref="McpToolArgumentsException">
    /// A value does not fit its declared type, or the template declares no such parameter.
    /// </exception>
    internal static IReadOnlyDictionary<string, object?> Convert(
        TypstTemplateManifest manifest,
        JsonElement? parameters)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var values = new Dictionary<string, object?>(StringComparer.Ordinal);

        if (parameters is not { } element || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return values;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new McpToolArgumentsException(
                "the 'parameters' argument is an object keyed by parameter name");
        }

        foreach (var property in element.EnumerateObject())
        {
            if (!manifest.Parameters.TryGetValue(property.Name, out var parameter))
            {
                if (manifest.AllowExtraParameters)
                {
                    values[property.Name] = Value(property.Value);
                    continue;
                }

                throw new McpToolArgumentsException(
                    $"template '{manifest.Name}' has no parameter '{property.Name}'");
            }

            if (property.Value.ValueKind is JsonValueKind.Null)
            {
                continue;
            }

            values[property.Name] = Declared(manifest, parameter, property.Value);
        }

        foreach (var parameter in manifest.Parameters.Values)
        {
            if (parameter.Required && !values.ContainsKey(parameter.Name) && parameter.Default is null)
            {
                throw new McpToolArgumentsException(
                    $"template '{manifest.Name}' requires parameter '{parameter.Name}'");
            }
        }

        return values;
    }

    private static object? Declared(
        TypstTemplateManifest manifest,
        TypstTemplateParameter parameter,
        JsonElement value) => parameter.Type switch
        {
        TypstParameterType.String or TypstParameterType.Asset => Text(manifest, parameter, value),
        TypstParameterType.Number => Number(manifest, parameter, value),
        TypstParameterType.Boolean => Boolean(manifest, parameter, value),
        TypstParameterType.Date => Date(manifest, parameter, value),
        TypstParameterType.Array => Array(manifest, parameter, value),
        TypstParameterType.Object => Dictionary(manifest, parameter, value),
        _ => throw new McpToolArgumentsException(
            $"template '{manifest.Name}' parameter '{parameter.Name}' has a type this server cannot fill"),
    };

    private static string Text(
        TypstTemplateManifest manifest,
        TypstTemplateParameter parameter,
        JsonElement value) => value.ValueKind == JsonValueKind.String
        ? value.GetString() ?? string.Empty
        : throw Wrong(manifest, parameter, "a string");

    private static decimal Number(
        TypstTemplateManifest manifest,
        TypstTemplateParameter parameter,
        JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)
        ? number
        : throw Wrong(manifest, parameter, "a number");

    private static bool Boolean(
        TypstTemplateManifest manifest,
        TypstTemplateParameter parameter,
        JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw Wrong(manifest, parameter, "true or false"),
        };

    private static DateOnly Date(
        TypstTemplateManifest manifest,
        TypstTemplateParameter parameter,
        JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String
            && DateOnly.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        throw Wrong(manifest, parameter, "a date written yyyy-MM-dd");
    }

    private static List<object?> Array(
        TypstTemplateManifest manifest,
        TypstTemplateParameter parameter,
        JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Wrong(manifest, parameter, "an array");
        }

        var items = new List<object?>(value.GetArrayLength());

        foreach (var item in value.EnumerateArray())
        {
            items.Add(parameter.ItemType switch
            {
                TypstParameterType.String or TypstParameterType.Asset => Text(manifest, parameter, item),
                TypstParameterType.Number => Number(manifest, parameter, item),
                TypstParameterType.Boolean => Boolean(manifest, parameter, item),
                TypstParameterType.Date => Date(manifest, parameter, item),
                TypstParameterType.Object => Dictionary(manifest, parameter, item),
                _ => Value(item),
            });
        }

        return items;
    }

    private static Dictionary<string, object?> Dictionary(
        TypstTemplateManifest manifest,
        TypstTemplateParameter parameter,
        JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Wrong(manifest, parameter, "an object");
        }

        var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var property in value.EnumerateObject())
        {
            dictionary[property.Name] = Value(property.Value);
        }

        return dictionary;
    }

    /// <summary>Converts a value whose declared type is not known, as inside an object.</summary>
    private static object? Value(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.TryGetDecimal(out var number) ? number : value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => ArrayOf(value),
        JsonValueKind.Object => ObjectOf(value),
        _ => null,
    };

    private static List<object?> ArrayOf(JsonElement value)
    {
        var items = new List<object?>(value.GetArrayLength());

        foreach (var item in value.EnumerateArray())
        {
            items.Add(Value(item));
        }

        return items;
    }

    private static Dictionary<string, object?> ObjectOf(JsonElement value)
    {
        var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var property in value.EnumerateObject())
        {
            dictionary[property.Name] = Value(property.Value);
        }

        return dictionary;
    }

    private static McpToolArgumentsException Wrong(
        TypstTemplateManifest manifest,
        TypstTemplateParameter parameter,
        string expected)
        => new($"template '{manifest.Name}' parameter '{parameter.Name}' must be {expected}");
}
