using System;
using Xunit;

namespace M3scalinum.Text.Barcodes.Tests;

/// <summary>
/// The printable half: a target in millimetres and dots per inch, and the size the symbol really
/// prints at. Every assertion reads a real render, because the point of the feature is that the
/// number a caller puts in a layout is the number the printer produces.
/// </summary>
public sealed class PrintTests
{
    [Fact]
    public void A_print_reports_the_width_it_really_prints_at()
    {
        var print = Barcode.Print(
            BarcodeSymbology.QrCode,
            "HELLO",
            new BarcodePrintOptions { Dpi = 300, WidthMillimetres = 30 });

        // 30 mm at 300 dpi is 354.33 pixels; 29 modules take 12 pixels each, so 348 and 29.464 mm.
        Assert.Equal(300, print.Dpi);
        Assert.Equal(29, print.WidthModules);
        Assert.Equal(29, print.HeightModules);
        Assert.Equal(12, print.ModulePixels);
        Assert.Equal(348, print.WidthPixels);
        Assert.Equal(348, print.HeightPixels);
        Assert.Equal(1.016, print.ModuleMillimetres, 9);
        Assert.Equal(29.464, print.WidthMillimetres, 9);
        Assert.True(print.WidthMillimetres <= 30.0, "the printed width never exceeds the target");
    }

    [Fact]
    public void The_raster_is_the_size_the_geometry_promises()
    {
        var print = Barcode.Print(
            BarcodeSymbology.Code128,
            "ABC-123",
            new BarcodePrintOptions { Dpi = 300, ModuleMillimetres = 0.5 });

        Assert.Equal(print.WidthPixels, PngHeader.Width(print.Png));
        Assert.Equal(print.HeightPixels, PngHeader.Height(print.Png));
        Assert.Equal(6, print.ModulePixels);
        Assert.Equal(0.508, print.ModuleMillimetres, 9);
        Assert.True(print.HeightMillimetres > print.WidthMillimetres / 10);
    }

    [Fact]
    public void A_printed_symbol_still_decodes_back_to_its_payload()
    {
        var print = Barcode.Print(
            BarcodeSymbology.QrCode,
            "PRINTED",
            new BarcodePrintOptions { Dpi = 300, WidthMillimetres = 25 });

        var decoded = Barcode.Decode(print.Png);

        Assert.True(decoded.Success, decoded.Error);
        Assert.Equal("PRINTED", decoded.Text);
    }

    [Fact]
    public void The_print_uses_the_normative_quiet_zone_of_the_symbology()
    {
        var code = Barcode.Print(
            BarcodeSymbology.Code128,
            "QUIET",
            new BarcodePrintOptions { ModuleMillimetres = 0.5 });

        var narrower = Barcode.Encode(
            BarcodeSymbology.Code128, "QUIET", new BarcodeOptions { QuietZone = 10, BarHeight = 40 });

        Assert.Equal(10, code.QuietZone);
        Assert.Equal(narrower.ModuleWidth, code.WidthModules);

        var qr = Barcode.Print(
            BarcodeSymbology.QrCode,
            "QUIET",
            new BarcodePrintOptions { ModuleMillimetres = 0.5 });

        Assert.Equal(4, qr.QuietZone);
    }

    [Theory]
    [InlineData(BarcodeSymbology.QrCode, 4, 0.4)]
    [InlineData(BarcodeSymbology.DataMatrix, 1, 0.4)]
    [InlineData(BarcodeSymbology.Pdf417, 2, 0.4)]
    [InlineData(BarcodeSymbology.Code128, 10, 0.25)]
    [InlineData(BarcodeSymbology.Code39, 10, 0.25)]
    [InlineData(BarcodeSymbology.Ean13, 11, 0.25)]
    [InlineData(BarcodeSymbology.Ean8, 7, 0.25)]
    [InlineData(BarcodeSymbology.UpcA, 9, 0.25)]
    public void The_floors_are_the_ones_the_specifications_set(
        BarcodeSymbology symbology, int quietZone, double minimumModule)
    {
        Assert.Equal(quietZone, Barcode.MinimumQuietZone(symbology));
        Assert.Equal(minimumModule, Barcode.MinimumModuleSize(symbology), 9);
    }

    [Fact]
    public void A_quiet_zone_below_the_minimum_is_refused_with_the_specification_named()
    {
        var error = Assert.Throws<BarcodeException>(() => Barcode.Print(
            BarcodeSymbology.QrCode,
            "TIGHT",
            new BarcodePrintOptions { QuietZone = 3 }));

        Assert.Equal(BarcodeErrorCode.InvalidArgument, error.Code);
        Assert.Contains("ISO/IEC 18004", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_module_below_the_family_minimum_is_refused()
    {
        var error = Assert.Throws<BarcodeException>(() => Barcode.Print(
            BarcodeSymbology.DataMatrix,
            "TOO-SMALL",
            new BarcodePrintOptions { ModuleMillimetres = 0.2 }));

        Assert.Equal(BarcodeErrorCode.InvalidArgument, error.Code);
        Assert.Contains("0.4", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_layout_too_narrow_for_the_quiet_zone_is_refused_rather_than_cropped()
    {
        var bare = Barcode.Encode(
            BarcodeSymbology.QrCode, "RESERVED", new BarcodeOptions { QuietZone = 0 });

        var error = Assert.Throws<BarcodeException>(() => Barcode.Print(
            BarcodeSymbology.QrCode,
            "RESERVED",
            new BarcodePrintOptions
            {
                ModuleMillimetres = 0.5,
                WidthModules = bare.ModuleWidth + 8 - 1,
            }));

        Assert.Equal(BarcodeErrorCode.InvalidArgument, error.Code);
        Assert.Contains("quiet zone", error.Message, StringComparison.Ordinal);

        // Exactly the room the symbol needs is accepted.
        var fits = Barcode.Print(
            BarcodeSymbology.QrCode,
            "RESERVED",
            new BarcodePrintOptions { ModuleMillimetres = 0.5, WidthModules = bare.ModuleWidth + 8 });

        Assert.Equal(bare.ModuleWidth + 8, fits.WidthModules);
    }

    [Fact]
    public void Both_sizing_options_at_once_are_refused()
    {
        Assert.Throws<BarcodeException>(() => Barcode.Print(
            BarcodeSymbology.QrCode,
            "AMBIGUOUS",
            new BarcodePrintOptions { WidthMillimetres = 30, ModuleMillimetres = 0.5 }));
    }

    [Fact]
    public void The_defaults_print_at_the_symbologys_minimum_module()
    {
        var print = Barcode.Print(BarcodeSymbology.QrCode, "DEFAULT");

        Assert.Equal(300, print.Dpi);
        Assert.True(print.ModuleMillimetres >= Barcode.MinimumModuleSize(BarcodeSymbology.QrCode));
        Assert.Equal(5, print.ModulePixels);
        Assert.True(PngHeader.IsPng(print.Png));
    }

    [Fact]
    public void A_read_only_symbology_is_refused_by_name()
    {
        Assert.Throws<BarcodeUnsupportedException>(() => Barcode.Print(
            BarcodeSymbology.MaxiCode, "READ-ONLY", new BarcodePrintOptions()));
    }
}
