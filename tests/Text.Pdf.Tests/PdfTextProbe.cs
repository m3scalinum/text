using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// Reads the text a page draws, through the fonts' <c>/ToUnicode</c> maps. It is a test probe, not a
/// text extractor: it concatenates the strings the content streams show and ignores positioning, so
/// a word comes out whole but the layout does not.
/// </summary>
/// <remarks>
/// A producer subsets its fonts and writes glyph identifiers, not characters: the page cannot be read
/// without the CMap. Mapping them is what makes "the text survived the merge" a claim about the
/// characters the reader will see, rather than about bytes that happen to be equal.
/// </remarks>
internal static class PdfTextProbe
{
    /// <summary>The text drawn by each page, in page order.</summary>
    public static IReadOnlyList<string> Pages(byte[] pdf)
    {
        Assert.True(PdfFile.TryOpen(pdf, out var file), "the bytes were not read as a PDF");

        var pages = new List<string>();
        foreach (var page in file!.Pages())
        {
            pages.Add(Text(file, page));
        }

        return pages;
    }

    /// <summary>The text drawn by the page at <paramref name="index"/>, zero-based.</summary>
    public static string Page(byte[] pdf, int index) => Pages(pdf)[index];

    private static string Text(PdfFile file, PdfPage page)
    {
        var fonts = Fonts(file, page);
        var builder = new StringBuilder();
        var operands = new List<string>();
        Dictionary<int, string>? map = null;
        var codeLength = 0;

        foreach (Match token in Tokens().Matches(Content(file, page.Dictionary)))
        {
            if (token.Groups["name"].Success)
            {
                operands.Add("/" + token.Groups["name"].Value);
                continue;
            }

            if (token.Groups["number"].Success)
            {
                operands.Add(token.Value);
                continue;
            }

            if (token.Groups["literal"].Success)
            {
                operands.Add("s:" + PdfSyntax.Unescape(token.Groups["literal"].Value));
                continue;
            }

            if (token.Groups["hex"].Success)
            {
                var digits = new string(token.Groups["hex"].Value.Where(Uri.IsHexDigit).ToArray());
                if (digits.Length % 2 != 0)
                {
                    digits += "0";
                }

                operands.Add("s:" + Encoding.Latin1.GetString(Convert.FromHexString(digits)));
                continue;
            }

            switch (token.Groups["operator"].Value)
            {
                case "Tf" when operands.Count >= 2 && fonts.TryGetValue(operands[^2].TrimStart('/'), out var font):
                    map = font.Map;
                    codeLength = font.CodeLength;
                    break;
                case "Tj" or "'" or "\"" when map is not null && operands.Count > 0 && operands[^1].StartsWith("s:"):
                    builder.Append(Decode(operands[^1][2..], map, codeLength));
                    break;
                case "TJ" when map is not null:
                    foreach (var operand in operands)
                    {
                        if (operand.StartsWith("s:"))
                        {
                            builder.Append(Decode(operand[2..], map, codeLength));
                        }
                    }

                    break;
            }

            operands.Clear();
        }

        return builder.ToString();
    }

    private static string Decode(string text, Dictionary<int, string> map, int codeLength)
    {
        if (codeLength <= 0)
        {
            return string.Empty;
        }

        var bytes = Encoding.Latin1.GetBytes(text);
        var builder = new StringBuilder();

        for (var index = 0; index + codeLength <= bytes.Length; index += codeLength)
        {
            var code = 0;
            for (var offset = 0; offset < codeLength; offset++)
            {
                code = (code << 8) | bytes[index + offset];
            }

            if (map.TryGetValue(code, out var value))
            {
                builder.Append(value);
            }
        }

        return builder.ToString();
    }

    private static string Content(PdfFile file, PdfDictionary page)
    {
        var contents = page["Contents"];
        var items = contents is PdfArray array ? array.Items : [contents ?? PdfValue.Null];
        var builder = new StringBuilder();

        foreach (var item in items)
        {
            var stream = file.ResolveStream(item);
            if (stream is null || file.Decode(stream) is not { } data)
            {
                continue;
            }

            builder.Append(Encoding.Latin1.GetString(data)).Append('\n');
        }

        return builder.ToString();
    }

    private static Dictionary<string, FontMap> Fonts(PdfFile file, PdfPage page)
    {
        var fonts = new Dictionary<string, FontMap>(StringComparer.Ordinal);
        var dictionary = page.Resources is null ? null : file.ResolveDictionary(page.Resources["Font"]);

        if (dictionary is null)
        {
            return fonts;
        }

        foreach (var (name, value) in dictionary.Entries)
        {
            var font = file.ResolveDictionary(value);
            var toUnicode = font is null ? null : file.ResolveStream(font["ToUnicode"]);
            var data = toUnicode is null ? null : file.Decode(toUnicode);

            if (data is null)
            {
                continue;
            }

            var (map, codeLength) = Cmap(Encoding.Latin1.GetString(data));
            if (map.Count > 0)
            {
                fonts[name] = new FontMap(map, codeLength);
            }
        }

        return fonts;
    }

    private static (Dictionary<int, string> Map, int CodeLength) Cmap(string text)
    {
        var map = new Dictionary<int, string>();
        var codeLength = 0;

        foreach (Match block in Regex.Matches(
            text,
            @"beginbfchar(.*?)endbfchar",
            RegexOptions.Singleline | RegexOptions.CultureInvariant))
        {
            foreach (Match pair in Regex.Matches(
                block.Groups[1].Value,
                @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>",
                RegexOptions.CultureInvariant))
            {
                var source = pair.Groups[1].Value;
                codeLength = Math.Max(codeLength, source.Length / 2);
                map[Convert.ToInt32(source, 16)] = Utf16(pair.Groups[2].Value);
            }
        }

        foreach (Match block in Regex.Matches(
            text,
            @"beginbfrange(.*?)endbfrange",
            RegexOptions.Singleline | RegexOptions.CultureInvariant))
        {
            foreach (Match range in Regex.Matches(
                block.Groups[1].Value,
                @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*(?:<([0-9A-Fa-f]+)>|\[([^\]]*)\])",
                RegexOptions.CultureInvariant))
            {
                var first = Convert.ToInt32(range.Groups[1].Value, 16);
                var last = Convert.ToInt32(range.Groups[2].Value, 16);
                codeLength = Math.Max(codeLength, range.Groups[1].Value.Length / 2);

                if (range.Groups[3].Success)
                {
                    var start = Convert.FromHexString(range.Groups[3].Value);

                    for (var code = first; code <= last; code++)
                    {
                        map[code] = Utf16(Increment(start, code - first));
                    }
                }
                else
                {
                    var destinations = Regex.Matches(
                        range.Groups[4].Value,
                        @"<([0-9A-Fa-f]+)>",
                        RegexOptions.CultureInvariant);

                    for (var offset = 0; offset < destinations.Count && first + offset <= last; offset++)
                    {
                        map[first + offset] = Utf16(destinations[offset].Groups[1].Value);
                    }
                }
            }
        }

        return (map, codeLength);
    }

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

    private static string Utf16(string hex)
        => Encoding.BigEndianUnicode.GetString(Convert.FromHexString(hex));

    private static string Utf16(byte[] bytes) => Encoding.BigEndianUnicode.GetString(bytes);

    private static Regex Tokens() => new(
        @"/(?<name>[^\s/<>\[\]()%{}]+)" +
        @"|(?<number>[-+]?(?:\d+\.?\d*|\.\d+))" +
        @"|\((?<literal>(?:\\.|[^\\()])*)\)" +
        @"|<(?<hex>[0-9A-Fa-f\s]+)>" +
        @"|(?<operator>[A-Za-z'""*]+)",
        RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private sealed record FontMap(Dictionary<int, string> Map, int CodeLength);
}
