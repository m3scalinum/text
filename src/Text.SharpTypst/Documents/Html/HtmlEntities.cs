using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace M3scalinum.Text.SharpTypst.Documents;

/// <summary>Decodes the HTML character references the importer recognises.</summary>
/// <remarks>
/// The table is deliberately a fixed list rather than the full HTML 5 entity set: a document that
/// needs the rest can decode it before handing the markup over. An unknown reference is left
/// verbatim, never dropped.
/// </remarks>
internal static class HtmlEntities
{
    private static readonly Dictionary<string, string> Named = new(StringComparer.Ordinal)
    {
        ["amp"] = "&",
        ["apos"] = "'",
        ["bull"] = "\u2022",
        ["cent"] = "\u00A2",
        ["copy"] = "\u00A9",
        ["dagger"] = "\u2020",
        ["Dagger"] = "\u2021",
        ["deg"] = "\u00B0",
        ["divide"] = "\u00F7",
        ["emsp"] = "\u2003",
        ["ensp"] = "\u2002",
        ["euro"] = "\u20AC",
        ["frac12"] = "\u00BD",
        ["frac14"] = "\u00BC",
        ["frac34"] = "\u00BE",
        ["gt"] = ">",
        ["hellip"] = "\u2026",
        ["laquo"] = "\u00AB",
        ["ldquo"] = "\u201C",
        ["lsquo"] = "\u2018",
        ["lt"] = "<",
        ["mdash"] = "\u2014",
        ["micro"] = "\u00B5",
        ["middot"] = "\u00B7",
        ["nbsp"] = "\u00A0",
        ["ndash"] = "\u2013",
        ["para"] = "\u00B6",
        ["plusmn"] = "\u00B1",
        ["pound"] = "\u00A3",
        ["quot"] = "\"",
        ["raquo"] = "\u00BB",
        ["rdquo"] = "\u201D",
        ["reg"] = "\u00AE",
        ["rsquo"] = "\u2019",
        ["sect"] = "\u00A7",
        ["shy"] = "\u00AD",
        ["sup2"] = "\u00B2",
        ["sup3"] = "\u00B3",
        ["thinsp"] = "\u2009",
        ["times"] = "\u00D7",
        ["trade"] = "\u2122",
        ["yen"] = "\u00A5",
    };

    /// <summary>Decodes the references in a run of text or in an attribute value.</summary>
    public static string Decode(ReadOnlySpan<char> text)
    {
        var start = text.IndexOf('&');

        if (start < 0)
        {
            return text.ToString();
        }

        var builder = new StringBuilder(text.Length);
        var copied = 0;
        var index = start;

        while (index < text.Length)
        {
            if (text[index] != '&')
            {
                index++;
                continue;
            }

            var end = FindTerminator(text, index);

            if (end < 0)
            {
                index++;
                continue;
            }

            if (TryDecode(text[(index + 1)..end], out var value))
            {
                builder.Append(text[copied..index]).Append(value);
                copied = end + 1;
            }

            index = end + 1;
        }

        if (copied == 0)
        {
            return text.ToString();
        }

        builder.Append(text[copied..]);
        return builder.ToString();
    }

    /// <summary>Finds the semicolon that ends a reference, or -1 when this ampersand starts none.</summary>
    private static int FindTerminator(ReadOnlySpan<char> text, int ampersand)
    {
        // The longest named reference in the table is far below 32 characters. A longer run is
        // text, not a reference, and scanning it would be the only unbounded work in the tokenizer.
        var limit = Math.Min(text.Length, ampersand + 34);

        for (var index = ampersand + 1; index < limit; index++)
        {
            var c = text[index];

            if (c == ';')
            {
                return index;
            }

            if (c is '&' or '<' || char.IsWhiteSpace(c))
            {
                return -1;
            }
        }

        return -1;
    }

    private static bool TryDecode(ReadOnlySpan<char> name, out string value)
    {
        if (name.Length > 1 && name[0] == '#')
        {
            return TryDecodeNumeric(name, out value);
        }

        return Named.TryGetValue(new string(name), out value!);
    }

    private static bool TryDecodeNumeric(ReadOnlySpan<char> name, out string value)
    {
        value = string.Empty;

        if (name.Length > 2 && name[1] is 'x' or 'X')
        {
            if (!int.TryParse(name[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
            {
                return false;
            }

            return FromCodePoint(hex, out value);
        }

        if (!int.TryParse(name[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            return false;
        }

        return FromCodePoint(number, out value);
    }

    private static bool FromCodePoint(int codePoint, out string value)
    {
        value = string.Empty;

        if (codePoint <= 0 || codePoint > 0x10FFFF || (codePoint >= 0xD800 && codePoint <= 0xDFFF))
        {
            return false;
        }

        value = char.ConvertFromUtf32(codePoint);
        return true;
    }
}
