using System;
using System.Security.Cryptography;

namespace M3scalinum.Text.Pdf;

/// <summary>How an attachment relates to the document that carries it: its <c>/AFRelationship</c>.</summary>
/// <remarks>
/// The four named values are the ones ISO 32000-2 defines for an associated file. <see langword="Unspecified"/>
/// means the document declares no relationship at all — which is legal in plain PDF and fatal to a
/// claim of PDF/A-4f, where every associated file must state one.
/// </remarks>
public enum PdfAttachmentRole
{
    /// <summary>The document declares no <c>/AFRelationship</c>.</summary>
    Unspecified,

    /// <summary>The attachment is the source material the document was produced from.</summary>
    Source,

    /// <summary>The attachment carries the data the document presents, as an invoice's XML does.</summary>
    Data,

    /// <summary>The attachment is an alternative representation of the document.</summary>
    Alternative,

    /// <summary>The attachment supplements the document without being either source or data.</summary>
    Supplement,
}

/// <summary>One embedded file found in a PDF, with the bytes it carries.</summary>
/// <remarks>
/// The properties restate the file specification and the embedded file stream: the name a reader
/// shows, the MIME type of the bytes, the relationship to the document, the size, and a SHA-256
/// fingerprint of exactly the bytes <see cref="Content"/> holds. Nothing is inferred from the
/// extension; a PDF that declares <c>/Subtype /text#2Fxml</c> reports <c>text/xml</c> and one that
/// declares nothing reports <see langword="null"/>.
/// </remarks>
public sealed class PdfAttachment
{
    internal PdfAttachment(
        string name,
        string? fileName,
        string? mimeType,
        PdfAttachmentRole role,
        byte[] content,
        bool isStoredEncoded)
    {
        Name = name;
        FileName = fileName;
        MimeType = mimeType;
        Role = role;
        Content = content;
        IsStoredEncoded = isStoredEncoded;
        Sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
    }

    /// <summary>
    /// The name a reader shows: the Unicode <c>/UF</c> entry when the file specification has one, the
    /// <c>/F</c> entry otherwise, and the key of the name tree when neither is written.
    /// </summary>
    public string Name { get; }

    /// <summary>The raw <c>/F</c> entry of the file specification, or <see langword="null"/> when it has none.</summary>
    public string? FileName { get; }

    /// <summary>Whether the file specification carries the <c>/F</c> entry <see cref="FileName"/> reports.</summary>
    public bool HasFileName => FileName is not null;

    /// <summary>The <c>/Subtype</c> of the embedded file stream, such as <c>text/xml</c>, or <see langword="null"/>.</summary>
    public string? MimeType { get; }

    /// <summary>The relationship the document declares in <c>/AFRelationship</c>.</summary>
    public PdfAttachmentRole Role { get; }

    /// <summary>The attachment's bytes, decoded: the file the sender embedded, not the stream that carried it.</summary>
    public byte[] Content { get; }

    /// <summary>The number of bytes in <see cref="Content"/>.</summary>
    public long Size => Content.Length;

    /// <summary>
    /// The SHA-256 of <see cref="Content"/>, as lowercase hexadecimal: the fingerprint a caller compares
    /// against the file it expected instead of reading the bytes twice.
    /// </summary>
    public string Sha256 { get; }

    /// <summary>
    /// Whether <see cref="Content"/> is the bytes as they are stored rather than the file they encode,
    /// because the stream's filter chain is one this reader does not undo. A Flate-compressed
    /// attachment, which is what every producer writes, is decoded and reports <see langword="false"/>.
    /// </summary>
    public bool IsStoredEncoded { get; }
}
