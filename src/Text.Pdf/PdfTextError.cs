namespace M3scalinum.Text.Pdf;

/// <summary>Why a document's text could not be read at all.</summary>
public enum PdfTextError
{
    /// <summary>The bytes are not a PDF.</summary>
    NotAPdf = 0,

    /// <summary>
    /// The document is encrypted (<c>/Encrypt</c> in its trailer) and no password was supplied, or
    /// its security handler is one this package does not decrypt. The exception names the handler
    /// the file declares.
    /// </summary>
    Encrypted = 1,

    /// <summary>The file is a PDF but its catalog cannot be resolved, so its pages cannot be found.</summary>
    Unreadable = 2,

    /// <summary>
    /// The password supplied does not open the encrypted document: it matches neither the user
    /// password nor the owner password, so the ciphertext cannot be decrypted.
    /// </summary>
    WrongPassword = 3,
}
