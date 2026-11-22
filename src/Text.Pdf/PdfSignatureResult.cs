using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Pdf;

/// <summary>A signed document, and what the revocation seam contributed to it.</summary>
/// <remarks>
/// <see cref="PdfSigner.Sign"/> returns the bytes alone; this object exists for the caller that
/// supplied an <see cref="IRevocationSource"/> and wants to know, without parsing its own output,
/// which evidence went into the store and which certificate is still missing some.
/// </remarks>
public sealed class PdfSignatureResult
{
    internal PdfSignatureResult(
        byte[] content,
        PdfSignatureLevel level,
        IReadOnlyList<PdfRevocationUse> revocation,
        IReadOnlyList<PdfCertificateReference> missingRevocation)
    {
        Content = content;
        Level = level;
        Revocation = revocation;
        MissingRevocation = missingRevocation;
    }

    /// <summary>The signed PDF, as an incremental update of the input.</summary>
    public byte[] Content { get; }

    /// <summary>
    /// The level this call was configured to reach: <see cref="PdfSignatureOptions.Level"/> when it
    /// was set, otherwise PAdES-B-LT when a revocation source was supplied, PAdES-B-T when a
    /// timestamp authority was, and PAdES-B otherwise. It is what was asked for, not a proof: the
    /// verifier is what proves a level.
    /// </summary>
    public PdfSignatureLevel Level { get; }

    /// <summary>The evidence embedded in the security store, one entry per certificate and kind.</summary>
    public IReadOnlyList<PdfRevocationUse> Revocation { get; }

    /// <summary>
    /// The certificates of the chain that no evidence covers. Empty for a signature that reached
    /// PAdES-B-LT; non-empty only when the caller explicitly asked for a lower level.
    /// </summary>
    public IReadOnlyList<PdfCertificateReference> MissingRevocation { get; }
}

/// <summary>One piece of revocation evidence the store carries.</summary>
public sealed class PdfRevocationUse
{
    internal PdfRevocationUse(
        string subject,
        string issuer,
        string serialNumber,
        PdfRevocationKind kind,
        DateTimeOffset? issuedAt)
    {
        Subject = subject;
        Issuer = issuer;
        SerialNumber = serialNumber;
        Kind = kind;
        IssuedAt = issuedAt;
    }

    /// <summary>The subject of the certificate the evidence is about.</summary>
    public string Subject { get; }

    /// <summary>The issuer of that certificate, as an X.500 string.</summary>
    public string Issuer { get; }

    /// <summary>The serial number of that certificate, in hexadecimal.</summary>
    public string SerialNumber { get; }

    /// <summary>Whether the evidence is an OCSP answer or a CRL.</summary>
    public PdfRevocationKind Kind { get; }

    /// <summary>
    /// The moment the answer was produced or the list was issued, as the structure itself says.
    /// Null when the structure's own time could not be read.
    /// </summary>
    public DateTimeOffset? IssuedAt { get; }
}

/// <summary>A certificate of the chain, named the way a report names it.</summary>
public sealed class PdfCertificateReference
{
    internal PdfCertificateReference(string subject, string issuer, string serialNumber)
    {
        Subject = subject;
        Issuer = issuer;
        SerialNumber = serialNumber;
    }

    /// <summary>The subject, as an X.500 string.</summary>
    public string Subject { get; }

    /// <summary>The issuer, as an X.500 string.</summary>
    public string Issuer { get; }

    /// <summary>The serial number, in hexadecimal.</summary>
    public string SerialNumber { get; }
}

/// <summary>Which kind of structure carries a revocation answer.</summary>
public enum PdfRevocationKind
{
    /// <summary>An OCSP answer, an <c>OCSPResponse</c>.</summary>
    Ocsp = 0,

    /// <summary>A certificate revocation list, a <c>CertificateList</c>.</summary>
    Crl = 1,
}
