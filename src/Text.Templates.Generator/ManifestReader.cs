using System;
using System.Collections.Generic;
using System.Globalization;

namespace M3scalinum.Text.Templates.Generator;

/// <summary>
/// Reads the <c>[parameters]</c> table of a manifest.
/// </summary>
/// <remarks>
/// This is deliberately not a general TOML parser: the generator only needs the parameter
/// declarations, and the runtime already owns the full parser. Keeping the two apart means the
/// analyzer stays on <c>netstandard2.0</c> without polyfills for <c>System.Index</c> or the newer
/// <c>string</c> overloads.
/// </remarks>
internal static class ManifestReader
{
    /// <summary>Returns the declared parameters, in manifest order.</summary>
    /// <exception cref="FormatException">A parameter entry is malformed.</exception>
    public static IReadOnlyList<TemplateParameter> ReadParameters(string text)
    {
        var parameters = new List<TemplateParameter>();
        var section = string.Empty;
        var lineNumber = 0;

        foreach (var raw in text.Split('\n'))
        {
            lineNumber++;
            var line = StripComment(raw).Trim();

            if (line.Length == 0)
            {
                continue;
            }

            if (line[0] == '[' && line[line.Length - 1] == ']')
            {
                section = line.Substring(1, line.Length - 2).Trim();
                continue;
            }

            if (!string.Equals(section, "parameters", StringComparison.Ordinal))
            {
                continue;
            }

            var equals = line.IndexOf('=');
            if (equals <= 0)
            {
                throw new FormatException("line " + lineNumber.ToString(CultureInfo.InvariantCulture) + ": expected 'name = { ... }'");
            }

            var name = Unquote(line.Substring(0, equals).Trim());
            var value = line.Substring(equals + 1).Trim();

            if (value.Length == 0 || value[0] != '{')
            {
                throw new FormatException("line " + lineNumber.ToString(CultureInfo.InvariantCulture) + ": expected an inline table for '" + name + "'");
            }

            var fields = ParseInlineTable(value, lineNumber);

            if (!fields.TryGetValue("type", out var type) || type is not string typeName)
            {
                throw new FormatException("line " + lineNumber.ToString(CultureInfo.InvariantCulture) + ": parameter '" + name + "' has no 'type'");
            }

            parameters.Add(new TemplateParameter(
                name,
                typeName,
                fields.TryGetValue("of", out var item) ? item as string : null,
                fields.TryGetValue("required", out var required) && required is true,
                fields.TryGetValue("default", out var fallback) ? fallback : null));
        }

        return parameters;
    }

    private static Dictionary<string, object?> ParseInlineTable(string text, int lineNumber)
    {
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
        var body = text.Trim();
        body = body.Substring(1, body.Length - 2);

        foreach (var part in SplitTopLevel(body))
        {
            var entry = part.Trim();
            if (entry.Length == 0)
            {
                continue;
            }

            var equals = entry.IndexOf('=');
            if (equals <= 0)
            {
                throw new FormatException("line " + lineNumber.ToString(CultureInfo.InvariantCulture) + ": expected 'key = value' in an inline table");
            }

            fields[Unquote(entry.Substring(0, equals).Trim())] = ParseValue(entry.Substring(equals + 1).Trim());
        }

        return fields;
    }

    private static IEnumerable<string> SplitTopLevel(string body)
    {
        var start = 0;
        var depth = 0;
        var inString = false;

        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];

            if (inString)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                case '[':
                    depth++;
                    break;
                case '}':
                case ']':
                    depth--;
                    break;
                case ',' when depth == 0:
                    yield return body.Substring(start, i - start);
                    start = i + 1;
                    break;
            }
        }

        yield return body.Substring(start);
    }

    private static object? ParseValue(string text)
    {
        if (text.Length == 0)
        {
            return null;
        }

        if (text[0] == '"')
        {
            return Unquote(text);
        }

        if (string.Equals(text, "true", StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(text, "false", StringComparison.Ordinal))
        {
            return false;
        }

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return integer;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var real))
        {
            return real;
        }

        return text;
    }

    private static string Unquote(string text)
    {
        if (text.Length < 2 || text[0] != '"' || text[text.Length - 1] != '"')
        {
            return text;
        }

        var body = text.Substring(1, text.Length - 2);
        if (body.IndexOf('\\') < 0)
        {
            return body;
        }

        var builder = new System.Text.StringBuilder(body.Length);
        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] != '\\' || i + 1 >= body.Length)
            {
                builder.Append(body[i]);
                continue;
            }

            i++;
            switch (body[i])
            {
                case 'n':
                    builder.Append('\n');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                default:
                    builder.Append(body[i]);
                    break;
            }
        }

        return builder.ToString();
    }

    private static string StripComment(string line)
    {
        var inString = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (inString)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (c == '"')
            {
                inString = true;
            }
            else if (c == '#')
            {
                return line.Substring(0, i);
            }
        }

        return line;
    }
}
