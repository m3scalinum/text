using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// Reads and writes AES-encrypted PDFs: opens a protected document with its password, and produces
/// a document protected with one.
/// </summary>
/// <remarks>
/// <para>
/// Two revisions of the standard security handler are supported because those are the two real
/// documents carry: AES-256 with V=5/R=6, which every producer writes today, and AES-128 with
/// V=4/R=4 and an <c>/AESV2</c> crypt filter, which older producers and the PDF/A era wrote. RC4
/// (revision 2 and 3, and a revision 4 with <c>/V2</c> filters) and public-key encryption, where the
/// file key is wrapped for a certificate rather than for a password, are out of scope and refused by
/// name.
/// </para>
/// <para>
/// <see cref="Open"/> returns a new, unencrypted document rather than a decrypting reader. That is
/// what lets the rest of this package work on a protected file without knowing it was protected:
/// decrypt first, then merge, fill, check or sign. The decrypted file is a rebuild, not a byte range
/// of the original — objects are renumbered in the same order, the cross-reference table is written
/// afresh, and content streams are decrypted and left uncompressed exactly as they were stored.
/// </para>
/// <para>
/// Encryption is not a signature. Encrypting a signed document keeps the signature dictionary and
/// its <c>/ByteRange</c> where they are, but the bytes the range covers are the ciphertext of the
/// encrypted file, so the signature no longer verifies. Sign after encrypting, never before: open
/// the protected document, then sign the bytes <see cref="Open"/> returned.
/// </para>
/// </remarks>
public static class PdfEncryption
{
    /// <summary>
    /// Opens an encrypted document and returns its decrypted bytes and the permissions the password
    /// grants.
    /// </summary>
    /// <param name="pdf">The encrypted document, as whole bytes.</param>
    /// <param name="password">
    /// The user or the owner password. An empty or omitted password is tried as it is, which is what
    /// a document that opens without asking carries.
    /// </param>
    /// <returns>The decrypted document and what the password permits.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pdf"/> is <see langword="null"/>.</exception>
    /// <exception cref="PdfEncryptionException">
    /// The bytes are not a PDF, the password is wrong, or the document uses a security handler this
    /// library does not implement.
    /// </exception>
    public static PdfDecryptionResult Open(byte[] pdf, string? password = null)
    {
        ArgumentNullException.ThrowIfNull(pdf);

        if (!PdfFile.TryOpen(pdf, out var file))
        {
            throw new PdfEncryptionException("the bytes are not a PDF, so there is nothing to decrypt");
        }

        if (!file.IsEncrypted)
        {
            // Nothing to do: the document is already readable, and every permission is moot.
            return new PdfDecryptionResult(pdf, PdfPermissions.All, false, 0, false);
        }

        var reference = file.Trailer["Encrypt"] as PdfReference;
        var encrypt = file.ResolveDictionary(file.Trailer["Encrypt"])
            ?? throw new PdfEncryptionException(
                "the trailer names an /Encrypt entry that does not resolve to a dictionary, so the document cannot be decrypted");

        var number = reference?.Number ?? 0;
        var security = PdfStandardSecurity.ForReading(file, encrypt, password ?? string.Empty);
        var encryptMetadata = file.Resolve(encrypt["EncryptMetadata"]) is not PdfBoolean flag || flag.Value;

        file.UseDecryption(security, number, encryptMetadata);

        if (file.Catalog is null)
        {
            throw new PdfEncryptionException(
                "the password opened the document, but its catalog does not resolve, so the decrypted bytes would not be a document");
        }

        var bytes = Rewrite(file, number, null, null, null);

        return new PdfDecryptionResult(
            bytes,
            security.Permissions,
            true,
            security.Revision,
            security.OpenedWithOwnerPassword);
    }

    /// <summary>
    /// Returns <paramref name="pdf"/> encrypted with AES-256 (V=5/R=6) and carrying
    /// <paramref name="permissions"/>.
    /// </summary>
    /// <param name="pdf">The unencrypted document, as whole bytes.</param>
    /// <param name="userPassword">
    /// The password a reader needs to open the document. An empty password is allowed and produces a
    /// document that opens without asking, which is how permissions are applied without a password.
    /// </param>
    /// <param name="permissions">What the user password permits.</param>
    /// <param name="ownerPassword">
    /// The password that grants every permission. Defaults to <paramref name="userPassword"/>.
    /// </param>
    /// <returns>The encrypted document.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pdf"/> or <paramref name="userPassword"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="permissions"/> carries bits the format does not define.</exception>
    /// <exception cref="PdfEncryptionException">
    /// The bytes are not a PDF, are already encrypted, or have no catalog to protect.
    /// </exception>
    public static byte[] Encrypt(
        byte[] pdf,
        string userPassword,
        PdfPermissions permissions,
        string? ownerPassword = null)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(userPassword);

        if ((permissions & ~PdfPermissions.All) != 0)
        {
            throw new ArgumentException(
                $"{permissions} carries bits the format does not define as permissions", nameof(permissions));
        }

        if (!PdfFile.TryOpen(pdf, out var file))
        {
            throw new PdfEncryptionException("the bytes are not a PDF, so there is nothing to encrypt");
        }

        if (file.IsEncrypted)
        {
            throw new PdfEncryptionException(
                "the document is already encrypted; open it with PdfEncryption.Open and encrypt the bytes it returned");
        }

        if (file.Catalog is null)
        {
            throw new PdfEncryptionException(
                "the document has no catalog that resolves, so there is nothing to encrypt");
        }

        var security = PdfStandardSecurity.ForWriting(
            userPassword,
            ownerPassword ?? userPassword,
            permissions,
            out var encrypt,
            out var identifier);

        return Rewrite(file, -1, security, encrypt, identifier);
    }

    /// <summary>
    /// Writes every object of <paramref name="file"/> back as a new PDF, encrypting what
    /// <paramref name="security"/> covers and dropping the object at <paramref name="drop"/>.
    /// </summary>
    /// <param name="file">The document to copy.</param>
    /// <param name="drop">An object number to write as <c>null</c>, such as the old <c>/Encrypt</c> dictionary.</param>
    /// <param name="security">The handler that encrypts strings and streams, when writing.</param>
    /// <param name="encrypt">The new <c>/Encrypt</c> dictionary, when writing.</param>
    /// <param name="identifier">The first element of the new <c>/ID</c>, when writing.</param>
    private static byte[] Rewrite(
        PdfFile file,
        int drop,
        PdfStandardSecurity? security,
        PdfDictionary? encrypt,
        byte[]? identifier)
    {
        var root = (file.Trailer["Root"] as PdfReference)?.Number ?? 0;

        if (root <= 0)
        {
            throw new PdfEncryptionException(
                "the trailer names no /Root that is an indirect object, so the document cannot be rebuilt");
        }

        var size = file.Size;
        var writer = new PdfObjectWriter();

        for (var number = 1; number < size; number++)
        {
            writer.Reserve();
        }

        var encryptNumber = encrypt is null ? 0 : writer.Reserve();

        for (var number = 1; number < size; number++)
        {
            var value = file.GetObject(number) ?? PdfValue.Null;

            if (number == drop)
            {
                value = PdfValue.Null;
            }
            else if (value is PdfStream container &&
                (container.Dictionary.IsType("XRef") || container.Dictionary.IsType("ObjStm")))
            {
                // The output has a classic cross-reference table and no object stream, so the
                // containers of the input hold nothing a reader would follow: an object stream's
                // members are written as objects of their own, and an xref stream describes
                // offsets that no longer exist.
                value = PdfValue.Null;
            }
            else if (security is not null)
            {
                // AES-256 is not part of PDF 1.7: a file whose header claims an older version names
                // the Adobe extension level that defines it, which is what a producer of 256-bit
                // AES writes in a 1.7 file.
                if (encrypt is not null && number == root && file.MajorVersion < 2 && value is PdfDictionary catalog)
                {
                    catalog.Entries["Extensions"] = AdobeExtensions(file, catalog["Extensions"]);
                }

                value = EncryptObject(security, number, file.GenerationOf(number), value);
            }

            writer.Set(number, value);
        }

        if (encrypt is not null)
        {
            writer.Set(encryptNumber, encrypt);
        }

        var trailer = new Dictionary<string, PdfValue>(StringComparer.Ordinal);

        if (file.Trailer["Info"] is { } info)
        {
            trailer["Info"] = info;
        }

        if (encrypt is not null)
        {
            trailer["Encrypt"] = new PdfReference(encryptNumber);
            trailer["ID"] = Identifier(identifier!);
        }
        else if (file.Trailer["ID"] is PdfArray existing)
        {
            trailer["ID"] = existing;
        }

        return writer.Build(root, file.MajorVersion, file.MinorVersion, trailer);
    }

    /// <summary>Encrypts the strings and, unless it is an xref stream, the data of one object.</summary>
    private static PdfValue EncryptObject(PdfStandardSecurity security, int number, int generation, PdfValue value)
    {
        if (value is PdfStream stream)
        {
            var dictionary = (PdfDictionary)PdfObjectTree.TransformStrings(
                stream.Dictionary,
                security.EncryptsStrings ? bytes => security.EncryptObject(number, generation, bytes) : bytes => bytes);

            if (!security.EncryptsStreams || stream.Dictionary.IsType("XRef"))
            {
                return new PdfStream(dictionary, stream.Data);
            }

            var data = security.EncryptObject(number, generation, stream.Data);
            dictionary.Entries["Length"] = new PdfNumber(data.Length);

            return new PdfStream(dictionary, data);
        }

        return PdfObjectTree.TransformStrings(
            value,
            security.EncryptsStrings ? bytes => security.EncryptObject(number, generation, bytes) : bytes => bytes);
    }

    /// <summary>The <c>/Extensions</c> dictionary that places AES-256 in a file below PDF 2.0.</summary>
    private static PdfDictionary AdobeExtensions(PdfFile file, PdfValue? existing)
    {
        var extensions = file.ResolveDictionary(existing) ?? new PdfDictionary();
        var adbe = new PdfDictionary();
        adbe.Entries["BaseVersion"] = new PdfName("1.7");
        adbe.Entries["ExtensionLevel"] = new PdfNumber(8);
        extensions.Entries["ADBE"] = adbe;

        return extensions;
    }

    /// <summary>The <c>/ID</c> array of a new document: the same value twice, as the format asks.</summary>
    private static PdfArray Identifier(byte[] value)
    {
        var array = new PdfArray();
        array.Items.Add(new PdfString(value));
        array.Items.Add(new PdfString(value));

        return array;
    }
}
