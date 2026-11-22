using System;
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

    /// <summary>Recognises an image with the model set of a named language.</summary>
    /// <remarks>
    /// The default refuses. A double written before language selection existed keeps
    /// compiling and fails loudly rather than reading with a set the caller did not
    /// choose.
    /// </remarks>
    OcrRecognition RecognizeLanguage(byte[] image, OcrOptions options, string language)
        => throw new NotSupportedException("this backend cannot read in a named language");

    /// <summary>
    /// Renders and recognises a PDF, asking <paramref name="languageForPage"/> which
    /// set reads each page.
    /// </summary>
    /// <remarks>Same default as <see cref="RecognizeLanguage"/>.</remarks>
    IReadOnlyList<OcrPageOutcome> RecognizePdfLanguages(
        byte[] pdf,
        OcrPdfOptions options,
        Func<int, string?> languageForPage)
        => throw new NotSupportedException("this backend cannot choose a language per page");
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
