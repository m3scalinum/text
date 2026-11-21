using System;
using System.Text;

namespace M3scalinum.Text.Charts.Internal;

/// <summary>Quoting for the Typst source the convenience method emits.</summary>
/// <remarks>
/// A Typst string literal is delimited by double quotes; a backslash, a double quote and the
/// whitespace escapes are written as escapes, and every other character stands as it is. The SVG
/// therefore reaches <c>bytes(…)</c> byte for byte, which is what makes the embedded chart identical
/// to the one <see cref="Chart.RenderSvg(ChartSpec, ChartSize)"/> returns, and the markup stays on
/// one line instead of spilling the chart's line breaks into the caller's source.
/// </remarks>
internal static class TypstText
{
    /// <summary>Returns <paramref name="value"/> as a quoted Typst string literal.</summary>
    internal static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var quoted = new StringBuilder(value.Length + 16);
        quoted.Append('"');

        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    quoted.Append("\\\"");
                    break;
                case '\\':
                    quoted.Append("\\\\");
                    break;
                case '\n':
                    quoted.Append("\\n");
                    break;
                case '\r':
                    quoted.Append("\\r");
                    break;
                case '\t':
                    quoted.Append("\\t");
                    break;
                default:
                    quoted.Append(c);
                    break;
            }
        }

        quoted.Append('"');

        return quoted.ToString();
    }

    /// <summary>Validates a Typst length: a number and an optional unit, and nothing a caller could inject.</summary>
    /// <param name="value">The length as the caller wrote it.</param>
    /// <param name="parameter">The parameter name to report.</param>
    internal static string Length(string value, string parameter)
    {
        if (!IsLength(value))
        {
            throw new ArgumentException(
                $"'{value}' is not a Typst length: write a number and an optional unit (pt, mm, cm, in, em, rem, %), for example 100% or 12cm",
                parameter);
        }

        return value;
    }

    private static bool IsLength(string value)
    {
        var index = 0;
        var digits = 0;

        while (index < value.Length && char.IsAsciiDigit(value[index]))
        {
            index++;
            digits++;
        }

        if (digits == 0)
        {
            return false;
        }

        if (index < value.Length && value[index] == '.')
        {
            index++;
            var decimals = 0;

            while (index < value.Length && char.IsAsciiDigit(value[index]))
            {
                index++;
                decimals++;
            }

            if (decimals == 0)
            {
                return false;
            }
        }

        return value[index..] is "" or "pt" or "mm" or "cm" or "in" or "em" or "rem" or "%";
    }
}
