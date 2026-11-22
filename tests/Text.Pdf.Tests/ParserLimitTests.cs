using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Xunit;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// The bounds a received PDF meets on the two surfaces that had none: the decoded size of a stream,
/// and the XMP packet the converter reads before it rewrites it.
/// </summary>
/// <remarks>
/// <para>
/// Both are rows of <c>docs/specifications/29-threat-model.md</c> that said "no limit". Each test
/// here fails if its bound is removed: the decompression bomb inflates into the page's text, and the
/// hostile packet is accepted instead of refused. Neither asserts the value of a constant, which is
/// what P5 of that document forbids.
/// </para>
/// <para>
/// A stream is built from its real bytes — a deflated run of zeroes — because the point of the bound
/// is that the declared <c>/Length</c> says nothing about what the filters produce.
/// </para>
/// </remarks>
public sealed class ParserLimitTests
{
    /// <summary>What the deflated payload inflates to: far past the cap, and from a few kilobytes.</summary>
    private const int BombDecodedBytes = 100 * 1024 * 1024;

    [Fact]
    public void A_flate_stream_that_would_decode_past_the_limit_is_not_read()
    {
        // One hundred megabytes of zeroes, deflated to tens of kilobytes. Before the bound existed,
        // this page's content stream was copied into a MemoryStream whole: a mailed file allocated
        // its decoded size in the reader's process.
        var pdf = DocumentWithBombedContentStream();

        var page = PdfText.Extract(pdf).Pages[0];

        Assert.Equal(string.Empty, page.Text);
        Assert.Equal(1, page.SkippedContentStreams);
    }

    [Fact]
    public void A_stream_past_the_limit_is_a_reading_problem_that_names_the_limit()
    {
        // The typed half of the same refusal: the stream is not decoded, and the report says why, in
        // bytes, rather than leaving the caller to infer it from an empty page.
        Assert.True(PdfFile.TryOpen(DocumentWithBombedContentStream(), out var file));
        Assert.NotNull(file);

        var stream = Assert.IsType<PdfStream>(file.GetObject(4));
        Assert.Null(file.Decode(stream));

        var problem = Assert.Single(file.Problems, entry => entry.Code == PdfConformanceCodes.ObjectUnreadable);
        Assert.Contains($"{PdfFile.MaxStreamBytes}-byte", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stream_just_under_the_limit_is_decoded()
    {
        // The bound refuses what is past it, not what is near it: a stream of exactly the cap is a
        // document, and a reader that refused it would break on an embedded profile that is large.
        var data = new byte[PdfFile.MaxStreamBytes];
        var pdf = Document(
            Glyphs("<</Type/Catalog/Pages 2 0 R>>"),
            Glyphs("<</Type/Pages/Count 1/Kids[3 0 R]>>"),
            Glyphs("<</Type/Page/Parent 2 0 R/MediaBox[0 0 100 100]/Contents 4 0 R>>"),
            Stream("/Filter/FlateDecode", Deflate(data)));

        Assert.True(PdfFile.TryOpen(pdf, out var file));
        Assert.NotNull(file);

        var stream = Assert.IsType<PdfStream>(file.GetObject(4));
        Assert.Equal(data.Length, file.Decode(stream)?.Length);
    }

    [Fact]
    public void An_XMP_packet_over_the_byte_limit_is_refused()
    {
        var exception = Assert.Throws<PdfOperationException>(
            () => PdfAXmp.Rewrite(new byte[PdfAXmp.MaxXmpBytes + 1], 2, "b", null));

        Assert.Contains("XMP limit", exception.Message, StringComparison.Ordinal);
        Assert.Contains((PdfAXmp.MaxXmpBytes + 1).ToString(CultureInfo.InvariantCulture), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_XMP_packet_nested_past_the_depth_limit_is_refused()
    {
        var packet = Packet($"<x:deep>{string.Concat(Enumerable.Repeat("<a>", PdfAXmp.MaxXmpDepth + 1))}</x:deep>");

        var exception = Assert.Throws<PdfOperationException>(() => PdfAXmp.Rewrite(Utf8(packet), 2, "b", null));

        Assert.Contains($"{PdfAXmp.MaxXmpDepth}-level XMP limit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_XMP_packet_past_the_node_limit_is_refused()
    {
        // Siblings, not nesting: the depth bound must not be what refuses it. Four hundred kilobytes
        // of empty elements are a hundred thousand nodes.
        var packet = Packet($"<x:many>{string.Concat(Enumerable.Repeat("<n/>", PdfAXmp.MaxXmpNodes))}</x:many>");

        var exception = Assert.Throws<PdfOperationException>(() => PdfAXmp.Rewrite(Utf8(packet), 2, "b", null));

        Assert.Contains($"{PdfAXmp.MaxXmpNodes}-node XMP limit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_XMP_packet_that_declares_a_document_type_is_refused()
    {
        var packet = "<!DOCTYPE xmpmeta [<!ELEMENT xmpmeta ANY>]>" + Packet("<x:empty/>");

        var exception = Assert.Throws<PdfOperationException>(() => PdfAXmp.Rewrite(Utf8(packet), 2, "b", null));

        Assert.Contains("DTD", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_XMP_packet_that_declares_an_external_entity_is_refused()
    {
        // The packet that made the old default reader worth questioning: a metadata stream is bytes a
        // stranger wrote, and one of them asks the parser to read a file.
        var packet = "<!DOCTYPE xmpmeta [<!ENTITY secret SYSTEM \"file:///etc/passwd\">]>" + Packet("<x:empty/>");

        var exception = Assert.Throws<PdfOperationException>(() => PdfAXmp.Rewrite(Utf8(packet), 2, "b", null));

        Assert.Contains("external", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_XMP_packet_within_the_policy_is_kept_and_declares_the_standard()
    {
        // The control: the policy bounds hostile packets without refusing an ordinary one, and what
        // the packet already said survives the rewrite.
        var packet = Packet("<rdf:Description rdf:about=\"\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:title>Facture</dc:title></rdf:Description>");

        var bytes = PdfAXmp.Rewrite(Utf8(packet), 2, "b", null);
        var text = Encoding.UTF8.GetString(bytes);

        Assert.Contains("pdfaid:part=\"2\"", text, StringComparison.Ordinal);
        Assert.Contains("Facture", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Converting_a_document_whose_XMP_declares_a_document_type_is_refused_by_name()
    {
        // The public half: a caller converting a received PDF gets a typed refusal that names the
        // rule, not a replaced packet and not a silent exception from inside the XML reader.
        var hostile = "<!DOCTYPE xmpmeta [<!ENTITY e \"x\">]>" + Packet("<x:empty/>");
        var pdf = Document(
            Glyphs("<</Type/Catalog/Pages 2 0 R/Metadata 4 0 R>>"),
            Glyphs("<</Type/Pages/Count 1/Kids[3 0 R]>>"),
            Glyphs("<</Type/Page/Parent 2 0 R/MediaBox[0 0 100 100]>>"),
            Stream("/Type/Metadata/Subtype/XML", Utf8(hostile)));

        var options = new PdfAConversionOptions
        {
            Target = PdfConformanceStandard.PdfA2b,
            IccProfile = SyntheticProfile(),
        };

        var exception = Assert.Throws<PdfOperationException>(() => PdfAConverter.Convert(pdf, options));

        Assert.Contains("DTD", exception.Message, StringComparison.Ordinal);
        Assert.Contains("XMP", exception.Message, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // Fixtures
    // -----------------------------------------------------------------------

    private static byte[] DocumentWithBombedContentStream()
        => Document(
            Glyphs("<</Type/Catalog/Pages 2 0 R>>"),
            Glyphs("<</Type/Pages/Count 1/Kids[3 0 R]>>"),
            Glyphs("<</Type/Page/Parent 2 0 R/MediaBox[0 0 100 100]/Contents 4 0 R>>"),
            Stream("/Filter/FlateDecode", Deflate(new byte[BombDecodedBytes])));

    /// <summary>An object body written in Latin-1, the encoding a PDF's syntax is defined in.</summary>
    private static byte[] Glyphs(string body) => Encoding.Latin1.GetBytes(body);

    /// <summary>A stream object: its dictionary, its bytes, and the <c>/Length</c> that measures them.</summary>
    private static byte[] Stream(string entries, byte[] data)
        =>
        [
            .. Glyphs($"<<{entries}/Length {data.Length.ToString(CultureInfo.InvariantCulture)}>>\nstream\n"),
            .. data,
            .. Glyphs("\nendstream"),
        ];

    /// <summary>
    /// A document whose objects are the arrays given, numbered from one, with a classic
    /// cross-reference table. Offsets are byte offsets into the file, as the format requires.
    /// </summary>
    private static byte[] Document(params byte[][] bodies)
    {
        using var file = new MemoryStream();
        using var text = new MemoryStream();

        text.Write(Glyphs("%PDF-1.7\n"));

        var offsets = new List<long>();

        for (var index = 0; index < bodies.Length; index++)
        {
            offsets.Add(text.Length);
            text.Write(Glyphs(string.Create(CultureInfo.InvariantCulture, $"{index + 1} 0 obj\n")));
            text.Write(bodies[index]);
            text.Write(Glyphs("\nendobj\n"));
        }

        var startxref = text.Length;

        text.Write(Glyphs(string.Create(CultureInfo.InvariantCulture, $"xref\n0 {bodies.Length + 1}\n")));
        text.Write(Glyphs("0000000000 65535 f \r\n"));

        foreach (var offset in offsets)
        {
            text.Write(Glyphs(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \r\n"));
        }

        text.Write(Glyphs(string.Create(
            CultureInfo.InvariantCulture,
            $"trailer\n<</Size {bodies.Length + 1}/Root 1 0 R>>\nstartxref\n{startxref}\n%%EOF\n")));

        text.Position = 0;
        text.CopyTo(file);

        return file.ToArray();
    }

    /// <summary>The bytes written by a zlib stream: what a <c>FlateDecode</c> stream holds.</summary>
    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();

        using (var inflater = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            inflater.Write(data);
        }

        return output.ToArray();
    }

    /// <summary>An XMP packet in the shape a producer writes, with <paramref name="body"/> as its payload.</summary>
    private static string Packet(string body)
        => "<?xpacket begin=\"\uFEFF\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>"
            + "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">"
            + "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">"
            + body
            + "</rdf:RDF></x:xmpmeta>"
            + "<?xpacket end=\"w\"?>";

    private static byte[] Utf8(string text) => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);

    /// <summary>
    /// A profile the converter can describe — <c>acsp</c> at byte 36, an RGB data colour space at
    /// byte 16 — because the test needs the conversion to reach the metadata, not to convert a colour.
    /// </summary>
    private static byte[] SyntheticProfile()
    {
        var profile = new byte[132];

        Encoding.ASCII.GetBytes("RGB ").CopyTo(profile, 16);
        Encoding.ASCII.GetBytes("acsp").CopyTo(profile, 36);

        return profile;
    }
}
