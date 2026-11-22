using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using M3scalinum.Text.Ocr.Internal;
using Xunit;

namespace M3scalinum.Text.Ocr.Tests;

/// <summary>
/// The confidence thresholds and the typed refusal: the document shape the native
/// side writes, the mapping from options to the ABI, and what the public surface
/// promises a caller who never sees the native library.
/// </summary>
/// <remarks>
/// Everything here runs without a model and without the native library, which is why
/// it runs everywhere. The rule itself — which page is refused, and why — is proved in
/// Rust, in <c>native/text-ocr/src/engine.rs</c>, where the readings can be built
/// without two 40 MB models; these tests prove that the two halves agree on the shape
/// and that the options reach the ABI unchanged.
/// </remarks>
public sealed class ConfidenceThresholdTests
{
    private const string UnreadableImageDocument =
        """
        {"width":2480,"height":3508,"status":"unreadable","text":"","meanConfidence":0.21,
         "wordCount":5,"rejectedWordCount":5,"lines":[],
         "refusal":{"reason":"mean_confidence","threshold":0.6,"observed":0.21,"wordCount":5}}
        """;

    private const string RejectedWordsDocument =
        """
        {"width":2480,"height":3508,"status":"unreadable","text":"","meanConfidence":0.71,
         "wordCount":4,"rejectedWordCount":3,"lines":[],
         "refusal":{"reason":"rejected_words","threshold":0.5,"observed":0.75,"wordCount":3}}
        """;

    [Fact]
    public void A_refused_image_carries_the_rule_the_threshold_the_value_and_the_word_count()
    {
        var outcome = ResultParser.ParseImage(Bytes(UnreadableImageDocument));

        var unreadable = Assert.IsType<OcrUnreadable>(outcome);
        Assert.Equal(OcrOutcome.Unreadable, unreadable.Outcome);
        Assert.Equal(2480, unreadable.Width);
        Assert.Equal(3508, unreadable.Height);
        Assert.Equal(0.21, unreadable.MeanConfidence);

        Assert.Equal(OcrRefusalReason.MeanConfidence, unreadable.Refusal.Reason);
        Assert.Equal(0.6, unreadable.Refusal.Threshold);
        Assert.Equal(0.21, unreadable.Refusal.Observed);
        Assert.Equal(5, unreadable.Refusal.WordCount);
    }

    [Fact]
    public void A_refusal_reads_as_one_actionable_sentence()
    {
        var unreadable = Assert.IsType<OcrUnreadable>(ResultParser.ParseImage(Bytes(UnreadableImageDocument)));

        Assert.Equal(
            "The page is unreadable (mean confidence below the page threshold): " +
            "the threshold is 0.600, the value observed is 0.210, 5 word(s) concerned.",
            unreadable.Refusal.Message);

        // A refusal never formats as text: there is no text to format.
        Assert.Equal(unreadable.Refusal.Message, unreadable.ToString());
    }

    [Fact]
    public void A_blank_page_is_refused_as_a_blank_page_with_no_word_concerned()
    {
        var unreadable = Assert.IsType<OcrUnreadable>(ResultParser.ParseImage(Bytes(
            """
            {"width":10,"height":10,"status":"unreadable","text":"","meanConfidence":0.0,
             "wordCount":0,"rejectedWordCount":0,"lines":[],
             "refusal":{"reason":"blank_page","threshold":0.6,"observed":0.0,"wordCount":0}}
            """)));

        Assert.Equal(OcrRefusalReason.BlankPage, unreadable.Refusal.Reason);
        Assert.Equal(0, unreadable.Refusal.WordCount);
        Assert.Contains("blank page", unreadable.Refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Too_many_rejected_words_is_its_own_reason_and_names_the_ratio()
    {
        var unreadable = Assert.IsType<OcrUnreadable>(ResultParser.ParseImage(Bytes(RejectedWordsDocument)));

        Assert.Equal(OcrRefusalReason.RejectedWords, unreadable.Refusal.Reason);
        Assert.Equal(0.5, unreadable.Refusal.Threshold);
        Assert.Equal(0.75, unreadable.Refusal.Observed);
        Assert.Equal(3, unreadable.Refusal.WordCount);
    }

    [Fact]
    public void A_refusal_reason_the_shape_does_not_know_is_refused_rather_than_guessed()
        => Assert.Throws<JsonException>(() => ResultParser.ParseImage(Bytes(
            """
            {"width":10,"height":10,"status":"unreadable","text":"","meanConfidence":0.0,
             "wordCount":0,"rejectedWordCount":0,"lines":[],
             "refusal":{"reason":"surely_fine","threshold":0.6,"observed":0.0,"wordCount":0}}
            """)));

    [Fact]
    public void A_word_below_the_word_threshold_comes_back_flagged_with_its_box_and_its_score()
    {
        var reading = Assert.IsType<OcrResult>(ResultParser.ParseImage(Bytes(
            """
            {"width":100,"height":50,"status":"recognized","text":"hello world","meanConfidence":0.8,
             "wordCount":2,"rejectedWordCount":1,
             "lines":[{"text":"hello world","bounds":{"x":1,"y":2,"width":60,"height":10},"confidence":0.8,
                       "words":[{"text":"hello","bounds":{"x":1,"y":2,"width":25,"height":10},"confidence":0.9,
                                 "rejected":false},
                                {"text":"world","bounds":{"x":32,"y":2,"width":29,"height":10},"confidence":0.2,
                                 "rejected":true}]}]}
            """)));

        var word = reading.Lines[0].Words[1];
        Assert.True(word.Rejected);
        Assert.Equal("world", word.Text);
        Assert.Equal(0.2, word.Confidence);
        Assert.Equal(32, word.Bounds.X);
        Assert.False(reading.Lines[0].Words[0].Rejected);

        Assert.Equal(1, reading.RejectedWordCount);
        Assert.Equal(0.5, reading.RejectedWordRatio);
    }

    [Fact]
    public void A_reading_with_no_word_reports_a_zero_ratio_rather_than_dividing_by_zero()
    {
        var reading = Assert.IsType<OcrResult>(ResultParser.ParseImage(Bytes(
            """{"width":10,"height":10,"text":"","meanConfidence":0.0,"lines":[]}""")));

        Assert.Equal(0, reading.RejectedWordCount);
        Assert.Equal(0.0, reading.RejectedWordRatio);
    }

    [Fact]
    public void One_refused_page_does_not_turn_the_pages_around_it_into_refusals()
    {
        var pages = ResultParser.ParsePages(Bytes(
            """
            {"pages":[
              {"pageIndex":0,"width":100,"height":50,"dpi":300,"status":"recognized","text":"one","meanConfidence":0.9,
               "lines":[{"text":"one","bounds":{"x":0,"y":0,"width":30,"height":8},"confidence":0.9,
                         "words":[{"text":"one","bounds":{"x":0,"y":0,"width":30,"height":8},"confidence":0.9}]}]},
              {"pageIndex":1,"width":100,"height":50,"dpi":300,"status":"unreadable","text":"",
               "meanConfidence":0.0,"lines":[],
               "refusal":{"reason":"blank_page","threshold":0.6,"observed":0.0,"wordCount":0}}]}
            """));

        Assert.Equal(2, pages.Count);
        Assert.Equal([OcrOutcome.Recognized, OcrOutcome.Unreadable], pages.Select(page => page.Outcome));
        Assert.Equal("one", Assert.IsType<OcrPageResult>(pages[0]).Text);

        var refused = Assert.IsType<OcrPageUnreadable>(pages[1]);
        Assert.Equal(1, refused.PageIndex);
        Assert.Equal(300, refused.Dpi);
        Assert.Equal(OcrRefusalReason.BlankPage, refused.Refusal.Reason);
    }

    [Fact]
    public void The_backend_hands_the_three_thresholds_to_the_recognize_export_unchanged()
    {
        var library = new FakeNativeLibrary();
        var backend = NativeOcrBackend.Create(OcrEngineHandle.Create(library, [1], [2], null));

        backend.Recognize(
            [1],
            new OcrOptions
            {
                MinimumPageConfidence = 0.75,
                MinimumWordConfidence = 0.4,
                MaximumRejectedWordRatio = 0.25,
            });

        Assert.Equal(0.75, library.LastPageConfidence);
        Assert.Equal(0.4, library.LastWordConfidence);
        Assert.Equal(0.25, library.LastMaxRejectedWordRatio);
    }

    [Fact]
    public void The_backend_hands_the_three_thresholds_to_the_recognize_pdf_export_unchanged()
    {
        var library = new FakeNativeLibrary
        {
            Document = Encoding.UTF8.GetBytes("""{"pages":[]}"""),
        };
        var backend = NativeOcrBackend.Create(OcrEngineHandle.Create(library, [1], [2], null));

        backend.RecognizePdf(
            [1],
            new OcrPdfOptions
            {
                Dpi = 150,
                MinimumPageConfidence = 0.7,
                MinimumWordConfidence = 0.3,
                MaximumRejectedWordRatio = 0.2,
            });

        Assert.Equal(0.7, library.LastPageConfidence);
        Assert.Equal(0.3, library.LastWordConfidence);
        Assert.Equal(0.2, library.LastMaxRejectedWordRatio);
        Assert.Equal(150u, library.LastDpi);
    }

    [Fact]
    public void The_pdf_options_default_to_the_same_thresholds_as_the_image_options()
    {
        var pdf = new OcrPdfOptions();
        var image = new OcrOptions();

        Assert.Equal(image.MinimumPageConfidence, pdf.MinimumPageConfidence);
        Assert.Equal(image.MinimumWordConfidence, pdf.MinimumWordConfidence);
        Assert.Equal(image.MaximumRejectedWordRatio, pdf.MaximumRejectedWordRatio);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void A_pdf_ratio_out_of_range_is_refused_before_any_native_call(double ratio)
    {
        var library = new FakeNativeLibrary();
        using var engine = new OcrEngine(
            NativeOcrBackend.Create(OcrEngineHandle.Create(library, [1], [2], null)));

        var error = Assert.Throws<ArgumentOutOfRangeException>(
            () => engine.RecognizePdf([1], new OcrPdfOptions { MaximumRejectedWordRatio = ratio }));

        Assert.Equal("MaximumRejectedWordRatio", error.ParamName);
        Assert.Equal(0, library.RecognizePdfCalls);
    }

    [Fact]
    public void A_backend_that_refuses_comes_through_the_engine_as_the_typed_refusal()
    {
        var backend = new FakeOcrBackend
        {
            Result = new OcrUnreadable(
                2480,
                3508,
                0.0,
                new OcrRefusal(OcrRefusalReason.BlankPage, 0.6, 0.0, 0)),
        };
        using var engine = new OcrEngine(backend);

        var outcome = engine.Recognize([1]);

        var unreadable = Assert.IsType<OcrUnreadable>(outcome);
        Assert.Equal(OcrOutcome.Unreadable, unreadable.Outcome);
        Assert.Equal(OcrRefusalReason.BlankPage, unreadable.Refusal.Reason);
    }

    private static byte[] Bytes(string document) => Encoding.UTF8.GetBytes(document);
}
