using System;
using System.Collections.Generic;
using System.Linq;
using M3scalinum.Text.Ocr.Internal;
using Xunit;

namespace M3scalinum.Text.Ocr.Tests;

/// <summary>
/// The managed half of per-page language selection: which call a named language takes,
/// what reaches the backend, and what an engine that holds one set does when it is
/// asked to choose.
/// </summary>
/// <remarks>
/// The routing itself — which set reads which page, and the fallback when a language has
/// none — runs in Rust and is proved there, in <c>native/text-ocr/src/language.rs</c>,
/// without a model. What this file asserts is the mapping: a language reaches the call
/// that carries it, an omitted language takes the call revision 2 already exported, and
/// a rule is resolved once per page in page order.
/// </remarks>
public sealed class LanguageSelectionTests
{
    [Fact]
    public void Recognize_without_a_language_takes_the_call_of_a_single_set()
    {
        var backend = new LanguageBackend();
        using var engine = new OcrEngine(backend);

        engine.Recognize([1, 2, 3]);

        Assert.Equal(1, backend.PlainRecognizeCalls);
        Assert.Equal(0, backend.LanguageRecognizeCalls);
    }

    [Fact]
    public void Recognize_with_a_language_passes_the_tag_to_the_backend()
    {
        var backend = new LanguageBackend();
        using var engine = new OcrEngine(backend, hasLanguageSets: true);

        engine.Recognize([1, 2, 3], language: " en ");

        Assert.Equal(0, backend.PlainRecognizeCalls);
        Assert.Equal(1, backend.LanguageRecognizeCalls);
        // The tag is trimmed here; the native side lowercases it into the report.
        Assert.Equal("en", backend.LastLanguage);
    }

    [Fact]
    public void An_empty_language_is_a_request_for_nothing_and_takes_the_plain_call()
    {
        var backend = new LanguageBackend();
        using var engine = new OcrEngine(backend, hasLanguageSets: true);

        engine.Recognize([1, 2, 3], language: "   ");

        Assert.Equal(1, backend.PlainRecognizeCalls);
        Assert.Equal(0, backend.LanguageRecognizeCalls);
    }

    [Fact]
    public void Naming_a_language_on_a_single_set_engine_is_refused_rather_than_ignored()
    {
        var backend = new LanguageBackend();
        using var engine = new OcrEngine(backend);

        var error = Assert.Throws<InvalidOperationException>(
            () => engine.Recognize([1, 2, 3], language: "en"));

        Assert.Contains("FromLanguages", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, backend.LanguageRecognizeCalls);
    }

    [Fact]
    public void RecognizePdf_without_a_rule_takes_the_call_of_a_single_set()
    {
        var backend = new LanguageBackend();
        using var engine = new OcrEngine(backend);

        engine.RecognizePdf([1, 2, 3]);

        Assert.Equal(1, backend.PlainPdfCalls);
        Assert.Equal(0, backend.LanguagePdfCalls);
    }

    [Fact]
    public void The_rule_is_asked_once_per_page_in_page_order()
    {
        var backend = new LanguageBackend();
        using var engine = new OcrEngine(backend, hasLanguageSets: true);
        var asked = new List<int>();

        engine.RecognizePdf([1, 2, 3], languageForPage: index =>
        {
            asked.Add(index);
            return index == 0 ? "fr" : "en";
        });

        Assert.Equal([0, 1], asked);
        Assert.Equal(1, backend.LanguagePdfCalls);
        Assert.Equal(["fr", "en"], backend.LastPagePlan);
    }

    [Fact]
    public void A_rule_that_returns_null_for_a_page_names_no_language_for_it()
    {
        var backend = new LanguageBackend();
        using var engine = new OcrEngine(backend, hasLanguageSets: true);

        engine.RecognizePdf([1, 2, 3], languageForPage: index => index == 1 ? null : "fr");

        Assert.Equal(["fr", null], backend.LastPagePlan);
    }

    [Fact]
    public void A_rule_supplied_to_a_single_set_engine_is_refused_rather_than_ignored()
    {
        var backend = new LanguageBackend();
        using var engine = new OcrEngine(backend);

        var error = Assert.Throws<InvalidOperationException>(
            () => engine.RecognizePdf([1, 2, 3], languageForPage: _ => "en"));

        Assert.Contains("FromLanguages", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, backend.LanguagePdfCalls);
    }

    [Fact]
    public void FromLanguages_rejects_an_empty_plan_before_touching_the_library()
    {
        var error = Assert.Throws<ArgumentException>(
            () => OcrEngine.FromLanguages("fr", Array.Empty<OcrLanguageModelSet>()));

        Assert.Equal("modelSets", error.ParamName);
    }

    [Fact]
    public void FromLanguages_rejects_a_null_or_empty_default_language()
    {
        Assert.Throws<ArgumentNullException>(
            () => OcrEngine.FromLanguages(null!, [null!]));

        var error = Assert.Throws<ArgumentException>(
            () => OcrEngine.FromLanguages("  ", [null!]));

        Assert.Equal("defaultLanguage", error.ParamName);
    }

    [Fact]
    public void FromLanguages_rejects_a_null_plan()
    {
        Assert.Throws<ArgumentNullException>(
            () => OcrEngine.FromLanguages("fr", null!));
    }

    [Fact]
    public void A_named_language_still_carries_the_thresholds_and_the_preprocessing_flags()
    {
        var backend = new LanguageBackend();
        using var engine = new OcrEngine(backend, hasLanguageSets: true);

        engine.Recognize(
            [4, 5, 6],
            new OcrOptions
            {
                MinimumPageConfidence = 0.7,
                MinimumWordConfidence = 0.4,
                MaximumRejectedWordRatio = 0.3,
                DetectRotation = true,
                Deskew = true,
                Denoise = true,
            },
            language: "fr");

        var options = Assert.IsType<OcrOptions>(backend.LastOptions);

        Assert.Equal(0.7, options.MinimumPageConfidence);
        Assert.Equal(0.4, options.MinimumWordConfidence);
        Assert.Equal(0.3, options.MaximumRejectedWordRatio);
        Assert.True(options.RequiresPreprocessing);
        Assert.Equal("fr", backend.LastLanguage);
    }
}

/// <summary>
/// A backend that records which entry point the engine chose, and hands back a report.
/// </summary>
/// <remarks>
/// It answers for two pages whatever the document holds: the page count is the native
/// side's business and is proved against the real library, in
/// <see cref="LanguageNativeTests"/>; here only the mapping is under test.
/// </remarks>
internal sealed class LanguageBackend : IOcrBackend, IDisposable
{
    private const int PageCount = 2;

    internal OcrRecognition Result { get; set; } = new OcrResult(
        "bonjour",
        [new OcrLine("bonjour", new OcrBox(0, 0, 100, 20), 0.9,
            [new OcrWord("bonjour", new OcrBox(0, 0, 100, 20), 0.9)])],
        0.9,
        200,
        100,
        language: new OcrLanguage("fr", "fr", false, OcrLanguageFallbackReason.Unspecified, null));

    internal int PlainRecognizeCalls { get; private set; }

    internal int LanguageRecognizeCalls { get; private set; }

    internal int PlainPdfCalls { get; private set; }

    internal int LanguagePdfCalls { get; private set; }

    internal string? LastLanguage { get; private set; }

    internal OcrOptions? LastOptions { get; private set; }

    internal string?[] LastPagePlan { get; private set; } = [];

    public OcrRecognition Recognize(byte[] image, OcrOptions options)
    {
        PlainRecognizeCalls++;
        LastOptions = options;

        return Result;
    }

    public IReadOnlyList<OcrPageOutcome> RecognizePdf(byte[] pdf, OcrPdfOptions options)
    {
        PlainPdfCalls++;
        LastOptions = options;

        return Pages();
    }

    public OcrRecognition RecognizeLanguage(byte[] image, OcrOptions options, string language)
    {
        LanguageRecognizeCalls++;
        LastOptions = options;
        LastLanguage = language;

        return Result;
    }

    public IReadOnlyList<OcrPageOutcome> RecognizePdfLanguages(
        byte[] pdf,
        OcrPdfOptions options,
        Func<int, string?> languageForPage)
    {
        LanguagePdfCalls++;
        LastOptions = options;

        var plan = new string?[PageCount];
        for (var index = 0; index < PageCount; index++)
        {
            plan[index] = languageForPage(index);
        }

        LastPagePlan = plan;

        return Pages();
    }

    public void Dispose()
    {
    }

    private static IReadOnlyList<OcrPageOutcome> Pages()
        =>
        [
            new OcrPageResult(0, "bonjour", [], 0.9, 200, 100, 300,
                language: new OcrLanguage("fr", "fr", false, OcrLanguageFallbackReason.Unspecified, null)),
            new OcrPageResult(1, "hello", [], 0.9, 200, 100, 300,
                language: new OcrLanguage("en", "en", false, OcrLanguageFallbackReason.Unspecified, null)),
        ];
}
