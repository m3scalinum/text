using System;
using System.Linq;
using Xunit;

namespace M3scalinum.Text.Ocr.Tests;

/// <summary>
/// The public surface, against a deterministic backend: validation, the shape of a
/// result, disposal, and the page-range arithmetic a PDF read needs.
/// </summary>
public sealed class OcrEngineTests
{
    [Fact]
    public void FromModels_rejects_a_null_detection_model()
    {
        Assert.Throws<ArgumentNullException>(() => OcrEngine.FromModels(null!, [1]));
    }

    [Fact]
    public void FromModels_rejects_a_null_recognition_model()
    {
        Assert.Throws<ArgumentNullException>(() => OcrEngine.FromModels([1], null!));
    }

    [Fact]
    public void FromModels_rejects_an_empty_detection_model()
    {
        var error = Assert.Throws<ArgumentException>(() => OcrEngine.FromModels([], [1]));
        Assert.Equal("detectionModel", error.ParamName);
    }

    [Fact]
    public void FromModels_rejects_an_empty_recognition_model()
    {
        var error = Assert.Throws<ArgumentException>(() => OcrEngine.FromModels([1], []));
        Assert.Equal("recognitionModel", error.ParamName);
    }

    [Fact]
    public void Recognize_returns_every_line_the_backend_reports()
    {
        using var engine = new OcrEngine(new FakeOcrBackend());

        var result = engine.Recognize([1, 2, 3]);

        Assert.Equal("first line\nsecond line", result.Text);
        Assert.Equal(2, result.Lines.Count);
        Assert.Equal(2480, result.Width);
        Assert.Equal(3508, result.Height);
        Assert.Equal(0.91, result.MeanConfidence);
    }

    [Fact]
    public void Recognize_reports_each_line_box_confidence_and_its_words()
    {
        using var engine = new OcrEngine(new FakeOcrBackend());

        var line = engine.Recognize([1, 2, 3]).Lines[0];

        Assert.Equal("first line", line.Text);
        Assert.Equal(10, line.Bounds.X);
        Assert.Equal(20, line.Bounds.Y);
        Assert.Equal(120, line.Bounds.Width);
        Assert.Equal(18, line.Bounds.Height);
        Assert.Equal(0.94, line.Confidence);
        Assert.Equal(["first", "line"], line.Words.Select(w => w.Text));
        Assert.Equal(0.95, line.Words[0].Confidence);
    }

    [Fact]
    public void Recognize_passes_the_image_bytes_through_unchanged()
    {
        var backend = new FakeOcrBackend();
        using var engine = new OcrEngine(backend);
        byte[] image = [0x89, 0x50, 0x4E, 0x47];

        engine.Recognize(image);

        Assert.Same(image, backend.LastImage);
        Assert.Equal(1, backend.RecognizeCalls);
    }

    [Fact]
    public void Recognize_rejects_a_null_image()
    {
        using var engine = new OcrEngine(new FakeOcrBackend());
        Assert.Throws<ArgumentNullException>(() => engine.Recognize(null!));
    }

    [Fact]
    public void Recognize_rejects_an_empty_image()
    {
        using var engine = new OcrEngine(new FakeOcrBackend());
        var error = Assert.Throws<ArgumentException>(() => engine.Recognize([]));
        Assert.Equal("imageBytes", error.ParamName);
    }

    [Fact]
    public void Recognize_after_Dispose_throws()
    {
        var engine = new OcrEngine(new FakeOcrBackend());
        engine.Dispose();

        Assert.Throws<ObjectDisposedException>(() => engine.Recognize([1]));
    }

    [Fact]
    public void Dispose_is_idempotent_and_disposes_the_backend_once()
    {
        var backend = new FakeOcrBackend();
        var engine = new OcrEngine(backend);

        engine.Dispose();
        engine.Dispose();

        Assert.True(backend.Disposed);
    }

    [Fact]
    public void RecognizePdf_defaults_to_300_dpi_from_the_first_page_to_the_end()
    {
        var backend = new FakeOcrBackend();
        using var engine = new OcrEngine(backend);

        engine.RecognizePdf([1]);

        var options = Assert.IsType<OcrPdfOptions>(backend.LastOptions);
        Assert.Equal(300, options.Dpi);
        Assert.Equal(0, options.FirstPage);
        Assert.Null(options.LastPage);
    }

    [Fact]
    public void RecognizePdf_returns_one_result_per_page_in_page_order()
    {
        var backend = new FakeOcrBackend();
        using var engine = new OcrEngine(backend);

        var pages = engine.RecognizePdf([1], new OcrPdfOptions { Dpi = 150 });

        Assert.Equal([0, 1], pages.Select(p => p.PageIndex));
        Assert.Equal(150, backend.LastOptions!.Dpi);
        Assert.Equal(1, backend.RecognizePdfCalls);
    }

    [Fact]
    public void RecognizePdf_keeps_the_page_size_and_resolution()
    {
        using var engine = new OcrEngine(new FakeOcrBackend());

        var page = engine.RecognizePdf([1])[0];

        Assert.Equal(2480, page.Width);
        Assert.Equal(3508, page.Height);
        Assert.Equal(300, page.Dpi);
        Assert.Equal(0.94, page.MeanConfidence);
        Assert.Equal("first line", page.Text);
    }

    [Fact]
    public void RecognizePdf_passes_the_document_and_options_through()
    {
        var backend = new FakeOcrBackend();
        using var engine = new OcrEngine(backend);
        byte[] pdf = [0x25, 0x50, 0x44, 0x46];

        engine.RecognizePdf(pdf, new OcrPdfOptions { Dpi = 200, FirstPage = 1, LastPage = 3 });

        Assert.Same(pdf, backend.LastPdf);
        Assert.Equal(200, backend.LastOptions!.Dpi);
        Assert.Equal(1, backend.LastOptions.FirstPage);
        Assert.Equal(3, backend.LastOptions.LastPage);
    }

    [Fact]
    public void RecognizePdf_rejects_an_empty_document()
    {
        using var engine = new OcrEngine(new FakeOcrBackend());
        var error = Assert.Throws<ArgumentException>(() => engine.RecognizePdf([]));
        Assert.Equal("pdf", error.ParamName);
    }

    [Theory]
    [InlineData(71)]
    [InlineData(1201)]
    [InlineData(0)]
    public void RecognizePdf_rejects_a_dpi_out_of_range(int dpi)
    {
        using var engine = new OcrEngine(new FakeOcrBackend());

        var error = Assert.Throws<ArgumentOutOfRangeException>(
            () => engine.RecognizePdf([1], new OcrPdfOptions { Dpi = dpi }));

        Assert.Equal("Dpi", error.ParamName);
    }

    [Fact]
    public void RecognizePdf_rejects_a_negative_first_page()
    {
        using var engine = new OcrEngine(new FakeOcrBackend());

        var error = Assert.Throws<ArgumentOutOfRangeException>(
            () => engine.RecognizePdf([1], new OcrPdfOptions { FirstPage = -1 }));

        Assert.Equal("FirstPage", error.ParamName);
    }

    [Fact]
    public void RecognizePdf_rejects_a_last_page_before_the_first()
    {
        using var engine = new OcrEngine(new FakeOcrBackend());

        var error = Assert.Throws<ArgumentOutOfRangeException>(
            () => engine.RecognizePdf([1], new OcrPdfOptions { FirstPage = 4, LastPage = 2 }));

        Assert.Equal("LastPage", error.ParamName);
    }

    [Fact]
    public void RecognizePdf_after_Dispose_throws()
    {
        var engine = new OcrEngine(new FakeOcrBackend());
        engine.Dispose();

        Assert.Throws<ObjectDisposedException>(() => engine.RecognizePdf([1]));
    }

    [Fact]
    public void OcrBox_formats_as_a_size_and_a_position()
        => Assert.Equal("120x18 at (10, 20)", new OcrBox(10, 20, 120, 18).ToString());
}
