using System.Collections.Generic;
using System.Text.Json;

namespace M3scalinum.Text.Ocr.Internal;

/// <summary>
/// The backend that calls the Rust library. It does no parsing of its own: the
/// result documents go to <see cref="ResultParser"/>.
/// </summary>
internal sealed class NativeOcrBackend : IOcrBackend
{
    private readonly OcrEngineHandle _handle;

    private NativeOcrBackend(OcrEngineHandle handle) => _handle = handle;

    internal static NativeOcrBackend Create(byte[] detectionModel, byte[] recognitionModel, byte[]? dictionary)
        => new(OcrEngineHandle.Create(detectionModel, recognitionModel, dictionary));

    public OcrResult Recognize(byte[] image)
        => ResultParser.ParseImage(_handle.Recognize(image));

    public IReadOnlyList<OcrPageResult> RecognizePdf(byte[] pdf, OcrPdfOptions options)
    {
        var firstPage = (uint)options.FirstPage;
        var pageCount = options.LastPage is { } last ? (uint)(last - options.FirstPage + 1) : 0u;

        return ResultParser.ParsePages(
            _handle.RecognizePdf(pdf, (uint)options.Dpi, firstPage, pageCount));
    }

    /// <summary>Releases the native handle, and with it both models.</summary>
    public void Dispose() => _handle.Dispose();
}

