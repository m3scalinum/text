using System;
using System.IO;
using System.Linq;
using M3scalinum.Text.Ocr.Internal;
using Xunit;

namespace M3scalinum.Text.Ocr.Tests;

/// <summary>
/// The managed half of model management: the identity in a result, the preflight
/// of a directory, and the mapping of a native refusal to a typed exception.
/// </summary>
/// <remarks>
/// These run without a model and without the native library: the documents are the
/// ones <c>native/text-ocr</c> writes, and the double answers the ABI. The tests
/// that need the real library to refuse a real file are in
/// <see cref="NativeModelTests"/>, and they skip when it cannot be loaded.
/// </remarks>
public sealed class ModelIdentityTests
{
    private static string ResultDocument(string models) => $$"""
        {"width":100,"height":50,"status":"recognized","text":"INVOICE",
        "meanConfidence":0.9,"lines":[],"preprocess":null{{models}}}
        """;

    [Fact]
    public void A_model_set_preflight_returns_both_fingerprints_and_the_pair_digest()
    {
        var native = new FakeModelNative();
        var directory = Path.Combine(Path.GetTempPath(), "models-preflight");

        var set = OcrModelSet.LoadFromDirectory(native, directory);

        Assert.Equal(directory, set.Directory);
        Assert.Equal(Path.Combine(directory, "detection.rten"), set.DetectionPath);
        Assert.Equal(Path.Combine(directory, "recognition.rten"), set.RecognitionPath);

        Assert.Equal(OcrModelRole.Detection, set.Identity.Detection.Role);
        Assert.Equal(8192, set.Identity.Detection.Length);
        Assert.Equal("1111111111111111111111111111111111111111111111111111111111111111",
            set.Identity.Detection.Sha256);
        Assert.Equal(OcrModelRole.Recognition, set.Identity.Recognition.Role);
        Assert.Equal("3333333333333333333333333333333333333333333333333333333333333333",
            set.Identity.Sha256);
        Assert.Contains("detection 1111", set.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_result_document_carries_the_models_that_produced_it()
    {
        var document = ResultDocument(@",""models"":{""detection"":{""role"":""detection"",""format"":""rten"",""length"":8192,""sha256"":""aaaa""},""recognition"":{""role"":""recognition"",""format"":""rten"",""length"":16384,""sha256"":""bbbb""},""set"":""cccc""}");

        var result = Assert.IsType<OcrResult>(ResultParser.ParseImage(System.Text.Encoding.UTF8.GetBytes(document)));

        Assert.NotNull(result.Models);
        Assert.Equal("aaaa", result.Models!.Detection.Sha256);
        Assert.Equal("bbbb", result.Models.Recognition.Sha256);
        Assert.Equal("cccc", result.Models.Sha256);
        Assert.Equal(OcrModelRole.Detection, result.Models.Detection.Role);
    }

    [Fact]
    public void A_result_document_without_a_models_object_reports_no_identity()
    {
        var document = ResultDocument(string.Empty);

        var result = Assert.IsType<OcrResult>(ResultParser.ParseImage(System.Text.Encoding.UTF8.GetBytes(document)));

        Assert.Null(result.Models);
    }

    [Fact]
    public void A_refused_page_also_carries_the_models_that_refused_it()
    {
        var document = """
            {"width":100,"height":50,"status":"unreadable","text":"","meanConfidence":0.1,
            "wordCount":0,"rejectedWordCount":0,"lines":[],
            "refusal":{"reason":"blank_page","threshold":0.6,"observed":0.0,"wordCount":0,"message":"blank"},
            "models":{"detection":{"role":"detection","format":"rten","length":8192,"sha256":"aaaa"},
            "recognition":{"role":"recognition","format":"rten","length":16384,"sha256":"bbbb"},
            "set":"cccc"}}
            """;

        var result = Assert.IsType<OcrUnreadable>(
            ResultParser.ParseImage(System.Text.Encoding.UTF8.GetBytes(document)));

        Assert.Equal("cccc", result.Models!.Sha256);
    }

    [Fact]
    public void Every_page_of_a_document_carries_the_identity_of_the_root()
    {
        const string pageDocuments = """
            [
            {"pageIndex":0,"width":100,"height":50,"status":"recognized","text":"a","meanConfidence":0.9,"lines":[],"dpi":300},
            {"pageIndex":1,"width":100,"height":50,"status":"unreadable","text":"","meanConfidence":0.1,
            "wordCount":0,"rejectedWordCount":0,"lines":[],"dpi":300,
            "refusal":{"reason":"blank_page","threshold":0.6,"observed":0.0,"wordCount":0,"message":"blank"}}
            ]
            """;

        var document = "{\"pages\":" + pageDocuments + ",\"models\":" + IdentityDocument + "}";

        var pages = ResultParser.ParsePages(System.Text.Encoding.UTF8.GetBytes(document));

        Assert.Equal(2, pages.Count);
        Assert.All(pages, page => Assert.Equal("3333333333333333333333333333333333333333333333333333333333333333", page.Models!.Sha256));
    }

    private const string IdentityDocument = """
        {"detection":{"role":"detection","format":"rten","length":8192,
        "sha256":"1111111111111111111111111111111111111111111111111111111111111111"},
        "recognition":{"role":"recognition","format":"rten","length":16384,
        "sha256":"2222222222222222222222222222222222222222222222222222222222222222"},
        "set":"3333333333333333333333333333333333333333333333333333333333333333"}
        """;

    [Theory]
    [InlineData("empty", OcrModelDefect.Empty)]
    [InlineData("too_small", OcrModelDefect.TooSmall)]
    [InlineData("too_large", OcrModelDefect.TooLarge)]
    [InlineData("unrecognized_format", OcrModelDefect.UnrecognizedFormat)]
    [InlineData("truncated_header", OcrModelDefect.TruncatedHeader)]
    [InlineData("unsupported_version", OcrModelDefect.UnsupportedVersion)]
    [InlineData("inconsistent_layout", OcrModelDefect.InconsistentLayout)]
    [InlineData("unparsable_graph", OcrModelDefect.UnparsableGraph)]
    [InlineData("input_shape", OcrModelDefect.InputShape)]
    [InlineData("missing_file", OcrModelDefect.MissingFile)]
    [InlineData("missing_directory", OcrModelDefect.MissingDirectory)]
    [InlineData("not_a_directory", OcrModelDefect.NotADirectory)]
    [InlineData("unreadable_file", OcrModelDefect.UnreadableFile)]
    public void Every_defect_the_native_library_names_maps_to_its_enum(string name, OcrModelDefect expected)
    {
        var native = new FakeNativeLibrary
        {
            ErrorDocumentOverride =
                $$"""{"code":"model","defect":"{{name}}","role":"recognition","file":"/models/recognition.rten","message":"refused"}""",
        };

        var exception = Assert.IsType<OcrException>(
            NativeOcr.ToException(native, FakeNativeLibrary.ErrorValue));

        Assert.Equal(expected, exception.Defect);
        Assert.Equal(OcrModelRole.Recognition, exception.Role);
        Assert.Equal("/models/recognition.rten", exception.FilePath);
        Assert.Equal("model", exception.Code);
        Assert.Equal(1, native.ErrorFreeCalls);
    }

    [Fact]
    public void A_defect_name_this_package_does_not_know_still_names_a_model_refusal()
    {
        var native = new FakeNativeLibrary
        {
            ErrorDocumentOverride = """{"code":"model","defect":"future_defect","message":"refused"}""",
        };

        var exception = Assert.IsType<OcrException>(
            NativeOcr.ToException(native, FakeNativeLibrary.ErrorValue));

        Assert.Equal(OcrModelDefect.Unspecified, exception.Defect);
        Assert.Equal("refused", exception.Message);
    }

    [Fact]
    public void An_error_without_a_defect_stays_a_plain_ocr_exception()
    {
        var native = new FakeNativeLibrary
        {
            ErrorDocumentOverride = """{"code":"ocr","message":"the page could not be read"}""",
        };

        var exception = NativeOcr.ToException(native, FakeNativeLibrary.ErrorValue);

        Assert.Equal("ocr", exception.Code);
        Assert.Equal(OcrModelDefect.Unspecified, exception.Defect);
        Assert.Null(exception.FilePath);
    }

    [Fact]
    public void A_missing_file_arrives_with_the_path_the_native_side_looked_for()
    {
        var native = new FakeModelNative { ModelSetStatus = 4 };
        native.UseError("""
            {"code":"model","defect":"missing_file","role":"detection",
            "file":"/opt/models/detection.rten",
            "message":"detection model (/opt/models/detection.rten) is missing: expected the file /opt/models/detection.rten"}
            """);

        var exception = Assert.Throws<OcrException>(
            () => NativeOcr.InspectModelSet(native, "/opt/models"));

        Assert.Equal(OcrModelDefect.MissingFile, exception.Defect);
        Assert.Equal(OcrModelRole.Detection, exception.Role);
        Assert.Equal("/opt/models/detection.rten", exception.FilePath);
        Assert.Contains("detection.rten", exception.Message, StringComparison.Ordinal);
        Assert.Contains("missing_file", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_engine_reports_the_identity_the_native_side_recorded_for_its_handle()
    {
        var native = new FakeModelNative();

        using var handle = OcrEngineHandle.CreateFromDirectory(native, "/opt/models");
        var backend = NativeOcrBackend.Create(handle);
        using var engine = new OcrEngine(backend);

        var models = engine.Models;

        Assert.Equal(FakeNativeLibrary.HandleValue, native.LastModelsHandle);
        Assert.Equal("3333333333333333333333333333333333333333333333333333333333333333",
            models!.Sha256);
        Assert.Equal(OcrModelRole.Detection, models.Detection.Role);

        // Read once: the getter caches, so a second read must not call the ABI again.
        Assert.Same(models, engine.Models);
    }

    [Fact]
    public void A_directory_creation_that_returns_no_handle_throws_the_typed_refusal()
    {
        var native = new FakeModelNative { DirectoryHandle = IntPtr.Zero };
        native.UseError("""
            {"code":"model","defect":"missing_directory","message":"model is not a directory that exists: /nowhere"}
            """);

        var exception = Assert.Throws<OcrException>(
            () => OcrEngineHandle.CreateFromDirectory(native, "/nowhere"));

        Assert.Equal(OcrModelDefect.MissingDirectory, exception.Defect);
    }

    [Fact]
    public void A_directory_engine_passes_the_directory_through_as_utf8_bytes()
    {
        var native = new FakeModelNative();

        using var handle = OcrEngineHandle.CreateFromDirectory(native, "/opt/modèles");

        Assert.Equal("/opt/modèles", System.Text.Encoding.UTF8.GetString(native.LastDirectory!));
    }

    [Fact]
    public void A_backend_that_does_not_know_its_weights_reports_no_identity()
    {
        using var engine = new OcrEngine(new FakeOcrBackend());

        Assert.Null(engine.Models);
    }

    [Fact]
    public void FromDirectory_refuses_an_empty_path_before_calling_out()
    {
        Assert.Throws<ArgumentNullException>(() => OcrEngine.FromDirectory(null!));
        Assert.Throws<ArgumentException>(() => OcrEngine.FromDirectory(string.Empty));
        Assert.Throws<ArgumentNullException>(() => OcrModelSet.LoadFromDirectory(null!));
        Assert.Throws<ArgumentException>(() => OcrModelSet.LoadFromDirectory(string.Empty));
    }
}
