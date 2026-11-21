using System;
using Xunit;

namespace M3scalinum.Text.Barcodes.Tests;

/// <summary>Every way a caller can be wrong, and which exception says so.</summary>
public sealed class ErrorTests
{
    [Theory]
    [InlineData("590123412345")]
    [InlineData("59012341234567")]
    public void An_ean13_payload_that_is_not_thirteen_digits_is_a_format_error(string payload)
    {
        var error = Assert.Throws<BarcodeFormatException>(
            () => Barcode.Encode(BarcodeSymbology.Ean13, payload));

        Assert.Equal(BarcodeErrorCode.Encode, error.Code);
        Assert.Contains("EAN13", error.Message, StringComparison.Ordinal);
        Assert.Contains("13", error.Message, StringComparison.Ordinal);
        Assert.Contains(payload.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_ean8_payload_that_is_not_eight_digits_is_a_format_error()
    {
        var error = Assert.Throws<BarcodeFormatException>(
            () => Barcode.Encode(BarcodeSymbology.Ean8, "1234567"));

        Assert.Equal(BarcodeErrorCode.Encode, error.Code);
        Assert.Contains("EAN8", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Thirteen_digits_that_are_not_digits_are_a_format_error()
    {
        var error = Assert.Throws<BarcodeFormatException>(
            () => Barcode.Encode(BarcodeSymbology.Ean13, "59012341234X7"));

        Assert.Equal(BarcodeErrorCode.Encode, error.Code);
    }

    [Fact]
    public void A_payload_a_symbology_cannot_hold_is_a_format_error()
    {
        // Code 39's extended set stops at ASCII: a euro sign has no encoding, and rxing refuses
        // rather than writing a symbol that would read back as something else.
        var error = Assert.Throws<BarcodeFormatException>(
            () => Barcode.Encode(BarcodeSymbology.Code39, "HELLO €"));

        Assert.Equal(BarcodeErrorCode.Encode, error.Code);
    }

    [Fact]
    public void An_empty_payload_is_an_invalid_argument()
    {
        var error = Assert.Throws<BarcodeException>(
            () => Barcode.Encode(BarcodeSymbology.QrCode, string.Empty));

        Assert.Equal(BarcodeErrorCode.InvalidArgument, error.Code);
    }

    [Fact]
    public void A_null_payload_is_an_argument_null()
    {
        Assert.Throws<ArgumentNullException>(
            () => Barcode.Encode(BarcodeSymbology.QrCode, null!));
    }

    [Fact]
    public void A_payload_with_a_nul_is_refused_before_the_abi_truncates_it()
    {
        var error = Assert.Throws<BarcodeException>(
            () => Barcode.Encode(BarcodeSymbology.QrCode, "before\0after"));

        Assert.Equal(BarcodeErrorCode.InvalidArgument, error.Code);
    }

    [Fact]
    public void A_payload_past_the_limit_is_a_limit_error()
    {
        var payload = new string('A', Barcode.MaxPayloadBytes + 1);

        var error = Assert.Throws<BarcodeException>(
            () => Barcode.Encode(BarcodeSymbology.QrCode, payload));

        Assert.Equal(BarcodeErrorCode.Limit, error.Code);
    }

    [Fact]
    public void A_symbology_outside_the_enum_is_unsupported()
    {
        var error = Assert.Throws<BarcodeUnsupportedException>(
            () => Barcode.Encode((BarcodeSymbology)42, "HELLO"));

        Assert.Equal(BarcodeErrorCode.Unsupported, error.Code);
        Assert.Contains("42", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_scale_of_zero_is_an_invalid_argument()
    {
        var error = Assert.Throws<BarcodeException>(
            () => Barcode.Encode(BarcodeSymbology.QrCode, "HELLO", new BarcodeOptions { Scale = 0 }));

        Assert.Equal(BarcodeErrorCode.InvalidArgument, error.Code);
    }

    [Fact]
    public void An_error_correction_level_on_a_symbology_that_has_none_is_refused()
    {
        var error = Assert.Throws<BarcodeException>(
            () => Barcode.Encode(
                BarcodeSymbology.Code128,
                "HELLO",
                new BarcodeOptions { ErrorCorrection = BarcodeErrorCorrection.High }));

        Assert.Equal(BarcodeErrorCode.InvalidArgument, error.Code);
        Assert.Contains("QR Code", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Bytes_that_are_not_a_png_are_an_image_error()
    {
        var error = Assert.Throws<BarcodeImageException>(
            () => Barcode.Decode("this is not a png"u8.ToArray()));

        Assert.Equal(BarcodeErrorCode.Decode, error.Code);
    }

    [Fact]
    public void An_empty_image_is_an_invalid_argument()
    {
        var error = Assert.Throws<BarcodeException>(() => Barcode.Decode([]));

        Assert.Equal(BarcodeErrorCode.InvalidArgument, error.Code);
    }

    [Fact]
    public void A_null_image_is_an_argument_null()
    {
        Assert.Throws<ArgumentNullException>(() => Barcode.Decode(null!));
    }
}
