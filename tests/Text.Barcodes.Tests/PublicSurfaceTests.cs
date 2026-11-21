using System;
using M3scalinum.Text.Barcodes;
using Xunit;

namespace M3scalinum.Text.Barcodes.Tests;

/// <summary>
/// The edges of the public surface the error tests do not reach: the exception types themselves, the
/// option ranges the native library would otherwise refuse, and the failure a corrupt image is.
/// </summary>
public sealed class PublicSurfaceTests
{
    [Fact]
    public void A_native_failure_carries_its_panic_traceback()
    {
        var error = new BarcodeNativeException("the encoder panicked", "0: rxing::encode\n1: main");

        Assert.Equal(BarcodeErrorCode.Native, error.Code);
        Assert.Equal("the encoder panicked", error.Message);
        Assert.Equal("0: rxing::encode\n1: main", error.Traceback);
        Assert.IsAssignableFrom<BarcodeException>(error);
    }

    [Fact]
    public void A_native_failure_without_a_traceback_has_none()
    {
        var error = new BarcodeNativeException("the ABI version is not one this build speaks");

        Assert.Equal(BarcodeErrorCode.Native, error.Code);
        Assert.Null(error.Traceback);
    }

    [Fact]
    public void An_exception_can_wrap_the_failure_beneath_it()
    {
        var cause = new InvalidOperationException("the raster was not written");

        var error = new BarcodeException(BarcodeErrorCode.Native, "the call failed", cause);

        Assert.Same(cause, error.InnerException);
        Assert.Equal(BarcodeErrorCode.Native, error.Code);
        Assert.Equal("the call failed", error.Message);
    }

    [Fact]
    public void A_negative_quiet_zone_is_an_invalid_argument()
    {
        var error = Assert.Throws<BarcodeException>(
            () => Barcode.Encode(BarcodeSymbology.QrCode, "HELLO", new BarcodeOptions { QuietZone = -1 }));

        Assert.Equal(BarcodeErrorCode.InvalidArgument, error.Code);
        Assert.Contains("QuietZone", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1025)]
    public void A_bar_height_outside_the_supported_range_is_an_invalid_argument(int height)
    {
        var error = Assert.Throws<BarcodeException>(
            () => Barcode.Encode(
                BarcodeSymbology.Code128, "HELLO", new BarcodeOptions { BarHeight = height }));

        Assert.Equal(BarcodeErrorCode.InvalidArgument, error.Code);
        Assert.Contains("BarHeight", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_error_correction_level_outside_the_enum_has_no_abi_name()
    {
        Assert.Null(((BarcodeErrorCorrection)99).ToAbiName());
        Assert.Equal("H", BarcodeErrorCorrection.High.ToAbiName());
    }

    [Fact]
    public void A_result_renders_both_ways_for_a_log()
    {
        var image = Barcode.Encode(BarcodeSymbology.Code128, "HELLO");

        var read = Barcode.Decode(image.Png);

        Assert.True(read.Success);
        Assert.Equal($"{read.Format}: {read.Text}", read.ToString());

        var blank = Barcode.Decode(BlankPng.Create(64, 64));

        Assert.False(blank.Success);
        Assert.Equal($"no barcode: {blank.Error}", blank.ToString());
    }

    [Fact]
    public void A_png_whose_image_data_is_corrupt_is_an_image_error()
    {
        var png = Barcode.Encode(BarcodeSymbology.QrCode, "CORRUPT ME").Png;

        // Same signature, same IHDR, same chunk layout: only the compressed image data is damaged,
        // so this is a PNG a viewer opens and a decoder cannot.
        var corrupted = CorruptImageData(png);

        var error = Assert.Throws<BarcodeImageException>(() => Barcode.Decode(corrupted));

        Assert.Equal(BarcodeErrorCode.Decode, error.Code);
    }

    [Fact]
    public void A_truncated_png_is_an_image_error()
    {
        var png = Barcode.Encode(BarcodeSymbology.QrCode, "TRUNCATED").Png;

        var error = Assert.Throws<BarcodeImageException>(
            () => Barcode.Decode(png.AsSpan(0, png.Length / 2).ToArray()));

        Assert.Equal(BarcodeErrorCode.Decode, error.Code);
    }

    /// <summary>
    /// Flips the bytes inside the first <c>IDAT</c> chunk, leaving the chunk boundaries and the
    /// signature intact.
    /// </summary>
    private static byte[] CorruptImageData(byte[] png)
    {
        var bytes = (byte[])png.Clone();
        var idat = IndexOf(bytes, "IDAT"u8);

        Assert.True(idat > 0, "the encoded PNG has no IDAT chunk");

        // Past the four-byte type and into the payload; the length prefix stays as it was.
        for (var index = idat + 8; index < Math.Min(idat + 24, bytes.Length); index++)
        {
            bytes[index] ^= 0xFF;
        }

        return bytes;
    }

    private static int IndexOf(byte[] haystack, ReadOnlySpan<byte> needle)
    {
        for (var index = 0; index <= haystack.Length - needle.Length; index++)
        {
            if (haystack.AsSpan(index, needle.Length).SequenceEqual(needle))
            {
                return index;
            }
        }

        return -1;
    }
}
