using System;
using System.IO;
using System.Text;
using Xunit;

namespace M3scalinum.Text.Pdf.FactureX.Tests;

/// <summary>
/// The patch rewrites a PDF it can read and refuses the rest with a message that names the reason.
/// A silent failure here would produce a file that looks like an invoice and is not one, so every
/// refusal is pinned.
/// </summary>
public sealed class PdfPatchTests
{
    private const string Catalog = """
        %PDF-1.7
        1 0 obj
        <</Type/Catalog/Pages 2 0 R>>
        endobj
        2 0 obj
        <</Type/Pages/Count 0/Kids[]>>
        endobj
        trailer
        <</Size 3/Root 1 0 R>>
        startxref
        0
        %%EOF
        """;

    private static byte[] Patch(string pdf) => FacturXPdf.AddMetadata(
        Encoding.Latin1.GetBytes(pdf), "factur-x.xml", FacturXProfile.En16931);

    [Fact]
    public void Data_that_is_not_a_pdf_is_refused()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Patch("hello"));

        Assert.Contains("not a PDF", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pdf_without_a_trailer_is_refused()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => Patch("%PDF-1.7\nstartxref\n0\n%%EOF\n"));

        Assert.Contains("trailer", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_trailer_without_a_root_is_refused()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => Patch("%PDF-1.7\ntrailer\n<</Size 1>>\nstartxref\n0\n%%EOF\n"));

        Assert.Contains("/Root", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_catalog_without_metadata_is_refused()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Patch(Catalog));

        Assert.Contains("/Metadata", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_compressed_metadata_stream_is_refused()
    {
        const string Compressed = """
            %PDF-1.7
            1 0 obj
            <</Type/Catalog/Pages 2 0 R/Metadata 3 0 R>>
            endobj
            2 0 obj
            <</Type/Pages/Count 0/Kids[]>>
            endobj
            3 0 obj
            <</Length 4/Filter/FlateDecode/Type/Metadata/Subtype/XML>>
            stream
            xxxx
            endstream
            endobj
            trailer
            <</Size 4/Root 1 0 R>>
            startxref
            0
            %%EOF
            """;

        var exception = Assert.Throws<InvalidOperationException>(() => Patch(Compressed));

        Assert.Contains("compressed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Metadata_without_a_pdfa_schema_bag_is_refused()
    {
        const string NoSchema = """
            %PDF-1.7
            1 0 obj
            <</Type/Catalog/Pages 2 0 R/Metadata 3 0 R>>
            endobj
            2 0 obj
            <</Type/Pages/Count 0/Kids[]>>
            endobj
            3 0 obj
            <</Length 40/Type/Metadata/Subtype/XML>>
            stream
            <x:xmpmeta><rdf:RDF></rdf:RDF></x:xmpmeta>
            endstream
            endobj
            trailer
            <</Size 4/Root 1 0 R>>
            startxref
            0
            %%EOF
            """;

        var exception = Assert.Throws<InvalidOperationException>(() => Patch(NoSchema));

        Assert.Contains("pdfaExtension:schemas", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_patchable_pdf_gets_the_extension_and_keeps_its_original_bytes()
    {
        const string Patchable = """
            %PDF-1.7
            1 0 obj
            <</Type/Catalog/Pages 2 0 R/Metadata 3 0 R>>
            endobj
            2 0 obj
            <</Type/Pages/Count 0/Kids[]>>
            endobj
            3 0 obj
            <</Length 120/Type/Metadata/Subtype/XML>>
            stream
            <x:xmpmeta><rdf:RDF><rdf:Description><pdfaExtension:schemas><rdf:Bag></rdf:Bag></pdfaExtension:schemas></rdf:Description></rdf:RDF></x:xmpmeta>
            endstream
            endobj
            trailer
            <</Size 4/Root 1 0 R/ID[<aa><bb>]>>
            startxref
            0
            %%EOF
            """;

        var patched = Encoding.Latin1.GetString(Patch(Patchable));

        Assert.StartsWith("%PDF-1.7", patched, StringComparison.Ordinal);
        Assert.Contains("<fx:DocumentFileName>factur-x.xml</fx:DocumentFileName>", patched, StringComparison.Ordinal);
        Assert.Contains("<fx:ConformanceLevel>EN 16931</fx:ConformanceLevel>", patched, StringComparison.Ordinal);
        Assert.Contains("Factur-X PDFA Extension Schema", patched, StringComparison.Ordinal);

        // The incremental update appends; the original document is still there, byte for byte.
        Assert.Contains("<</Type/Catalog/Pages 2 0 R/Metadata 3 0 R>>", patched, StringComparison.Ordinal);
        Assert.Contains("/Prev 0", patched, StringComparison.Ordinal);
        Assert.Contains("/ID[<aa><bb>]", patched, StringComparison.Ordinal);
        Assert.EndsWith("%%EOF\n", patched, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_name_with_markup_characters_is_escaped()
    {
        var patched = Encoding.Latin1.GetString(FacturXPdf.AddMetadata(
            Encoding.Latin1.GetBytes("""
                %PDF-1.7
                1 0 obj
                <</Type/Catalog/Pages 2 0 R/Metadata 3 0 R>>
                endobj
                3 0 obj
                <</Length 120/Type/Metadata/Subtype/XML>>
                stream
                <x:xmpmeta><rdf:RDF><rdf:Description><pdfaExtension:schemas><rdf:Bag></rdf:Bag></pdfaExtension:schemas></rdf:Description></rdf:RDF></x:xmpmeta>
                endstream
                endobj
                trailer
                <</Size 4/Root 1 0 R>>
                startxref
                0
                %%EOF
                """),
            "a&b.xml",
            FacturXProfile.Basic));

        Assert.Contains("<fx:DocumentFileName>a&amp;b.xml</fx:DocumentFileName>", patched, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_file_name_is_refused()
    {
        Assert.Throws<ArgumentException>(() => FacturXPdf.AddMetadata(
            Encoding.Latin1.GetBytes(Catalog), " ", FacturXProfile.En16931));
    }
}
