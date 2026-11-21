using System;

namespace M3scalinum.Text.Pdf;

/// <summary>The text of a document could not be read at all.</summary>
/// <remarks>
/// This is for the reasons that leave nothing to extract: the bytes are not a PDF, the document is
/// encrypted, or its catalog is unreadable. A page whose fonts cannot be mapped is not an exception:
/// it comes back with text and says what it fell back on, because a page with uncertain text is
/// still more useful than a document that fails to read.
/// </remarks>
public sealed class PdfTextException : Exception
{
    /// <summary>Initialises the exception.</summary>
    /// <param name="error">Why the text could not be read.</param>
    /// <param name="message">What went wrong, in terms the caller can act on.</param>
    public PdfTextException(PdfTextError error, string message)
        : base(message) => Error = error;

    /// <summary>Why the text could not be read.</summary>
    public PdfTextError Error { get; }
}
