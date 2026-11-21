using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace M3scalinum.Text.Epub.Internal;

/// <summary>
/// The HTML character references. XML knows five of them, so every other named reference has to be
/// decoded here or the XHTML would carry a bare <c>&amp;nbsp;</c> and stop being well-formed.
/// </summary>
/// <remarks>
/// The table covers the Latin-1 set, typography and the punctuation a document actually uses. An
/// unknown name is left as text, which serializes as <c>&amp;amp;name;</c>: visible, but well-formed,
/// which is the property that matters for a container.
/// </remarks>
internal static class HtmlEntities
{
    private static readonly Dictionary<string, string> Named = new(StringComparer.Ordinal)
    {
        ["amp"] = "&",
        ["lt"] = "<",
        ["gt"] = ">",
        ["quot"] = "\"",
        ["apos"] = "'",
        ["nbsp"] = "\u00A0",
        ["ensp"] = "\u2002",
        ["emsp"] = "\u2003",
        ["thinsp"] = "\u2009",
        ["shy"] = "\u00AD",
        ["copy"] = "\u00A9",
        ["reg"] = "\u00AE",
        ["trade"] = "\u2122",
        ["hellip"] = "\u2026",
        ["mdash"] = "\u2014",
        ["ndash"] = "\u2013",
        ["lsquo"] = "\u2018",
        ["rsquo"] = "\u2019",
        ["ldquo"] = "\u201C",
        ["rdquo"] = "\u201D",
        ["laquo"] = "\u00AB",
        ["raquo"] = "\u00BB",
        ["lsaquo"] = "\u2039",
        ["rsaquo"] = "\u203A",
        ["sbquo"] = "\u201A",
        ["bdquo"] = "\u201E",
        ["bull"] = "\u2022",
        ["middot"] = "\u00B7",
        ["dagger"] = "\u2020",
        ["Dagger"] = "\u2021",
        ["permil"] = "\u2030",
        ["prime"] = "\u2032",
        ["Prime"] = "\u2033",
        ["sect"] = "\u00A7",
        ["para"] = "\u00B6",
        ["deg"] = "\u00B0",
        ["plusmn"] = "\u00B1",
        ["times"] = "\u00D7",
        ["divide"] = "\u00F7",
        ["frac12"] = "\u00BD",
        ["frac14"] = "\u00BC",
        ["frac34"] = "\u00BE",
        ["sup1"] = "\u00B9",
        ["sup2"] = "\u00B2",
        ["sup3"] = "\u00B3",
        ["micro"] = "\u00B5",
        ["euro"] = "\u20AC",
        ["pound"] = "\u00A3",
        ["yen"] = "\u00A5",
        ["cent"] = "\u00A2",
        ["curren"] = "\u00A4",
        ["iexcl"] = "\u00A1",
        ["iquest"] = "\u00BF",
        ["brvbar"] = "\u00A6",
        ["uml"] = "\u00A8",
        ["ordf"] = "\u00AA",
        ["not"] = "\u00AC",
        ["macr"] = "\u00AF",
        ["acute"] = "\u00B4",
        ["cedil"] = "\u00B8",
        ["ordm"] = "\u00BA",
        ["agrave"] = "\u00E0",
        ["aacute"] = "\u00E1",
        ["acirc"] = "\u00E2",
        ["atilde"] = "\u00E3",
        ["auml"] = "\u00E4",
        ["aring"] = "\u00E5",
        ["aelig"] = "\u00E6",
        ["ccedil"] = "\u00E7",
        ["egrave"] = "\u00E8",
        ["eacute"] = "\u00E9",
        ["ecirc"] = "\u00EA",
        ["euml"] = "\u00EB",
        ["igrave"] = "\u00EC",
        ["iacute"] = "\u00ED",
        ["icirc"] = "\u00EE",
        ["iuml"] = "\u00EF",
        ["eth"] = "\u00F0",
        ["ntilde"] = "\u00F1",
        ["ograve"] = "\u00F2",
        ["oacute"] = "\u00F3",
        ["ocirc"] = "\u00F4",
        ["otilde"] = "\u00F5",
        ["ouml"] = "\u00F6",
        ["oslash"] = "\u00F8",
        ["ugrave"] = "\u00F9",
        ["uacute"] = "\u00FA",
        ["ucirc"] = "\u00FB",
        ["uuml"] = "\u00FC",
        ["yacute"] = "\u00FD",
        ["thorn"] = "\u00FE",
        ["yuml"] = "\u00FF",
        ["szlig"] = "\u00DF",
        ["Agrave"] = "\u00C0",
        ["Aacute"] = "\u00C1",
        ["Acirc"] = "\u00C2",
        ["Atilde"] = "\u00C3",
        ["Auml"] = "\u00C4",
        ["Aring"] = "\u00C5",
        ["AElig"] = "\u00C6",
        ["Ccedil"] = "\u00C7",
        ["Egrave"] = "\u00C8",
        ["Eacute"] = "\u00C9",
        ["Ecirc"] = "\u00CA",
        ["Euml"] = "\u00CB",
        ["Igrave"] = "\u00CC",
        ["Iacute"] = "\u00CD",
        ["Icirc"] = "\u00CE",
        ["Iuml"] = "\u00CF",
        ["ETH"] = "\u00D0",
        ["Ntilde"] = "\u00D1",
        ["Ograve"] = "\u00D2",
        ["Oacute"] = "\u00D3",
        ["Ocirc"] = "\u00D4",
        ["Otilde"] = "\u00D5",
        ["Ouml"] = "\u00D6",
        ["Oslash"] = "\u00D8",
        ["Ugrave"] = "\u00D9",
        ["Uacute"] = "\u00DA",
        ["Ucirc"] = "\u00DB",
        ["Uuml"] = "\u00DC",
        ["Yacute"] = "\u00DD",
        ["THORN"] = "\u00DE",
        ["alpha"] = "\u03B1",
        ["beta"] = "\u03B2",
        ["gamma"] = "\u03B3",
        ["delta"] = "\u03B4",
        ["pi"] = "\u03C0",
        ["sigma"] = "\u03C3",
        ["omega"] = "\u03C9",
        ["Omega"] = "\u03A9",
        ["larr"] = "\u2190",
        ["rarr"] = "\u2192",
        ["uarr"] = "\u2191",
        ["darr"] = "\u2193",
        ["harr"] = "\u2194",
        ["infin"] = "\u221E",
        ["le"] = "\u2264",
        ["ge"] = "\u2265",
        ["ne"] = "\u2260",
        ["minus"] = "\u2212",
        ["sum"] = "\u2211",
        ["radic"] = "\u221A",
        ["check"] = "\u2713",
    };

    /// <summary>Decodes the character references in <paramref name="text"/>.</summary>
    public static string Decode(string text)
    {
        if (text.IndexOf('&') < 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        var index = 0;
        while (index < text.Length)
        {
            var ampersand = text.IndexOf('&', index);
            if (ampersand < 0)
            {
                builder.Append(text, index, text.Length - index);
                break;
            }

            builder.Append(text, index, ampersand - index);

            var semicolon = text.IndexOf(';', ampersand + 1);
            if (semicolon < 0 || semicolon - ampersand > 32)
            {
                builder.Append('&');
                index = ampersand + 1;
                continue;
            }

            var body = text.AsSpan(ampersand + 1, semicolon - ampersand - 1);
            var decoded = DecodeReference(body);
            if (decoded is null)
            {
                builder.Append('&');
                index = ampersand + 1;
                continue;
            }

            builder.Append(decoded);
            index = semicolon + 1;
        }

        return builder.ToString();
    }

    private static string? DecodeReference(ReadOnlySpan<char> body)
    {
        if (body.Length == 0)
        {
            return null;
        }

        if (body[0] == '#')
        {
            var digits = body[1..];
            var hexadecimal = digits.Length > 1 && (digits[0] == 'x' || digits[0] == 'X');
            if (hexadecimal)
            {
                digits = digits[1..];
            }

            if (!int.TryParse(digits, hexadecimal ? NumberStyles.HexNumber : NumberStyles.None, CultureInfo.InvariantCulture, out var code))
            {
                return null;
            }

            if (code <= 0 || code > 0x10FFFF || (code >= 0xD800 && code <= 0xDFFF))
            {
                return null;
            }

            return char.ConvertFromUtf32(code);
        }

        return Named.TryGetValue(body.ToString(), out var named) ? named : null;
    }
}
