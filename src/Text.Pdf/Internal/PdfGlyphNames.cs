using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// The glyph names of the Adobe Glyph List, as far as text extraction needs them: the ASCII names,
/// the Latin-1 names a simple font's <c>/Differences</c> array actually uses, and the typographic
/// punctuation a producer maps by name.
/// </summary>
/// <remarks>
/// This is deliberately not the whole list. A name it does not know returns <see langword="null"/>,
/// and the caller falls back to the raw code and reports the font, which is honest: inventing a
/// character for a name would be worse than saying the name was not understood.
/// </remarks>
internal static class GlyphNames
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal)
    {
        ["space"] = " ",
        ["exclam"] = "!",
        ["quotedbl"] = "\"",
        ["numbersign"] = "#",
        ["dollar"] = "$",
        ["percent"] = "%",
        ["ampersand"] = "&",
        ["quotesingle"] = "'",
        ["parenleft"] = "(",
        ["parenright"] = ")",
        ["asterisk"] = "*",
        ["plus"] = "+",
        ["comma"] = ",",
        ["hyphen"] = "-",
        ["period"] = ".",
        ["slash"] = "/",
        ["zero"] = "0",
        ["one"] = "1",
        ["two"] = "2",
        ["three"] = "3",
        ["four"] = "4",
        ["five"] = "5",
        ["six"] = "6",
        ["seven"] = "7",
        ["eight"] = "8",
        ["nine"] = "9",
        ["colon"] = ":",
        ["semicolon"] = ";",
        ["less"] = "<",
        ["equal"] = "=",
        ["greater"] = ">",
        ["question"] = "?",
        ["at"] = "@",
        ["bracketleft"] = "[",
        ["backslash"] = "\\",
        ["bracketright"] = "]",
        ["asciicircum"] = "^",
        ["underscore"] = "_",
        ["grave"] = "`",
        ["braceleft"] = "{",
        ["bar"] = "|",
        ["braceright"] = "}",
        ["asciitilde"] = "~",
        ["exclamdown"] = "\u00A1",
        ["cent"] = "\u00A2",
        ["sterling"] = "\u00A3",
        ["currency"] = "\u00A4",
        ["yen"] = "\u00A5",
        ["brokenbar"] = "\u00A6",
        ["section"] = "\u00A7",
        ["dieresis"] = "\u00A8",
        ["copyright"] = "\u00A9",
        ["ordfeminine"] = "\u00AA",
        ["guillemotleft"] = "\u00AB",
        ["logicalnot"] = "\u00AC",
        ["registered"] = "\u00AE",
        ["macron"] = "\u00AF",
        ["degree"] = "\u00B0",
        ["plusminus"] = "\u00B1",
        ["twosuperior"] = "\u00B2",
        ["threesuperior"] = "\u00B3",
        ["acute"] = "\u00B4",
        ["mu"] = "\u00B5",
        ["paragraph"] = "\u00B6",
        ["periodcentered"] = "\u00B7",
        ["cedilla"] = "\u00B8",
        ["onesuperior"] = "\u00B9",
        ["ordmasculine"] = "\u00BA",
        ["guillemotright"] = "\u00BB",
        ["onequarter"] = "\u00BC",
        ["onehalf"] = "\u00BD",
        ["threequarters"] = "\u00BE",
        ["questiondown"] = "\u00BF",
        ["nbspace"] = "\u00A0",
        ["sfthyphen"] = "\u00AD",
        ["AE"] = "\u00C6",
        ["ordm"] = "\u00BA",
        ["germandbls"] = "\u00DF",
        ["ae"] = "\u00E6",
        ["oslash"] = "\u00F8",
        ["Oslash"] = "\u00D8",
        ["dotlessi"] = "\u0131",
        ["Lslash"] = "\u0141",
        ["lslash"] = "\u0142",
        ["OE"] = "\u0152",
        ["oe"] = "\u0153",
        ["Scaron"] = "\u0160",
        ["scaron"] = "\u0161",
        ["Zcaron"] = "\u017D",
        ["zcaron"] = "\u017E",
        ["Ydieresis"] = "\u0178",
        ["florin"] = "\u0192",
        ["circumflex"] = "\u02C6",
        ["caron"] = "\u02C7",
        ["breve"] = "\u02D8",
        ["dotaccent"] = "\u02D9",
        ["ring"] = "\u02DA",
        ["ogonek"] = "\u02DB",
        ["tilde"] = "\u02DC",
        ["hungarumlaut"] = "\u02DD",
        ["endash"] = "\u2013",
        ["emdash"] = "\u2014",
        ["quoteleft"] = "\u2018",
        ["quoteright"] = "\u2019",
        ["quotesinglbase"] = "\u201A",
        ["quotedblleft"] = "\u201C",
        ["quotedblright"] = "\u201D",
        ["quotedblbase"] = "\u201E",
        ["dagger"] = "\u2020",
        ["daggerdbl"] = "\u2021",
        ["bullet"] = "\u2022",
        ["ellipsis"] = "\u2026",
        ["perthousand"] = "\u2030",
        ["guilsinglleft"] = "\u2039",
        ["guilsinglright"] = "\u203A",
        ["fraction"] = "\u2044",
        ["Euro"] = "\u20AC",
        ["trademark"] = "\u2122",
        ["fi"] = "\uFB01",
        ["fl"] = "\uFB02",
        ["minus"] = "\u2212",
    };

    /// <summary>The names of the Latin-1 accented letters, at codes 0xC0 to 0xFF in order.</summary>
    private static readonly string[] Latin1 =
    [
        "Agrave", "Aacute", "Acircumflex", "Atilde", "Adieresis", "Aring", "AE", "Ccedilla",
        "Egrave", "Eacute", "Ecircumflex", "Edieresis", "Igrave", "Iacute", "Icircumflex", "Idieresis",
        "Eth", "Ntilde", "Ograve", "Oacute", "Ocircumflex", "Otilde", "Odieresis", "multiply",
        "Oslash", "Ugrave", "Uacute", "Ucircumflex", "Udieresis", "Yacute", "Thorn", "germandbls",
        "agrave", "aacute", "acircumflex", "atilde", "adieresis", "aring", "ae", "ccedilla",
        "egrave", "eacute", "ecircumflex", "edieresis", "igrave", "iacute", "icircumflex", "idieresis",
        "eth", "ntilde", "ograve", "oacute", "ocircumflex", "otilde", "odieresis", "divide",
        "oslash", "ugrave", "uacute", "ucircumflex", "udieresis", "yacute", "thorn", "ydieresis",
    ];

    static GlyphNames()
    {
        for (var index = 0; index < Latin1.Length; index++)
        {
            Names[Latin1[index]] = ((char)(0xC0 + index)).ToString();
        }
    }

    /// <summary>The characters a glyph name stands for, or <see langword="null"/> when it is unknown.</summary>
    /// <param name="name">A name from a <c>/Differences</c> array or a CMap destination.</param>
    public static string? ToUnicode(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        if (Names.TryGetValue(name, out var known))
        {
            return known;
        }

        // A subset font names a glyph "a.sc" or "period.alt"; the base name is what carries meaning.
        var dot = name.IndexOf('.', StringComparison.Ordinal);
        if (dot > 0 && Names.TryGetValue(name.Substring(0, dot), out var baseName))
        {
            return baseName;
        }

        if (name.Length == 1)
        {
            return name;
        }

        if (name.StartsWith("uni", StringComparison.Ordinal) && name.Length > 3 && (name.Length - 3) % 4 == 0)
        {
            var builder = new StringBuilder((name.Length - 3) / 4);
            for (var index = 3; index + 4 <= name.Length; index += 4)
            {
                if (!int.TryParse(name.AsSpan(index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                {
                    return null;
                }

                builder.Append((char)code);
            }

            return builder.ToString();
        }

        if (name[0] == 'u' && name.Length is >= 5 and <= 7 &&
            int.TryParse(name.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var scalar) &&
            scalar is > 0 and < 0x110000)
        {
            return char.ConvertFromUtf32(scalar);
        }

        return null;
    }
}
