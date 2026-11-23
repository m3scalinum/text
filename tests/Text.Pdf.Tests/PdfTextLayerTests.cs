using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Xunit;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// The invisible text layer: the operators it writes, the font it embeds, what
/// <see cref="PdfText"/> reads back from it, and the documents it refuses.
/// </summary>
/// <remarks>
/// The fixtures are written by hand so their bytes are visible. The layer is append-only, so the
/// tests that matter most are the ones that say the earlier revision is untouched: the original bytes
/// are a prefix of the result, and a signature made before the layer still verifies after it.
/// </remarks>
public sealed class PdfTextLayerTests
{
    /// <summary>Two lines of words, positioned in the page's user space, in reading order.</summary>
    private static PdfTextLayerWord[] Words() =>
    [
        new(0, "Premier", 72, 600, 132, 620),
        new(0, "mot", 140, 600, 164, 620),
        new(0, "Deuxième", 72, 570, 142, 590),
        new(0, "ligne", 150, 570, 186, 590),
    ];

    [Fact]
    public void The_words_of_a_layer_come_back_in_reading_order()
    {
        var pdf = PdfTextLayer.Add(TestPdf.OnePage(), Words());

        var page = PdfText.ExtractPage(pdf, 0);

        Assert.Equal("Page 1\nPremier mot\nDeuxième ligne\n", page.Text);

        // The page's own Helvetica has no /ToUnicode, so it is the only font reported as a fallback;
        // the layer's font is not, because its CMap is what the layer's text came through.
        Assert.Equal(new[] { "F1" }, page.FallbackFonts);
        Assert.Equal(0, page.SkippedContentStreams);
    }

    [Fact]
    public void The_layer_of_a_scanned_page_is_the_only_text_on_it()
    {
        var pdf = PdfTextLayer.Add(Scanned(), Words());

        var page = PdfText.ExtractPage(pdf, 0);

        Assert.Equal("Premier mot\nDeuxième ligne\n", page.Text);
        Assert.Empty(page.FallbackFonts);
    }

    [Fact]
    public void The_words_keep_the_order_the_caller_gave_them()
    {
        // The second word sits above the first in the page, and the layer still writes them in the
        // order given: reading order is the caller's fact, and a layer that sorted by position would
        // read a right-to-left line backwards.
        var words = new[]
        {
            new PdfTextLayerWord(0, "bas", 72, 560, 104, 580),
            new PdfTextLayerWord(0, "haut", 72, 600, 112, 620),
        };

        var pdf = PdfTextLayer.Add(Scanned(), words);

        Assert.Equal("bas\nhaut\n", PdfText.ExtractPage(pdf, 0).Text);
    }

    [Fact]
    public void The_earlier_revision_is_a_prefix_of_the_result()
    {
        var original = Scanned();

        var pdf = PdfTextLayer.Add(original, Words());

        Assert.True(pdf.Length > original.Length);
        Assert.Equal(
            Encoding.Latin1.GetString(original),
            Encoding.Latin1.GetString(pdf).Substring(0, original.Length));
    }

    [Fact]
    public void The_growth_is_a_few_kilobytes_for_a_page_of_words()
    {
        var original = Scanned();

        var pdf = PdfTextLayer.Add(original, Words());

        // One content stream, five font objects, one merged font dictionary, one resources
        // dictionary and the rewritten page, plus a cross-reference section: comfortably under the
        // size of the image such a page carries.
        Assert.InRange(pdf.Length - original.Length, 1, 6144);
    }

    [Fact]
    public void The_layer_draws_invisible_text_with_a_positioned_matrix()
    {
        var pdf = PdfTextLayer.Add(Scanned(), Words());

        var content = LayerContents(pdf);

        Assert.Contains("BT\n", content, StringComparison.Ordinal);
        Assert.Contains("3 Tr", content, StringComparison.Ordinal);
        Assert.Contains("/M3TextLayer 20 Tf", content, StringComparison.Ordinal);
        // Two lines, four words: one text matrix per word.
        Assert.Equal(4, content.Split(" Tm\n", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void The_layer_declares_a_type0_font_with_an_identity_encoding()
    {
        var pdf = PdfTextLayer.Add(Scanned(), Words());
        var font = Font(pdf);
        var descendant = Descendant(pdf);

        Assert.Equal("Type0", (font["Subtype"] as PdfName)?.Value);
        Assert.Equal("Identity-H", (font["Encoding"] as PdfName)?.Value);
        Assert.Equal("M3scalinumTextLayer", (font["BaseFont"] as PdfName)?.Value);
        Assert.NotNull(font["ToUnicode"]);

        Assert.Equal("CIDFontType2", (descendant["Subtype"] as PdfName)?.Value);
        Assert.Equal("Identity", (descendant["CIDToGIDMap"] as PdfName)?.Value);
        Assert.Equal(500, (descendant["DW"] as PdfNumber)?.Value);

        var info = Read(pdf).ResolveDictionary(descendant["CIDSystemInfo"]);
        Assert.Equal("Adobe", (info?["Registry"] as PdfString)?.Decode());
        Assert.Equal("Identity", (info?["Ordering"] as PdfString)?.Decode());

        var descriptor = Read(pdf).ResolveDictionary(descendant["FontDescriptor"]);
        Assert.NotNull(descriptor?["FontFile2"]);
        Assert.Equal("M3scalinumTextLayer", (descriptor!["FontName"] as PdfName)?.Value);
        Assert.Equal(4, (descriptor["Flags"] as PdfNumber)?.Value);
    }

    [Fact]
    public void The_unicode_map_gives_every_glyph_the_character_it_stands_for()
    {
        var pdf = PdfTextLayer.Add(Scanned(), Words());
        var cmap = Encoding.ASCII.GetString(Decoded(pdf, "ToUnicode"));

        // The space is registered first, so it is glyph 1; "Premier" then adds P as glyph 2.
        Assert.Contains("<0001><0020>", cmap, StringComparison.Ordinal);
        Assert.Contains("<0002><0050>", cmap, StringComparison.Ordinal);
        Assert.Contains(
            "<" + GlyphOf(pdf, "è").ToString("X4", CultureInfo.InvariantCulture) + "><00E8>",
            cmap,
            StringComparison.Ordinal);
        Assert.Contains("1 begincodespacerange\n<0000><FFFF>", cmap, StringComparison.Ordinal);
        Assert.Contains("endcmap", cmap, StringComparison.Ordinal);
    }

    [Fact]
    public void The_embedded_program_is_a_readable_truetype_font()
    {
        var pdf = PdfTextLayer.Add(Scanned(), Words());
        var program = Decoded(pdf, "FontFile2");
        var tables = Tables(program);

        Assert.True(Word(program, 0) == 0x00010000u);
        Assert.True(((program[4] << 8) | program[5]) == 9);   // numTables is a 16-bit field
        Assert.Equal(
            tables.Keys.OrderBy(tag => tag, StringComparer.Ordinal).ToList(),
            tables.Keys.ToList());

        var head = tables["head"].Offset;
        Assert.True(Word(program, head + 12) == 0x5F0F3CF5u);
        Assert.Equal(1000, Short(program, head + 18));
        Assert.True((Word(program, head + 50) >> 16) == 0);   // indexToLocFormat: short offsets

        var glyphCount = Short(program, tables["maxp"].Offset + 4);
        Assert.True(glyphCount > 1);
        Assert.Equal(2 * (glyphCount + 1), tables["loca"].Length);
        Assert.Equal(4 * glyphCount, tables["hmtx"].Length);
        Assert.Equal(4, tables["glyf"].Length);

        // Every glyph is empty, and the file sums to the constant an sfnt must sum to.
        Assert.All(GlyphOffsets(program, tables, glyphCount), offset => Assert.True(offset == 0));
        Assert.True(Checksum(program) == 0xB1B0AFBAu);

        // The program the descriptor reaches declares the advance the CID font declares.
        var hmtx = tables["hmtx"].Offset;
        Assert.Equal(500, (program[hmtx] << 8) | program[hmtx + 1]);
    }

    [Fact]
    public void The_page_reaches_the_layer_through_its_contents_array()
    {
        var pdf = PdfTextLayer.Add(Scanned(), Words());
        var page = Assert.Single(Read(pdf).Pages());

        var contents = Read(pdf).Resolve(page.Dictionary["Contents"]);

        Assert.IsType<PdfArray>(contents);
        Assert.Equal(2, ((PdfArray)contents).Items.Count);
    }

    [Fact]
    public void An_encrypted_document_is_refused()
    {
        var exception = Assert.Throws<PdfOperationException>(() =>
            PdfTextLayer.Add(TestEncryption.Aes128(), Words()));

        Assert.Contains("encrypted", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Bytes_that_are_not_a_pdf_are_refused()
    {
        var exception = Assert.Throws<PdfOperationException>(() =>
            PdfTextLayer.Add(Encoding.ASCII.GetBytes("not a pdf at all"), Words()));

        Assert.Contains("not a PDF", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_signed_document_keeps_its_signature_intact()
    {
        var signed = PdfSigner.Sign(Scanned(), TestCertificates.Signer());

        var before = Assert.Single(PdfSignatureVerifier.Verify(signed).Signatures);
        Assert.True(before.IsIntact);
        Assert.True(before.CoversWholeFile);

        var pdf = PdfTextLayer.Add(signed, Words());
        var after = PdfSignatureVerifier.Verify(pdf);
        var signature = Assert.Single(after.Signatures);

        Assert.True(signature.IsIntact);
        Assert.False(signature.CoversWholeFile);
        Assert.True(after.AllIntact);
        Assert.Equal("Premier mot\nDeuxième ligne\n", PdfText.ExtractPage(pdf, 0).Text);
    }

    [Fact]
    public void A_page_that_already_carries_a_layer_is_refused()
    {
        var once = PdfTextLayer.Add(Scanned(), Words());

        var exception = Assert.Throws<PdfOperationException>(() => PdfTextLayer.Add(once, Words()));

        Assert.Contains("already carries a text layer", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_rotated_page_is_refused()
    {
        var rotated = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Count 1/Kids[3 0 R]>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]/Rotate 90/Contents 4 0 R>>",
            Stream("BT /F1 18 Tf 72 760 Td (Turned) Tj ET"),
        ]);

        var exception = Assert.Throws<PdfOperationException>(() => PdfTextLayer.Add(rotated, Words()));

        Assert.Contains("rotated 90 degrees", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_page_that_inherits_its_resources_keeps_the_fonts_it_inherited()
    {
        var inherited = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Count 1/Kids[3 0 R]/MediaBox[0 0 595 842]/Resources<</Font<</F1 5 0 R>>>>>>",
            "<</Type/Page/Parent 2 0 R/Contents 4 0 R>>",
            Stream("BT /F1 18 Tf 72 760 Td (Inherited) Tj ET"),
            "<</Type/Font/Subtype/Type1/BaseFont/Helvetica/Encoding/WinAnsiEncoding>>",
        ]);

        var pdf = PdfTextLayer.Add(inherited, Words());

        var page = PdfText.ExtractPage(pdf, 0);

        Assert.Equal("Inherited\nPremier mot\nDeuxième ligne\n", page.Text);

        // The page tree's own resources are untouched: the layer copied them for the page.
        Assert.Equal(
            Encoding.Latin1.GetString(inherited),
            Encoding.Latin1.GetString(pdf).Substring(0, inherited.Length));
    }

    [Fact]
    public void A_page_whose_contents_are_an_array_keeps_both_streams()
    {
        var arrayed = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Count 1/Kids[3 0 R]>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]/Resources<</Font<</F1 6 0 R>>>>/Contents[4 0 R 5 0 R]>>",
            Stream("BT /F1 18 Tf 72 780 Td (First) Tj ET"),
            Stream("BT /F1 18 Tf 72 740 Td (Second) Tj ET"),
            "<</Type/Font/Subtype/Type1/BaseFont/Helvetica/Encoding/WinAnsiEncoding>>",
        ]);

        var pdf = PdfTextLayer.Add(arrayed, Words());
        var page = Assert.Single(Read(pdf).Pages());

        Assert.Equal(3, Read(pdf).ResolveArray(page.Dictionary["Contents"]).Count);
        Assert.Equal("First\nSecond\nPremier mot\nDeuxième ligne\n", PdfText.ExtractPage(pdf, 0).Text);
    }

    [Fact]
    public void A_page_with_no_contents_gains_the_layer()
    {
        var empty = PdfTestDocument.Minimal();

        var pdf = PdfTextLayer.Add(empty, [new PdfTextLayerWord(0, "Bonjour", 10, 40, 60, 50)]);

        Assert.Equal("Bonjour\n", PdfText.ExtractPage(pdf, 0).Text);
    }

    [Fact]
    public void The_page_reaches_the_copy_of_its_inherited_resources()
    {
        var inherited = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Count 1/Kids[3 0 R]/MediaBox[0 0 595 842]/Resources<</Font<</F1 5 0 R>>>>>>",
            "<</Type/Page/Parent 2 0 R/Contents 4 0 R>>",
            Stream("BT /F1 18 Tf 72 760 Td (Inherited) Tj ET"),
            "<</Type/Font/Subtype/Type1/BaseFont/Helvetica/Encoding/WinAnsiEncoding>>",
        ]);

        var pdf = PdfTextLayer.Add(inherited, Words());
        var file = Read(pdf);
        var page = Assert.Single(file.Pages());

        Assert.IsType<PdfReference>(page.Dictionary["Resources"]);

        var resources = file.ResolveDictionary(page.Dictionary["Resources"]);
        var fonts = file.ResolveDictionary(resources?["Font"]);

        Assert.NotNull(fonts?["F1"]);
        Assert.NotNull(fonts?["M3TextLayer"]);
    }

    [Fact]
    public void Two_pages_receive_their_own_words_and_share_one_font()
    {
        var pages = TestPdf.WithPages(2);
        var words = new[]
        {
            new PdfTextLayerWord(1, "Seconde", 72, 700, 132, 720),
            new PdfTextLayerWord(0, "Première", 72, 700, 132, 720),
        };

        var pdf = PdfTextLayer.Add(pages, words);

        Assert.Equal("Page 1\nPremière\n", PdfText.ExtractPage(pdf, 0).Text);
        Assert.Equal("Page 2\nSeconde\n", PdfText.ExtractPage(pdf, 1).Text);

        var file = Read(pdf);
        var fonts = file.Pages()
            .Select(page => file.ResolveDictionary(page.Resources?["Font"])?["M3TextLayer"]?.ToString())
            .ToList();

        Assert.Equal(2, fonts.Count);
        Assert.All(fonts, name => Assert.Equal(fonts[0], name));
    }

    [Fact]
    public void Characters_outside_the_basic_plane_keep_their_code_point()
    {
        var pdf = PdfTextLayer.Add(Scanned(), [new PdfTextLayerWord(0, "A😀B", 72, 600, 120, 620)]);
        var cmap = Encoding.ASCII.GetString(Decoded(pdf, "ToUnicode"));

        // Space is glyph 1, A is 2, the emoji is 3: its destination is one UTF-16 surrogate pair.
        Assert.Contains("<0003><D83DDE00>", cmap, StringComparison.Ordinal);
        Assert.Equal("A😀B\n", PdfText.ExtractPage(pdf, 0).Text);
    }

    [Fact]
    public void A_layer_with_no_word_is_refused()
    {
        var exception = Assert.Throws<ArgumentException>(() => PdfTextLayer.Add(Scanned(), []));

        Assert.Contains("no word", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_word_naming_a_page_the_document_does_not_have_is_refused()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            PdfTextLayer.Add(Scanned(), [new PdfTextLayerWord(3, "Ailleurs", 72, 600, 120, 620)]));

        Assert.Contains("page 3", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_word_with_no_text_is_refused()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            PdfTextLayer.Add(Scanned(), [new PdfTextLayerWord(0, " ", 72, 600, 120, 620)]));

        Assert.Contains("no text", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_word_carrying_a_control_character_is_refused()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            PdfTextLayer.Add(Scanned(), [new PdfTextLayerWord(0, "deux\nlignes", 72, 600, 120, 620)]));

        Assert.Contains("U+000A", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_word_with_no_area_is_refused()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            PdfTextLayer.Add(Scanned(), [new PdfTextLayerWord(0, "plat", 72, 600, 72, 620)]));

        Assert.Contains("no area", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>A one-page document whose page draws a scan and holds no text.</summary>
    private static byte[] Scanned() => PdfTestDocument.Document(
    [
        "<</Type/Catalog/Pages 2 0 R>>",
        "<</Type/Pages/Count 1/Kids[3 0 R]>>",
        "<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]/Contents 4 0 R>>",
        Stream("q 595 0 0 842 0 0 cm /Im0 Do Q"),
    ]);

    /// <summary>A stream object holding <paramref name="content"/> verbatim.</summary>
    private static string Stream(string content)
        => "<</Length " + content.Length.ToString(CultureInfo.InvariantCulture) + ">>\nstream\n"
            + content + "\nendstream";

    /// <summary>The document, opened with the reader the tests already use.</summary>
    private static PdfFile Read(byte[] pdf)
    {
        Assert.True(PdfFile.TryOpen(pdf, out var file), "the bytes were not read as a PDF");

        return file!;
    }

    /// <summary>The layer's <c>Type0</c> font dictionary, read through the first page's resources.</summary>
    private static PdfDictionary Font(byte[] pdf)
    {
        var file = Read(pdf);
        var fonts = file.ResolveDictionary(file.Pages()[0].Resources?["Font"]);
        var font = file.ResolveDictionary(fonts?["M3TextLayer"]);

        Assert.NotNull(font);

        return font!;
    }

    /// <summary>The <c>CIDFontType2</c> the <c>Type0</c> font descends from.</summary>
    private static PdfDictionary Descendant(byte[] pdf)
    {
        var file = Read(pdf);
        var fonts = file.ResolveDictionary(file.Pages()[0].Resources?["Font"]);
        var font = file.ResolveDictionary(fonts?["M3TextLayer"]);
        var descendant = file.ResolveDictionary(file.ResolveArray(font?["DescendantFonts"])[0]);

        Assert.NotNull(descendant);

        return descendant!;
    }

    /// <summary>A stream of the layer, decoded: the <c>/ToUnicode</c> CMap or the font program.</summary>
    private static byte[] Decoded(byte[] pdf, string key)
    {
        var file = Read(pdf);
        var owner = key switch
        {
            "ToUnicode" => Font(pdf),
            "FontFile2" => file.ResolveDictionary(Descendant(pdf)["FontDescriptor"])!,
            _ => Descendant(pdf),
        };

        var stream = file.ResolveStream(owner[key]);

        Assert.NotNull(stream);

        var data = file.Decode(stream!);

        Assert.NotNull(data);

        return data!;
    }

    /// <summary>The layer's content stream, as text.</summary>
    private static string LayerContents(byte[] pdf) => Encoding.Latin1.GetString(ContentOf(pdf));

    /// <summary>The bytes of the layer's content stream, which the page reaches as the last of its array.</summary>
    private static byte[] ContentOf(byte[] pdf)
    {
        var file = Read(pdf);
        var contents = file.ResolveArray(file.Pages()[0].Dictionary["Contents"]);
        var stream = file.ResolveStream(contents[^1]);

        Assert.NotNull(stream);

        var data = file.Decode(stream!);

        Assert.NotNull(data);

        return data!;
    }

    /// <summary>The glyph identifier the CMap gives <paramref name="character"/>.</summary>
    private static int GlyphOf(byte[] pdf, string character)
    {
        var cmap = Encoding.ASCII.GetString(Decoded(pdf, "ToUnicode"));
        var hex = Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(character));

        foreach (var line in cmap.Split('\n'))
        {
            if (line.Contains("><" + hex + ">", StringComparison.Ordinal))
            {
                return int.Parse(line.AsSpan(1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }
        }

        throw new InvalidOperationException($"no glyph for {character}");
    }

    /// <summary>A big-endian 16-bit field of the program.</summary>
    private static int Short(byte[] program, int offset) => (program[offset] << 8) | program[offset + 1];

    /// <summary>A big-endian 32-bit word of the program.</summary>
    private static uint Word(byte[] program, int offset)
        => (uint)((program[offset] << 24) | (program[offset + 1] << 16) | (program[offset + 2] << 8) | program[offset + 3]);

    /// <summary>The table directory, as tag to where the table starts and how long it is.</summary>
    private static Dictionary<string, (int Offset, int Length)> Tables(byte[] program)
    {
        var count = (program[4] << 8) | program[5];
        var tables = new Dictionary<string, (int Offset, int Length)>(StringComparer.Ordinal);

        for (var index = 0; index < count; index++)
        {
            var entry = 12 + (index * 16);
            var tag = Encoding.ASCII.GetString(program, entry, 4);

            tables[tag] = ((int)Word(program, entry + 8), (int)Word(program, entry + 12));
        }

        return tables;
    }

    /// <summary>The offsets of the glyphs, read through the short <c>loca</c>.</summary>
    private static IEnumerable<uint> GlyphOffsets(
        byte[] program,
        Dictionary<string, (int Offset, int Length)> tables,
        int glyphCount)
    {
        var loca = tables["loca"].Offset;

        for (var index = 0; index <= glyphCount; index++)
        {
            var at = loca + (index * 2);

            yield return (uint)((program[at] << 8) | program[at + 1]);
        }
    }

    /// <summary>The sfnt checksum of the whole file, which a valid font makes <c>0xB1B0AFBA</c>.</summary>
    private static uint Checksum(byte[] program)
    {
        var sum = 0u;

        for (var index = 0; index < program.Length; index += 4)
        {
            var word = 0u;

            for (var offset = 0; offset < 4; offset++)
            {
                word <<= 8;

                if (index + offset < program.Length)
                {
                    word |= program[index + offset];
                }
            }

            sum = unchecked(sum + word);
        }

        return sum;
    }
}
