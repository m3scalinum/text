using System.Linq;
using System.Text;
using M3scalinum.Text.Ocr.Internal;
using Xunit;

namespace M3scalinum.Text.Ocr.Tests;

/// <summary>
/// The <c>preprocess</c> object of a result document, and the report it becomes.
/// </summary>
/// <remarks>
/// The document shapes here are the ones <c>native/text-ocr</c> writes: an object naming
/// the quarter turns applied, the fine rotation the deskew applied and whether the median
/// filter ran. A document without it — every document a native library older than this
/// feature writes — must read as "nothing was done" rather than as a guess.
/// </remarks>
public sealed class PreprocessResultTests
{
    private const string TurnedAndCleaned =
        """
        {"width":50,"height":100,"status":"recognized","text":"hello","meanConfidence":0.9,
         "wordCount":1,"rejectedWordCount":0,
         "preprocess":{"rotationDegrees":90,"skewDegrees":-1.25,"denoised":true},
         "lines":[{"text":"hello","bounds":{"x":1,"y":2,"width":40,"height":8},"confidence":0.9,
                   "words":[{"text":"hello","bounds":{"x":1,"y":2,"width":40,"height":8},"confidence":0.9}]}]}
        """;

    private const string Untouched =
        """
        {"width":100,"height":50,"status":"recognized","text":"hello","meanConfidence":0.9,
         "wordCount":1,"rejectedWordCount":0,
         "lines":[{"text":"hello","bounds":{"x":1,"y":2,"width":40,"height":8},"confidence":0.9,
                   "words":[{"text":"hello","bounds":{"x":1,"y":2,"width":40,"height":8},"confidence":0.9}]}]}
        """;

    [Fact]
    public void A_document_that_names_a_rotation_reports_it_with_the_page_it_was_applied_to()
    {
        var result = Assert.IsType<OcrResult>(
            ResultParser.ParseImage(Encoding.UTF8.GetBytes(TurnedAndCleaned)));

        Assert.Equal(90, result.Preprocess.RotationDegrees);
        Assert.Equal(-1.25, result.Preprocess.SkewDegrees);
        Assert.True(result.Preprocess.Denoised);
        Assert.True(result.Preprocess.Changed);

        // The boxes and the size are the preprocessed page's: that is the image the
        // recogniser saw, and the one its coordinates belong to.
        Assert.Equal(50, result.Width);
        Assert.Equal(100, result.Height);
        Assert.Equal(1, result.Lines.Single().Bounds.X);
    }

    [Fact]
    public void A_document_without_a_preprocess_object_reads_as_an_untouched_page()
    {
        var result = Assert.IsType<OcrResult>(
            ResultParser.ParseImage(Encoding.UTF8.GetBytes(Untouched)));

        Assert.Same(OcrPreprocessReport.None, result.Preprocess);
        Assert.Equal(0, result.Preprocess.RotationDegrees);
        Assert.Equal(0.0, result.Preprocess.SkewDegrees);
        Assert.False(result.Preprocess.Denoised);
        Assert.False(result.Preprocess.Changed);
    }

    [Fact]
    public void A_partial_report_fills_the_missing_fields_with_nothing_done()
    {
        const string partial =
            """
            {"width":10,"height":10,"text":"x","meanConfidence":0.9,
             "preprocess":{"rotationDegrees":180},"lines":[]}
            """;

        var result = Assert.IsType<OcrResult>(ResultParser.ParseImage(Encoding.UTF8.GetBytes(partial)));

        Assert.Equal(180, result.Preprocess.RotationDegrees);
        Assert.Equal(0.0, result.Preprocess.SkewDegrees);
        Assert.False(result.Preprocess.Denoised);
        Assert.True(result.Preprocess.Changed);
    }

    [Fact]
    public void A_preprocess_object_that_is_not_an_object_reads_as_nothing_done()
    {
        const string odd =
            """
            {"width":10,"height":10,"text":"x","meanConfidence":0.9,"preprocess":null,"lines":[]}
            """;

        var result = Assert.IsType<OcrResult>(ResultParser.ParseImage(Encoding.UTF8.GetBytes(odd)));

        Assert.Same(OcrPreprocessReport.None, result.Preprocess);
    }

    [Fact]
    public void A_refused_page_still_parses_when_the_reports_of_the_native_side_include_preprocessing()
    {
        const string refused =
            """
            {"width":10,"height":10,"status":"unreadable","text":"","meanConfidence":0.1,
             "wordCount":0,"rejectedWordCount":0,"lines":[],
             "preprocess":{"rotationDegrees":270,"skewDegrees":0.0,"denoised":false},
             "refusal":{"reason":"blank_page","threshold":0.6,"observed":0.0,"wordCount":0}}
            """;

        var refusedPage = Assert.IsType<OcrUnreadable>(
            ResultParser.ParseImage(Encoding.UTF8.GetBytes(refused)));

        Assert.Equal(OcrRefusalReason.BlankPage, refusedPage.Refusal.Reason);
    }

    [Fact]
    public void Each_page_of_a_document_carries_the_report_of_its_own_preprocessing()
    {
        const string pages =
            """
            {"pages":[
              {"pageIndex":0,"width":50,"height":100,"dpi":300,"text":"one","meanConfidence":0.9,
               "preprocess":{"rotationDegrees":90,"skewDegrees":0.5,"denoised":false},
               "lines":[{"text":"one","bounds":{"x":0,"y":0,"width":20,"height":8},"confidence":0.9,
                         "words":[{"text":"one","bounds":{"x":0,"y":0,"width":20,"height":8},"confidence":0.9}]}]},
              {"pageIndex":1,"width":100,"height":100,"dpi":300,"text":"two","meanConfidence":0.9,
               "lines":[{"text":"two","bounds":{"x":0,"y":0,"width":20,"height":8},"confidence":0.9,
                         "words":[{"text":"two","bounds":{"x":0,"y":0,"width":20,"height":8},"confidence":0.9}]}]}]}
            """;

        var outcomes = ResultParser.ParsePages(Encoding.UTF8.GetBytes(pages));

        var turned = Assert.IsType<OcrPageResult>(outcomes[0]);
        var plain = Assert.IsType<OcrPageResult>(outcomes[1]);

        Assert.Equal(90, turned.Preprocess.RotationDegrees);
        Assert.Equal(0.5, turned.Preprocess.SkewDegrees);
        Assert.Equal(50, turned.Width);
        Assert.Same(OcrPreprocessReport.None, plain.Preprocess);
    }

    [Fact]
    public void The_report_says_in_one_line_what_was_done()
    {
        var report = new OcrPreprocessReport(180, -2.5, true);

        Assert.True(report.Changed);
        Assert.Contains("180", report.ToString(), System.StringComparison.Ordinal);
        Assert.Contains("-2.50", report.ToString(), System.StringComparison.Ordinal);
        Assert.Contains("denoised: True", report.ToString(), System.StringComparison.Ordinal);
        Assert.Equal("no preprocessing", OcrPreprocessReport.None.ToString());
    }
}
