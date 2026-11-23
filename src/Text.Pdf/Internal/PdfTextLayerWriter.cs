using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// Writes the invisible text layer: the generated font, one content stream per page, the resources
/// that carry the font, and the page dictionaries that reach both.
/// </summary>
/// <remarks>
/// <para>
/// Everything is appended. The page objects are rewritten — an incremental update replaces an object
/// by defining it again later in the file — and every other byte of the earlier revision stays where
/// it was, which is what keeps a signature over those bytes valid.
/// </para>
/// <para>
/// Each updated page gets a fresh <c>/Resources</c> dictionary holding the page's own entries plus a
/// <c>/Font</c> dictionary that merges the fonts the page already had with this layer's. Copying the
/// resources rather than editing them in place is what makes one code path cover a page that inherits
/// its resources, one that points at them, and one that carries them inline — and it never rewrites an
/// object another page also uses.
/// </para>
/// </remarks>
internal static class PdfTextLayerWriter
{
    /// <summary>The resource name the layer's font is registered under, and its <c>/BaseFont</c> name.</summary>
    private const string FontName = "M3TextLayer";

    /// <summary>The name of the family the embedded program declares.</summary>
    private const string BaseFont = "M3scalinumTextLayer";

    /// <summary>The codes and characters one <c>bfchar</c> block may hold.</summary>
    private const int CharBlockSize = 100;

    /// <summary>Adds the layer to <paramref name="pdf"/> and returns the whole file.</summary>
    /// <param name="pdf">The document.</param>
    /// <param name="words">The words, in reading order.</param>
    public static byte[] Add(byte[] pdf, IReadOnlyList<PdfTextLayerWord> words)
    {
        ArgumentNullException.ThrowIfNull(words);

        if (words.Count == 0)
        {
            throw new ArgumentException("the layer holds no word, so there is nothing to place", nameof(words));
        }

        var file = PdfSource.Open(pdf, "add a text layer to");
        var document = Document(pdf);
        var pages = file.Pages();

        if (pages.Count == 0)
        {
            throw new PdfOperationException("the document has no page, so a text layer has nowhere to go");
        }

        Validate(words, pages.Count);

        var glyphs = new GlyphSet();
        glyphs.Register(" ");

        var byPage = new List<PdfTextLayerWord>[pages.Count];

        foreach (var word in words)
        {
            glyphs.Register(word.Text);
            (byPage[word.PageIndex] ??= []).Add(word);
        }

        if (glyphs.Count > ushort.MaxValue)
        {
            throw new PdfOperationException(
                $"the layer holds {glyphs.Count - 1} distinct characters, and an Identity-H code is two bytes wide, "
                + "so it cannot name more than 65535 glyphs");
        }

        var numbers = new Numbers(document.NextObjectNumber());
        var objects = new List<(int Number, string Body)>();

        // The content streams first: one per page that has a word to place.
        var contents = new int[pages.Count];
        for (var page = 0; page < pages.Count; page++)
        {
            if (byPage[page] is not { Count: > 0 } placed)
            {
                continue;
            }

            contents[page] = numbers.Next();
            objects.Add((contents[page], ContentBody(Encoding.Latin1.GetBytes(Content(placed, glyphs)))));
        }

        var fontFile = numbers.Next();
        var descriptor = numbers.Next();
        var cidFont = numbers.Next();
        var toUnicode = numbers.Next();
        var font = numbers.Next();

        objects.Add((fontFile, StreamBody(PdfTextLayerFont.Build(glyphs.Count), length1: true)));
        objects.Add((descriptor, DescriptorBody(fontFile)));
        objects.Add((cidFont, CidFontBody(descriptor)));
        objects.Add((toUnicode, StreamBody(Encoding.ASCII.GetBytes(ToUnicodeCMap(glyphs)))));
        objects.Add((font, FontBody(cidFont, toUnicode)));

        var merged = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var page = 0; page < pages.Count; page++)
        {
            if (byPage[page] is not { Count: > 0 })
            {
                continue;
            }

            var entry = pages[page];

            if (entry.Number == 0)
            {
                throw new PdfOperationException(
                    "a page is named directly in the page tree instead of by object, so an update cannot rewrite it");
            }

            RequireUpright(file, entry);

            var fonts = file.ResolveDictionary(entry.Resources?["Font"]);
            var key = Serialize(fonts);

            if (!merged.TryGetValue(key, out var mergedFont))
            {
                mergedFont = numbers.Next();
                objects.Add((mergedFont, MergedFontBody(fonts, font)));
                merged[key] = mergedFont;
            }

            var resources = numbers.Next();
            objects.Add((resources, ResourcesBody(entry.Resources, mergedFont)));

            var number = entry.Number;
            objects.Add((number, PageBody(document.Require(number).Body, contents[page], resources)));
        }

        var append = new PdfAppend(document.Text);

        foreach (var (number, body) in objects.OrderBy(item => item.Number))
        {
            append.Add(number, body);
        }

        return Encoding.Latin1.GetBytes(append.Finish(document, numbers.Value));
    }

    /// <summary>The object numbers an update may use, handed out in order.</summary>
    private sealed class Numbers(int first)
    {
        /// <summary>The last number handed out, or the first free one before any is.</summary>
        public int Value { get; private set; } = first;

        /// <summary>Reserves the next free object number.</summary>
        public int Next() => Value++;
    }

    /// <summary>Checks every word before a byte is written.</summary>
    private static void Validate(IReadOnlyList<PdfTextLayerWord> words, int pageCount)
    {
        for (var index = 0; index < words.Count; index++)
        {
            var word = words[index];

            if (word.PageIndex < 0 || word.PageIndex >= pageCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(words),
                    word.PageIndex,
                    $"word {index} names page {word.PageIndex}, and the document has {pageCount} page(s), numbered from zero");
            }

            if (string.IsNullOrWhiteSpace(word.Text))
            {
                throw new ArgumentException($"word {index} holds no text to place", nameof(words));
            }

            foreach (var character in word.Text)
            {
                if (char.IsControl(character))
                {
                    throw new ArgumentException(
                        $"word {index} carries the control character U+{(int)character:X4}, which a word cannot hold",
                        nameof(words));
                }
            }

            var box = new[] { word.Left, word.Bottom, word.Right, word.Top };
            if (box.Any(value => !double.IsFinite(value)) || word.Right <= word.Left || word.Top <= word.Bottom)
            {
                throw new ArgumentException(
                    $"word {index} has no area: its box is ({word.Left}, {word.Bottom})-({word.Right}, {word.Top})",
                    nameof(words));
            }
        }
    }

    /// <summary>Refuses a rotated page, whose boxes would be placed in the wrong space.</summary>
    private static void RequireUpright(PdfFile file, PdfPage page)
    {
        var rotation = Inherited(file, page.Dictionary, "Rotate", 0);

        if (rotation % 360 != 0)
        {
            throw new PdfOperationException(
                $"page {page.Number} is rotated {rotation} degrees, and a text layer places its boxes in the page's own user space, "
                + "so the words would sit where the rotation does not put them; rotate the page to zero before adding a layer");
        }
    }

    /// <summary>Reads a page attribute the page tree may carry instead of the page, as a reader does.</summary>
    private static int Inherited(PdfFile file, PdfDictionary page, string key, int fallback)
    {
        var current = page;

        for (var depth = 0; depth < 64; depth++)
        {
            if (file.Resolve(current[key]) is PdfNumber number && number.IsInteger)
            {
                return (int)number.AsInteger;
            }

            current = file.ResolveDictionary(current["Parent"]);
            if (current is null)
            {
                break;
            }
        }

        return fallback;
    }

    /// <summary>The document as an updatable revision, or an exception naming what blocks the update.</summary>
    private static PdfDocument Document(byte[] pdf)
    {
        try
        {
            return PdfDocument.Parse(Encoding.Latin1.GetString(pdf));
        }
        catch (PdfSignatureException exception)
        {
            throw new PdfOperationException(
                "the document cannot take an incremental update: " + exception.Message, exception);
        }
    }

    /// <summary>
    /// The page's content stream: every word positioned by its own text matrix, drawn invisible, in
    /// the order the caller supplied.
    /// </summary>
    /// <remarks>
    /// Each word is shown as one string with a trailing space, and each string's horizontal scale is
    /// fitted to the word's box, so the advance a reader computes spans the box the word was read
    /// from. The trailing space is what guarantees a word boundary in the text a reader extracts; it
    /// is invisible and is trimmed at the end of a line, so it never doubles the separator the reader
    /// inserts from the geometry.
    /// </remarks>
    private static string Content(IEnumerable<PdfTextLayerWord> words, GlyphSet glyphs)
    {
        var builder = new StringBuilder("q\nBT\n3 Tr\n");

        foreach (var word in words)
        {
            var codes = new StringBuilder();
            var characters = 0;

            foreach (var rune in word.Text.EnumerateRunes())
            {
                codes.Append(glyphs.Glyph(rune.Value).ToString("X4", CultureInfo.InvariantCulture));
                characters++;
            }

            codes.Append(glyphs.Glyph(' ').ToString("X4", CultureInfo.InvariantCulture));

            var size = word.Top - word.Bottom;
            var advance = (double)PdfTextLayerFont.Advance / PdfTextLayerFont.UnitsPerEm;
            var scale = (word.Right - word.Left) / (characters * advance * size);

            builder.Append('/').Append(FontName).Append(' ').Append(Number(size)).Append(" Tf\n")
                   .Append(Number(scale)).Append(" 0 0 1 ")
                   .Append(Number(word.Left)).Append(' ').Append(Number(word.Bottom)).Append(" Tm\n")
                   .Append('<').Append(codes).Append("> Tj\n");
        }

        return builder.Append("ET\nQ\n").ToString();
    }

    /// <summary>The <c>Type0</c> font: two-byte codes, the descendant CID font, and the CMap.</summary>
    private static string FontBody(int cidFont, int toUnicode)
        => "<</Type/Font/Subtype/Type0/BaseFont/" + BaseFont
            + "/Encoding/Identity-H/DescendantFonts[" + Reference(cidFont) + "]"
            + "/ToUnicode " + Reference(toUnicode) + ">>";

    /// <summary>The CID font: every CID is its own glyph, every advance is the font's one advance.</summary>
    private static string CidFontBody(int descriptor)
        => "<</Type/Font/Subtype/CIDFontType2/BaseFont/" + BaseFont
            + "/CIDSystemInfo<</Registry(Adobe)/Ordering(Identity)/Supplement 0>>"
            + "/FontDescriptor " + Reference(descriptor)
            + "/CIDToGIDMap/Identity/DW " + PdfTextLayerFont.Advance.ToString(CultureInfo.InvariantCulture) + ">>";

    /// <summary>The descriptor that reaches the embedded program.</summary>
    private static string DescriptorBody(int fontFile)
        => "<</Type/FontDescriptor/FontName/" + BaseFont + "/Flags 4"
            + "/FontBBox[0 " + PdfTextLayerFont.Descent.ToString(CultureInfo.InvariantCulture)
            + " " + PdfTextLayerFont.Advance.ToString(CultureInfo.InvariantCulture)
            + " " + PdfTextLayerFont.Ascent.ToString(CultureInfo.InvariantCulture) + "]"
            + "/ItalicAngle 0"
            + "/Ascent " + PdfTextLayerFont.Ascent.ToString(CultureInfo.InvariantCulture)
            + "/Descent " + PdfTextLayerFont.Descent.ToString(CultureInfo.InvariantCulture)
            + "/CapHeight 700/StemV 80/FontFile2 " + Reference(fontFile) + ">>";

    /// <summary>
    /// The <c>/ToUnicode</c> CMap: each glyph identifier the layer writes, and the character it
    /// stands for, so the layer reads back as the words it holds.
    /// </summary>
    private static string ToUnicodeCMap(GlyphSet glyphs)
    {
        var builder = new StringBuilder();

        builder.Append("/CIDInit/ProcSet findresource begin\n")
               .Append("12 dict begin\n")
               .Append("begincmap\n")
               .Append("/CIDSystemInfo<</Registry(Adobe)/Ordering(UCS)/Supplement 0>>def\n")
               .Append("/CMapName/Adobe-Identity-UCS def\n")
               .Append("/CMapType 2 def\n")
               .Append("1 begincodespacerange\n<0000><FFFF>\nendcodespacerange\n");

        var entries = glyphs.Codes;

        for (var start = 0; start < entries.Count; start += CharBlockSize)
        {
            var count = Math.Min(CharBlockSize, entries.Count - start);
            builder.Append(count.ToString(CultureInfo.InvariantCulture)).Append(" beginbfchar\n");

            for (var index = start; index < start + count; index++)
            {
                builder.Append('<').Append((index + 1).ToString("X4", CultureInfo.InvariantCulture)).Append("><")
                       .Append(Utf16Hex(entries[index])).Append(">\n");
            }

            builder.Append("endbfchar\n");
        }

        return builder.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n").ToString();
    }

    /// <summary>A code point as UTF-16BE hexadecimal, surrogate pair and all.</summary>
    private static string Utf16Hex(int codePoint)
        => Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(char.ConvertFromUtf32(codePoint)));

    /// <summary>The page's font dictionary: what it already held, plus this layer's font.</summary>
    /// <param name="fonts">The font dictionary the page resolves to, or <see langword="null"/>.</param>
    /// <param name="font">The object number of this layer's <c>Type0</c> font.</param>
    private static string MergedFontBody(PdfDictionary? fonts, int font)
    {
        var builder = new StringBuilder("<<");

        if (fonts is not null)
        {
            foreach (var (key, value) in fonts.Entries)
            {
                if (string.Equals(key, FontName, StringComparison.Ordinal))
                {
                    throw new PdfOperationException(
                        $"a page already carries a text layer font named {FontName}; a second layer would renumber the glyphs "
                        + "the first one draws, so it is refused rather than written over it");
                }

                builder.Append('/').Append(key).Append(' ').Append(PdfIncrementalUpdate.Serialize(value)).Append(' ');
            }
        }

        return builder.Append('/').Append(FontName).Append(' ').Append(Reference(font)).Append(">>").ToString();
    }

    /// <summary>The page's resources: what the page resolved to, with the font dictionary replaced.</summary>
    private static string ResourcesBody(PdfDictionary? resources, int fonts)
    {
        var builder = new StringBuilder("<<");

        if (resources is not null)
        {
            foreach (var (key, value) in resources.Entries)
            {
                if (string.Equals(key, "Font", StringComparison.Ordinal))
                {
                    continue;
                }

                builder.Append('/').Append(key).Append(' ').Append(PdfIncrementalUpdate.Serialize(value)).Append(' ');
            }
        }

        return builder.Append("/Font ").Append(Reference(fonts)).Append(">>").ToString();
    }

    /// <summary>The page dictionary, with the new content stream appended and the new resources reached.</summary>
    private static string PageBody(string body, int contents, int resources)
    {
        var updated = SetEntry(body, "Contents", Reference(contents), array: true);
        return SetEntry(updated, "Resources", Reference(resources), array: false);
    }

    /// <summary>Replaces the value of <paramref name="key"/>, or adds the entry when the dictionary has none.</summary>
    private static string SetEntry(string body, string key, string value, bool array)
    {
        if (PdfIncrementalUpdate.Locate(body, key) is { } located)
        {
            var current = body.Substring(located.ValueStart, located.ValueLength).Trim();
            var replacement = array ? AppendToArray(current, value) : value;

            // The span starts after the whitespace the scan consumed, so the separator is written
            // back with the value; without it a key and its new value would run together.
            return PdfIncrementalUpdate.Replace(body, located.ValueStart, located.ValueLength, " " + replacement);
        }

        return PdfIncrementalUpdate.InsertBeforeClose(body, "/" + key + " " + value);
    }

    /// <summary>
    /// Appends <paramref name="value"/> to an <c>/Contents</c> value: the array gains one item, and a
    /// single stream becomes an array of two, which is how a page keeps its own stream and gains this
    /// one.
    /// </summary>
    private static string AppendToArray(string value, string addition)
    {
        if (value.Length < 2 || !value.StartsWith('['))
        {
            return "[" + value + " " + addition + "]";
        }

        var inner = value.AsSpan(1, value.Length - 2).Trim();

        return inner.IsEmpty ? "[" + addition + "]" : "[" + inner.ToString() + " " + addition + "]";
    }

    /// <summary>
    /// A content stream object. The layer's streams are left uncompressed: a few hundred bytes each,
    /// and the operators a reader, a validator or a person with an editor must be able to see.
    /// </summary>
    /// <param name="content">The stream's bytes.</param>
    private static string ContentBody(byte[] content)
        => "<</Length " + content.Length.ToString(CultureInfo.InvariantCulture) + ">>\nstream\n"
            + Encoding.Latin1.GetString(content)
            + "\nendstream";

    /// <summary>
    /// A stream object, Flate-compressed, with its true length. A font program also carries
    /// <c>/Length1</c>, which is the program's uncompressed length.
    /// </summary>
    private static string StreamBody(byte[] content, bool length1 = false)
    {
        using var output = new MemoryStream();

        using (var deflate = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(content, 0, content.Length);
        }

        var compressed = output.ToArray();
        var builder = new StringBuilder("<</Length ").Append(compressed.Length.ToString(CultureInfo.InvariantCulture));

        if (length1)
        {
            builder.Append("/Length1 ").Append(content.Length.ToString(CultureInfo.InvariantCulture));
        }

        builder.Append("/Filter/FlateDecode>>\nstream\n")
               .Append(Encoding.Latin1.GetString(compressed))
               .Append("\nendstream");

        return builder.ToString();
    }

    /// <summary>A key of the font dictionary, for the pages that share one.</summary>
    private static string Serialize(PdfDictionary? fonts)
        => fonts is null
            ? string.Empty
            : string.Concat(fonts.Entries.Select(entry => "/" + entry.Key + " " + PdfIncrementalUpdate.Serialize(entry.Value)));

    private static string Reference(int number) => number.ToString(CultureInfo.InvariantCulture) + " 0 R";

    /// <summary>A number as PDF syntax, invariant and short.</summary>
    private static string Number(double value) => PdfSyntax.Number(value);

    /// <summary>
    /// The characters the layer draws, each mapped to the glyph identifier the font gives it, in the
    /// order they were first seen. Glyph 0 is <c>.notdef</c>, so the first character is glyph 1.
    /// </summary>
    private sealed class GlyphSet
    {
        private readonly Dictionary<int, int> _glyphs = [];
        private readonly List<int> _codes = [];

        /// <summary>How many glyphs the font holds, including <c>.notdef</c>.</summary>
        public int Count => _codes.Count + 1;

        /// <summary>The code points, in glyph-identifier order from glyph 1.</summary>
        public IReadOnlyList<int> Codes => _codes;

        /// <summary>Gives <paramref name="text"/> a glyph for every character it holds.</summary>
        /// <param name="text">The text to register.</param>
        public void Register(string text)
        {
            foreach (var rune in text.EnumerateRunes())
            {
                if (_glyphs.ContainsKey(rune.Value))
                {
                    continue;
                }

                _glyphs[rune.Value] = _codes.Count + 1;
                _codes.Add(rune.Value);
            }
        }

        /// <summary>The identifier <paramref name="codePoint"/> is drawn with.</summary>
        /// <param name="codePoint">The character.</param>
        public int Glyph(int codePoint) => _glyphs[codePoint];
    }
}
