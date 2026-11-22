using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Ocr.Internal;

/// <summary>
/// The backend that calls the Rust library. It does no parsing of its own: the
/// result documents go to <see cref="ResultParser"/>, and the thresholds go straight
/// to the ABI, so the decision and the text come from one place.
/// </summary>
internal sealed class NativeOcrBackend : IOcrBackend, IModelSource
{
    private readonly OcrEngineHandle _handle;
    private OcrModelIdentity? _models;

    private NativeOcrBackend(OcrEngineHandle handle) => _handle = handle;

    internal static NativeOcrBackend Create(byte[] detectionModel, byte[] recognitionModel, byte[]? dictionary)
        => new(OcrEngineHandle.Create(detectionModel, recognitionModel, dictionary));

    /// <summary>
    /// Creates the backend from the two models of a directory. The native side reads
    /// the two fixed file names, validates them and loads them; a refusal arrives as
    /// an <see cref="OcrException"/>.
    /// </summary>
    internal static NativeOcrBackend CreateFromDirectory(string directory)
        => new(OcrEngineHandle.CreateFromDirectory(directory));

    /// <summary>
    /// Creates the backend from a language plan: one validated model set per language,
    /// loaded by the native side from the directories the plan names.
    /// </summary>
    internal static NativeOcrBackend CreateLanguages(byte[] plan)
        => new(OcrEngineHandle.CreateLanguages(plan));

    /// <summary>
    /// The identity of the weights the engine loaded, read once from the handle and
    /// kept: it cannot change for the life of the engine.
    /// </summary>
    public OcrModelIdentity Models => _models ??= _handle.Models();

    /// <summary>
    /// Wraps a handle the caller already made, which is how the tests reach the
    /// document mapping and the page-range arithmetic without a model.
    /// </summary>
    internal static NativeOcrBackend Create(OcrEngineHandle handle) => new(handle);

    public OcrRecognition Recognize(byte[] image, OcrOptions options)
        => ResultParser.ParseImage(_handle.Recognize(image, options));

    public IReadOnlyList<OcrPageOutcome> RecognizePdf(byte[] pdf, OcrPdfOptions options)
    {
        var firstPage = (uint)options.FirstPage;
        var pageCount = options.LastPage is { } last ? (uint)(last - options.FirstPage + 1) : 0u;

        return ResultParser.ParsePages(_handle.RecognizePdf(pdf, options, firstPage, pageCount));
    }

    public OcrRecognition RecognizeLanguage(byte[] image, OcrOptions options, string language)
        => ResultParser.ParseImage(_handle.RecognizeLanguage(image, options, language));

    /// <summary>
    /// Resolves the caller's rule for every page of the range and hands the native side
    /// an aligned plan.
    /// </summary>
    /// <remarks>
    /// The range is known before the render only when the caller bounded it, so a read
    /// to the end asks the native side for the page count first, from the page tree and
    /// without rasterising anything. The rule is evaluated here, once per page, which
    /// is what keeps it deterministic: a language that depended on the text of the
    /// previous page would make a reading depend on its own output.
    /// </remarks>
    public IReadOnlyList<OcrPageOutcome> RecognizePdfLanguages(
        byte[] pdf,
        OcrPdfOptions options,
        Func<int, string?> languageForPage)
    {
        var firstPage = (uint)options.FirstPage;

        var count = options.LastPage is { } last
            ? last - options.FirstPage + 1
            : (int)_handle.PageCount(pdf) - options.FirstPage;

        // A first page past the end is the native side's refusal to report, with its
        // own numbers; an empty plan leaves it to say so.
        count = Math.Max(count, 0);

        var languages = new string?[count];
        for (var index = 0; index < count; index++)
        {
            languages[index] = languageForPage(options.FirstPage + index);
        }

        return ResultParser.ParsePages(
            _handle.RecognizePdfLanguages(pdf, options, firstPage, (uint)count, languages));
    }

    /// <summary>Releases the native handle, and with it both models.</summary>
    public void Dispose() => _handle.Dispose();
}
