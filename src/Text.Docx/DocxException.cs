using System;

namespace M3scalinum.Text.Docx;

/// <summary>A `.docx` package could not be read.</summary>
/// <remarks>
/// This is for the reasons that leave nothing to read: the bytes are not a package, a part every
/// package must carry is absent, or the document is over the size limit. Content this reader does
/// not implement never throws — it is dropped, and <c>docs/specifications/22-docx.md</c> says what.
/// </remarks>
public sealed class DocxException : Exception
{
    /// <summary>Initialises the exception.</summary>
    /// <param name="code">Why the package could not be read.</param>
    /// <param name="message">What went wrong, in terms the caller can act on.</param>
    public DocxException(DocxErrorCode code, string message)
        : base(message) => Code = code;

    /// <summary>Initialises the exception around another failure.</summary>
    /// <param name="code">Why the package could not be read.</param>
    /// <param name="message">What went wrong, in terms the caller can act on.</param>
    /// <param name="innerException">The failure this one wraps.</param>
    public DocxException(DocxErrorCode code, string message, Exception innerException)
        : base(message, innerException) => Code = code;

    /// <summary>Why the package could not be read.</summary>
    public DocxErrorCode Code { get; }
}
