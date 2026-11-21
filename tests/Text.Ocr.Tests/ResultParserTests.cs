using System;
using System.Linq;
using System.Text;
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

    private static byte[] Bytes(string document) => Encoding.UTF8.GetBytes(document);
}
