using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Xunit;

namespace M3scalinum.Text.Pdf.FactureX.Tests;

/// <summary>
/// The receiving half: a platform, an auditor or a test wants the bytes the sender embedded. These
/// tests pin the shapes a PDF writer produces and the refusals that keep a broken file from being
/// read as an invoice.
/// </summary>
public sealed class PdfExtractionTests
{
    private const string Invoice = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rsm:CrossIndustryInvoice xmlns:rsm="urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100" xmlns:ram="urn:un:unece:uncefact:data:standard:ReusableAggregateBusinessInformationEntity:100" xmlns:udt="urn:un:unece:uncefact:data:standard:UnqualifiedDataType:100">
          <rsm:ExchangedDocument>
            <ram:ID>FA-2026-0042</ram:ID>
            <ram:TypeCode>380</ram:TypeCode>
            <ram:IssueDateTime><udt:DateTimeString format="102">20260201</udt:DateTimeString></ram:IssueDateTime>
          </rsm:ExchangedDocument>
          <rsm:SupplyChainTradeTransaction/>
        </rsm:CrossIndustryInvoice>
        """;

    /// <summary>A PDF with one attachment, written the way a producer writes it.</summary>
    private static string Pdf(string name, byte[] payload, string filter = "/Filter/FlateDecode", string nameToken = "")
    {
        var nameEntry = nameToken.Length > 0 ? nameToken : $"({name})";
        var filterEntry = filter.Length > 0 ? filter : string.Empty;

        return $"""
            %PDF-1.7
            1 0 obj
            <</Type/Catalog/Pages 2 0 R/Names 4 0 R>>
            endobj
            2 0 obj
            <</Type/Pages/Count 0/Kids[]>>
            endobj
            3 0 obj
            <</Type/Filespec/F({name})/EF<</F 5 0 R>>>>
            endobj
            4 0 obj
            <</EmbeddedFiles<</Names[{nameEntry} 3 0 R]>>>>
            endobj
            5 0 obj
            <</Type/EmbeddedFile{filterEntry}/Length {payload.Length}>>
            stream
            {Encoding.Latin1.GetString(payload)}
            endstream
            endobj
            trailer
            <</Size 6/Root 1 0 R>>
            startxref
            0
            %%EOF
            """;
    }

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }

    [Fact]
    public void A_flate_compressed_attachment_is_extracted_and_read()
    {
        var xml = Encoding.UTF8.GetBytes(Invoice);
        var pdf = Encoding.Latin1.GetBytes(Pdf("factur-x.xml", Deflate(xml)));

        Assert.True(FacturXReader.TryExtractXml(pdf, out var name, out var extracted));
        Assert.Equal("factur-x.xml", name);
        Assert.Equal(xml, extracted);

        var invoice = FacturXReader.ReadPdf(pdf);
        Assert.Equal("FA-2026-0042", invoice.Number);
    }

    [Fact]
    public void An_uncompressed_attachment_is_extracted_too()
    {
        var xml = Encoding.UTF8.GetBytes(Invoice);
        var pdf = Encoding.Latin1.GetBytes(Pdf("factur-x.xml", xml, filter: string.Empty));

        Assert.True(FacturXReader.TryExtractXml(pdf, out _, out var extracted));
        Assert.Equal(xml, extracted);
    }

    [Fact]
    public void A_hex_encoded_name_is_decoded()
    {
        var xml = Encoding.UTF8.GetBytes(Invoice);
        var hex = Convert.ToHexString(Encoding.ASCII.GetBytes("factur-x.xml"));
        var pdf = Encoding.Latin1.GetBytes(Pdf("factur-x.xml", Deflate(xml), nameToken: $"<{hex}>"));

        Assert.True(FacturXReader.TryExtractXml(pdf, out var name, out _));
        Assert.Equal("factur-x.xml", name);
    }

    [Fact]
    public void A_pdf_without_an_attachment_is_refused_with_a_reason()
    {
        const string Bare = """
            %PDF-1.7
            1 0 obj
            <</Type/Catalog/Pages 2 0 R>>
            endobj
            trailer
            <</Size 2/Root 1 0 R>>
            startxref
            0
            %%EOF
            """;

        var pdf = Encoding.Latin1.GetBytes(Bare);

        Assert.False(FacturXReader.TryExtractXml(pdf, out _, out _));

        var exception = Assert.Throws<FacturXFormatException>(() => FacturXReader.ReadPdf(pdf));
        Assert.Contains("no embedded invoice", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_input_is_refused_without_throwing()
    {
        Assert.False(FacturXReader.TryExtractXml(ReadOnlySpan<byte>.Empty, out _, out _));
    }

    [Fact]
    public void An_unsupported_filter_is_named_in_the_error()
    {
        var pdf = Encoding.Latin1.GetBytes(Pdf("factur-x.xml", Encoding.UTF8.GetBytes(Invoice), filter: "/Filter/LZWDecode"));

        var exception = Assert.Throws<FacturXFormatException>(() => FacturXReader.ReadPdf(pdf));

        Assert.Contains("LZWDecode", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Corrupt_flate_data_is_reported_as_such()
    {
        var pdf = Encoding.Latin1.GetBytes(Pdf("factur-x.xml", Encoding.ASCII.GetBytes("not zlib at all")));

        var exception = Assert.Throws<FacturXFormatException>(() => FacturXReader.ReadPdf(pdf));

        Assert.Contains("Flate", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_attachment_that_is_not_an_invoice_is_refused()
    {
        var pdf = Encoding.Latin1.GetBytes(Pdf("factur-x.xml", Deflate(Encoding.UTF8.GetBytes("<Order><ID>1</ID></Order>"))));

        var exception = Assert.Throws<FacturXFormatException>(() => FacturXReader.ReadPdf(pdf));

        Assert.Contains("not a readable invoice", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_canonical_name_wins_over_another_attachment()
    {
        // Two attachments: the reader must pick factur-x.xml, not the first one it meets.
        var xml = Encoding.UTF8.GetBytes(Invoice);
        var payload = Deflate(xml);

        var pdf = Encoding.Latin1.GetBytes($"""
            %PDF-1.7
            1 0 obj
            <</Type/Catalog/Pages 2 0 R/Names 4 0 R>>
            endobj
            2 0 obj
            <</Type/Pages/Count 0/Kids[]>>
            endobj
            3 0 obj
            <</Type/Filespec/F(notes.txt)/EF<</F 6 0 R>>>>
            endobj
            4 0 obj
            <</EmbeddedFiles<</Names[(notes.txt) 3 0 R (factur-x.xml) 5 0 R]>>>>
            endobj
            5 0 obj
            <</Type/Filespec/F(factur-x.xml)/EF<</F 7 0 R>>>>
            endobj
            6 0 obj
            <</Type/EmbeddedFile/Length 5>>
            stream
            hello
            endstream
            endobj
            7 0 obj
            <</Type/EmbeddedFile/Filter/FlateDecode/Length {payload.Length}>>
            stream
            {Encoding.Latin1.GetString(payload)}
            endstream
            endobj
            trailer
            <</Size 8/Root 1 0 R>>
            startxref
            0
            %%EOF
            """);

        Assert.True(FacturXReader.TryExtractXml(pdf, out var name, out var extracted));
        Assert.Equal("factur-x.xml", name);
        Assert.Equal(xml, extracted);
    }
}
