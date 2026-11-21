using System.Collections.Generic;

namespace M3scalinum.Text.Ocr.Internal;

/// <summary>
/// What <see cref="OcrEngine"/> needs from a recognition engine.
/// </summary>
/// <remarks>
/// The native library is one implementation; the tests use another, because the
/// models weigh tens of megabytes and this repository ships none. Everything the
/// public surface promises is asserted through this interface, and the native
/// implementation is exercised for its ABI and its error paths.
/// </remarks>
internal interface IOcrBackend
{
    OcrResult Recognize(byte[] image);

    IReadOnlyList<OcrPageResult> RecognizePdf(byte[] pdf, OcrPdfOptions options);
}
