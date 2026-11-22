using System;
using M3scalinum.Text.Documents;
using Xunit;

namespace M3scalinum.Text.Barcodes.Tests;

/// <summary>
/// The dressed QR Code: a brand colour, a reserved centre, and the read that proves the point.
/// </summary>
/// <remarks>
/// Every end-to-end assertion here reads the raster back with <see cref="Barcode.DecodeAll"/> — the
/// package's own decoder, not an inspection of the pixels. A styled symbol that this suite cannot
/// decode is a symbol a scanner will not decode either, which is the whole risk of the feature.
/// </remarks>
public sealed class StyledQrTests
{
    private const string Payload =
        "https://m3scalinum.example/products/07?lot=42&serial=0001234567";

    private static readonly BarcodeColor Brand = BarcodeColor.FromHex("#1f3a93");

    private static readonly BarcodeColor Cream = BarcodeColor.FromHex("#fff4e0");

    [Fact]
    public void A_brand_module_on_a_light_ground_renders_in_colour_and_decodes()
    {
        var image = Barcode.Encode(
            BarcodeSymbology.QrCode,
            Payload,
            new BarcodeOptions { Foreground = Brand, Background = Cream });

        // 2 is the PNG colour type for truecolour: the raster is no longer greyscale.
        Assert.Equal((byte)2, PngHeader.Format(image.Png).ColourType);

        var decoded = Barcode.DecodeAll(image.Png);
        Assert.True(decoded.Success, decoded.Error);
        Assert.Equal(Payload, decoded.Symbols[0].Text);

        Assert.Contains("#1f3a93", image.Svg, StringComparison.Ordinal);
        Assert.Contains("#fff4e0", image.Svg, StringComparison.Ordinal);
    }

    [Fact]
    public void The_default_rendering_is_unchanged_by_naming_the_default_colours()
    {
        var plain = Barcode.Encode(BarcodeSymbology.QrCode, Payload);
        var named = Barcode.Encode(
            BarcodeSymbology.QrCode,
            Payload,
            new BarcodeOptions { Foreground = BarcodeColor.Black, Background = BarcodeColor.White });

        Assert.Equal(plain.Png, named.Png);
        Assert.Equal(plain.Svg, named.Svg);
    }

    [Theory]
    [InlineData(BarcodeErrorCorrection.Low, 0.04)]
    [InlineData(BarcodeErrorCorrection.Medium, 0.07)]
    [InlineData(BarcodeErrorCorrection.Quartile, 0.12)]
    [InlineData(BarcodeErrorCorrection.High, 0.15)]
    public void A_logo_reads_back_at_every_error_correction_level(
        BarcodeErrorCorrection level, double fraction)
    {
        var image = Barcode.Encode(
            BarcodeSymbology.QrCode,
            Payload,
            new BarcodeOptions
            {
                ErrorCorrection = level,
                Logo = BarcodeLogo.Solid(fraction, Brand),
            });

        var decoded = Barcode.DecodeAll(image.Png);

        Assert.True(decoded.Success, $"{level} with a {fraction:P0} logo: {decoded.Error}");
        Assert.Equal(Payload, decoded.Symbols[0].Text);
    }

    [Fact]
    public void A_fifth_of_the_symbol_reads_back_at_high_correction()
    {
        var image = Barcode.Encode(
            BarcodeSymbology.QrCode,
            Payload,
            new BarcodeOptions
            {
                ErrorCorrection = BarcodeErrorCorrection.High,
                Logo = BarcodeLogo.Solid(0.20, Brand),
            });

        var decoded = Barcode.DecodeAll(image.Png);

        Assert.True(decoded.Success, decoded.Error);
        Assert.Equal(Payload, decoded.Symbols[0].Text);
    }

    [Fact]
    public void A_logo_the_decoder_cannot_get_past_is_refused_with_a_message_saying_so()
    {
        var error = Assert.Throws<BarcodeException>(() => Barcode.Encode(
            BarcodeSymbology.QrCode,
            Payload,
            new BarcodeOptions
            {
                ErrorCorrection = BarcodeErrorCorrection.High,
                Logo = BarcodeLogo.Solid(0.30, Brand),
            }));

        Assert.Equal(BarcodeErrorCode.InvalidArgument, error.Code);
        Assert.Contains("cannot read back", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_logo_past_the_levels_capacity_is_refused_with_both_numbers()
    {
        var error = Assert.Throws<BarcodeException>(() => Barcode.Encode(
            BarcodeSymbology.QrCode,
            Payload,
            new BarcodeOptions
            {
                ErrorCorrection = BarcodeErrorCorrection.Quartile,
                Logo = BarcodeLogo.Solid(0.30, Brand),
            }));

        Assert.Equal(BarcodeErrorCode.InvalidArgument, error.Code);
        Assert.Contains("25%", error.Message, StringComparison.Ordinal);
        Assert.Contains("ISO/IEC 18004", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pale_module_is_refused_with_the_ratio_it_measured()
    {
        var error = Assert.Throws<BarcodeException>(() => Barcode.Encode(
            BarcodeSymbology.QrCode,
            Payload,
            new BarcodeOptions { Foreground = BarcodeColor.FromHex("#9e9e9e") }));

        Assert.Equal(BarcodeErrorCode.InvalidArgument, error.Code);
        Assert.Contains("2.68", error.Message, StringComparison.Ordinal);
        Assert.Contains("4.5", error.Message, StringComparison.Ordinal);
        Assert.Contains("WCAG", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_ground_darker_than_the_module_is_refused()
    {
        var error = Assert.Throws<BarcodeException>(() => Barcode.Encode(
            BarcodeSymbology.QrCode,
            Payload,
            new BarcodeOptions
            {
                Foreground = BarcodeColor.White,
                Background = BarcodeColor.Black,
            }));

        Assert.Equal(BarcodeErrorCode.InvalidArgument, error.Code);
        Assert.Contains("not darker", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_logo_on_a_one_dimensional_symbol_is_refused_by_name()
    {
        var error = Assert.Throws<BarcodeUnsupportedException>(() => Barcode.Encode(
            BarcodeSymbology.Code128,
            "ABC-123",
            new BarcodeOptions { Logo = BarcodeLogo.Cleared(0.03) }));

        Assert.Contains("Code128", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_ratio_is_the_wcag_one_and_the_floor_is_the_text_criterion()
    {
        Assert.Equal(21.0, Barcode.ContrastRatio(BarcodeColor.Black, BarcodeColor.White), 9);
        Assert.Equal(
            Barcode.ContrastRatio(BarcodeColor.Black, BarcodeColor.White),
            Barcode.ContrastRatio(BarcodeColor.White, BarcodeColor.Black),
            9);
        Assert.Equal(
            Barcode.MinimumContrastRatio,
            Barcode.ContrastRatio(BarcodeColor.FromHex("#767676"), BarcodeColor.White),
            precision: 1);
    }

    [Fact]
    public void A_printed_symbol_carries_its_style_and_keeps_its_geometry()
    {
        var print = Barcode.Print(
            BarcodeSymbology.QrCode,
            Payload,
            new BarcodePrintOptions
            {
                Dpi = 300,
                ModuleMillimetres = 0.6,
                ErrorCorrection = BarcodeErrorCorrection.High,
                Foreground = Brand,
                Background = Cream,
                Logo = BarcodeLogo.Cleared(0.15),
            });

        Assert.Equal(print.WidthPixels, PngHeader.Width(print.Png));
        Assert.Equal((byte)2, PngHeader.Format(print.Png).ColourType);

        var decoded = Barcode.DecodeAll(print.Png);
        Assert.True(decoded.Success, decoded.Error);
        Assert.Equal(Payload, decoded.Symbols[0].Text);
    }

    [Fact]
    public void A_figure_added_to_a_document_is_the_styled_raster()
    {
        var document = TypstDocument.Create();
        var figure = Barcode.AddToDocument(
            document,
            BarcodeSymbology.QrCode,
            Payload,
            new BarcodePrintOptions
            {
                Dpi = 300,
                ModuleMillimetres = 0.6,
                ErrorCorrection = BarcodeErrorCorrection.High,
                Foreground = Brand,
                Logo = BarcodeLogo.Solid(0.15, Brand),
            });

        Assert.Equal((byte)2, PngHeader.Format(figure.Png).ColourType);

        var decoded = Barcode.DecodeAll(figure.Png);
        Assert.True(decoded.Success, decoded.Error);
        Assert.Equal(Payload, decoded.Symbols[0].Text);
    }

    [Fact]
    public void A_colour_is_read_from_and_written_back_to_hexadecimal()
    {
        Assert.Equal("#1f3a93", Brand.ToHex());
        Assert.Equal(Brand, BarcodeColor.FromHex("1f3a93"));
        Assert.Equal(Brand, BarcodeColor.FromHex("#1F3A93"));
        Assert.Throws<FormatException>(() => BarcodeColor.FromHex("#1f3a9"));
        Assert.Throws<FormatException>(() => BarcodeColor.FromHex("zzzzzz"));
    }

    [Fact]
    public void A_logo_share_outside_the_interval_is_refused_before_the_native_call()
    {
        var error = Assert.Throws<BarcodeException>(() => Barcode.Encode(
            BarcodeSymbology.QrCode,
            Payload,
            new BarcodeOptions { Logo = BarcodeLogo.Cleared(0) }));

        Assert.Equal(BarcodeErrorCode.InvalidArgument, error.Code);
        Assert.Contains("BarcodeLogo.Fraction", error.Message, StringComparison.Ordinal);
    }
}
