using System;
using System.IO;
using Xunit;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// Whether the rasteriser's native library can be loaded in this process, and how to say so.
/// </summary>
internal static class RasterProbe
{
    internal const string SkipReason =
        "sharp_text_pdf is not loadable here: build native/text-pdf and point " +
        "SHARPTEXTPDF_NATIVE_PATH at the library to run the raster tests.";

    internal static bool IsAvailable
    {
        get
        {
            try
            {
                return PdfRaster.PageCount(TestPdf.WithPages(1)) == 1;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (BadImageFormatException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }
    }
}

/// <summary>
/// Rendering a page of a PDF as an image.
/// </summary>
/// <remarks>
/// The tests that render need the native library, which a developer machine without a Rust build
/// does not have and this repository does not commit; they skip with a message naming the variable
/// to set, and the six-RID job of <c>ci-text.yml</c> is where they run for real. The argument and
/// option checks need no library at all, so they run everywhere.
/// </remarks>
public sealed class PdfRasterTests
{
    private const int A4WidthPoints = 595;
    private const int A4HeightPoints = 842;

    [Fact]
    public void Render_writes_a_png_of_the_page_at_the_default_resolution()
    {
        if (!RasterProbe.IsAvailable)
        {
            Assert.Skip(RasterProbe.SkipReason);
        }

        var image = PdfRaster.Render(TestPdf.WithPages(1), 1);

        Assert.Equal(PdfPageImageFormat.Png, image.Format);
        Assert.Equal(1, image.Page);
        Assert.Equal(1, image.PageCount);
        Assert.Equal(PdfRasterOptions.DefaultDpi, image.Dpi);
        Assert.Equal(
            (A4WidthPoints * PdfRasterOptions.DefaultDpi / 72),
            image.Width);
        Assert.Equal(
            (A4HeightPoints * PdfRasterOptions.DefaultDpi / 72),
            image.Height);
        Assert.Equal(
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A },
            image.Bytes[..8]);
    }

    [Fact]
    public void Render_scales_the_pixels_with_the_resolution()
    {
        if (!RasterProbe.IsAvailable)
        {
            Assert.Skip(RasterProbe.SkipReason);
        }

        var at72 = PdfRaster.Render(TestPdf.WithPages(1), 1, new PdfRasterOptions { Dpi = 72 });
        var at144 = PdfRaster.Render(TestPdf.WithPages(1), 1, new PdfRasterOptions { Dpi = 144 });

        Assert.Equal(A4WidthPoints, at72.Width);
        Assert.Equal(A4HeightPoints, at72.Height);
        Assert.Equal(A4WidthPoints * 2, at144.Width);
        Assert.Equal(A4HeightPoints * 2, at144.Height);
    }

    [Fact]
    public void Render_reports_the_resolution_the_pixels_actually_carry()
    {
        if (!RasterProbe.IsAvailable)
        {
            Assert.Skip(RasterProbe.SkipReason);
        }

        var image = PdfRaster.Render(TestPdf.WithPages(1), 1, new PdfRasterOptions { Dpi = 72 });

        // The page is 595 pt wide, which is 8.2639 in: 595 px over that is 72 DPI to the precision a
        // preview needs.
        Assert.InRange(image.EffectiveDpi, 71.9, 72.1);
    }

    [Fact]
    public void Render_writes_the_png_to_a_file_that_starts_like_a_png()
    {
        if (!RasterProbe.IsAvailable)
        {
            Assert.Skip(RasterProbe.SkipReason);
        }

        var image = PdfRaster.Render(TestPdf.WithPages(1), 1);
        var path = Path.Combine(Path.GetTempPath(), $"pdfraster-{Guid.NewGuid():N}.png");

        try
        {
            File.WriteAllBytes(path, image.Bytes);

            var written = File.ReadAllBytes(path);

            Assert.Equal(image.Bytes.Length, written.Length);
            Assert.Equal(image.Bytes, written);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Render_reads_the_requested_page_of_a_multi_page_document()
    {
        if (!RasterProbe.IsAvailable)
        {
            Assert.Skip(RasterProbe.SkipReason);
        }

        var image = PdfRaster.Render(TestPdf.WithPages(3), 3);

        Assert.Equal(3, image.Page);
        Assert.Equal(3, image.PageCount);
    }

    [Fact]
    public void PageCount_reports_what_the_document_declares()
    {
        if (!RasterProbe.IsAvailable)
        {
            Assert.Skip(RasterProbe.SkipReason);
        }

        Assert.Equal(2, PdfRaster.PageCount(TestPdf.WithPages(2)));
    }

    [Fact]
    public void Render_refuses_a_page_past_the_last_one()
    {
        if (!RasterProbe.IsAvailable)
        {
            Assert.Skip(RasterProbe.SkipReason);
        }

        var exception = Assert.Throws<PdfRasterException>(
            () => PdfRaster.Render(TestPdf.WithPages(1), 2));

        Assert.Equal(PdfRasterError.PageOutOfRange, exception.Error);
        Assert.Contains("1 page", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_refuses_a_page_below_one_without_loading_the_library()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => PdfRaster.Render(TestPdf.WithPages(1), 0));

        Assert.Equal("page", exception.ParamName);
    }

    [Fact]
    public void Render_refuses_bytes_that_are_not_a_pdf()
    {
        if (!RasterProbe.IsAvailable)
        {
            Assert.Skip(RasterProbe.SkipReason);
        }

        var exception = Assert.Throws<PdfRasterException>(
            () => PdfRaster.Render("not a pdf at all"u8.ToArray(), 1));

        Assert.Equal(PdfRasterError.NotAPdf, exception.Error);
    }

    [Fact]
    public void Render_refuses_an_encrypted_document_and_names_the_way_out()
    {
        if (!RasterProbe.IsAvailable)
        {
            Assert.Skip(RasterProbe.SkipReason);
        }

        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "encryption", "qpdf-r6-user-password.pdf");

        var exception = Assert.Throws<PdfRasterException>(() => PdfRaster.Render(File.ReadAllBytes(path), 1));

        Assert.Equal(PdfRasterError.Encrypted, exception.Error);
        Assert.Contains("PdfEncryption.Open", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_refuses_a_page_past_the_pixel_ceiling()
    {
        if (!RasterProbe.IsAvailable)
        {
            Assert.Skip(RasterProbe.SkipReason);
        }

        var exception = Assert.Throws<PdfRasterException>(
            () => PdfRaster.Render(
                TestPdf.WithPages(1),
                1,
                new PdfRasterOptions { Dpi = 300, MaximumPixels = 1000 }));

        Assert.Equal(PdfRasterError.Limit, exception.Error);
        Assert.Contains("pixel limit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_refuses_a_page_past_the_byte_ceiling()
    {
        if (!RasterProbe.IsAvailable)
        {
            Assert.Skip(RasterProbe.SkipReason);
        }

        var exception = Assert.Throws<PdfRasterException>(
            () => PdfRaster.Render(
                TestPdf.WithPages(1),
                1,
                new PdfRasterOptions { Dpi = 72, MaximumBytes = 1024 }));

        Assert.Equal(PdfRasterError.Limit, exception.Error);
        Assert.Contains("byte limit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_option_outside_its_range_is_refused_before_the_library_is_loaded()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PdfRaster.Render(TestPdf.WithPages(1), 1, new PdfRasterOptions { Dpi = 1 }));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => PdfRaster.Render(TestPdf.WithPages(1), 1, new PdfRasterOptions { MaximumPixels = 0 }));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => PdfRaster.Render(
                TestPdf.WithPages(1),
                1,
                new PdfRasterOptions { MaximumBytes = PdfRaster.HardMaximumBytes + 1 }));
    }

    [Fact]
    public void The_default_ceilings_are_the_documented_ones()
    {
        var options = new PdfRasterOptions();

        Assert.Equal(150, options.Dpi);
        Assert.Equal(50_000_000, options.MaximumPixels);
        Assert.Equal(256L * 1024 * 1024, options.MaximumBytes);
    }

    [Fact]
    public void Null_bytes_are_refused_by_argument()
    {
        Assert.Throws<ArgumentNullException>(() => PdfRaster.Render(null!, 1));
        Assert.Throws<ArgumentNullException>(() => PdfRaster.PageCount(null!));
    }
}
