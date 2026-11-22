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
    OcrRecognition Recognize(byte[] image, OcrOptions options);

    IReadOnlyList<OcrPageOutcome> RecognizePdf(byte[] pdf, OcrPdfOptions options);
}

/// <summary>
/// A backend that knows which weights it loaded.
/// </summary>
/// <remarks>
/// Separate from <see cref="IOcrBackend"/> so a test double written before model
/// identity existed keeps compiling and simply reports none: a double is not a
/// weighed engine and must not pretend to be one.
/// </remarks>
internal interface IModelSource
{
    /// <summary>The identity of the two models the engine runs.</summary>
    OcrModelIdentity Models { get; }
}
