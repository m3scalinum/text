using System;
using M3scalinum.Text.Documents;
using Xunit;

namespace M3scalinum.Text.Barcodes.Tests;

/// <summary>
/// A symbol placed directly in a document: the caller hands over the document and the content, and
/// gets back a figure whose image and alternative text are already in the Typst source.
/// </summary>
public sealed class DocumentFigureTests
{
    [Fact]
    public void A_barcode_becomes_a_figure_with_an_image_and_an_alt()
    {
        var document = TypstDocument.Create();

        var figure = Barcode.AddToDocument(document, BarcodeSymbology.QrCode, "HELLO");

        var markup = document.ToMarkup();

        Assert.Contains("#figure(", markup, StringComparison.Ordinal);
        Assert.Contains($"image(\"{figure.AssetName}\"", markup, StringComparison.Ordinal);
        Assert.EndsWith(".png", figure.AssetName, StringComparison.Ordinal);
        Assert.Contains("alt: \"QRCode barcode: HELLO\"", markup, StringComparison.Ordinal);
        Assert.Contains("width:", markup, StringComparison.Ordinal);
        Assert.Contains("mm", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_alternative_text_can_be_replaced()
    {
        var document = TypstDocument.Create();

        Barcode.AddToDocument(
            document, BarcodeSymbology.Code128, "SN-42", altText: "Serial number 42");

        Assert.Contains("alt: \"Serial number 42\"", document.ToMarkup(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_figure_hands_back_the_raster_to_register_under_its_asset_name()
    {
        var document = TypstDocument.Create();

        var figure = Barcode.AddToDocument(
            document,
            BarcodeSymbology.QrCode,
            "ASSET",
            new BarcodePrintOptions { Dpi = 300, WidthMillimetres = 20 });

        Assert.True(PngHeader.IsPng(figure.Png));
        Assert.Equal(figure.Print.WidthPixels, PngHeader.Width(figure.Png));
        Assert.InRange(figure.WidthMillimetres, 19.0, 20.0);
        Assert.Equal(figure.Print.WidthMillimetres, figure.WidthMillimetres);
        Assert.StartsWith("barcode-qrcode-", figure.AssetName, StringComparison.Ordinal);
    }

    [Fact]
    public void The_asset_name_is_deterministic_and_payload_specific()
    {
        var first = Barcode.AddToDocument(
            TypstDocument.Create(), BarcodeSymbology.QrCode, "SAME");
        var second = Barcode.AddToDocument(
            TypstDocument.Create(), BarcodeSymbology.QrCode, "SAME");
        var other = Barcode.AddToDocument(
            TypstDocument.Create(), BarcodeSymbology.QrCode, "OTHER");

        Assert.Equal(first.AssetName, second.AssetName);
        Assert.NotEqual(first.AssetName, other.AssetName);
    }

    [Fact]
    public void An_asset_name_with_a_directory_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Barcode.AddToDocument(
            TypstDocument.Create(),
            BarcodeSymbology.QrCode,
            "ESCAPE",
            assetName: "../outside.png"));
    }

    [Fact]
    public void A_gs1_symbol_carries_the_human_readable_form_as_its_alt()
    {
        var document = TypstDocument.Create();
        var data = Gs1Data.Parse("0109501101530003\u001d10LOT-42");

        var figure = Barcode.AddGs1ToDocument(document, data);

        Assert.Contains(
            $"alt: \"GS1-128 barcode: {data.HumanReadable}\"",
            document.ToMarkup(),
            StringComparison.Ordinal);
        Assert.Equal(BarcodeSymbology.Gs1_128, figure.Print.Symbology);
    }

    [Fact]
    public void Two_symbols_land_in_one_document_as_two_figures()
    {
        var document = TypstDocument.Create();

        var qr = Barcode.AddToDocument(document, BarcodeSymbology.QrCode, "FIRST");
        var code = Barcode.AddToDocument(document, BarcodeSymbology.Code128, "SECOND");

        var markup = document.ToMarkup();

        Assert.Equal(2, Count(markup, "#figure("));
        Assert.Contains(qr.AssetName, markup, StringComparison.Ordinal);
        Assert.Contains(code.AssetName, markup, StringComparison.Ordinal);
        Assert.NotEqual(qr.AssetName, code.AssetName);
    }

    [Fact]
    public void The_document_is_required()
    {
        Assert.Throws<ArgumentNullException>(() => Barcode.AddToDocument(
            null!, BarcodeSymbology.QrCode, "NULL"));
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
