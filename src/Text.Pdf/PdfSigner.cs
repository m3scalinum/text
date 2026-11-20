using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// Signs a PDF with PAdES, as an incremental update.
/// </summary>
/// <remarks>
/// A PAdES signature is a detached CMS structure over the whole file, minus the bytes that hold the
/// signature itself. The file is therefore built first with a fixed-size placeholder, the byte range
/// is computed on the result, the CMS is produced over the two ranges, and the placeholder is
/// overwritten in place. Nothing before the appended block changes, so a document that was PDF/A
/// stays PDF/A and a document that was already signed keeps its earlier signatures valid.
/// </remarks>
public static class PdfSigner
{
    /// <summary>Room reserved for the CMS structure: 8 KiB, far more than a certificate chain needs.</summary>
    private const int ContentsBytes = 8192;

    private const string ByteRangePlaceholder = "/ByteRange[0 0000000000 0000000000 0000000000]";

    /// <summary>
    /// Returns a copy of <paramref name="pdf"/> carrying a PAdES-B signature made with
    /// <paramref name="certificate"/>.
    /// </summary>
    /// <param name="pdf">The document to sign.</param>
    /// <param name="certificate">The signing certificate, with its private key.</param>
    /// <param name="options">What the signature declares. Optional.</param>
    /// <exception cref="PdfSignatureException">
    /// The data is not a PDF this writer can update, or the certificate has no private key.
    /// </exception>
    public static byte[] Sign(
        ReadOnlySpan<byte> pdf,
        X509Certificate2 certificate,
        PdfSignatureOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        options ??= new PdfSignatureOptions();

        if (!certificate.HasPrivateKey)
        {
            throw new PdfSignatureException("the certificate carries no private key, so it cannot sign");
        }

        if (pdf.IsEmpty)
        {
            throw new PdfSignatureException("the document is empty");
        }

        var text = Encoding.Latin1.GetString(pdf);
        var structure = PdfStructure.Parse(text);

        var fieldNumber = structure.Size;
        var signatureNumber = structure.Size + 1;
        var newSize = structure.Size + 2;

        var builder = new StringBuilder(text);
        if (!text.EndsWith('\n'))
        {
            builder.Append('\n');
        }

        // The catalog is rewritten under its own number to point at the new field.
        var catalogOffset = builder.Length;
        builder.Append(structure.RootNumber.ToString(CultureInfo.InvariantCulture)).Append(" 0 obj\n")
               .Append(WithAcroForm(structure.Catalog, fieldNumber))
               .Append("\nendobj\n");

        var fieldOffset = builder.Length;
        builder.Append(fieldNumber.ToString(CultureInfo.InvariantCulture)).Append(" 0 obj\n")
               .Append("<</Type/Annot/Subtype/Widget/FT/Sig/T(").Append(EscapeName(options.FieldName))
               .Append(")/V ").Append(signatureNumber.ToString(CultureInfo.InvariantCulture))
               .Append(" 0 R/Rect[0 0 0 0]/F 132>>\nendobj\n");

        var signatureOffset = builder.Length;
        builder.Append(signatureNumber.ToString(CultureInfo.InvariantCulture)).Append(" 0 obj\n")
               .Append("<</Type/Sig/Filter/Adobe.PPKLite/SubFilter/ETSI.CAdES.detached")
               .Append(ByteRangePlaceholder)
               .Append("/Contents<").Append('0', ContentsBytes * 2).Append('>');

        if (options.Reason is { Length: > 0 } reason)
        {
            builder.Append("/Reason(").Append(EscapeString(reason)).Append(')');
        }

        if (options.Location is { Length: > 0 } location)
        {
            builder.Append("/Location(").Append(EscapeString(location)).Append(')');
        }

        builder.Append("/M(").Append(PdfDate(options.SignedAt ?? DateTimeOffset.UtcNow)).Append(')');
        builder.Append(">>\nendobj\n");

        var xrefOffset = builder.Length;
        builder.Append("xref\n")
               .Append(structure.RootNumber.ToString(CultureInfo.InvariantCulture)).Append(" 1\n")
               .Append(Offset(catalogOffset)).Append(" 00000 n \n")
               .Append(fieldNumber.ToString(CultureInfo.InvariantCulture)).Append(" 1\n")
               .Append(Offset(fieldOffset)).Append(" 00000 n \n")
               .Append(signatureNumber.ToString(CultureInfo.InvariantCulture)).Append(" 1\n")
               .Append(Offset(signatureOffset)).Append(" 00000 n \n")
               .Append("trailer\n<</Size ").Append(newSize.ToString(CultureInfo.InvariantCulture))
               .Append("/Root ").Append(structure.RootNumber.ToString(CultureInfo.InvariantCulture)).Append(" 0 R")
               .Append("/Prev ").Append(structure.PreviousXref.ToString(CultureInfo.InvariantCulture));

        if (structure.Id is { } id)
        {
            builder.Append("/ID ").Append(id);
        }

        builder.Append(">>\nstartxref\n").Append(xrefOffset.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF\n");

        var built = builder.ToString();

        // The byte range excludes the hex string and its two delimiters, and it is written with a
        // fixed width so that filling it in does not move anything.
        var contentsStart = built.IndexOf("/Contents<", signatureOffset, StringComparison.Ordinal) + "/Contents".Length;
        var contentsEnd = contentsStart + 1 + (ContentsBytes * 2) + 1;
        var total = built.Length;

        built = built.Replace(
            ByteRangePlaceholder,
            string.Create(
                CultureInfo.InvariantCulture,
                $"/ByteRange[0 {contentsStart:D10} {contentsEnd:D10} {total - contentsEnd:D10}]"),
            StringComparison.Ordinal);

        var bytes = Encoding.Latin1.GetBytes(built);

        var signed = new byte[contentsStart + (total - contentsEnd)];
        Buffer.BlockCopy(bytes, 0, signed, 0, contentsStart);
        Buffer.BlockCopy(bytes, contentsEnd, signed, contentsStart, total - contentsEnd);

        var signature = ComputeSignature(signed, certificate);
        var hex = Convert.ToHexString(signature);

        if (hex.Length > ContentsBytes * 2)
        {
            throw new PdfSignatureException(
                $"the signature is {hex.Length / 2} bytes and the reserved space is {ContentsBytes}; a longer certificate chain needs a larger placeholder");
        }

        var encoded = Encoding.ASCII.GetBytes(hex.PadRight(ContentsBytes * 2, '0'));
        Buffer.BlockCopy(encoded, 0, bytes, contentsStart + 1, encoded.Length);

        return bytes;
    }

    private static byte[] ComputeSignature(byte[] content, X509Certificate2 certificate)
    {
        var cms = new SignedCms(new ContentInfo(content), detached: true);

        var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, certificate)
        {
            IncludeOption = X509IncludeOption.EndCertOnly,
            DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1", "SHA-256"),
        };

        cms.ComputeSignature(signer);
        return cms.Encode();
    }

    /// <summary>Adds the <c>/AcroForm</c> that holds the signature field to the catalog.</summary>
    private static string WithAcroForm(string catalog, int fieldNumber)
    {
        var close = catalog.LastIndexOf(">>", StringComparison.Ordinal);
        if (close < 0)
        {
            throw new PdfSignatureException("the PDF catalog is not a dictionary");
        }

        var acroForm = "/AcroForm<</Fields[" + fieldNumber.ToString(CultureInfo.InvariantCulture) + " 0 R]/SigFlags 3>>";

        return catalog.Insert(close, acroForm);
    }

    private static string Offset(int value) => value.ToString("D10", CultureInfo.InvariantCulture);

    private static string PdfDate(DateTimeOffset moment)
    {
        var utc = moment.ToUniversalTime();

        return "D:" + utc.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "+00'00'";
    }

    private static string EscapeName(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("(", "\\(", StringComparison.Ordinal)
        .Replace(")", "\\)", StringComparison.Ordinal);

    private static string EscapeString(string value) => EscapeName(value);
}
