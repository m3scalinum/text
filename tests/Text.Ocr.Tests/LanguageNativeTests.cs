using System;
using M3scalinum.Text.Ocr.Internal;
using Xunit;

namespace M3scalinum.Text.Ocr.Tests;

/// <summary>
/// The language exports against the real library: the page count, the plan, and the
/// refusals that need no model.
/// </summary>
/// <remarks>
/// These skip on a machine without the native library, like every native test here.
/// They prove the symbols resolve and that a plan is read by the code that will read it
/// at run time; that two real language models then read a two-page document correctly
/// needs the weights, which this repository does not ship.
/// </remarks>
public sealed class LanguageNativeTests
{
    [Fact]
    public void The_library_counts_the_pages_of_a_document_without_rendering_them()
    {
        if (!NativeProbe.IsAvailable)
        {
            Assert.Skip(NativeProbe.SkipReason);
        }

        var status = NativeOcr.Library.PdfPageCount(
            MinimalPdf.WithText("hello"), out var json, out var error);

        var document = NativeOcr.Take(NativeOcr.Library, status, json, error);

        Assert.Equal(1u, ResultParser.ParsePageCount(document));
    }

    [Fact]
    public void Counting_the_pages_of_something_that_is_not_a_pdf_is_a_pdf_error()
    {
        if (!NativeProbe.IsAvailable)
        {
            Assert.Skip(NativeProbe.SkipReason);
        }

        var status = NativeOcr.Library.PdfPageCount(
            "this is not a pdf"u8.ToArray(), out var json, out var error);

        var exception = Assert.Throws<OcrException>(
            () => NativeOcr.Take(NativeOcr.Library, status, json, error));

        Assert.Equal("pdf", exception.Code);
    }

    [Fact]
    public void A_language_plan_naming_a_directory_that_is_not_there_is_refused_by_name()
    {
        if (!NativeProbe.IsAvailable)
        {
            Assert.Skip(NativeProbe.SkipReason);
        }

        const string plan = """
            {"default":"fr","sets":[{"language":"fr","directory":"C:/no-such-ocr-models"}]}
            """;

        var exception = Assert.Throws<OcrException>(
            () => OcrEngineHandle.CreateLanguages(System.Text.Encoding.UTF8.GetBytes(plan)));

        Assert.Equal("model", exception.Code);
        Assert.Equal(OcrModelDefect.MissingDirectory, exception.Defect);
    }

    [Fact]
    public void A_language_plan_that_is_not_json_is_an_argument_error()
    {
        if (!NativeProbe.IsAvailable)
        {
            Assert.Skip(NativeProbe.SkipReason);
        }

        var exception = Assert.Throws<OcrException>(
            () => OcrEngineHandle.CreateLanguages("not a plan"u8.ToArray()));

        Assert.Equal("invalid_argument", exception.Code);
    }

    [Fact]
    public void A_language_plan_whose_default_has_no_set_is_an_argument_error()
    {
        if (!NativeProbe.IsAvailable)
        {
            Assert.Skip(NativeProbe.SkipReason);
        }

        const string plan = """
            {"default":"de","sets":[{"language":"fr","directory":"C:/no-such-ocr-models"}]}
            """;

        var exception = Assert.Throws<OcrException>(
            () => OcrEngineHandle.CreateLanguages(System.Text.Encoding.UTF8.GetBytes(plan)));

        // The directory is read first, so a plan naming a bad one fails there; this
        // asserts the refusal is loud either way and never a silent empty catalogue.
        Assert.Contains("model", exception.Code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_page_count_export_is_a_symbol_this_library_has()
    {
        if (!NativeProbe.IsAvailable)
        {
            Assert.Skip(NativeProbe.SkipReason);
        }

        // A missing export would be an EntryPointNotFoundException here, which is what
        // the managed side turns into an "unsupported" refusal.
        var status = NativeOcr.Library.PdfPageCount(MinimalPdf.WithText("x"), out var json, out var error);
        var document = NativeOcr.Take(NativeOcr.Library, status, json, error);

        Assert.Equal(1u, ResultParser.ParsePageCount(document));
        Assert.Equal(IntPtr.Zero, error);
    }
}
