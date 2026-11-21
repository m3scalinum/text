using System;

namespace M3scalinum.Text.Docx;

/// <summary>What <see cref="DocxReader.Read(byte[])"/> should do beyond parsing.</summary>
public sealed class DocxOptions
{
    /// <summary>The largest document this reader will open, in bytes, unless overridden.</summary>
    public const long DefaultMaxDocumentBytes = 64L * 1024 * 1024;

    /// <summary>
    /// Whether embedded images are read and appear in the document. When false they are dropped
    /// entirely: no bytes are copied out of the package, and <see cref="DocxDocument.Images"/> is
    /// empty. Defaults to <see langword="true"/>.
    /// </summary>
    public bool ExtractImages { get; init; } = true;

    /// <summary>
    /// The largest document, in bytes, this reader will open. A document over it fails with
    /// <see cref="DocxErrorCode.Limit"/> instead of being read into memory.
    /// </summary>
    public long MaxDocumentBytes { get; init; } = DefaultMaxDocumentBytes;

    /// <summary>Throws when the values are outside what the native reader accepts.</summary>
    internal void Validate()
    {
        if (MaxDocumentBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxDocumentBytes), MaxDocumentBytes, "the limit is a positive byte count");
        }
    }
}
