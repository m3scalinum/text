using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace M3scalinum.Text.Pdf;

/// <summary>Regexes for the handful of PDF constructs this package reads and writes.</summary>
internal static class PdfSyntax
{
    private const RegexOptions Options =
        RegexOptions.CultureInvariant | RegexOptions.Singleline;

    /// <summary>The value of <c>/Name</c> when it holds a name, without the slash.</summary>
    public static string? Name(string body, string key)
    {
        var match = Regex.Match(body, "/" + Regex.Escape(key) + @"\s*/([^\s/<>\[\]()]+)", Options);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>Whether the dictionary carries <c>/Key</c> at all.</summary>
    public static bool Has(string body, string key)
        => Regex.IsMatch(body, "/" + Regex.Escape(key) + @"(?![A-Za-z0-9])", Options);

    /// <summary>The number of <c>/Key 12</c>.</summary>
    public static int? Integer(string body, string key)
    {
        var match = Regex.Match(
            body,
            "/" + Regex.Escape(key) + @"\s*(\d+)(?![A-Za-z0-9])",
            Options);

        return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>The object number of <c>/Key 12 0 R</c>.</summary>
    public static bool TryReference(string body, string key, out int number)
    {
        var match = Regex.Match(
            body,
            "/" + Regex.Escape(key) + @"\s*(\d+)\s+0\s+R",
            Options);

        number = match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        return match.Success;
    }

    /// <summary>The contents of <c>/Key [...]</c>, brackets excluded.</summary>
    public static string? ArrayBody(string body, string key)
    {
        var match = Regex.Match(body, "/" + Regex.Escape(key) + @"\s*\[([^\]]*)\]", Options);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>The text of a literal string, <c>/Key (value)</c>, with escapes resolved.</summary>
    public static string? Text(string body, string key)
    {
        var match = Regex.Match(
            body,
            "/" + Regex.Escape(key) + @"\s*\(((?:[^()\\]|\\.)*)\)",
            Options);

        return match.Success ? Unescape(match.Groups[1].Value) : null;
    }

    /// <summary>Resolves the escapes of a PDF literal string.</summary>
    public static string Unescape(string value)
    {
        if (!value.Contains('\\', StringComparison.Ordinal))
        {
            return value;
        }

        var builder = new System.Text.StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (current != '\\' || index == value.Length - 1)
            {
                builder.Append(current);
                continue;
            }

            index++;
            var escaped = value[index];

            switch (escaped)
            {
                case 'n': builder.Append('\n'); break;
                case 'r': builder.Append('\r'); break;
                case 't': builder.Append('\t'); break;
                case 'b': builder.Append('\b'); break;
                case 'f': builder.Append('\f'); break;
                case '(': builder.Append('('); break;
                case ')': builder.Append(')'); break;
                case '\\': builder.Append('\\'); break;
                default:
                    if (escaped >= '0' && escaped <= '7')
                    {
                        var digits = 1;
                        var code = escaped - '0';
                        while (digits < 3 && index + 1 < value.Length && value[index + 1] >= '0' && value[index + 1] <= '7')
                        {
                            index++;
                            digits++;
                            code = (code * 8) + (value[index] - '0');
                        }

                        builder.Append((char)code);
                        break;
                    }

                    builder.Append(escaped);
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Parses a PDF date, <c>D:YYYYMMDDHHmmSS+HH'mm'</c>, tolerating the truncated forms readers
    /// accept. Returns <see langword="null"/> when the text is not a date.
    /// </summary>
    public static DateTimeOffset? Date(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        if (text.StartsWith("D:", StringComparison.Ordinal))
        {
            text = text.Substring(2);
        }

        var digits = 0;
        while (digits < text.Length && char.IsDigit(text[digits]))
        {
            digits++;
        }

        if (digits < 4)
        {
            return null;
        }

        var stamp = text.Substring(0, digits);
        var rest = text.Substring(digits);

        var year = Number(stamp, 0, 4);
        var month = Number(stamp, 4, 2) ?? 1;
        var day = Number(stamp, 6, 2) ?? 1;
        var hour = Number(stamp, 8, 2) ?? 0;
        var minute = Number(stamp, 10, 2) ?? 0;
        var second = Number(stamp, 12, 2) ?? 0;

        if (year is null || month is < 1 or > 12 || day is < 1 or > 31 || hour > 23 || minute > 59 || second > 59)
        {
            return null;
        }

        var offset = TimeSpan.Zero;
        if (rest.Length > 0 && (rest[0] == '+' || rest[0] == '-'))
        {
            var sign = rest[0] == '-' ? -1 : 1;
            var offsetDigits = Regex.Match(rest.Substring(1), @"^(\d{0,2})'?(\d{0,2})'?");
            var hours = offsetDigits.Groups[1].Success && offsetDigits.Groups[1].Length > 0
                ? int.Parse(offsetDigits.Groups[1].Value, CultureInfo.InvariantCulture)
                : 0;
            var minutes = offsetDigits.Groups[2].Success && offsetDigits.Groups[2].Length > 0
                ? int.Parse(offsetDigits.Groups[2].Value, CultureInfo.InvariantCulture)
                : 0;

            offset = new TimeSpan(sign * hours, sign * minutes, 0);
        }
        else if (rest.Length > 0 && rest[0] == 'Z')
        {
            offset = TimeSpan.Zero;
        }

        try
        {
            return new DateTimeOffset(year.Value, month, day, hour, minute, Math.Min(second, 59), offset);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>A PDF number: invariant, no exponent, no trailing zeros.</summary>
    public static string Number(double value)
        => Math.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>
    /// The index of the <c>&gt;&gt;</c> that closes the dictionary opening at
    /// <paramref name="openIndex"/>, or -1. Nested dictionaries, hex strings and literal strings are
    /// skipped, so a catalog that already holds an <c>/AcroForm</c> still finds its own end.
    /// </summary>
    public static int CloseDictionary(string body, int openIndex)
    {
        if (openIndex < 0 || openIndex + 1 >= body.Length || body[openIndex] != '<' || body[openIndex + 1] != '<')
        {
            return -1;
        }

        var depth = 0;
        var index = openIndex;

        while (index < body.Length - 1)
        {
            var current = body[index];

            if (current == '<' && body[index + 1] == '<')
            {
                depth++;
                index += 2;
                continue;
            }

            if (current == '>' && body[index + 1] == '>')
            {
                depth--;
                index += 2;

                if (depth == 0)
                {
                    return index - 2;
                }

                continue;
            }

            if (current == '<')
            {
                var end = body.IndexOf('>', index);
                index = end < 0 ? body.Length : end + 1;
                continue;
            }

            if (current == '(')
            {
                index = SkipLiteral(body, index);
                continue;
            }

            index++;
        }

        return -1;
    }

    private static int SkipLiteral(string body, int openIndex)
    {
        var depth = 0;

        for (var index = openIndex; index < body.Length; index++)
        {
            var current = body[index];

            if (current == '\\')
            {
                index++;
                continue;
            }

            if (current == '(')
            {
                depth++;
                continue;
            }

            if (current == ')')
            {
                depth--;

                if (depth == 0)
                {
                    return index + 1;
                }
            }
        }

        return body.Length;
    }

    private static int? Number(string text, int index, int length)
    {
        if (text.Length < index + length)
        {
            return null;
        }

        var slice = text.AsSpan(index, length);
        return int.TryParse(slice, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}
