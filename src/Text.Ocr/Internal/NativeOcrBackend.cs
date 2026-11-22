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

    /// <summary>Releases the native handle, and with it both models.</summary>
    public void Dispose() => _handle.Dispose();
}
