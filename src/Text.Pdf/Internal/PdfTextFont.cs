using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace M3scalinum.Text.Pdf;

/// <summary>One glyph of a shown string: what it says and how far it moves the pen.</summary>
/// <param name="Text">The characters the code maps to.</param>
/// <param name="Width">The advance in glyph space units, where 1000 units are one em.</param>
/// <param name="IsSpace">Whether the code is the single-byte space, which takes the word spacing.</param>
internal readonly record struct PdfGlyph(string Text, double Width, bool IsSpace);

/// <summary>
/// A font as text extraction needs it: how a code in a shown string becomes characters, and how wide
/// the glyph is. Widths matter because they are the only way to know whether the space between two
/// shown strings is a word gap or merely the next glyph.
/// </summary>
internal sealed class PdfTextFont
{
    /// <summary>The widest code in bytes: a Type0 font names glyphs with two, a simple font with one.</summary>
    public const int MaxCodeLength = 2;

    private readonly Dictionary<int, string>? _toUnicode;
    private readonly Dictionary<int, string> _encoding;
    private readonly Dictionary<int, double> _widths;
    private readonly int _codeLength;
    private readonly double _defaultWidth;

    private PdfTextFont(
        string name,
        Dictionary<int, string>? toUnicode,
        Dictionary<int, string> encoding,
        Dictionary<int, double> widths,
        int codeLength,
        double defaultWidth)
    {
        Name = name;
        _toUnicode = toUnicode;
        _encoding = encoding;
        _widths = widths;
        _codeLength = codeLength;
        _defaultWidth = defaultWidth;
    }

    /// <summary>The resource name the page uses for this font, for example <c>F1</c>.</summary>
    public string Name { get; }

    /// <summary>Whether the font carries a <c>/ToUnicode</c> CMap to map its codes through.</summary>
    public bool HasUnicodeMap => _toUnicode is not null;

    /// <summary>
    /// Reads <paramref name="font"/> and its <c>/ToUnicode</c> CMap. A font the reader cannot make
    /// sense of still yields an object, so a page is never lost to one unusable font: its codes come
    /// out as the raw values and <see cref="HasUnicodeMap"/> reports what happened.
    /// </summary>
    /// <param name="file">The document the font belongs to.</param>
    /// <param name="name">The resource name, reported when the page is read.</param>
    /// <param name="font">The font dictionary.</param>
    public static PdfTextFont Create(PdfFile file, string name, PdfDictionary font)
    {
        var subtype = (file.Resolve(font["Subtype"]) as PdfName)?.Value;
        var isType0 = string.Equals(subtype, "Type0", StringComparison.Ordinal);

        Dictionary<int, string>? map;
        var codeLength = isType0 ? 2 : 1;

        var toUnicode = file.ResolveStream(font["ToUnicode"]);
        var data = toUnicode is null ? null : file.Decode(toUnicode);
        if (data is not null)
        {
            map = CMap.Parse(data, out var length);
            if (map.Count == 0)
            {
                map = null;
            }
            else if (length > 0)
            {
                codeLength = length;
            }
        }
        else
        {
            map = null;
        }

        // The encoding is only consulted when there is no CMap to consult: with a /ToUnicode map the
        // producer has already said what the codes mean, and second-guessing it would corrupt text.
        var encoding = map is null && !isType0
            ? EncodingMap(file, file.ResolveDictionary(font["Encoding"]), file.Resolve(font["Encoding"]) as PdfName)
            : [];

        var (widths, defaultWidth) = isType0 ? Type0Widths(file, font) : SimpleWidths(file, font);

        return new PdfTextFont(name, map, encoding, widths, codeLength, defaultWidth);
    }

    /// <summary>
    /// Splits a shown string into glyphs. Every code is <see cref="MaxCodeLength"/> or fewer bytes
    /// wide; a trailing byte that cannot complete a code is ignored, as readers do.
    /// </summary>
    /// <param name="bytes">The bytes of a <c>Tj</c>, <c>TJ</c>, <c>'</c> or <c>"</c> string.</param>
    public IReadOnlyList<PdfGlyph> Decode(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return [];
        }

        var glyphs = new List<PdfGlyph>(bytes.Length / _codeLength);

        for (var index = 0; index + _codeLength <= bytes.Length; index += _codeLength)
        {
            var code = 0;
            for (var offset = 0; offset < _codeLength; offset++)
            {
                code = (code << 8) | bytes[index + offset];
            }

            glyphs.Add(new PdfGlyph(TextOf(code), WidthOf(code), _codeLength == 1 && code == 32));
        }

        return glyphs;
    }

    private string TextOf(int code)
    {
        if (_toUnicode is not null)
        {
            return _toUnicode.TryGetValue(code, out var mapped) ? mapped : Replace();
        }

        if (_encoding.TryGetValue(code, out var named))
        {
            return named;
        }

        // Documented fallback: the code itself, read as a Unicode scalar value. It is what the raw
        // byte stream says, not what the producer meant, which is why the page reports the font.
        // A code that names no visible character would only put a stray control character into the
        // text, so it contributes nothing.
        if (code is (>= 0 and <= 0x1F) or 0x7F)
        {
            return string.Empty;
        }

        if (code is >= 0 and <= 0xFF)
        {
            return ((char)code).ToString();
        }

        return code is > 0xFF and < 0x110000 and not (>= 0xD800 and <= 0xDFFF)
            ? char.ConvertFromUtf32(code)
            : Replace();

        static string Replace() => "\uFFFD";
    }

    private double WidthOf(int code)
    {
        if (_widths.TryGetValue(code, out var width))
        {
            return width;
        }

        return _defaultWidth;
    }

    /// <summary>The <c>/Widths</c> of a simple font, keyed by code.</summary>
    private static (Dictionary<int, double> Widths, double DefaultWidth) SimpleWidths(PdfFile file, PdfDictionary font)
    {
        var widths = new Dictionary<int, double>();
        var first = (int)Number(file, font, "FirstChar", 0);
        var descriptor = file.ResolveDictionary(font["FontDescriptor"]);
        var missing = descriptor is null ? 500 : Number(file, descriptor, "MissingWidth", 500);

        var array = file.Resolve(font["Widths"]);
        if (array is PdfArray items)
        {
            for (var index = 0; index < items.Items.Count; index++)
            {
                if (file.Resolve(items.Items[index]) is PdfNumber number)
                {
                    widths[first + index] = number.Value;
                }
            }
        }

        return (widths, missing <= 0 ? 500 : missing);
    }

    /// <summary>The widths of a Type0 font, read from its descendant's <c>/W</c> array.</summary>
    private static (Dictionary<int, double> Widths, double DefaultWidth) Type0Widths(PdfFile file, PdfDictionary font)
    {
        var widths = new Dictionary<int, double>();
        var descendant = file.ResolveDictionary(
            file.ResolveArray(font["DescendantFonts"]) is { Count: > 0 } descendants ? descendants[0] : null);

        if (descendant is null)
        {
            return (widths, 1000);
        }

        var defaultWidth = Number(file, descendant, "DW", 1000);
        var w = file.Resolve(descendant["W"]);

        if (w is PdfArray entries)
        {
            var index = 0;
            while (index < entries.Items.Count)
            {
                if (file.Resolve(entries.Items[index]) is not PdfNumber start)
                {
                    index++;
                    continue;
                }

                if (index + 1 < entries.Items.Count && file.Resolve(entries.Items[index + 1]) is PdfArray run)
                {
                    for (var offset = 0; offset < run.Items.Count; offset++)
                    {
                        if (file.Resolve(run.Items[offset]) is PdfNumber width)
                        {
                            widths[(int)start.AsInteger + offset] = width.Value;
                        }
                    }

                    index += 2;
                    continue;
                }

                if (index + 2 < entries.Items.Count &&
                    file.Resolve(entries.Items[index + 1]) is PdfNumber end &&
                    file.Resolve(entries.Items[index + 2]) is PdfNumber single)
                {
                    var last = (int)Math.Min(end.AsInteger, start.AsInteger + 65_535);
                    for (var code = (int)start.AsInteger; code <= last; code++)
                    {
                        widths[code] = single.Value;
                    }

                    index += 3;
                    continue;
                }

                index++;
            }
        }

        return (widths, defaultWidth <= 0 ? 1000 : defaultWidth);
    }

    /// <summary>
    /// The codes a simple font reads without a CMap: <c>/BaseEncoding</c>, overridden by the glyph
    /// names of <c>/Differences</c>.
    /// </summary>
    private static Dictionary<int, string> EncodingMap(PdfFile file, PdfDictionary? encoding, PdfName? name)
    {
        var map = new Dictionary<int, string>();

        var baseName = encoding is null ? name?.Value : (file.Resolve(encoding["BaseEncoding"]) as PdfName)?.Value;
        AddBase(map, baseName);

        if (encoding is null)
        {
            return map;
        }

        var differences = file.Resolve(encoding["Differences"]);
        if (differences is not PdfArray items)
        {
            return map;
        }

        var code = 0;
        foreach (var item in items.Items)
        {
            switch (file.Resolve(item))
            {
                case PdfNumber number when number.IsInteger:
                    code = (int)number.AsInteger;
                    break;
                case PdfName glyph:
                    var text = GlyphNames.ToUnicode(glyph.Value);
                    if (text is not null && code is >= 0 and <= 0xFF)
                    {
                        map[code] = text;
                    }

                    code++;
                    break;
            }
        }

        return map;
    }

    private static void AddBase(Dictionary<int, string> map, string? name)
    {
        // WinAnsiEncoding is Latin-1 apart from 0x80..0x9F; the other two are approximated by Latin-1
        // rather than guessed, and the page reports the font as a fallback either way.
        for (var code = 32; code <= 0xFF; code++)
        {
            map[code] = string.Equals(name, "WinAnsiEncoding", StringComparison.Ordinal)
                ? WinAnsi(code)
                : ((char)code).ToString();
        }

        if (!string.Equals(name, "WinAnsiEncoding", StringComparison.Ordinal))
        {
            return;
        }

        map[0x7F] = "\u2022";
    }

    private static string WinAnsi(int code) => code switch
    {
        0x80 => "\u20AC",
        0x82 => "\u201A",
        0x83 => "\u0192",
        0x84 => "\u201E",
        0x85 => "\u2026",
        0x86 => "\u2020",
        0x87 => "\u2021",
        0x88 => "\u02C6",
        0x89 => "\u2030",
        0x8A => "\u0160",
        0x8B => "\u2039",
        0x8C => "\u0152",
        0x8E => "\u017D",
        0x91 => "\u2018",
        0x92 => "\u2019",
        0x93 => "\u201C",
        0x94 => "\u201D",
        0x95 => "\u2022",
        0x96 => "\u2013",
        0x97 => "\u2014",
        0x98 => "\u02DC",
        0x99 => "\u2122",
        0x9A => "\u0161",
        0x9B => "\u203A",
        0x9C => "\u0153",
        0x9E => "\u017E",
        0x9F => "\u0178",
        _ => ((char)code).ToString(),
    };

    private static double Number(PdfFile file, PdfDictionary dictionary, string key, double fallback)
        => file.Resolve(dictionary[key]) is PdfNumber number ? number.Value : fallback;
}
