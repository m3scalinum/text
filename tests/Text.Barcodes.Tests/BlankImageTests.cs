using Xunit;

namespace M3scalinum.Text.Barcodes.Tests;

/// <summary>
/// An image that is readable and holds no barcode is a result, not an exception: a caller scanning
/// a stack of invoices must be able to tell "no symbol on this page" from "this file is broken".
/// </summary>
public sealed class BlankImageTests
{
    [Theory]
    [InlineData(200, 200)]
    [InlineData(600, 120)]
    [InlineData(1, 1)]
    public void A_blank_png_is_a_failed_decode_not_an_exception(int width, int height)
    {
        var png = BlankPng.Create(width, height);

        var decoded = Barcode.Decode(png);

        Assert.False(decoded.Success);
        Assert.Equal(string.Empty, decoded.Text);
        Assert.Null(decoded.Symbology);
        Assert.Empty(decoded.Points);
        Assert.NotNull(decoded.Error);
        Assert.Equal(string.Empty, decoded.Format);
    }

    [Fact]
    public void The_failure_names_the_reason()
    {
        var decoded = Barcode.Decode(BlankPng.Create(300, 100));

        Assert.False(decoded.Success);
        Assert.NotNull(decoded.Error);
        Assert.Contains("no barcode", decoded.Error, System.StringComparison.OrdinalIgnoreCase);
    }

}
