using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using M3scalinum.Text.Ocr.Internal;
using Xunit;

namespace M3scalinum.Text.Ocr.Tests;

/// <summary>
/// The plan as it crosses the ABI: the JSON the managed side writes for a page range,
/// the documents the native side answers with, and the one error path a language
/// engine reaches without real weights.
/// </summary>
/// <remarks>
/// The fake here speaks the four language exports and nothing else, so the plan bytes
/// can be asserted exactly. The tests that need the real library — a plan that reaches
/// the loader, a page count — skip where it is not loadable, like every native test.
/// </remarks>
public sealed class LanguagePlanTests
{
    [Fact]
    public void The_page_plan_is_a_json_array_with_nulls_for_the_pages_that_name_none()
    {
        var json = Encoding.UTF8.GetString(
            NativeOcr.PageLanguagesJson(["fr", null, "en-GB \"x\""]));

        Assert.Equal("[\"fr\",null,\"en-GB \\u0022x\\u0022\"]", json);
    }

    [Fact]
    public void An_empty_plan_is_an_empty_array()
    {
        Assert.Equal("[]", Encoding.UTF8.GetString(NativeOcr.PageLanguagesJson([])));
    }

    [Fact]
    public void A_named_language_reaches_the_language_export_with_the_thresholds()
    {
        var native = new LanguageNative
        {
            Document = """
                {"width":10,"height":10,"text":"bonjour","meanConfidence":0.9,"lines":[],
                 "language":{"requested":"fr","used":"fr","fallback":false}}
                """,
        };

        using var handle = OcrEngineHandle.Create(native, [1], [2], null);
        using var engine = new OcrEngine(NativeOcrBackend.Create(handle), hasLanguageSets: true);

        var result = Assert.IsType<OcrResult>(engine.Recognize(
            [9],
            new OcrOptions { MinimumPageConfidence = 0.7, Deskew = true },
            "FR"));

        Assert.Equal("FR", native.LastLanguage);
        Assert.Equal(0.7, native.LastPageConfidence);
        Assert.True(native.LastDeskew);
        Assert.Equal("fr", result.Language!.Used);
    }

    [Fact]
    public void The_range_rule_becomes_an_aligned_plan_and_the_page_count_comes_from_the_document()
    {
        var native = new LanguageNative
        {
            PageCount = 3,
            Document = """
                {"pages":[
                  {"pageIndex":0,"width":10,"height":10,"dpi":300,"text":"a","meanConfidence":0.9,"lines":[],
                   "language":{"requested":"fr","used":"fr","fallback":false}},
                  {"pageIndex":1,"width":10,"height":10,"dpi":300,"text":"b","meanConfidence":0.9,"lines":[],
                   "language":{"requested":"de","used":"fr","fallback":true,"reason":"missing_language"}}]}
                """,
        };

        using var handle = OcrEngineHandle.Create(native, [1], [2], null);
        using var engine = new OcrEngine(NativeOcrBackend.Create(handle), hasLanguageSets: true);

        var pages = engine.RecognizePdf([7], languageForPage: index => index == 0 ? "fr" : index == 1 ? "de" : null);

        // Three pages were counted, so the rule was asked three times and the plan has
        // three entries — even though the document answers with two.
        Assert.Equal(["fr", "de", null], native.LastPageLanguages);
        Assert.True(native.LastPageLanguages.Count == 3);
        Assert.Equal("[\"fr\",\"de\",null]", Encoding.UTF8.GetString(native.PageLanguagesBytes));
        Assert.Equal(3u, native.LastPageCount);
        Assert.False(pages[0].Language!.FellBack);
        Assert.True(pages[1].Language!.FellBack);
        Assert.Equal("de", pages[1].Language!.Requested);
    }

    [Fact]
    public void A_bounded_range_needs_no_page_count_call()
    {
        var native = new LanguageNative { Document = """{"pages":[]}""" };

        using var handle = OcrEngineHandle.Create(native, [1], [2], null);
        using var engine = new OcrEngine(NativeOcrBackend.Create(handle), hasLanguageSets: true);

        engine.RecognizePdf([7], new OcrPdfOptions { FirstPage = 1, LastPage = 2 }, _ => "fr");

        Assert.Equal(0, native.PageCountCalls);
        Assert.Equal("[\"fr\",\"fr\"]", Encoding.UTF8.GetString(native.PageLanguagesBytes));
    }

    [Fact]
    public void A_first_page_past_the_end_is_left_for_the_native_side_to_refuse()
    {
        var native = new LanguageNative { PageCount = 2, Document = """{"pages":[]}""" };

        using var handle = OcrEngineHandle.Create(native, [1], [2], null);
        using var engine = new OcrEngine(NativeOcrBackend.Create(handle), hasLanguageSets: true);

        engine.RecognizePdf([7], new OcrPdfOptions { FirstPage = 5 }, _ => "fr");

        Assert.Equal("[]", Encoding.UTF8.GetString(native.PageLanguagesBytes));
    }

    [Fact]
    public void A_null_set_in_the_plan_is_refused_by_argument()
    {
        var error = Assert.Throws<ArgumentException>(
            () => OcrEngine.FromLanguages("fr", new OcrLanguageModelSet[] { null! }));

        Assert.Equal("modelSets", error.ParamName);
        Assert.Contains("null", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_plan_of_two_sets_and_an_alphabet_reaches_the_loader()
    {
        if (!NativeProbe.IsAvailable)
        {
            Assert.Skip(NativeProbe.SkipReason);
        }

        using var french = new ModelDirectory();
        french.WriteModelFiles();
        using var english = new ModelDirectory();
        english.WriteModelFiles();

        var sets = new[]
        {
            OcrLanguageModelSet.Load("fr", french.Path),
            OcrLanguageModelSet.Load("en", english.Path),
        };

        var exception = Assert.Throws<OcrException>(
            () => OcrEngine.FromLanguages("fr", sets, "abcdefghijklmnopqrstuvwxyz"u8.ToArray()));

        // Both entries were written into the plan and the first directory was loaded:
        // the loader refuses the zeros before any recognition, and the refusal names
        // the role and the file.
        Assert.Equal(OcrModelDefect.UnparsableGraph, exception.Defect);
        Assert.Equal(OcrModelRole.Detection, exception.Role);
    }

    [Fact]
    public void The_language_verdict_reads_as_the_set_it_used_or_the_reason_it_did_not()
    {
        var direct = new OcrLanguage("fr", "fr", false, OcrLanguageFallbackReason.Unspecified, null);
        var fallback = new OcrLanguage(
            "de", "fr", true, OcrLanguageFallbackReason.MissingLanguage, "the page asked for 'de'");
        var fallbackWithoutMessage = new OcrLanguage(
            "de", "fr", true, OcrLanguageFallbackReason.MissingLanguage, null);

        Assert.Equal("fr", direct.ToString());
        Assert.Equal("the page asked for 'de'", fallback.ToString());
        Assert.Contains("requested de", fallbackWithoutMessage.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_language_plan_reaches_the_loader_with_the_directory_it_names()
    {
        if (!NativeProbe.IsAvailable)
        {
            Assert.Skip(NativeProbe.SkipReason);
        }

        using var directory = new ModelDirectory();
        directory.WriteModelFiles();

        var set = OcrLanguageModelSet.Load("fr", directory.Path);

        Assert.Equal("fr", set.Language);
        Assert.Equal(64, set.Models.Identity.Sha256.Length);
        Assert.Contains("fr:", set.ToString(), StringComparison.Ordinal);

        var exception = Assert.Throws<OcrException>(
            () => OcrEngine.FromLanguages("fr", [set]));

        // The header is well formed and the graph inside it is zeros: the preflight
        // accepts the container, and the loader refuses it by role and by file, which
        // is proof the plan crossed with the directory the caller named.
        Assert.Equal("model", exception.Code);
        Assert.Equal(OcrModelDefect.UnparsableGraph, exception.Defect);
        Assert.Equal(OcrModelRole.Detection, exception.Role);
        Assert.EndsWith(OcrModelSet.DetectionFileName, exception.FilePath!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_default_language_without_a_set_is_refused_before_any_load()
    {
        if (!NativeProbe.IsAvailable)
        {
            Assert.Skip(NativeProbe.SkipReason);
        }

        using var directory = new ModelDirectory();
        directory.WriteModelFiles();

        var set = OcrLanguageModelSet.Load("fr", directory.Path);

        var error = Assert.Throws<ArgumentException>(
            () => OcrEngine.FromLanguages("de", [set]));

        Assert.Equal("defaultLanguage", error.ParamName);
    }

    [Fact]
    public void A_repeated_language_is_refused_before_any_load()
    {
        if (!NativeProbe.IsAvailable)
        {
            Assert.Skip(NativeProbe.SkipReason);
        }

        using var first = new ModelDirectory();
        first.WriteModelFiles();
        using var second = new ModelDirectory();
        second.WriteModelFiles();

        var sets = new[]
        {
            OcrLanguageModelSet.Load("fr", first.Path),
            OcrLanguageModelSet.Load("FR", second.Path),
        };

        var error = Assert.Throws<ArgumentException>(
            () => OcrEngine.FromLanguages("fr", sets));

        Assert.Equal("modelSets", error.ParamName);
    }

    [Fact]
    public void A_plan_with_no_sets_array_is_the_library_argument_error()
    {
        if (!NativeProbe.IsAvailable)
        {
            Assert.Skip(NativeProbe.SkipReason);
        }

        // The plan the managed side writes is what the library parses: a plan with no
        // sets array is the library's argument error, not a crash.
        var exception = Assert.Throws<OcrException>(
            () => OcrEngineHandle.CreateLanguages("""{"default":"fr"}"""u8.ToArray()));

        Assert.Equal("invalid_argument", exception.Code);
        Assert.Contains("sets", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_language_set_without_models_is_refused_by_argument()
    {
        Assert.Throws<ArgumentNullException>(() => new OcrLanguageModelSet("fr", null!));
    }

    [Fact]
    public void A_refused_page_and_an_unnamed_refusal_still_read_as_sentences()
    {
        var refusal = new OcrRefusal((OcrRefusalReason)99, 0.5, 0.25, 3);

        Assert.Contains("unnamed rule", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(refusal.Message, refusal.ToString());

        var page = new OcrPageUnreadable(0, 10, 10, 300, 0.25, refusal);

        Assert.Equal(refusal.Message, page.ToString());
    }

    /// <summary>
    /// A temporary directory holding the two model file names, with headers the
    /// container check accepts and a graph the loader refuses.
    /// </summary>
    private sealed class ModelDirectory : IDisposable
    {
        internal ModelDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "sharp-text-ocr-language-" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        internal void WriteModelFiles()
        {
            File.WriteAllBytes(System.IO.Path.Combine(Path, OcrModelSet.DetectionFileName), Header(1));
            File.WriteAllBytes(System.IO.Path.Combine(Path, OcrModelSet.RecognitionFileName), Header(9));
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // A leaked temporary directory is not a test failure.
            }
        }

        /// <summary>An `RTEN` v2 header for a 1 KB graph, padded to 8 KB.</summary>
        private static byte[] Header(byte seed)
        {
            var bytes = new byte[8192];
            Array.Fill(bytes, seed);

            Encoding.ASCII.GetBytes("RTEN").CopyTo(bytes, 0);
            BitConverter.GetBytes(2u).CopyTo(bytes, 4);
            BitConverter.GetBytes(32UL).CopyTo(bytes, 8);
            BitConverter.GetBytes(1024UL).CopyTo(bytes, 16);
            BitConverter.GetBytes(1056UL).CopyTo(bytes, 24);

            return bytes;
        }
    }
}

/// <summary>
/// An <see cref="IOcrNative"/> that answers the four language exports, with no library
/// behind it.
/// </summary>
/// <remarks>
/// It records the plan bytes exactly, which is how the alignment and the nulls of a page
/// plan are asserted without a native call: the JSON is the contract, and this is the
/// only place it can be read back verbatim.
/// </remarks>
internal sealed class LanguageNative : IOcrNative
{
    internal static readonly IntPtr HandleValue = new(0x3000);

    internal static readonly IntPtr ErrorValue = new(0x4000);

    internal string Document { get; set; } = """{"width":10,"height":10,"text":"","meanConfidence":0.0,"lines":[]}""";

    internal uint PageCount { get; set; } = 1;

    internal int Status { get; set; }

    internal int PageCountCalls { get; private set; }

    internal string? LastLanguage { get; private set; }

    internal double LastPageConfidence { get; private set; }

    internal bool LastDeskew { get; private set; }

    internal uint LastPageCount { get; private set; }

    internal IReadOnlyList<string?> LastPageLanguages { get; private set; } = [];

    internal byte[] PageLanguagesBytes { get; private set; } = [];

    public uint AbiVersion() => 2;

    public IntPtr BuildInfo() => Marshal.StringToCoTaskMemUTF8("""{"ocrs":"0.14"}""");

    public IntPtr EngineCreate(byte[] detectionModel, byte[] recognitionModel, byte[]? dictionary, out IntPtr error)
    {
        error = IntPtr.Zero;
        return HandleValue;
    }

    public IntPtr EngineCreateLanguages(byte[] plan, out IntPtr error)
    {
        error = IntPtr.Zero;
        return HandleValue;
    }

    public void EngineFree(IntPtr engine)
    {
    }

    public uint EngineLiveCount() => 1;

    public int EngineRecognize(
        IntPtr engine,
        byte[] image,
        double pageConfidence,
        double wordConfidence,
        double maxRejectedWordRatio,
        out NativeBuffer json,
        out IntPtr error)
        => throw new NotSupportedException("this test double answers only the language exports");

    public int EngineRecognizePdf(
        IntPtr engine,
        byte[] pdf,
        uint dpi,
        uint firstPage,
        uint pageCount,
        double pageConfidence,
        double wordConfidence,
        double maxRejectedWordRatio,
        out NativeBuffer json,
        out IntPtr error)
        => throw new NotSupportedException("this test double answers only the language exports");

    public int EngineRecognizeLanguage(
        IntPtr engine,
        byte[] image,
        double pageConfidence,
        double wordConfidence,
        double maxRejectedWordRatio,
        byte[] language,
        bool detectRotation,
        bool deskew,
        bool denoise,
        out NativeBuffer json,
        out IntPtr error)
    {
        LastLanguage = Encoding.UTF8.GetString(language);
        LastPageConfidence = pageConfidence;
        LastDeskew = deskew;

        return Complete(Document, out json, out error);
    }

    public int EngineRecognizePdfLanguages(
        IntPtr engine,
        byte[] pdf,
        uint dpi,
        uint firstPage,
        uint pageCount,
        double pageConfidence,
        double wordConfidence,
        double maxRejectedWordRatio,
        byte[] pageLanguages,
        bool detectRotation,
        bool deskew,
        bool denoise,
        out NativeBuffer json,
        out IntPtr error)
    {
        LastPageCount = pageCount;
        PageLanguagesBytes = pageLanguages;

        using var document = JsonDocument.Parse(pageLanguages);
        var entries = new List<string?>();
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            entries.Add(entry.ValueKind == JsonValueKind.Null ? null : entry.GetString());
        }

        LastPageLanguages = entries;

        return Complete(Document, out json, out error);
    }

    public int PdfPageCount(byte[] pdf, out NativeBuffer json, out IntPtr error)
    {
        PageCountCalls++;

        return Complete($"{{\"pages\":{PageCount}}}", out json, out error);
    }

    public int EngineModels(IntPtr engine, out NativeBuffer json, out IntPtr error)
        => throw new NotSupportedException("this test double answers only the language exports");

    public NativeBuffer ErrorJson(IntPtr error)
        => Buffer(Encoding.UTF8.GetBytes("""{"code":"unknown","message":"the call failed"}"""));

    public void ErrorFree(IntPtr error)
    {
    }

    public void BufferFree(NativeBuffer buffer)
    {
        if (buffer.Pointer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(buffer.Pointer);
        }
    }

    private int Complete(string document, out NativeBuffer json, out IntPtr error)
    {
        json = Buffer(Encoding.UTF8.GetBytes(document));
        error = Status == 0 ? IntPtr.Zero : ErrorValue;

        return Status;
    }

    private static NativeBuffer Buffer(byte[] bytes)
    {
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);

        return new NativeBuffer { Pointer = pointer, Length = (nuint)bytes.Length };
    }
}
