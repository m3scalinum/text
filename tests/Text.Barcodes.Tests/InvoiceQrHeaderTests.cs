using System;
using Xunit;

namespace M3scalinum.Text.Barcodes.Tests;

/// <summary>
/// An invoice header is what a QR code on an invoice carries: ASCII <c>key=value</c> lines. It is
/// the payload this package was built for, so it gets its own round trip rather than a stand-in.
/// </summary>
public sealed class InvoiceQrHeaderTests
{
    private static readonly string[] Header =
    [
        "BT-1=FA-2026-000123",
        "BT-2=380",
        "BT-3=EUR",
        "BT-5=1250.00",
        "BT-9=2026-11-21",
        "BT-10=FR12345678901",
        "BT-13=FR99123456789",
        "BT-31=FR12345678901",
        "BT-49=seller@example.fr",
    ];

    private static string Payload => string.Join("\n", Header);

    [Fact]
    public void A_factur_x_header_round_trips_through_a_qr_code()
    {
        var image = Barcode.Encode(BarcodeSymbology.QrCode, Payload);

        var decoded = Barcode.Decode(image.Png);

        Assert.True(decoded.Success, decoded.Error);
        Assert.Equal(Payload, decoded.Text);
        Assert.Equal(BarcodeSymbology.QrCode, decoded.Symbology);
    }

    [Fact]
    public void Every_business_term_survives_the_round_trip_individually()
    {
        var decoded = Barcode.Decode(Barcode.Encode(BarcodeSymbology.QrCode, Payload).Png);

        var lines = decoded.Text.Split('\n');

        Assert.Equal(Header.Length, lines.Length);
        for (var index = 0; index < Header.Length; index++)
        {
            Assert.Equal(Header[index], lines[index]);
        }
    }

    [Fact]
    public void The_header_is_dense_enough_to_need_more_than_a_version_one_symbol()
    {
        // A version 1 QR is 21 modules plus the quiet zone. A header of nine terms is past it, which
        // is the property that makes this test worth its own class: a real invoice header is not a
        // toy payload that any encoder gets right.
        var image = Barcode.Encode(BarcodeSymbology.QrCode, Payload);

        Assert.True(image.ModuleWidth > 29, $"the symbol is {image.ModuleWidth} modules wide");
        Assert.Equal(image.ModuleWidth, image.ModuleHeight);
    }

    [Fact]
    public void A_higher_error_correction_makes_the_same_header_larger()
    {
        var low = Barcode.Encode(
            BarcodeSymbology.QrCode, Payload, new BarcodeOptions { ErrorCorrection = BarcodeErrorCorrection.Low });
        var high = Barcode.Encode(
            BarcodeSymbology.QrCode, Payload, new BarcodeOptions { ErrorCorrection = BarcodeErrorCorrection.High });

        Assert.True(
            high.ModuleWidth >= low.ModuleWidth,
            $"H is {high.ModuleWidth} modules, L is {low.ModuleWidth}");
        Assert.Equal(Payload, Barcode.Decode(high.Png).Text);
    }

    [Fact]
    public void The_same_header_also_fits_a_data_matrix()
    {
        // An invoice header belongs in a QR code, but a DataMatrix is what a
        // supplier label carries; both are in the supported set and both must hold the header.
        var image = Barcode.Encode(BarcodeSymbology.DataMatrix, Payload);

        var decoded = Barcode.Decode(image.Png);

        Assert.True(decoded.Success, decoded.Error);
        Assert.Equal(Payload, decoded.Text);
        Assert.Equal(BarcodeSymbology.DataMatrix, decoded.Symbology);
    }
}
