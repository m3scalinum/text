using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace M3scalinum.Text.Ocr.Internal;

/// <summary>
/// The managed face of the native library: the probe, the error objects, and the
/// out-buffer lifecycle. Every call into Rust goes through here.
/// </summary>
internal static class NativeOcr
{
    /// <summary>
    /// The one implementation a consumer gets: the P/Invoke declarations.
    /// </summary>
    internal static IOcrNative Library { get; } = new NativeLibraryOcr();

    /// <summary>
    /// Installs the library resolver once, before any P/Invoke runs, so an override
    /// path or the <c>runtimes/&lt;rid&gt;/native/</c> layout is found without a probe
    /// call. <see cref="OcrEngineHandle"/>'s static constructor is the single caller,
    /// which is why every entry point reaches the resolver first.
    /// </summary>
    internal static void Initialize() => NativeLibraryResolver.Initialize();

    /// <summary>The handle count the native library keeps, for leak tests.</summary>
    internal static uint LiveHandleCount() => Library.EngineLiveCount();

    internal static string BuildInfo() => BuildInfo(Library);

    /// <summary>
    /// The build information, or an empty string when the library cannot be loaded:
    /// a host that asks what the package carries must get an answer, not a crash.
    /// </summary>
    internal static string BuildInfo(IOcrNative library)
    {
        try
        {
            return NativeMethods.ReadUtf8(library.BuildInfo());
        }
        catch (DllNotFoundException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Turns an error object into an exception and frees it.
    /// </summary>
    internal static OcrException ToException(IntPtr error) => ToException(Library, error);

    /// <summary>
    /// Turns an error object into an exception and frees it.
    /// </summary>
    /// <remarks>
    /// The status code returned alongside the error is not the contract: it is a
    /// convenience for a C host that has no JSON reader. The <c>code</c> inside the
    /// error document is what <see cref="OcrException.Code"/> reports, so the two
    /// halves can gain a status without the managed side learning a status table.
    /// </remarks>
    internal static OcrException ToException(IOcrNative library, IntPtr error)
    {
        var code = "unknown";
        var message = "the native library failed without an error object";
        var defect = OcrModelDefect.Unspecified;
        var role = OcrModelRole.Unspecified;
        var namedDefect = false;
        string? file = null;

        if (error != IntPtr.Zero)
        {
            try
            {
                var buffer = library.ErrorJson(error);
                try
                {
                    var text = Encoding.UTF8.GetString(buffer.ToArray());
                    using var document = JsonDocument.Parse(text);
                    var root = document.RootElement;

                    if (root.TryGetProperty("code", out var codeElement))
                    {
                        code = codeElement.GetString() ?? code;
                    }

                    if (root.TryGetProperty("message", out var messageElement))
                    {
                        message = messageElement.GetString() ?? message;
                    }

                    if (root.TryGetProperty("defect", out var defectElement)
                        && defectElement.ValueKind == JsonValueKind.String)
                    {
                        namedDefect = true;
                        defect = ParseDefect(defectElement.GetString());
                    }

                    if (root.TryGetProperty("role", out var roleElement)
                        && roleElement.ValueKind == JsonValueKind.String)
                    {
                        role = roleElement.GetString() switch
                        {
                            "detection" => OcrModelRole.Detection,
                            "recognition" => OcrModelRole.Recognition,
                            _ => OcrModelRole.Unspecified,
                        };
                    }

                    if (root.TryGetProperty("file", out var fileElement)
                        && fileElement.ValueKind == JsonValueKind.String)
                    {
                        file = fileElement.GetString();
                    }
                }
                finally
                {
                    library.BufferFree(buffer);
                }
            }
            finally
            {
                library.ErrorFree(error);
            }
        }

        // A model refusal is its own type: the caller can switch on what is wrong
        // and on which file, without reading the sentence. A defect name this
        // package does not know still means "a model was refused": it reports
        // Unspecified rather than degrading to a plain exception.
        return namedDefect
            ? new OcrException(code, message, role, defect, file)
            : new OcrException(code, message);
    }

    /// <summary>
    /// The defect name the native library reported, as the enum this package exposes.
    /// </summary>
    /// <remarks>
    /// An unknown name becomes <see cref="OcrModelDefect.Unspecified"/> rather than
    /// an exception: a library newer than this package still refuses the file, and
    /// the message still says why.
    /// </remarks>
    private static OcrModelDefect ParseDefect(string? name) => name switch
    {
        "empty" => OcrModelDefect.Empty,
        "too_small" => OcrModelDefect.TooSmall,
        "too_large" => OcrModelDefect.TooLarge,
        "unrecognized_format" => OcrModelDefect.UnrecognizedFormat,
        "truncated_header" => OcrModelDefect.TruncatedHeader,
        "unsupported_version" => OcrModelDefect.UnsupportedVersion,
        "inconsistent_layout" => OcrModelDefect.InconsistentLayout,
        "unparsable_graph" => OcrModelDefect.UnparsableGraph,
        "input_shape" => OcrModelDefect.InputShape,
        "missing_file" => OcrModelDefect.MissingFile,
        "missing_directory" => OcrModelDefect.MissingDirectory,
        "not_a_directory" => OcrModelDefect.NotADirectory,
        "unreadable_file" => OcrModelDefect.UnreadableFile,
        _ => OcrModelDefect.Unspecified,
    };

    /// <summary>
    /// Takes an out-buffer on success, or throws the typed exception the error
    /// object carries on failure. The buffer is always freed.
    /// </summary>
    internal static byte[] Take(IOcrNative library, int status, NativeBuffer buffer, IntPtr error)
    {
        if (status != 0)
        {
            if (buffer.Pointer != IntPtr.Zero)
            {
                library.BufferFree(buffer);
            }

            throw ToException(library, error);
        }

        try
        {
            return buffer.ToArray();
        }
        finally
        {
            library.BufferFree(buffer);
        }
    }

    /// <summary>
    /// Validates and identifies the models of a directory, without loading them.
    /// </summary>
    internal static (string Directory, OcrModelIdentity Identity) InspectModelSet(string directory)
        => InspectModelSet(Library, directory);

    internal static (string Directory, OcrModelIdentity Identity) InspectModelSet(
        IOcrNative library, string directory)
    {
        var utf8 = Encoding.UTF8.GetBytes(directory);
        var status = library.ModelSetInspect(utf8, out var json, out var error);

        return ResultParser.ParseModelSet(Take(library, status, json, error));
    }

    /// <summary>
    /// The per-page plan the native side parses: a JSON array with one entry per page
    /// of the range, each a language tag or <c>null</c> for a page that names none.
    /// </summary>
    internal static byte[] PageLanguagesJson(IReadOnlyList<string?> languages)
    {
        var builder = new StringBuilder("[");

        for (var index = 0; index < languages.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            var language = languages[index];

            if (language is null)
            {
                builder.Append("null");
            }
            else
            {
                builder.Append('"').Append(JsonEncodedText.Encode(language).ToString()).Append('"');
            }
        }

        builder.Append(']');

        return Encoding.UTF8.GetBytes(builder.ToString());
    }
}

/// <summary>
/// The <see cref="IOcrNative"/> that talks to the Rust library. Every method here only
/// forwards: pinning and the out-parameters stay on this side of the interface, so
/// <see cref="OcrEngineHandle"/> holds no pointers and can be driven by a double.
/// </summary>
internal sealed unsafe class NativeLibraryOcr : IOcrNative
{
    /// <inheritdoc />
    public uint AbiVersion() => NativeMethods.AbiVersion();

    /// <inheritdoc />
    public IntPtr BuildInfo() => NativeMethods.BuildInfo();

    /// <inheritdoc />
    public IntPtr EngineCreate(
        byte[] detectionModel,
        byte[] recognitionModel,
        byte[]? dictionary,
        out IntPtr error)
    {
        fixed (byte* detection = detectionModel)
        fixed (byte* recognition = recognitionModel)
        fixed (byte* alphabet = dictionary)
        {
            return NativeMethods.EngineCreate(
                detection, (nuint)detectionModel.Length,
                recognition, (nuint)recognitionModel.Length,
                alphabet, (nuint)(dictionary?.Length ?? 0),
                out error);
        }
    }

    /// <inheritdoc />
    public void EngineFree(IntPtr engine) => NativeMethods.EngineFree(engine);

    /// <inheritdoc />
    public uint EngineLiveCount() => NativeMethods.EngineLiveCount();

    /// <inheritdoc />
    public int EngineRecognize(
        IntPtr engine,
        byte[] image,
        double pageConfidence,
        double wordConfidence,
        double maxRejectedWordRatio,
        out NativeBuffer json,
        out IntPtr error)
    {
        fixed (byte* pixels = image)
        {
            return NativeMethods.EngineRecognize(
                engine, pixels, (nuint)image.Length,
                pageConfidence, wordConfidence, maxRejectedWordRatio,
                out json, out error);
        }
    }

    /// <inheritdoc />
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
    {
        fixed (byte* bytes = pdf)
        {
            return NativeMethods.EngineRecognizePdf(
                engine, bytes, (nuint)pdf.Length, dpi, firstPage, pageCount,
                pageConfidence, wordConfidence, maxRejectedWordRatio,
                out json, out error);
        }
    }

    /// <inheritdoc />
    public int EngineRecognizePreprocessed(
        IntPtr engine,
        byte[] image,
        double pageConfidence,
        double wordConfidence,
        double maxRejectedWordRatio,
        bool detectRotation,
        bool deskew,
        bool denoise,
        out NativeBuffer json,
        out IntPtr error)
    {
        fixed (byte* pixels = image)
        {
            return NativeMethods.EngineRecognizePreprocessed(
                engine, pixels, (nuint)image.Length,
                pageConfidence, wordConfidence, maxRejectedWordRatio,
                detectRotation ? 1 : 0, deskew ? 1 : 0, denoise ? 1 : 0,
                out json, out error);
        }
    }

    /// <inheritdoc />
    public int EngineRecognizePdfPreprocessed(
        IntPtr engine,
        byte[] pdf,
        uint dpi,
        uint firstPage,
        uint pageCount,
        double pageConfidence,
        double wordConfidence,
        double maxRejectedWordRatio,
        bool detectRotation,
        bool deskew,
        bool denoise,
        out NativeBuffer json,
        out IntPtr error)
    {
        fixed (byte* bytes = pdf)
        {
            return NativeMethods.EngineRecognizePdfPreprocessed(
                engine, bytes, (nuint)pdf.Length, dpi, firstPage, pageCount,
                pageConfidence, wordConfidence, maxRejectedWordRatio,
                detectRotation ? 1 : 0, deskew ? 1 : 0, denoise ? 1 : 0,
                out json, out error);
        }
    }

    /// <inheritdoc />
    public NativeBuffer ErrorJson(IntPtr error) => NativeMethods.ErrorJson(error);

    /// <inheritdoc />
    public void ErrorFree(IntPtr error) => NativeMethods.ErrorFree(error);

    /// <inheritdoc />
    public void BufferFree(NativeBuffer buffer) => NativeMethods.BufferFree(buffer);

    /// <inheritdoc />
    public int ModelInspect(byte[] model, int kind, out NativeBuffer json, out IntPtr error)
    {
        fixed (byte* bytes = model)
        {
            return NativeMethods.ModelInspect(bytes, (nuint)model.Length, kind, out json, out error);
        }
    }

    /// <inheritdoc />
    public int ModelSetInspect(byte[] directory, out NativeBuffer json, out IntPtr error)
    {
        fixed (byte* path = directory)
        {
            return NativeMethods.ModelSetInspect(path, (nuint)directory.Length, out json, out error);
        }
    }

    /// <inheritdoc />
    public IntPtr EngineCreateFromDirectory(byte[] directory, byte[]? dictionary, out IntPtr error)
    {
        fixed (byte* path = directory)
        fixed (byte* alphabet = dictionary)
        {
            return NativeMethods.EngineCreateFromDirectory(
                path,
                (nuint)directory.Length,
                alphabet,
                (nuint)(dictionary?.Length ?? 0),
                out error);
        }
    }

    /// <inheritdoc />
    public int EngineModels(IntPtr engine, out NativeBuffer json, out IntPtr error)
        => NativeMethods.EngineModels(engine, out json, out error);

    /// <inheritdoc />
    public IntPtr EngineCreateLanguages(byte[] plan, out IntPtr error)
    {
        fixed (byte* bytes = plan)
        {
            return NativeMethods.EngineCreateLanguages(bytes, (nuint)plan.Length, out error);
        }
    }

    /// <inheritdoc />
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
        fixed (byte* pixels = image)
        fixed (byte* tag = language)
        {
            return NativeMethods.EngineRecognizeLanguage(
                engine, pixels, (nuint)image.Length,
                pageConfidence, wordConfidence, maxRejectedWordRatio,
                tag, (nuint)language.Length,
                detectRotation ? 1 : 0, deskew ? 1 : 0, denoise ? 1 : 0,
                out json, out error);
        }
    }

    /// <inheritdoc />
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
        fixed (byte* bytes = pdf)
        fixed (byte* plan = pageLanguages)
        {
            return NativeMethods.EngineRecognizePdfLanguages(
                engine, bytes, (nuint)pdf.Length, dpi, firstPage, pageCount,
                pageConfidence, wordConfidence, maxRejectedWordRatio,
                plan, (nuint)pageLanguages.Length,
                detectRotation ? 1 : 0, deskew ? 1 : 0, denoise ? 1 : 0,
                out json, out error);
        }
    }

    /// <inheritdoc />
    public int PdfPageCount(byte[] pdf, out NativeBuffer json, out IntPtr error)
    {
        fixed (byte* bytes = pdf)
        {
            return NativeMethods.PdfPageCount(bytes, (nuint)pdf.Length, out json, out error);
        }
    }
}

/// <summary>
/// An engine handle. It owns the models: disposing it drops them in Rust.
/// </summary>
internal sealed class OcrEngineHandle : IDisposable
{
    private readonly IOcrNative _library;
    private IntPtr _handle;

    static OcrEngineHandle() => NativeOcr.Initialize();

    private OcrEngineHandle(IOcrNative library, IntPtr handle)
    {
        _library = library;
        _handle = handle;
    }

    internal static OcrEngineHandle Create(byte[] detectionModel, byte[] recognitionModel, byte[]? dictionary)
        => Create(NativeOcr.Library, detectionModel, recognitionModel, dictionary);

    internal static OcrEngineHandle Create(
        IOcrNative library,
        byte[] detectionModel,
        byte[] recognitionModel,
        byte[]? dictionary)
    {
        var handle = library.EngineCreate(detectionModel, recognitionModel, dictionary, out var error);

        if (handle == IntPtr.Zero)
        {
            throw NativeOcr.ToException(library, error);
        }

        if (error != IntPtr.Zero)
        {
            // A handle and an error together means a bug in Rust; never leak the error.
            library.ErrorFree(error);
        }

        return new OcrEngineHandle(library, handle);
    }

    /// <summary>
    /// Creates a handle from the two models of a directory: the native side reads
    /// <c>detection.rten</c> and <c>recognition.rten</c> there, validates them and
    /// loads them, and a refusal crosses as the typed model exception.
    /// </summary>
    internal static OcrEngineHandle CreateFromDirectory(string directory)
        => CreateFromDirectory(NativeOcr.Library, directory);

    internal static OcrEngineHandle CreateFromDirectory(IOcrNative library, string directory)
    {
        var handle = library.EngineCreateFromDirectory(
            System.Text.Encoding.UTF8.GetBytes(directory), null, out var error);

        if (handle == IntPtr.Zero)
        {
            throw NativeOcr.ToException(library, error);
        }

        if (error != IntPtr.Zero)
        {
            library.ErrorFree(error);
        }

        return new OcrEngineHandle(library, handle);
    }

    /// <summary>
    /// Creates a handle from a UTF-8 JSON language plan. The native side reads every
    /// directory the plan names, validates each pair and loads it; a refusal crosses
    /// as the typed model exception, and a malformed plan as an argument error.
    /// </summary>
    internal static OcrEngineHandle CreateLanguages(byte[] plan)
        => CreateLanguages(NativeOcr.Library, plan);

    internal static OcrEngineHandle CreateLanguages(IOcrNative library, byte[] plan)
    {
        IntPtr handle;
        IntPtr error;

        try
        {
            handle = library.EngineCreateLanguages(plan, out error);
        }
        catch (EntryPointNotFoundException exception)
        {
            throw Unsupported(exception, "to_engine_create_languages");
        }

        if (handle == IntPtr.Zero)
        {
            throw NativeOcr.ToException(library, error);
        }

        if (error != IntPtr.Zero)
        {
            library.ErrorFree(error);
        }

        return new OcrEngineHandle(library, handle);
    }

    /// <summary>
    /// The identity of the models this handle runs, as the native side recorded it
    /// when it loaded them.
    /// </summary>
    internal OcrModelIdentity Models()
    {
        var status = _library.EngineModels(Handle(), out var json, out var error);

        return ResultParser.ParseIdentity(NativeOcr.Take(_library, status, json, error));
    }

    internal byte[] Recognize(byte[] image, OcrOptions options)
    {
        var handle = Handle();

        int status;
        NativeBuffer json;
        IntPtr error;

        if (options.RequiresPreprocessing)
        {
            try
            {
                status = _library.EngineRecognizePreprocessed(
                    handle,
                    image,
                    options.MinimumPageConfidence,
                    options.MinimumWordConfidence,
                    options.MaximumRejectedWordRatio,
                    options.DetectRotation,
                    options.Deskew,
                    options.Denoise,
                    out json,
                    out error);
            }
            catch (EntryPointNotFoundException exception)
            {
                throw Unsupported(exception, "to_engine_recognize_preprocessed");
            }
        }
        else
        {
            status = _library.EngineRecognize(
                handle,
                image,
                options.MinimumPageConfidence,
                options.MinimumWordConfidence,
                options.MaximumRejectedWordRatio,
                out json,
                out error);
        }

        return Take(status, json, error);
    }

    internal byte[] RecognizePdf(byte[] pdf, OcrPdfOptions options, uint firstPage, uint pageCount)
    {
        var handle = Handle();

        int status;
        NativeBuffer json;
        IntPtr error;

        if (options.RequiresPreprocessing)
        {
            try
            {
                status = _library.EngineRecognizePdfPreprocessed(
                    handle,
                    pdf,
                    (uint)options.Dpi,
                    firstPage,
                    pageCount,
                    options.MinimumPageConfidence,
                    options.MinimumWordConfidence,
                    options.MaximumRejectedWordRatio,
                    options.DetectRotation,
                    options.Deskew,
                    options.Denoise,
                    out json,
                    out error);
            }
            catch (EntryPointNotFoundException exception)
            {
                throw Unsupported(exception, "to_engine_recognize_pdf_preprocessed");
            }
        }
        else
        {
            status = _library.EngineRecognizePdf(
                handle,
                pdf,
                (uint)options.Dpi,
                firstPage,
                pageCount,
                options.MinimumPageConfidence,
                options.MinimumWordConfidence,
                options.MaximumRejectedWordRatio,
                out json,
                out error);
        }

        return Take(status, json, error);
    }

    /// <summary>
    /// Recognises an image with the model set of a named language. The tag reaches the
    /// native side as UTF-8; a tag the engine has no set for is not an error there — it
    /// is the reported fallback.
    /// </summary>
    internal byte[] RecognizeLanguage(byte[] image, OcrOptions options, string language)
    {
        var handle = Handle();

        int status;
        NativeBuffer json;
        IntPtr error;

        try
        {
            status = _library.EngineRecognizeLanguage(
                handle,
                image,
                options.MinimumPageConfidence,
                options.MinimumWordConfidence,
                options.MaximumRejectedWordRatio,
                Encoding.UTF8.GetBytes(language),
                options.DetectRotation,
                options.Deskew,
                options.Denoise,
                out json,
                out error);
        }
        catch (EntryPointNotFoundException exception)
        {
            throw Unsupported(exception, "to_engine_recognize_language");
        }

        return Take(status, json, error);
    }

    /// <summary>
    /// Renders and recognises pages, each with the set of its language. The plan is
    /// aligned with the rendered range by the caller.
    /// </summary>
    internal byte[] RecognizePdfLanguages(
        byte[] pdf,
        OcrPdfOptions options,
        uint firstPage,
        uint pageCount,
        IReadOnlyList<string?> pageLanguages)
    {
        var handle = Handle();

        int status;
        NativeBuffer json;
        IntPtr error;

        try
        {
            status = _library.EngineRecognizePdfLanguages(
                handle,
                pdf,
                (uint)options.Dpi,
                firstPage,
                pageCount,
                options.MinimumPageConfidence,
                options.MinimumWordConfidence,
                options.MaximumRejectedWordRatio,
                NativeOcr.PageLanguagesJson(pageLanguages),
                options.DetectRotation,
                options.Deskew,
                options.Denoise,
                out json,
                out error);
        }
        catch (EntryPointNotFoundException exception)
        {
            throw Unsupported(exception, "to_engine_recognize_pdf_languages");
        }

        return Take(status, json, error);
    }

    /// <summary>The number of pages a PDF holds, read from its page tree.</summary>
    internal uint PageCount(byte[] pdf)
    {
        int status;
        NativeBuffer json;
        IntPtr error;

        try
        {
            status = _library.PdfPageCount(pdf, out json, out error);
        }
        catch (EntryPointNotFoundException exception)
        {
            throw Unsupported(exception, "to_pdf_page_count");
        }

        return ResultParser.ParsePageCount(Take(status, json, error));
    }

    /// <summary>
    /// The native library predates the exports this call needs: an ABI revision 2
    /// library built before them loads and recognises, and only that call is missing.
    /// </summary>
    private static OcrException Unsupported(EntryPointNotFoundException exception, string export)
        => new(
            "unsupported",
            $"the native library does not export {export} ({exception.Message}); "
            + "rebuild native/text-ocr to preprocess pages");

    public void Dispose()
    {
        var handle = _handle;
        _handle = IntPtr.Zero;

        if (handle != IntPtr.Zero)
        {
            _library.EngineFree(handle);
        }
    }

    private byte[] Take(int status, NativeBuffer json, IntPtr error)
    {
        if (status != 0)
        {
            if (json.Pointer != IntPtr.Zero)
            {
                _library.BufferFree(json);
            }

            throw NativeOcr.ToException(_library, error);
        }

        try
        {
            return json.ToArray();
        }
        finally
        {
            _library.BufferFree(json);
        }
    }

    private IntPtr Handle()
        => _handle != IntPtr.Zero
            ? _handle
            : throw new ObjectDisposedException(nameof(OcrEngine));
}
