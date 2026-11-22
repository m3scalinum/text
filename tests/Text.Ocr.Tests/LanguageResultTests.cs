using System;
using System.Linq;
using System.Text;
using M3scalinum.Text.Ocr.Internal;
using Xunit;

namespace M3scalinum.Text.Ocr.Tests;

/// <summary>
/// The <c>language</c> object and the per-page <c>models</c> object the native side
/// writes, parsed as this package parses them.
/// </summary>
/// <remarks>
/// These documents are the contract between the two halves, field for field: the
/// fallback is only "reported" if the managed result carries all four of its fields,
/// and a page of a mixed document is only trustworthy if its identity is the one of the
/// set that read it, not the document's default.
/// </remarks>
public sealed class LanguageResultTests
{
    private const string MixedPagesDocument = """
        {"languages":{"default":"fr","available":["en","fr"]},
         "pages":[
           {"pageIndex":0,"width":100,"height":50,"dpi":300,"text":"bonjour","meanConfidence":0.9,
            "language":{"requested":"fr","used":"fr","fallback":false},
            "models":{"detection":{"role":"detection","format":"rten","length":10,"sha256":"aaa"},
                      "recognition":{"role":"recognition","format":"rten","length":10,"sha256":"bbb"},
                      "set":"set-fr"},
            "lines":[]},
           {"pageIndex":1,"width":100,"height":50,"dpi":300,"text":"hello","meanConfidence":0.8,
            "language":{"requested":"en","used":"en","fallback":false},
            "models":{"detection":{"role":"detection","format":"rten","length":10,"sha256":"ccc"},
                      "recognition":{"role":"recognition","format":"rten","length":10,"sha256":"ddd"},
                      "set":"set-en"},
            "lines":[]}]}
        """;

    private const string FallbackDocument = """
        {"width":100,"height":50,"text":"bonjour","meanConfidence":0.9,"lines":[],
         "language":{"requested":"de","used":"fr","fallback":true,"reason":"missing_language",
                     "message":"the page asked for language 'de', which has no model set; it was read with the default set 'fr' (missing_language)"}}
        """;

    [Fact]
    public void A_fallback_reaches_the_result_with_its_request_the_set_used_and_the_reason()
    {
        var result = Assert.IsType<OcrResult>(ResultParser.ParseImage(Bytes(FallbackDocument)));
        var language = Assert.IsType<OcrLanguage>(result.Language);

        Assert.True(language.FellBack);
        Assert.Equal("de", language.Requested);
        Assert.Equal("fr", language.Used);
        Assert.Equal(OcrLanguageFallbackReason.MissingLanguage, language.Reason);
        Assert.Contains("'de'", language.Message!, StringComparison.Ordinal);
        Assert.Contains("'fr'", language.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_page_that_named_no_language_reports_none_and_is_not_a_fallback()
    {
        const string document = """
            {"width":100,"height":50,"text":"bonjour","meanConfidence":0.9,"lines":[],
             "language":{"requested":null,"used":"fr","fallback":false}}
            """;

        var result = Assert.IsType<OcrResult>(ResultParser.ParseImage(Bytes(document)));
        var language = Assert.IsType<OcrLanguage>(result.Language);

        Assert.False(language.FellBack);
        Assert.Null(language.Requested);
        Assert.Equal("fr", language.Used);
        Assert.Equal(OcrLanguageFallbackReason.Unspecified, language.Reason);
        Assert.Null(language.Message);
    }

    [Fact]
    public void A_document_without_a_language_object_reports_no_language()
    {
        const string document = """
            {"width":100,"height":50,"text":"bonjour","meanConfidence":0.9,"lines":[]}
            """;

        var result = Assert.IsType<OcrResult>(ResultParser.ParseImage(Bytes(document)));

        Assert.Null(result.Language);
    }

    [Fact]
    public void An_unreadable_page_still_carries_the_language_it_was_read_with()
    {
        const string document = """
            {"width":100,"height":50,"status":"unreadable","text":"","meanConfidence":0.1,"lines":[],
             "refusal":{"reason":"mean_confidence","threshold":0.6,"observed":0.1,"wordCount":1},
             "language":{"requested":"en","used":"fr","fallback":true,"reason":"missing_language"}}
            """;

        var result = Assert.IsType<OcrUnreadable>(ResultParser.ParseImage(Bytes(document)));
        var language = Assert.IsType<OcrLanguage>(result.Language);

        Assert.True(language.FellBack);
        Assert.Equal("en", language.Requested);
        Assert.Equal("fr", language.Used);
    }

    [Fact]
    public void Each_page_of_a_mixed_document_reports_its_own_weights_and_language()
    {
        var pages = ResultParser.ParsePages(Bytes(MixedPagesDocument));

        Assert.Equal(2, pages.Count);
        Assert.Equal("set-fr", pages[0].Models!.Sha256);
        Assert.Equal("set-en", pages[1].Models!.Sha256);
        Assert.Equal("fr", pages[0].Language!.Used);
        Assert.Equal("en", pages[1].Language!.Used);
        Assert.False(pages[0].Language!.FellBack);
    }

    [Fact]
    public void A_page_without_its_own_identity_takes_the_documents()
    {
        const string document = """
            {"models":{"detection":{"role":"detection","format":"rten","length":10,"sha256":"aaa"},
                       "recognition":{"role":"recognition","format":"rten","length":10,"sha256":"bbb"},
                       "set":"set-only"},
             "pages":[{"pageIndex":0,"width":100,"height":50,"dpi":300,"text":"x","meanConfidence":0.9,"lines":[]}]}
            """;

        var pages = ResultParser.ParsePages(Bytes(document));

        Assert.Equal("set-only", pages[0].Models!.Sha256);
        Assert.Null(pages[0].Language);
    }

    [Fact]
    public void A_page_fallback_reaches_the_page_outcome()
    {
        const string document = """
            {"languages":{"default":"fr","available":["fr"]},
             "pages":[{"pageIndex":3,"width":100,"height":50,"dpi":300,"text":"x","meanConfidence":0.9,"lines":[],
                       "language":{"requested":"es","used":"fr","fallback":true,"reason":"missing_language"}}]}
            """;

        var page = ResultParser.ParsePages(Bytes(document))[0];
        var language = Assert.IsType<OcrLanguage>(page.Language);

        Assert.Equal(3, page.PageIndex);
        Assert.True(language.FellBack);
        Assert.Equal("es", language.Requested);
        Assert.Equal("fr", language.Used);
        Assert.Equal(OcrLanguageFallbackReason.MissingLanguage, language.Reason);
    }

    [Fact]
    public void An_unknown_fallback_reason_still_reports_that_it_fell_back()
    {
        const string document = """
            {"width":100,"height":50,"text":"x","meanConfidence":0.9,"lines":[],
             "language":{"requested":"es","used":"fr","fallback":true,"reason":"something_new"}}
            """;

        var language = Assert.IsType<OcrLanguage>(
            Assert.IsType<OcrResult>(ResultParser.ParseImage(Bytes(document))).Language);

        Assert.True(language.FellBack);
        Assert.Equal(OcrLanguageFallbackReason.Unspecified, language.Reason);
    }

    [Fact]
    public void The_page_count_document_reads_as_a_number()
    {
        Assert.Equal(7u, ResultParser.ParsePageCount(Bytes("""{"pages":7}""")));
    }

    private static byte[] Bytes(string json) => Encoding.UTF8.GetBytes(json);
}
