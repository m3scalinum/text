using System;
using System.Collections.Generic;
using System.Text;
using M3scalinum.Text.Barcodes;
using M3scalinum.Text.Barcodes.Internal;
using Xunit;

namespace M3scalinum.Text.Barcodes.Tests;

/// <summary>
/// The two pieces of the label that are not its layout: the page writer and the PNG reader under
/// it. Both refuse what they cannot render, and the refusals are what these tests check.
/// </summary>
/// <remarks>
/// They are internal types, reached through <c>InternalsVisibleTo</c> — the same door the handle
/// counter uses. The point is not to test the internals for their own sake but to hold the refusals
/// that the public API can only reach through a damaged input, which is exactly the case a caller
/// cannot construct and a corrupt file can.
/// </remarks>
public sealed class LabelRendererTests
{
    [Fact]
    public void A_page_needs_a_positive_size()
    {
        Assert.Throws<BarcodeException>(() => new PdfLabelPage(0, 100));
        Assert.Throws<BarcodeException>(() => new PdfLabelPage(100, -1));
        Assert.Throws<BarcodeException>(() => new PdfLabelPage(double.NaN, 100));
    }

    [Fact]
    public void A_page_carries_one_image()
    {
        var page = new PdfLabelPage(100, 100);
        var pixels = new byte[4];

        page.DrawGreyscaleImage(pixels, 2, 2, 0, 0, 10, 10);

        var error = Assert.Throws<BarcodeException>(
            () => page.DrawGreyscaleImage(pixels, 2, 2, 0, 0, 10, 10));
        Assert.Contains("one symbol", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_image_whose_pixels_do_not_match_its_size_is_refused()
    {
        var page = new PdfLabelPage(100, 100);

        var error = Assert.Throws<BarcodeException>(
            () => page.DrawGreyscaleImage(new byte[3], 2, 2, 0, 0, 10, 10));

        Assert.Contains("needs 4 bytes", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_line_writes_no_text_operator()
    {
        var page = new PdfLabelPage(100, 100);
        page.DrawText(string.Empty, 0, 0, 10, bold: false, grey: 0);

        var pdf = Encoding.ASCII.GetString(page.Build());
        Assert.DoesNotContain("Tj", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void A_page_without_an_image_names_no_image_resource()
    {
        var page = new PdfLabelPage(100, 100);
        page.DrawText("SHIPPING LABEL", 10, 10, 10, bold: true, grey: 0);

        var pdf = Encoding.ASCII.GetString(page.Build());
        Assert.DoesNotContain("/XObject", pdf, StringComparison.Ordinal);
        Assert.Contains("/Producer (M3scalinum.Text.Barcodes)", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void Parentheses_and_backslashes_are_escaped_in_a_text_operator()
    {
        var page = new PdfLabelPage(100, 100);
        page.DrawText(@"Rue (du) \ Port", 0, 0, 10, bold: false, grey: 0);

        var pdf = Encoding.ASCII.GetString(page.Build());
        Assert.Contains(@"(Rue \(du\) \\ Port)", pdf, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Camille Dubois", "Camille Dubois")]
    [InlineData("Rue de l'Église", "Rue de l'\\311glise")]
    [InlineData("€ 12", "\\200 12")]
    [InlineData("Königsallee", "K\\366nigsallee")]
    [InlineData("左右", "??")]
    public void Text_is_written_in_windows_1252(string text, string written)
    {
        Assert.Equal(written, WinAnsiText.Escape(WinAnsiText.Encode(text)));
    }

    [Fact]
    public void A_tab_becomes_a_space()
    {
        Assert.Equal("a b", WinAnsiText.Escape(WinAnsiText.Encode("a\tb")));
    }

    [Fact]
    public void Bytes_that_are_not_a_png_are_refused()
    {
        Assert.Throws<BarcodeException>(() => PngGreyscale.Decode([1, 2, 3]));
        Assert.Throws<BarcodeException>(() => PngGreyscale.Decode([]));
    }

    [Fact]
    public void A_png_that_is_not_eight_bit_greyscale_is_refused()
    {
        Assert.Throws<BarcodeException>(() => PngGreyscale.Decode(Mutate(InBitDepth)));
        Assert.Throws<BarcodeException>(() => PngGreyscale.Decode(Mutate(InColourType)));
        Assert.Throws<BarcodeException>(() => PngGreyscale.Decode(Mutate(InInterlace)));
    }

    [Fact]
    public void A_png_with_a_chunk_past_its_end_is_refused()
    {
        var truncated = Mutate(png => png[33] = 0x7F); // the IHDR length, now far too large
        Assert.Throws<BarcodeException>(() => PngGreyscale.Decode(truncated));
    }

    [Fact]
    public void A_png_that_holds_no_image_data_is_refused()
    {
        Assert.Throws<BarcodeException>(() => PngGreyscale.Decode(GreyPng.Encode(OnePixel)[..33]));
    }

    [Fact]
    public void A_row_filter_the_specification_does_not_define_is_refused()
    {
        var png = GreyPng.Encode(OnePixel);
        var idat = IndexOf(png, "IDAT");

        // The first byte of the compressed stream cannot be patched into a filter, so the test
        // rebuilds the stream: one row, filter 9, one pixel. 0x78 0x9C is a zlib header.
        var filtered = new byte[] { 9, 0x00 };
        var deflated = Zlib(filtered);
        var rebuilt = Rebuild(png, idat, deflated);

        var error = Assert.Throws<BarcodeException>(() => PngGreyscale.Decode(rebuilt));
        Assert.Contains("row filter 9", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_scanline_that_stops_short_is_refused()
    {
        var png = GreyPng.Encode(new GreyImage(8, 2));
        var idat = IndexOf(png, "IDAT");

        // One byte of filter and one of data where two rows of eight pixels are promised.
        var rebuilt = Rebuild(png, idat, Zlib([0x00, 0x40]));

        var error = Assert.Throws<BarcodeException>(() => PngGreyscale.Decode(rebuilt));
        Assert.Contains("scanlines", error.Message, StringComparison.Ordinal);
    }

    private static GreyImage OnePixel
    {
        get
        {
            var image = new GreyImage(1, 1);
            image.Pixels[0] = 0;
            return image;
        }
    }

    private static byte[] Mutate(Action<byte[]> change)
    {
        var png = GreyPng.Encode(new GreyImage(4, 4));
        change(png);
        return png;
    }

    private static void InBitDepth(byte[] png) => png[24] = 16;

    private static void InColourType(byte[] png) => png[25] = 2;

    private static void InInterlace(byte[] png) => png[28] = 1;

    /// <summary>The offset of a chunk's four-byte type, in a PNG this test project wrote.</summary>
    private static int IndexOf(byte[] png, string type)
    {
        var signature = Encoding.ASCII.GetBytes(type);

        for (var index = 8; index < png.Length - 4; index++)
        {
            if (png.AsSpan(index, 4).SequenceEqual(signature))
            {
                return index;
            }
        }

        throw new InvalidOperationException($"no {type} chunk");
    }

    /// <summary>
    /// Replaces the IDAT payload of <paramref name="png"/> with <paramref name="data"/>, fixing the
    /// chunk lengths and dropping every other IDAT chunk.
    /// </summary>
    private static byte[] Rebuild(byte[] png, int idatType, byte[] data)
    {
        var lengthOffset = idatType - 4;
        var before = png.AsSpan(0, lengthOffset).ToArray();
        var after = png.AsSpan(idatType + 4 + ReadLength(png, lengthOffset) + 4).ToArray();

        var rebuilt = new List<byte>(before);
        var length = BitConverter.GetBytes(data.Length);
        Array.Reverse(length);
        rebuilt.AddRange(length);
        rebuilt.AddRange(Encoding.ASCII.GetBytes("IDAT"));
        rebuilt.AddRange(data);
        rebuilt.AddRange(new byte[4]); // the CRC, which this reader does not check
        rebuilt.AddRange(after);
        return [.. rebuilt];
    }

    private static int ReadLength(byte[] png, int offset)
        => (png[offset] << 24) | (png[offset + 1] << 16) | (png[offset + 2] << 8) | png[offset + 3];

    /// <summary>A zlib stream around <paramref name="data"/>, which is what an IDAT carries.</summary>
    private static byte[] Zlib(byte[] data)
    {
        using var output = new System.IO.MemoryStream();

        using (var zlib = new System.IO.Compression.ZLibStream(
            output, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }
}
