namespace M3scalinum.Text.Pdf;

/// <summary>Why a document's text could not be read at all.</summary>
public enum PdfTextError
{
    /// <summary>The bytes are not a PDF.</summary>
    NotAPdf = 0,

    /// <summary>
    /// The document is encrypted (<c>/Encrypt</c> in its trailer). Decryption is not implemented in
    /// this package, so the text cannot be read; the exception names the handler the file declares.
    /// </summary>
    Encrypted = 1,

    /// <summary>The file is a PDF but its catalog cannot be resolved, so its pages cannot be found.</summary>
    Unreadable = 2,
}
