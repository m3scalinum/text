using System;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// The document cannot be changed the way the caller asked: it is encrypted, a page range names a
/// page that does not exist, a form field is a signature field, or the file is not shaped like a
/// PDF this writer can rebuild.
/// </summary>
public sealed class PdfOperationException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    public PdfOperationException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">What went wrong.</param>
    public PdfOperationException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and an inner exception.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The failure underneath.</param>
    public PdfOperationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
