using M3scalinum.Text.Barcodes.Internal;
using Xunit;

namespace M3scalinum.Text.Barcodes.Tests;

/// <summary>
/// A page handle owns a page handle and one decode handle per symbol it hands out; this asserts the
/// count returns to its baseline, in the non-parallel collection the other handle soaks use.
/// </summary>
[Collection(HandleSoak.Name)]
public sealed class DecodePageHandleTests
{
    private const int Cycles = 200;

    [Fact]
    public void A_page_decode_releases_every_handle_it_took()
    {
        var png = Barcode.Encode(BarcodeSymbology.QrCode, "PAGE HANDLES").Png;

        var pages = BarcodeNative.LiveDecodePageCount;
        var decodes = BarcodeNative.LiveDecodeCount;

        for (var cycle = 0; cycle < Cycles; cycle++)
        {
            var page = Barcode.DecodeAll(png);
            Assert.True(page.Success, page.Error);
            Assert.Single(page.Symbols);
        }

        Assert.Equal(pages, BarcodeNative.LiveDecodePageCount);
        Assert.Equal(decodes, BarcodeNative.LiveDecodeCount);
    }
}
