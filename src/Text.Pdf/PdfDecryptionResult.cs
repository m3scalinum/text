using System;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// What <see cref="PdfEncryption.Open"/> recovered from an encrypted document: the decrypted bytes,
/// and what the password it was opened with permits.
/// </summary>
/// <remarks>
/// The bytes are a complete, unencrypted PDF with the same objects, page tree, signatures and
/// metadata as the encrypted one, and no <c>/Encrypt</c> entry in its trailer. Every other operation
/// of this package accepts them, which is what makes "decrypt, then merge" or "decrypt, then sign"
/// possible without teaching each operation about encryption.
/// </remarks>
public sealed class PdfDecryptionResult
{
    internal PdfDecryptionResult(
        byte[] pdf,
        PdfPermissions permissions,
        bool isEncrypted,
        int revision,
        bool openedWithOwnerPassword)
    {
        Pdf = pdf;
        Permissions = permissions;
        IsEncrypted = isEncrypted;
        Revision = revision;
        OpenedWithOwnerPassword = openedWithOwnerPassword;
    }

    /// <summary>The decrypted document, as whole bytes.</summary>
    public byte[] Pdf { get; }

    /// <summary>The permissions the password grants, as the document declares them.</summary>
    public PdfPermissions Permissions { get; }

    /// <summary>
    /// Whether the source document was encrypted. An unencrypted document is returned unchanged, with
    /// <see cref="PdfPermissions.All"/> and a <see cref="Revision"/> of zero.
    /// </summary>
    public bool IsEncrypted { get; }

    /// <summary>
    /// The revision of the standard security handler that protected the document: 6 for AES-256,
    /// 4 for AES-128, and 0 when the document was not encrypted.
    /// </summary>
    public int Revision { get; }

    /// <summary>
    /// Whether the password matched the owner password rather than the user password. An owner
    /// password grants every permission, whatever <see cref="Permissions"/> says.
    /// </summary>
    public bool OpenedWithOwnerPassword { get; }
}
