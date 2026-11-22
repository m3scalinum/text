using System;
using System.IO;
using System.Linq;
using M3scalinum.Text.Ocr.Internal;
using Xunit;

namespace M3scalinum.Text.Ocr.Tests;

/// <summary>
/// The tests that prove the engine actually reads text.
/// </summary>
/// <remarks>
/// They need two model files, and this repository ships none: the published ocrs
/// models weigh roughly 40 MB together, are not redistributable under the terms this
/// repository can accept for a fixture, and are never downloaded by the library
/// itself. So the tests skip unless an operator points
/// <c>M3SCALINUM_OCR_MODELS</c> at a directory holding
/// <c>text-detection.rten</c> and <c>text-recognition.rten</c>, and they say so in
/// the skip message rather than passing quietly.
/// </remarks>
public sealed class ModelBackedTests
{
    private const string ModelsVariable = "M3SCALINUM_OCR_MODELS";
    private const string ImageVariable = "M3SCALINUM_OCR_IMAGE";

    [Fact]
    public void RecognizePdf_reads_the_text_of_a_rendered_page()
    {
        using var engine = Engine();
        var pdf = MinimalPdf.WithText("INVOICE 2026");

        var pages = engine.RecognizePdf(pdf, new OcrPdfOptions { Dpi = 300 });

        var page = Assert.IsType<OcrPageResult>(Assert.Single(pages));
        Assert.Equal(0, page.PageIndex);
        Assert.True(page.Width > 2000, $"a 300 DPI A4 page is about 2480 px wide, not {page.Width}");
        Assert.NotEmpty(page.Lines);
        Assert.Contains("INVOICE", page.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Recognize_reads_the_text_of_an_image()
    {
        if (!NativeProbe.IsAvailable)
        {
            Assert.Skip(NativeProbe.SkipReason);
        }

        var path = Environment.GetEnvironmentVariable(ImageVariable);
        if (string.IsNullOrEmpty(path))
        {
            Assert.Skip(
                $"{ImageVariable} is not set: point it at a PNG or JPEG to read one for real.");
        }

        using var engine = Engine();

        var result = Assert.IsType<OcrResult>(engine.Recognize(File.ReadAllBytes(path!)));

        Assert.True(result.Width > 0);
        Assert.True(result.Height > 0);
        Assert.NotEmpty(result.Lines);
    }

    [Fact]
    public void RecognizePdf_reports_a_box_for_every_word_it_reads()
    {
        using var engine = Engine();

        var page = Assert.IsType<OcrPageResult>(
            Assert.Single(engine.RecognizePdf(MinimalPdf.WithText("TOTAL 42"))));

        var words = page.Lines.SelectMany(line => line.Words).ToArray();
        Assert.NotEmpty(words);
        Assert.All(words, word =>
        {
            Assert.True(word.Bounds.Width > 0);
            Assert.True(word.Bounds.Height > 0);
            Assert.InRange(word.Confidence, 0.0, 1.0);
        });
    }

    [Fact]
    public void Dispose_returns_the_live_handle_count_to_where_it_was()
    {
        if (!NativeProbe.IsAvailable)
        {
            Assert.Skip(NativeProbe.SkipReason);
        }

        var before = NativeOcr.LiveHandleCount();

        using (var engine = Engine())
        {
            Assert.Equal(before + 1, NativeOcr.LiveHandleCount());

            // Prove the handle works before it is dropped, so the count below is not
            // the count of an object that was never usable.
            _ = engine.RecognizePdf(MinimalPdf.WithText("HELLO"));
        }

        Assert.Equal(before, NativeOcr.LiveHandleCount());
    }

    private static OcrEngine Engine()
    {
        if (!NativeProbe.IsAvailable)
        {
            Assert.Skip(NativeProbe.SkipReason);
        }

        var directory = Environment.GetEnvironmentVariable(ModelsVariable);
        if (string.IsNullOrEmpty(directory))
        {
            Assert.Skip(
                $"{ModelsVariable} is not set: point it at a directory holding " +
                "text-detection.rten and text-recognition.rten to prove the native engine reads text.");
        }

        var detection = Path.Combine(directory!, "text-detection.rten");
        var recognition = Path.Combine(directory!, "text-recognition.rten");

        if (!File.Exists(detection) || !File.Exists(recognition))
        {
            Assert.Skip($"{directory} does not hold text-detection.rten and text-recognition.rten.");
        }

        return OcrEngine.FromModels(File.ReadAllBytes(detection), File.ReadAllBytes(recognition));
    }
}
