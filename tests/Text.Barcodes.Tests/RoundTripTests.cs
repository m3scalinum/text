using System;
using Xunit;

namespace M3scalinum.Text.Barcodes.Tests;

/// <summary>
/// The test that matters: encode, rasterise, decode, same payload — for every symbology the package
/// claims, at two raster scales.
/// </summary>
public sealed class RoundTripTests
{
    /// <summary>
    /// What a decoder returns for each payload.
    /// </summary>
    /// <remarks>
    /// Codabar's start and stop characters are guards, not data: an <c>A</c>…<c>B</c> symbol reads
    /// back as the digits between them, in every reader. Everything else comes back as written.
    /// </remarks>
    private static string Expected(BarcodeSymbology symbology, string payload)
        => symbology == BarcodeSymbology.Codabar ? payload[1..^1] : payload;

    [Theory]
    [InlineData(BarcodeSymbology.QrCode, "HELLO WORLD")]
    [InlineData(BarcodeSymbology.DataMatrix, "HELLO-DM")]
    [InlineData(BarcodeSymbology.Aztec, "HELLO-AZTEC")]
    [InlineData(BarcodeSymbology.Pdf417, "HELLO-PDF417")]
    [InlineData(BarcodeSymbology.Code128, "HELLO-128")]
    [InlineData(BarcodeSymbology.Code39, "HELLO-39")]
    [InlineData(BarcodeSymbology.Ean13, "5901234123457")]
    [InlineData(BarcodeSymbology.Ean8, "96385074")]
    [InlineData(BarcodeSymbology.Itf, "1234567890")]
    [InlineData(BarcodeSymbology.Codabar, "A123456789B")]
    public void A_symbol_round_trips_through_its_own_png(BarcodeSymbology symbology, string payload)
    {
        var image = Barcode.Encode(symbology, payload);

        Assert.True(PngHeader.IsPng(image.Png), "the raster is a PNG");
        Assert.Equal(image.ModuleWidth * 4, PngHeader.Width(image.Png));
        Assert.Equal(image.ModuleHeight * 4, PngHeader.Height(image.Png));

        var decoded = Barcode.Decode(image.Png);

        Assert.True(decoded.Success, decoded.Error);
        Assert.Equal(Expected(symbology, payload), decoded.Text);
        Assert.Equal(symbology, decoded.Symbology);

        // Two-dimensional symbols report the finder patterns their detector found; the stacked and
        // one-dimensional ones report the ends of the row that decoded, and a DataMatrix reports
        // nothing. Which points exist is the detector's business, so only the QR case is pinned.
        if (symbology == BarcodeSymbology.QrCode)
        {
            Assert.NotEmpty(decoded.Points);
        }
    }

    [Theory]
    [InlineData(BarcodeSymbology.QrCode, "TWO PIXELS")]
    [InlineData(BarcodeSymbology.DataMatrix, "TWO PIXELS")]
    [InlineData(BarcodeSymbology.Aztec, "TWO PIXELS")]
    [InlineData(BarcodeSymbology.Pdf417, "TWO PIXELS")]
    [InlineData(BarcodeSymbology.Code128, "TWO PIXELS")]
    [InlineData(BarcodeSymbology.Code39, "TWO PIXELS")]
    [InlineData(BarcodeSymbology.Ean13, "5901234123457")]
    [InlineData(BarcodeSymbology.Ean8, "96385074")]
    [InlineData(BarcodeSymbology.Itf, "1234567890")]
    [InlineData(BarcodeSymbology.Codabar, "A987654321B")]
    public void A_symbol_round_trips_at_two_pixels_per_module(
        BarcodeSymbology symbology, string payload)
    {
        var image = Barcode.Encode(symbology, payload, new BarcodeOptions { Scale = 2 });

        Assert.Equal(image.ModuleWidth * 2, PngHeader.Width(image.Png));

        var decoded = Barcode.Decode(image.Png);

        Assert.True(decoded.Success, decoded.Error);
        Assert.Equal(Expected(symbology, payload), decoded.Text);
    }

    [Fact]
    public void A_qr_code_round_trips_in_utf8()
    {
        const string Payload = "Facture n° 2026-0042 — 1 250,00 €";

        var image = Barcode.Encode(BarcodeSymbology.QrCode, Payload);
        var decoded = Barcode.Decode(image.Png);

        Assert.True(decoded.Success, decoded.Error);
        Assert.Equal(Payload, decoded.Text);
    }

    [Fact]
    public void The_png_is_a_grey_image_of_black_and_white_pixels()
    {
        var image = Barcode.Encode(BarcodeSymbology.QrCode, "MONOCHROME");

        Assert.Equal((8, 0), PngHeader.Format(image.Png));
    }

    [Fact]
    public void The_vector_and_the_raster_describe_the_same_matrix()
    {
        var image = Barcode.Encode(BarcodeSymbology.DataMatrix, "SAME MATRIX");

        // One unit per module in the vector, so the viewBox is the module matrix and the PNG is
        // that matrix times the scale. Neither rendering is allowed to disagree with the other.
        Assert.Contains($"viewBox=\"0 0 {image.ModuleWidth} {image.ModuleHeight}\"", image.Svg, StringComparison.Ordinal);
        Assert.Equal(image.ModuleWidth * 4, PngHeader.Width(image.Png));
        Assert.Equal(image.ModuleHeight * 4, PngHeader.Height(image.Png));
    }
}
