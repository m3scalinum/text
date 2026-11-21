using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace M3scalinum.Text.Typst.Internal;

/// <summary>
/// A parser for the TOML subset a template manifest needs: top-level keys, one level of
/// <c>[table]</c> headers, strings, booleans, numbers, single-line arrays and inline tables.
/// </summary>
/// <remarks>
/// Deliberately not a full TOML implementation: dates, multi-line strings, dotted keys and
/// arrays of tables are rejected with a message naming the line, rather than half-supported.
/// </remarks>
internal static class Toml
{
    /// <summary>
    /// Maximum nesting of arrays and inline tables. A manifest never needs more, and an unbounded
    /// recursion turns a malformed file into an uncatchable <c>StackOverflowException</c>.
    /// </summary>
    private const int MaxDepth = 64;

    public static TomlTable Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var root = new TomlTable();
        var current = root;
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            var line = StripComment(lines[index]).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line[0] == '[')
            {
                if (line[^1] != ']')
                {
                    throw Error(index, "unterminated table header");
                }

                var name = line[1..^1].Trim();
                if (name.Length == 0 || name.Contains('.', StringComparison.Ordinal))
                {
                    throw Error(index, $"unsupported table header '[{name}]'");
                }

                current = root.GetOrAddTable(name);
                continue;
            }

            var separator = IndexOfAssignment(line);
            if (separator < 0)
            {
                throw Error(index, "expected 'key = value'");
            }

            var key = line[..separator].Trim();
            if (key.Length == 0)
            {
                throw Error(index, "empty key");
            }

            var position = separator + 1;
            var value = ParseValue(line, ref position, index);
            current.Set(key, value);
        }

        return root;
    }

    private static int IndexOfAssignment(string line)
    {
        var inString = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"' && (i == 0 || line[i - 1] != '\\'))
            {
                inString = !inString;
            }
            else if (c == '=' && !inString)
            {
                return i;
            }
        }

        return -1;
    }

    private static string StripComment(string line)
    {
        var inString = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"' && (i == 0 || line[i - 1] != '\\'))
            {
                inString = !inString;
            }
            else if (c == '#' && !inString)
            {
                return line[..i];
            }
        }

        return line;
    }

    private static object? ParseValue(string line, ref int position, int lineIndex, int depth = 0)
    {
        SkipWhitespace(line, ref position);
        if (position >= line.Length)
        {
            throw Error(lineIndex, "missing value");
        }

        var c = line[position];
        return c switch
        {
            '"' => ParseString(line, ref position, lineIndex),
            '[' => ParseArray(line, ref position, lineIndex, depth + 1),
            '{' => ParseInlineTable(line, ref position, lineIndex, depth + 1),
            _ => ParseScalar(line, ref position, lineIndex),
        };
    }

    private static string ParseString(string line, ref int position, int lineIndex)
    {
        position++; // opening quote
        var builder = new StringBuilder();

        while (position < line.Length)
        {
            var c = line[position++];
            if (c == '"')
            {
                return builder.ToString();
            }

            if (c != '\\')
            {
                builder.Append(c);
                continue;
            }

            if (position >= line.Length)
            {
                break;
            }

            var escape = line[position++];
            builder.Append(escape switch
            {
                'n' => '\n',
                't' => '\t',
                'r' => '\r',
                '"' => '"',
                '\\' => '\\',
                _ => throw Error(lineIndex, $"unsupported escape '\\{escape}'"),
            });
        }

        throw Error(lineIndex, "unterminated string");
    }

    private static List<object?> ParseArray(string line, ref int position, int lineIndex, int depth)
    {
        if (depth > MaxDepth)
        {
            throw Error(lineIndex, $"arrays and inline tables nest deeper than {MaxDepth}");
        }

        position++; // '['
        var items = new List<object?>();

        while (true)
        {
            SkipWhitespace(line, ref position);
            if (position >= line.Length)
            {
                throw Error(lineIndex, "unterminated array");
            }

            if (line[position] == ']')
            {
                position++;
                return items;
            }

            items.Add(ParseValue(line, ref position, lineIndex, depth));

            SkipWhitespace(line, ref position);
            if (position < line.Length && line[position] == ',')
            {
                position++;
            }
        }
    }

    private static TomlTable ParseInlineTable(string line, ref int position, int lineIndex, int depth)
    {
        if (depth > MaxDepth)
        {
            throw Error(lineIndex, $"arrays and inline tables nest deeper than {MaxDepth}");
        }

        position++; // '{'
        var table = new TomlTable();

        while (true)
        {
            SkipWhitespace(line, ref position);
            if (position >= line.Length)
            {
                throw Error(lineIndex, "unterminated inline table");
            }

            if (line[position] == '}')
            {
                position++;
                return table;
            }

            var keyStart = position;
            while (position < line.Length && line[position] != '=' && line[position] != '}')
            {
                position++;
            }

            if (position >= line.Length || line[position] != '=')
            {
                throw Error(lineIndex, "expected 'key = value' in inline table");
            }

            var key = line[keyStart..position].Trim();
            position++; // '='
            table.Set(key, ParseValue(line, ref position, lineIndex, depth));

            SkipWhitespace(line, ref position);
            if (position < line.Length && line[position] == ',')
            {
                position++;
            }
        }
    }

    private static object ParseScalar(string line, ref int position, int lineIndex)
    {
        var start = position;
        while (position < line.Length && line[position] != ',' && line[position] != '}' && line[position] != ']')
        {
            position++;
        }

        var token = line[start..position].Trim();
        if (token.Length == 0)
        {
            throw Error(lineIndex, "missing value");
        }

        if (token == "true")
        {
            return true;
        }

        if (token == "false")
        {
            return false;
        }

        if (long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return integer;
        }

        if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var real))
        {
            return real;
        }

        throw Error(lineIndex, $"unsupported value '{token}'");
    }

    private static void SkipWhitespace(string line, ref int position)
    {
        while (position < line.Length && char.IsWhiteSpace(line[position]))
        {
            position++;
        }
    }

    private static FormatException Error(int lineIndex, string message)
        => new($"line {lineIndex + 1}: {message}");
}
