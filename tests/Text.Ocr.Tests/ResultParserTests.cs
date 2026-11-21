using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using M3scalinum.Text.Ocr.Internal;
using Xunit;

namespace M3scalinum.Text.Ocr.Tests;

/// <summary>
/// The result documents <c>native/text-ocr</c> emits, parsed as the library parses
/// them. These documents are the contract between the two halves: nothing else
/// asserts it without a native library, so the shapes here are the ones the Rust
/// code writes field for field.
/// </summary>
public sealed class ResultParserTests
{
    private const string ImageDocument = """
        {"width":2480,"height":3508,"text":"INVOICE 2026\nTotal 42","meanConfidence":0.913,
         "lines":[
           {"text":"INVOICE 2026","bounds":{"x":120,"y":300,"width":540,"height":40},"confidence":0.927,
            "words":[
              {"text":"INVOICE","bounds":{"x":120,"y":300,"width":300,"height":40},"confidence":0.935},
              {"text":"2026","bounds":{"x":440,"y":300,"width":220,"height":40},"confidence":0.919}]},
           {"text":"Total 42","bounds":{"x":120,"y":360,"width":260,"height":38},"confidence":0.899,
            "words":[
              {"text":"Total","bounds":{"x":120,"y":360,"width":160,"height":38},"confidence":0.901},
              {"text":"42","bounds":{"x":300,"y":360,"width":80,"height":38},"confidence":0.897}]}]}
        """;

    private const string PagesDocument = """
        {"pages":[
          {"pageIndex":0,"width":2480,"height":3508,"dpi":300,"text":"page one","meanConfidence":0.8,
           "lines":[{"text":"page one","bounds":{"x":0,"y":0,"width":100,"height":20},"confidence":0.8,
                     "words":[{"text":"page","bounds":{"x":0,"y":0,"width":50,"height":20},"confidence":0.8}]}]},
          {"pageIndex":2,"width":1240,"height":1754,"dpi":150,"text":"","meanConfidence":0.0,
           "lines":[]}]}
        """;

    [Fact]
    public void An_image_document_carries_the_text_the_size_and_the_mean_confidence()
    {
        var result = ResultParser.ParseImage(Bytes(ImageDocument));

        Assert.Equal("INVOICE 2026\nTotal 42", result.Text);
        Assert.Equal(2480, result.Width);
        Assert.Equal(3508, result.Height);
        Assert.Equal(0.913, result.MeanConfidence);
    }

    [Fact]
    public void An_image_document_carries_each_line_with_its_box_and_words()
    {
        var result = ResultParser.ParseImage(Bytes(ImageDocument));

        Assert.Equal(2, result.Lines.Count);

        var line = result.Lines[0];
        Assert.Equal("INVOICE 2026", line.Text);
        Assert.Equal(120, line.Bounds.X);
        Assert.Equal(300, line.Bounds.Y);
        Assert.Equal(540, line.Bounds.Width);
        Assert.Equal(40, line.Bounds.Height);
        Assert.Equal(0.927, line.Confidence);

        Assert.Equal(["INVOICE", "2026"], line.Words.Select(w => w.Text));
        Assert.Equal(440, line.Words[1].Bounds.X);
        Assert.Equal(220, line.Words[1].Bounds.Width);
        Assert.Equal(0.919, line.Words[1].Confidence);
    }

    [Fact]
    public void A_pages_document_carries_each_page_with_its_index_and_resolution()
    {
        var pages = ResultParser.ParsePages(Bytes(PagesDocument));

        Assert.Equal(2, pages.Count);
        Assert.Equal(0, pages[0].PageIndex);
        Assert.Equal(300, pages[0].Dpi);
        Assert.Equal("page one", pages[0].Text);
        Assert.Single(pages[0].Lines);

        Assert.Equal(2, pages[1].PageIndex);
        Assert.Equal(150, pages[1].Dpi);
        Assert.Equal(1240, pages[1].Width);
        Assert.Empty(pages[1].Lines);
        Assert.Equal(string.Empty, pages[1].Text);
    }

    [Fact]
    public void A_page_with_no_text_reads_as_empty_rather_than_failing()
    {
        var result = ResultParser.ParseImage(Bytes(
            """{"width":10,"height":10,"text":"","meanConfidence":0.0,"lines":[]}"""));

        Assert.Empty(result.Lines);
        Assert.Equal(string.Empty, result.Text);
        Assert.Equal(0.0, result.MeanConfidence);
    }

    [Fact]
    public void A_malformed_document_is_refused_rather_than_half_read()
        => Assert.ThrowsAny<JsonException>(
            () => ResultParser.ParseImage(Bytes("""{"width":2480,""")));

    [Fact]
    public void A_document_missing_a_field_the_shape_promises_is_refused()
        => Assert.Throws<KeyNotFoundException>(() => ResultParser.ParseImage(Bytes(
            """{"width":10,"height":10,"text":"","meanConfidence":0.0}""")));

    [Fact]
    public void A_field_the_native_side_gains_later_is_ignored()
    {
        var result = ResultParser.ParseImage(Bytes(
            """
            {"engine":"ocrs","elapsedMs":12,"width":10,"height":10,"text":"a","meanConfidence":0.5,
             "lines":[{"kind":"text","text":"a","bounds":{"x":0,"y":0,"width":5,"height":5},
                       "confidence":0.5,"words":[{"lang":"la","text":"a",
                       "bounds":{"x":0,"y":0,"width":5,"height":5},"confidence":0.5}]}]}
            """));

        Assert.Equal("a", result.Text);
        Assert.Equal("a", Assert.Single(Assert.Single(result.Lines).Words).Text);
    }

    [Fact]
    public void A_box_that_runs_off_the_page_is_clipped_to_it()
    {
        var line = Assert.Single(ResultParser.ParseImage(Bytes(
            """
            {"width":100,"height":50,"text":"e","meanConfidence":0.5,
             "lines":[{"text":"e","bounds":{"x":90,"y":40,"width":50,"height":20},"confidence":0.5,
                       "words":[{"text":"e","bounds":{"x":-5,"y":-5,"width":20,"height":10},
                                 "confidence":0.5}]}]}
            """)).Lines);

        Assert.Equal([90, 40, 10, 10], Boxes(line.Bounds));
        Assert.Equal([0, 0, 15, 5], Boxes(Assert.Single(line.Words).Bounds));
    }

    [Fact]
    public void A_line_box_with_no_area_takes_the_box_that_holds_its_words()
    {
        var line = Assert.Single(ResultParser.ParseImage(Bytes(
            """
            {"width":100,"height":50,"text":"ab","meanConfidence":0.5,
             "lines":[{"text":"ab","bounds":{"x":20,"y":10,"width":0,"height":0},"confidence":0.5,
                       "words":[{"text":"b","bounds":{"x":40,"y":10,"width":10,"height":5},
                                 "confidence":0.5},
                                {"text":"a","bounds":{"x":20,"y":10,"width":15,"height":5},
                                 "confidence":0.5}]}]}
            """)).Lines);

        Assert.Equal([20, 10, 30, 5], Boxes(line.Bounds));
    }

    [Fact]
    public void A_line_with_no_words_and_no_area_stays_an_empty_box()
    {
        var line = Assert.Single(ResultParser.ParseImage(Bytes(
            """
            {"width":100,"height":50,"text":"","meanConfidence":0.0,
             "lines":[{"text":"","bounds":{"x":0,"y":0,"width":0,"height":0},"confidence":0.0,
                       "words":[]}]}
            """)).Lines);

        Assert.Equal([0, 0, 0, 0], Boxes(line.Bounds));
    }

    [Fact]
    public void The_words_of_a_line_come_back_in_reading_order()
    {
        var line = Assert.Single(ResultParser.ParseImage(Bytes(
            """
            {"width":100,"height":50,"text":"c a b","meanConfidence":0.5,
             "lines":[{"text":"c a b","bounds":{"x":10,"y":10,"width":80,"height":10},
                       "confidence":0.5,
                       "words":[{"text":"c","bounds":{"x":70,"y":10,"width":10,"height":10},
                                 "confidence":0.5},
                                {"text":"a","bounds":{"x":10,"y":10,"width":10,"height":10},
                                 "confidence":0.5},
                                {"text":"b","bounds":{"x":30,"y":10,"width":10,"height":10},
                                 "confidence":0.5}]}]}
            """)).Lines);

        Assert.Equal(["a", "b", "c"], line.Words.Select(word => word.Text));
    }

    private static int[] Boxes(OcrBox box) => [box.X, box.Y, box.Width, box.Height];

    private static byte[] Bytes(string document) => Encoding.UTF8.GetBytes(document);
}
