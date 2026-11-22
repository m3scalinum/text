using System;
using M3scalinum.Text.Pdf;
using Xunit;

namespace M3scalinum.Text.Barcodes.Tests;

/// <summary>
/// The case the package is for: a PDF arrives, its page is rendered, the symbols on it are read.
/// </summary>
/// <remarks>
/// <para>
/// This is the documented composition rather than a method on <see cref="Barcode"/>: rendering a
/// page belongs to <c>M3scalinum.Text.Pdf</c>, and a barcode reader that pulled the PDF stack —
/// its renderer, its native library and its signing dependencies — into every consumer's graph
/// would make a leaf package heavy for a step most callers never need (Spec 20 §9).
/// </para>
/// <para>
/// The test is skipped when the PDF rasteriser's native library is not on the path, because a
/// checkout that has not built <c>native/text-pdf</c> cannot render anything. CI runs the barcode
/// suite without that library, so this is a local and release-pipeline proof.
/// </para>
/// </remarks>
public sealed class PdfBarcodeTests
{
    private const string SkipReason =
        "the PDF rasteriser's native library is not available; set SHARPTEXTPDF_NATIVE_PATH";

    private static readonly Lazy<bool> Renderer = new(Probe);

    private static bool RendererAvailable => Renderer.Value;

    [Fact]
    public void A_page_rendered_from_a_pdf_yields_the_symbol_it_carries()
    {
        Assert.SkipUnless(RendererAvailable, SkipReason);

        var image = Barcode.Encode(BarcodeSymbology.QrCode, "BT-1=FA-2026-000123", new BarcodeOptions { Scale = 4 });
        var pdf = MinimalPdf.WithImage(GreyPng.Decode(image.Png));

        var page = PdfRaster.Render(pdf, 1, new PdfRasterOptions { Dpi = 300 });
        var decoded = Barcode.DecodeAll(page.Bytes);

        Assert.True(decoded.Success, decoded.Error);
        var symbol = Assert.Single(decoded.Symbols);
        Assert.Equal("BT-1=FA-2026-000123", symbol.Text);
        Assert.Equal(BarcodeSymbology.QrCode, symbol.Symbology);

        // The box is in pixels of the rendered page, which is larger than the PDF's own page at
        // 300 DPI rather than at the 72 DPI a PDF's user space is measured in.
        var box = Assert.IsType<BarcodeBounds>(symbol.Bounds);
        Assert.InRange(box.Right, 0f, page.Width);
        Assert.InRange(box.Bottom, 0f, page.Height);
    }

    [Fact]
    public void A_rendered_page_without_a_symbol_is_an_empty_page()
    {
        Assert.SkipUnless(RendererAvailable, SkipReason);

        var blank = new GreyImage(200, 120);
        var pdf = MinimalPdf.WithImage(blank);

        var page = PdfRaster.Render(pdf, 1, new PdfRasterOptions { Dpi = 200 });
        var decoded = Barcode.DecodeAll(page.Bytes);

        Assert.False(decoded.Success);
        Assert.Empty(decoded.Symbols);
        Assert.NotNull(decoded.Error);
    }

    private static bool Probe()
    {
        try
        {
            // A page count is the cheapest call through the native library: it needs the renderer
            // and nothing else. A checkout without it throws DllNotFoundException here.
            _ = PdfRaster.PageCount(MinimalPdf.WithImage(new GreyImage(4, 4)));
            return true;
        }
        catch (Exception error) when (error is DllNotFoundException or TypeInitializationException)
        {
            return false;
        }
    }
}
