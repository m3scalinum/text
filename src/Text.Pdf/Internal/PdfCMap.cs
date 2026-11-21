using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// A font's <c>/ToUnicode</c> CMap: the codes of a shown string and the characters they stand for.
/// </summary>
/// <remarks>
/// Only the parts text extraction needs are read: the code-space ranges, which say how many bytes a
/// code takes, and the <c>bfchar</c> and <c>bfrange</c> blocks, in their one-destination and array
/// forms. The rest of a CMap — writing modes, the CID system information, usecmap — describes how a
/// glyph is shaped, which is a renderer's problem, not a reader's.
/// </remarks>
internal static class CMap
{
    /// <summary>Reads the map. <paramref name="codeLength"/> is 0 when no code-space range was declared.</summary>
    /// <param name="data">The decoded CMap stream.</param>
    /// <param name="codeLength">The bytes per code the CMap declares, or 0.</param>
    public static Dictionary<int, string> Parse(byte[] data, out int codeLength)
    {
        codeLength = 0;

        var map = new Dictionary<int, string>();
        var tokens = Tokenize(data);

        for (var index = 0; index < tokens.Count; index++)
        {
            switch (tokens[index].Word)
            {
                case "begincodespacerange":
                    index = CodeSpaceRanges(tokens, index + 1, ref codeLength);
                    break;
                case "beginbfchar":
                    index = Ranges(tokens, index + 1, "endbfchar", map, ref codeLength);
                    break;
                case "beginbfrange":
                    index = Ranges(tokens, index + 1, "endbfrange", map, ref codeLength);
                    break;
            }
        }

        return map;
    }

    /// <summary>Reads the <c>&lt;lo&gt; &lt;hi&gt;</c> pairs of a code-space block; only the width is used.</summary>
    private static int CodeSpaceRanges(List<CMapToken> tokens, int index, ref int codeLength)
    {
        while (index < tokens.Count && tokens[index].Word != "endcodespacerange")
        {
            if (tokens[index].Kind != CMapTokenKind.Hex)
            {
                index++;
                continue;
            }

            var low = tokens[index].Bytes!;
            codeLength = Math.Max(codeLength, low.Length);
            index++;

            if (index < tokens.Count && tokens[index].Kind == CMapTokenKind.Hex)
            {
                index++;
            }
        }

        return index;
    }

    /// <summary>
    /// Reads a <c>bfchar</c> or <c>bfrange</c> block up to its end keyword. The two blocks share a
    /// terminator and the same destination forms, so one loop reads both.
    /// </summary>
    private static int Ranges(
        List<CMapToken> tokens,
        int index,
        string end,
        Dictionary<int, string> map,
        ref int codeLength)
    {
        var isRange = string.Equals(end, "endbfrange", StringComparison.Ordinal);

        while (index < tokens.Count && tokens[index].Word != end)
        {
            if (tokens[index].Kind != CMapTokenKind.Hex)
            {
                index++;
                continue;
            }

            var source = tokens[index].Bytes!;
            codeLength = Math.Max(codeLength, source.Length);
            var start = Number(source);
            index++;

            var last = start;
            if (isRange)
            {
                if (index >= tokens.Count || tokens[index].Kind != CMapTokenKind.Hex)
                {
                    continue;
                }

                last = Number(tokens[index].Bytes!);
                index++;
            }

            if (index >= tokens.Count)
            {
                break;
            }

            var destination = tokens[index];
            index++;

            if (destination.Kind == CMapTokenKind.Hex)
            {
                var bytes = destination.Bytes!;
                for (var code = start; code <= last; code++)
                {
                    map[code] = Utf16(Increment(bytes, code - start));
                }

                continue;
            }

            if (destination.Kind == CMapTokenKind.Name)
            {
                var text = GlyphNames.ToUnicode(destination.Word!);
                if (text is not null)
                {
                    map[start] = text;
                }

                continue;
            }

            if (destination.Word != "[")
            {
                continue;
            }

            // The array form: one destination per code, from start to last inclusive.
            var offset = 0;
            while (index < tokens.Count && tokens[index].Word != "]")
            {
                if (tokens[index].Kind == CMapTokenKind.Hex)
                {
                    map[start + offset] = Utf16(tokens[index].Bytes!);
                    offset++;
                }

                index++;
            }

            if (index < tokens.Count)
            {
                index++;
            }
        }

        return index;
    }

    private static List<CMapToken> Tokenize(byte[] data)
    {
        var tokens = new List<CMapToken>();
        var position = 0;

        while (position < data.Length)
        {
            var value = data[position];

            if (value is 0 or 9 or 10 or 12 or 13 or 32)
            {
                position++;
                continue;
            }

            if (value == (byte)'%')
            {
                while (position < data.Length && data[position] is not (10 or 13))
                {
                    position++;
                }

                continue;
            }

            if (value == (byte)'<')
            {
                position++;
                var digits = new StringBuilder();
                while (position < data.Length && data[position] != (byte)'>')
                {
                    var current = (char)data[position];
                    if (char.IsAsciiHexDigit(current))
                    {
                        digits.Append(current);
                    }

                    position++;
                }

                position++;
                tokens.Add(new CMapToken(CMapTokenKind.Hex, Bytes(digits.ToString()), null));
                continue;
            }

            if (value == (byte)'/' || value == (byte)'[' || value == (byte)']')
            {
                position++;
                if (value == (byte)'/')
                {
                    var start = position;
                    while (position < data.Length && !IsSeparator(data[position]))
                    {
                        position++;
                    }

                    tokens.Add(new CMapToken(
                        CMapTokenKind.Name,
                        null,
                        Encoding.Latin1.GetString(data, start, position - start)));
                }
                else
                {
                    tokens.Add(new CMapToken(CMapTokenKind.Word, null, ((char)value).ToString()));
                }

                continue;
            }

            if (IsSeparator(value))
            {
                // A PostScript comment or a delimiter this reader does not track: skip it rather
                // than reading the same byte forever.
                position++;
                continue;
            }

            var wordStart = position;
            while (position < data.Length && !IsSeparator(data[position]))
            {
                position++;
            }

            var word = Encoding.Latin1.GetString(data, wordStart, position - wordStart);
            tokens.Add(double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                ? new CMapToken(CMapTokenKind.Number, null, word)
                : new CMapToken(CMapTokenKind.Word, null, word));
        }

        return tokens;
    }

    private static bool IsSeparator(byte value)
        => value is 0 or 9 or 10 or 12 or 13 or 32
            or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or (byte)'/' or (byte)'(' or (byte)')'
            or (byte)'{' or (byte)'}' or (byte)'%';

    private static byte[] Bytes(string digits)
    {
        if (digits.Length % 2 != 0)
        {
            digits += "0";
        }

        return digits.Length == 0 ? [] : Convert.FromHexString(digits);
    }

    private static int Number(byte[] bytes)
    {
        var value = 0;
        foreach (var b in bytes)
        {
            value = (value << 8) | b;
            if (value < 0)
            {
                return int.MaxValue;
            }
        }

        return value;
    }

    private static string Utf16(byte[] bytes)
        => bytes.Length == 0 ? string.Empty : Encoding.BigEndianUnicode.GetString(bytes);

    /// <summary>Adds <paramref name="delta"/> to a big-endian destination, keeping its width.</summary>
    private static byte[] Increment(byte[] value, int delta)
    {
        var result = (byte[])value.Clone();
        var carry = delta;

        for (var index = result.Length - 1; index >= 0 && carry > 0; index--)
        {
            var sum = result[index] + carry;
            result[index] = (byte)sum;
            carry = sum >> 8;
        }

        return result;
    }

    private enum CMapTokenKind
    {
        Hex,
        Name,
        Word,
        Number,
    }

    private readonly record struct CMapToken(CMapTokenKind Kind, byte[]? Bytes, string? Word);
}
