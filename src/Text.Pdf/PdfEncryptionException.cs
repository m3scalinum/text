using System;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// The document could not be opened or encrypted: the password is wrong, or the file uses a
/// security handler this library does not implement.
/// </summary>
/// <remarks>
/// A wrong password is this exception and not a damaged document: the bytes of an encrypted file are
/// perfectly readable, and it is only the password that decides whether their content can be
/// recovered. <see cref="PdfOperationException"/> therefore never stands for "the password was
/// wrong", and <see cref="PdfSignatureException"/> never stands for "the file is encrypted".
/// </remarks>
public sealed class PdfEncryptionException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    public PdfEncryptionException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">What went wrong.</param>
    public PdfEncryptionException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and an inner exception.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The failure underneath.</param>
    public PdfEncryptionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
