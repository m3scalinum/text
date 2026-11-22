namespace M3scalinum.Text.Pdf;

/// <summary>Why a page could not be rendered.</summary>
public enum PdfRasterError
{
    /// <summary>The bytes are not a PDF, or the document is too damaged to read.</summary>
    NotAPdf = 0,

    /// <summary>
    /// The document is encrypted. This package does not decrypt: open it with
    /// <see cref="PdfEncryption.Open"/> and render the bytes that returns.
    /// </summary>
    Encrypted = 1,

    /// <summary>
    /// The requested page is outside the document. Pages are numbered from 1, the way a reader
    /// numbers them; the exception names the count the document declares.
    /// </summary>
    PageOutOfRange = 2,

    /// <summary>
    /// The page would have rendered past the pixel or memory ceiling the caller set — the defence
    /// against a hostile page whose <c>MediaBox</c> asks for gigabytes at a high DPI.
    /// </summary>
    Limit = 3,

    /// <summary>The page asks for something this renderer does not draw.</summary>
    Unsupported = 4,

    /// <summary>An argument reached the native library that the managed side should have refused.</summary>
    InvalidArgument = 5,

    /// <summary>The native library failed in a way it could not name.</summary>
    Internal = 6,

    /// <summary>A panic in the native library, caught at the boundary instead of unwinding into the host.</summary>
    Panic = 7,
}
