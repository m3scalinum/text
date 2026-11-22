using System;
using System.Linq;
using Xunit;

namespace M3scalinum.Text.Barcodes.Tests;

/// <summary>
/// Reading a page: several symbols on one image, with their boxes, and the ceilings that stop an
/// image from a sender becoming an allocation.
/// </summary>
public sealed class DecodePageTests
{
    [Fact]
    public void A_single_symbol_page_reports_its_size_and_its_box()
    {
        var image = Barcode.Encode(BarcodeSymbology.QrCode, "PAGE ONE");

        var page = Barcode.DecodeAll(image.Png);

        Assert.True(page.Success, page.Error);
        Assert.Equal(image.ModuleWidth * 4, page.Width);
        Assert.Equal(image.ModuleHeight * 4, page.Height);
        Assert.Single(page.Symbols);

        var symbol = page.Symbols[0];
        Assert.True(symbol.Success, symbol.Error);
        Assert.Equal("PAGE ONE", symbol.Text);
        Assert.Equal(BarcodeSymbology.QrCode, symbol.Symbology);
        Assert.NotNull(symbol.Bounds);
        Assert.Equal($"{symbol.Format}: {symbol.Text}", page.ToString());
    }

    [Fact]
    public void The_box_of_a_symbol_lies_inside_the_image_and_on_its_points()
    {
        var image = Barcode.Encode(BarcodeSymbology.QrCode, "BOXED");

        var page = Barcode.DecodeAll(image.Png);
        var symbol = Assert.Single(page.Symbols);
        var box = Assert.IsType<BarcodeBounds>(symbol.Bounds);

        Assert.NotEmpty(symbol.Points);
        Assert.InRange(box.X, 0f, page.Width);
        Assert.InRange(box.Y, 0f, page.Height);
        Assert.InRange(box.Right, 0f, page.Width);
        Assert.InRange(box.Bottom, 0f, page.Height);
        Assert.True(box.Width > 0, "the box has a width");
        Assert.True(box.Height > 0, "the box has a height");

        // The box is the extent of the points: every point lies inside it.
        foreach (var point in symbol.Points)
        {
            Assert.InRange(point.X, box.X, box.Right);
            Assert.InRange(point.Y, box.Y, box.Bottom);
        }
    }

    [Fact]
    public void Two_symbols_on_one_page_come_back_with_distinct_boxes()
    {
        var qr = Barcode.Encode(BarcodeSymbology.QrCode, "ITEM-1", new BarcodeOptions { Scale = 6 });
        var linear = Barcode.Encode(BarcodeSymbology.Code128, "ITEM-2", new BarcodeOptions { Scale = 3 });

        var left = GreyPng.Decode(qr.Png);
        var right = GreyPng.Decode(linear.Png);

        const int margin = 40;
        var page = new GreyImage(
            margin * 3 + left.Width + right.Width,
            margin * 2 + Math.Max(left.Height, right.Height));

        page.Paste(left, margin, margin);
        page.Paste(right, margin * 2 + left.Width, margin);

        var decoded = Barcode.DecodeAll(GreyPng.Encode(page));

        Assert.True(decoded.Success, decoded.Error);
        Assert.Equal(2, decoded.Count);

        var texts = decoded.Symbols.Select(symbol => symbol.Text).OrderBy(text => text, StringComparer.Ordinal);
        Assert.Equal(["ITEM-1", "ITEM-2"], texts);

        var qrSymbol = decoded.Symbols.Single(symbol => symbol.Text == "ITEM-1");
        var linearSymbol = decoded.Symbols.Single(symbol => symbol.Text == "ITEM-2");

        Assert.Equal(BarcodeSymbology.QrCode, qrSymbol.Symbology);
        Assert.Equal(BarcodeSymbology.Code128, linearSymbol.Symbology);

        var qrBox = Assert.IsType<BarcodeBounds>(qrSymbol.Bounds);
        var linearBox = Assert.IsType<BarcodeBounds>(linearSymbol.Bounds);

        // One on the left, one on the right: their boxes cannot overlap, which is what a caller
        // positioning an annotation over each symbol relies on.
        Assert.True(
            qrBox.Right <= linearBox.X,
            $"the qr box {qrBox} must end before the linear symbol starts at {linearBox}");

        // And the boxes are where the symbols were pasted, not merely in the right order. Each
        // symbol sits inside its own quiet zone, so the box starts after the paste origin rather
        // than on it; what matters is that it falls inside the region that symbol was pasted into.
        Assert.InRange(qrBox.X, margin, margin + left.Width);
        Assert.InRange(linearBox.X, margin * 2 + left.Width, margin * 2 + left.Width + right.Width);
    }

    [Fact]
    public void An_image_without_a_symbol_is_an_empty_page_not_an_exception()
    {
        var page = Barcode.DecodeAll(BlankPng.Create(64, 48));

        Assert.False(page.Success);
        Assert.Equal(0, page.Count);
        Assert.Empty(page.Symbols);
        Assert.NotNull(page.Error);
        Assert.Contains("no barcode", page.Error, StringComparison.Ordinal);
        Assert.Equal(64, page.Width);
        Assert.Equal(48, page.Height);
        Assert.Equal($"no barcode: {page.Error}", page.ToString());
    }

    [Fact]
    public void An_image_at_the_pixel_ceiling_decodes_and_one_pixel_past_it_is_a_limit_error()
    {
        var atTheCeiling = new BarcodeDecodeOptions { MaximumPixels = 100 * 100 };
        var page = Barcode.DecodeAll(BlankPng.Create(100, 100), atTheCeiling);
        Assert.False(page.Success);

        var pastTheCeiling = new BarcodeDecodeOptions { MaximumPixels = (100 * 100) - 1 };

        var error = Assert.Throws<BarcodeException>(
            () => Barcode.DecodeAll(BlankPng.Create(100, 100), pastTheCeiling));

        Assert.Equal(BarcodeErrorCode.Limit, error.Code);
        Assert.Contains("10000", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_image_past_the_byte_ceiling_is_a_limit_error()
    {
        var png = Barcode.Encode(BarcodeSymbology.QrCode, "TOO MANY BYTES").Png;
        var options = new BarcodeDecodeOptions { MaximumBytes = png.Length - 1 };

        var error = Assert.Throws<BarcodeException>(() => Barcode.DecodeAll(png, options));

        Assert.Equal(BarcodeErrorCode.Limit, error.Code);
        Assert.Contains("bytes", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(BarcodeDecodeOptions.HardMaximumPixels + 1)]
    public void A_pixel_ceiling_outside_its_range_is_an_invalid_argument(long pixels)
    {
        var options = new BarcodeDecodeOptions { MaximumPixels = pixels };

        var error = Assert.Throws<ArgumentOutOfRangeException>(
            () => Barcode.DecodeAll(BlankPng.Create(8, 8), options));

        Assert.Contains("MaximumPixels", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_byte_ceiling_outside_its_range_is_an_invalid_argument()
    {
        var options = new BarcodeDecodeOptions { MaximumBytes = 0 };

        var error = Assert.Throws<ArgumentOutOfRangeException>(
            () => Barcode.DecodeAll(BlankPng.Create(8, 8), options));

        Assert.Contains("MaximumBytes", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_default_ceilings_are_the_ones_the_documentation_states()
    {
        Assert.Equal(64_000_000, BarcodeDecodeOptions.DefaultMaximumPixels);
        Assert.Equal(256L * 1024 * 1024, BarcodeDecodeOptions.DefaultMaximumBytes);
    }
}
