using System;
using System.Linq;
using System.Text;
using M3scalinum.Text.Ocr.Internal;
using Xunit;

namespace M3scalinum.Text.Ocr.Tests;

/// <summary>
/// The layers between the ABI and the public surface: handle creation, the status and
/// error translation, the out-buffer lifetime, the document mapping and disposal.
/// </summary>
/// <remarks>
/// These run against <see cref="FakeNativeLibrary"/>, so they run everywhere. They are
/// the coverage the model-backed tests used to be the only source of, and unlike those
/// they assert the <em>failure</em> paths, which a real engine can only reach by
/// accident.
/// </remarks>
public sealed class NativeBackendTests
{
    private const string ImageDocument =
        """
        {"width":100,"height":50,"text":"hello world","meanConfidence":0.9,
         "lines":[{"text":"hello world","bounds":{"x":1,"y":2,"width":60,"height":10},"confidence":0.9,
                   "words":[{"text":"hello","bounds":{"x":1,"y":2,"width":25,"height":10},"confidence":0.91},
                            {"text":"world","bounds":{"x":32,"y":2,"width":29,"height":10},"confidence":0.89}]}]}
        """;

    private const string PagesDocument =
        """
        {"pages":[
          {"pageIndex":0,"width":100,"height":50,"dpi":150,"text":"one","meanConfidence":0.8,
           "lines":[{"text":"one","bounds":{"x":0,"y":0,"width":30,"height":8},"confidence":0.8,
                     "words":[{"text":"one","bounds":{"x":0,"y":0,"width":30,"height":8},"confidence":0.8}]}]},
          {"pageIndex":1,"width":100,"height":50,"dpi":150,"text":"two","meanConfidence":0.7,
           "lines":[{"text":"two","bounds":{"x":0,"y":0,"width":30,"height":8},"confidence":0.7,
                     "words":[{"text":"two","bounds":{"x":0,"y":0,"width":30,"height":8},"confidence":0.7}]}]}]}
        """;

    [Fact]
    public void Create_hands_the_model_bytes_to_the_library_and_keeps_the_handle_it_gets_back()
    {
        var library = new FakeNativeLibrary();

        using var handle = OcrEngineHandle.Create(library, [1, 2], [3, 4], [5]);

        Assert.Equal(1, library.CreateCalls);
        Assert.Equal([1, 2], library.LastDetectionModel);
        Assert.Equal([3, 4], library.LastRecognitionModel);
        Assert.Equal([5], library.LastDictionary);

        var document = handle.Recognize([9], new OcrOptions());

        Assert.Equal(FakeNativeLibrary.HandleValue, library.LastEngine);
        Assert.Equal(1, library.RecognizeCalls);
        Assert.Equal([9], library.LastImage);
        Assert.Equal(Encoding.UTF8.GetString(library.Document), Encoding.UTF8.GetString(document));
    }

    [Fact]
    public void Create_passes_a_null_dictionary_as_no_bytes()
    {
        var library = new FakeNativeLibrary();

        using var handle = OcrEngineHandle.Create(library, [1], [2], null);

        Assert.Null(library.LastDictionary);
    }

    [Fact]
    public void Create_throws_the_native_error_when_the_library_returns_no_handle()
    {
        var library = new FakeNativeLibrary
        {
            CreatedHandle = IntPtr.Zero,
            ErrorAlongsideHandle = true,
            ErrorCode = "model",
            ErrorMessage = "the detection model is not a model",
        };

        var error = Assert.Throws<OcrException>(() => OcrEngineHandle.Create(library, [1], [2], null));

        Assert.Equal("model", error.Code);
        Assert.Equal("the detection model is not a model", error.Message);
        Assert.Equal(1, library.ErrorFreeCalls);
    }

    [Fact]
    public void Create_reports_an_unknown_code_when_the_library_returns_no_handle_and_no_error()
    {
        var library = new FakeNativeLibrary { CreatedHandle = IntPtr.Zero };

        var error = Assert.Throws<OcrException>(() => OcrEngineHandle.Create(library, [1], [2], null));

        Assert.Equal("unknown", error.Code);
        Assert.Contains("without an error object", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_releases_an_error_that_arrives_next_to_a_usable_handle()
    {
        var library = new FakeNativeLibrary { ErrorAlongsideHandle = true, ErrorCode = "panic" };

        using var handle = OcrEngineHandle.Create(library, [1], [2], null);

        Assert.Equal(1, library.ErrorFreeCalls);
        Assert.NotEmpty(handle.Recognize([1], new OcrOptions()));
    }

    [Fact]
    public void Recognize_returns_the_result_document_and_frees_the_buffer()
    {
        var library = new FakeNativeLibrary { Document = Encoding.UTF8.GetBytes(ImageDocument) };
        using var handle = OcrEngineHandle.Create(library, [1], [2], null);

        var document = handle.Recognize([1, 2, 3], new OcrOptions());

        Assert.Equal(ImageDocument, Encoding.UTF8.GetString(document));
        Assert.Equal(1, library.BufferFreeCalls);
    }

    [Fact]
    public void Recognize_of_an_empty_document_reads_as_no_bytes()
    {
        var library = new FakeNativeLibrary { Document = null! };
        using var handle = OcrEngineHandle.Create(library, [1], [2], null);

        var document = handle.Recognize([1], new OcrOptions());

        Assert.Empty(document);
    }

    [Fact]
    public void Recognize_frees_the_document_buffer_when_the_call_fails()
    {
        var library = new FakeNativeLibrary
        {
            Status = 5,
            DocumentOnFailure = [1, 2, 3],
            ErrorCode = "image",
            ErrorMessage = "the image could not be decoded",
        };
        using var handle = OcrEngineHandle.Create(library, [1], [2], null);

        var error = Assert.Throws<OcrException>(() => handle.Recognize([1], new OcrOptions()));

        Assert.Equal("image", error.Code);
        Assert.Equal(2, library.BufferFreeCalls);
        Assert.Equal(1, library.ErrorFreeCalls);
    }

    [Fact]
    public void Recognize_throws_the_error_when_no_document_came_back_with_the_failure()
    {
        var library = new FakeNativeLibrary { Status = 6, ErrorCode = "pdf" };
        using var handle = OcrEngineHandle.Create(library, [1], [2], null);

        var error = Assert.Throws<OcrException>(() => handle.Recognize([1], new OcrOptions()));

        Assert.Equal("pdf", error.Code);
        Assert.Equal(1, library.BufferFreeCalls);
    }

    [Theory]
    [InlineData(1, "invalid_argument")]
    [InlineData(2, "invalid_handle")]
    [InlineData(3, "panic")]
    [InlineData(4, "model")]
    [InlineData(5, "image")]
    [InlineData(6, "pdf")]
    [InlineData(7, "ocr")]
    public void Every_native_status_reaches_the_exception_with_the_code_it_names(int status, string code)
    {
        var library = new FakeNativeLibrary { Status = status, ErrorCode = code };
        using var handle = OcrEngineHandle.Create(library, [1], [2], null);

        var fromImage = Assert.Throws<OcrException>(() => handle.Recognize([1], new OcrOptions()));
        var fromPdf = Assert.Throws<OcrException>(() => handle.RecognizePdf([1], new OcrPdfOptions(), 0, 0));

        Assert.Equal(code, fromImage.Code);
        Assert.Equal(code, fromPdf.Code);
    }

    [Fact]
    public void RecognizePdf_asks_for_the_page_after_the_last_one_requested()
    {
        var library = new FakeNativeLibrary { Document = Encoding.UTF8.GetBytes(PagesDocument) };
        using var handle = OcrEngineHandle.Create(library, [1], [2], null);

        handle.RecognizePdf([1, 2], new OcrPdfOptions { Dpi = 150 }, 2, 4);

        Assert.Equal(150u, library.LastDpi);
        Assert.Equal(2u, library.LastFirstPage);
        Assert.Equal(4u, library.LastPageCount);
    }

    [Fact]
    public void Dispose_frees_a_handle_once_and_twice_is_harmless()
    {
        var library = new FakeNativeLibrary();

        var handle = OcrEngineHandle.Create(library, [1], [2], null);
        handle.Dispose();
        handle.Dispose();

        Assert.Equal(1, library.FreeCalls);
        Assert.Equal(FakeNativeLibrary.HandleValue, library.LastEngine);
    }

    [Fact]
    public void A_disposed_handle_refuses_to_recognize()
    {
        var library = new FakeNativeLibrary();
        var handle = OcrEngineHandle.Create(library, [1], [2], null);

        handle.Dispose();

        Assert.Throws<ObjectDisposedException>(() => handle.Recognize([1], new OcrOptions()));
        Assert.Throws<ObjectDisposedException>(() => handle.RecognizePdf([1], new OcrPdfOptions(), 0, 0));
    }

    [Fact]
    public void The_backend_maps_the_image_document_onto_the_result()
    {
        var library = new FakeNativeLibrary { Document = Encoding.UTF8.GetBytes(ImageDocument) };
        using var engine = new OcrEngine(NativeOcrBackend.Create(OcrEngineHandle.Create(library, [1], [2], null)));

        var result = Assert.IsType<OcrResult>(engine.Recognize([1, 2, 3]));

        Assert.Equal("hello world", result.Text);
        Assert.Equal(100, result.Width);
        Assert.Equal(50, result.Height);
        Assert.Equal(0.9, result.MeanConfidence);

        var line = Assert.Single(result.Lines);
        Assert.Equal([1, 2, 60, 10], [line.Bounds.X, line.Bounds.Y, line.Bounds.Width, line.Bounds.Height]);
        Assert.Equal(["hello", "world"], line.Words.Select(word => word.Text));
        Assert.Equal(0.89, line.Words[1].Confidence);
    }

    [Fact]
    public void The_backend_asks_for_every_page_to_the_end_when_no_last_page_is_given()
    {
        var library = new FakeNativeLibrary { Document = Encoding.UTF8.GetBytes(PagesDocument) };
        using var engine = new OcrEngine(NativeOcrBackend.Create(OcrEngineHandle.Create(library, [1], [2], null)));

        var pages = engine.RecognizePdf([1], new OcrPdfOptions { Dpi = 150, FirstPage = 3 });

        Assert.Equal(3u, library.LastFirstPage);
        Assert.Equal(0u, library.LastPageCount);
        Assert.Equal([0, 1], pages.Select(page => page.PageIndex));
        Assert.Equal("one", Assert.IsType<OcrPageResult>(pages[0]).Text);
    }

    [Fact]
    public void The_backend_counts_the_pages_of_a_closed_range()
    {
        var library = new FakeNativeLibrary { Document = Encoding.UTF8.GetBytes(PagesDocument) };
        using var engine = new OcrEngine(NativeOcrBackend.Create(OcrEngineHandle.Create(library, [1], [2], null)));

        engine.RecognizePdf([1], new OcrPdfOptions { Dpi = 150, FirstPage = 2, LastPage = 5 });

        Assert.Equal(2u, library.LastFirstPage);
        Assert.Equal(4u, library.LastPageCount);
    }

    [Fact]
    public void Disposing_the_backend_releases_the_handle()
    {
        var library = new FakeNativeLibrary();
        var backend = NativeOcrBackend.Create(OcrEngineHandle.Create(library, [1], [2], null));

        backend.Dispose();
        backend.Dispose();

        Assert.Equal(1, library.FreeCalls);
    }

    [Fact]
    public void An_error_object_without_a_code_or_a_message_falls_back_to_the_generic_wording()
    {
        var library = new FakeNativeLibrary
        {
            ErrorDocumentOverride = """{"code":null,"message":null,"traceback":""}""",
        };

        var error = NativeOcr.ToException(library, FakeNativeLibrary.ErrorValue);

        Assert.Equal("unknown", error.Code);
        Assert.Equal("the native library failed without an error object", error.Message);
    }

    [Fact]
    public void No_error_object_at_all_reads_as_unknown()
    {
        var error = NativeOcr.ToException(new FakeNativeLibrary(), IntPtr.Zero);

        Assert.Equal("unknown", error.Code);
        Assert.Equal("the native library failed without an error object", error.Message);
    }

    [Fact]
    public void BuildInfo_reads_as_empty_when_the_library_cannot_be_loaded()
    {
        var library = new FakeNativeLibrary { BuildInfoUnavailable = true };

        Assert.Equal(string.Empty, NativeOcr.BuildInfo(library));
    }

    [Fact]
    public void BuildInfo_reads_the_document_the_library_holds()
    {
        var library = new FakeNativeLibrary { BuildInfoJson = """{"ocrs":"0.14"}""" };

        Assert.Equal("""{"ocrs":"0.14"}""", NativeOcr.BuildInfo(library));
    }
}
