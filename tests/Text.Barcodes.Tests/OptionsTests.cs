using System;
using Xunit;

namespace M3scalinum.Text.Barcodes.Tests;

/// <summary>What the options actually change, measured on the two renderings.</summary>
public sealed class OptionsTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    public void The_scale_sets_the_raster_size_and_nothing_else(int scale)
    {
        var image = Barcode.Encode(
            BarcodeSymbology.QrCode, "SCALE", new BarcodeOptions { Scale = scale });

        Assert.Equal(image.ModuleWidth * scale, PngHeader.Width(image.Png));
        Assert.Equal(image.ModuleHeight * scale, PngHeader.Height(image.Png));

        // The vector does not carry a resolution: it is the module matrix, whatever the raster is.
        Assert.Contains(
            $"viewBox=\"0 0 {image.ModuleWidth} {image.ModuleHeight}\"",
            image.Svg,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_scale_of_one_is_accepted_for_every_symbol_whose_detector_reads_it()
    {
        // Aztec is the one symbology whose detector needs two pixels per module; it is asserted
        // separately below rather than quietly skipped here.
        foreach (var symbology in new[]
                 {
                     BarcodeSymbology.QrCode,
                     BarcodeSymbology.DataMatrix,
                     BarcodeSymbology.Pdf417,
                     BarcodeSymbology.Code128,
                     BarcodeSymbology.Code39,
                     BarcodeSymbology.Ean13,
                     BarcodeSymbology.Ean8,
                     BarcodeSymbology.Itf,
                     BarcodeSymbology.Codabar,
                 })
        {
            var payload = symbology switch
            {
                BarcodeSymbology.Ean13 => "5901234123457",
                BarcodeSymbology.Ean8 => "96385074",
                BarcodeSymbology.Itf => "1234567890",
                BarcodeSymbology.Codabar => "A123456789B",
                _ => "ONE-PIXEL",
            };

            var image = Barcode.Encode(symbology, payload, new BarcodeOptions { Scale = 1 });
            Assert.Equal(image.ModuleWidth, PngHeader.Width(image.Png));
            Assert.True(Barcode.Decode(image.Png).Success, symbology.ToString());
        }
    }

    [Fact]
    public void An_aztec_symbol_needs_two_pixels_per_module_to_be_read_back()
    {
        var one = Barcode.Encode(
            BarcodeSymbology.Aztec, "AZTEC", new BarcodeOptions { Scale = 1 });
        var two = Barcode.Encode(
            BarcodeSymbology.Aztec, "AZTEC", new BarcodeOptions { Scale = 2 });

        Assert.False(Barcode.Decode(one.Png).Success);
        Assert.True(Barcode.Decode(two.Png).Success);
    }

    [Fact]
    public void The_quiet_zone_widens_the_matrix_by_two_per_side()
    {
        var narrow = Barcode.Encode(
            BarcodeSymbology.QrCode, "QUIET", new BarcodeOptions { QuietZone = 1 });
        var wide = Barcode.Encode(
            BarcodeSymbology.QrCode, "QUIET", new BarcodeOptions { QuietZone = 6 });

        Assert.Equal(narrow.ModuleWidth + 10, wide.ModuleWidth);
        Assert.Equal(narrow.ModuleHeight + 10, wide.ModuleHeight);
    }

    [Fact]
    public void A_quiet_zone_of_one_module_still_decodes_a_qr_code()
    {
        // Below the specification's four modules, but a generated symbol with clean edges still
        // reads: this is the assertion that the option is wired through, not a recommendation.
        var image = Barcode.Encode(
            BarcodeSymbology.QrCode, "NARROW", new BarcodeOptions { QuietZone = 1 });

        Assert.True(Barcode.Decode(image.Png).Success);
    }

    [Fact]
    public void The_bar_height_sets_the_height_of_a_one_dimensional_symbol()
    {
        var shortBar = Barcode.Encode(
            BarcodeSymbology.Code128, "HEIGHT", new BarcodeOptions { BarHeight = 12 });
        var tallBar = Barcode.Encode(
            BarcodeSymbology.Code128, "HEIGHT", new BarcodeOptions { BarHeight = 90 });

        Assert.Equal(12, shortBar.ModuleHeight);
        Assert.Equal(90, tallBar.ModuleHeight);
        Assert.Equal(shortBar.ModuleWidth, tallBar.ModuleWidth);

        Assert.Equal(tallBar.ModuleHeight * 4, PngHeader.Height(tallBar.Png));
        Assert.True(Barcode.Decode(tallBar.Png).Success);
    }

    [Fact]
    public void The_bar_height_is_ignored_by_a_two_dimensional_symbol()
    {
        var plain = Barcode.Encode(BarcodeSymbology.QrCode, "FLAT");
        var withBarHeight = Barcode.Encode(
            BarcodeSymbology.QrCode, "FLAT", new BarcodeOptions { BarHeight = 100 });

        Assert.Equal(plain.ModuleHeight, withBarHeight.ModuleHeight);
    }

    [Fact]
    public void Every_error_correction_level_round_trips()
    {
        foreach (var level in Enum.GetValues<BarcodeErrorCorrection>())
        {
            var image = Barcode.Encode(
                BarcodeSymbology.QrCode, "EC", new BarcodeOptions { ErrorCorrection = level });

            var decoded = Barcode.Decode(image.Png);

            Assert.True(decoded.Success, level.ToString());
            Assert.Equal("EC", decoded.Text);
        }
    }
}
