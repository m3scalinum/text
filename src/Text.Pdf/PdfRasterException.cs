using System;

namespace M3scalinum.Text.Pdf;

/// <summary>A page of a PDF could not be rendered as an image.</summary>
/// <remarks>
/// The reasons are the ones that leave no image: bytes that are not a PDF, an encrypted document, a
/// page number outside the document, or a page that would exceed the pixel or memory ceiling. A
/// page the renderer draws imperfectly is not an exception: it comes back as an image, because a
/// preview with a missing font is still a preview.
/// </remarks>
public sealed class PdfRasterException : Exception
{
    /// <summary>Initialises the exception.</summary>
    /// <param name="error">Why the page could not be rendered.</param>
    /// <param name="message">What went wrong, in terms the caller can act on.</param>
    public PdfRasterException(PdfRasterError error, string message)
        : base(message) => Error = error;

    /// <summary>Initialises the exception with the underlying failure.</summary>
    /// <param name="error">Why the page could not be rendered.</param>
    /// <param name="message">What went wrong, in terms the caller can act on.</param>
    /// <param name="innerException">The failure this one wraps.</param>
    public PdfRasterException(PdfRasterError error, string message, Exception innerException)
        : base(message, innerException) => Error = error;

    /// <summary>Why the page could not be rendered.</summary>
    public PdfRasterError Error { get; }
}
