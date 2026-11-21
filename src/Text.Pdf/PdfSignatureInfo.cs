using System;
using System.Security.Cryptography.X509Certificates;

namespace M3scalinum.Text.Pdf;

/// <summary>What was established about one signature in a PDF.</summary>
/// <remarks>
/// Every property says what the verifier checked, and nothing it did not: a property that could not
/// be computed is <see langword="null"/>, and <see cref="Level"/> never claims a level whose evidence
/// is missing.
/// </remarks>
public sealed class PdfSignatureInfo
{
    internal PdfSignatureInfo(
        string? signerSubject,
        string? signerIssuer,
        X509Certificate2? signerCertificate,
        DateTimeOffset? claimedSigningTime,
        DateTimeOffset? timestampedSigningTime,
        bool isIntact,
        bool coversWholeFile,
        bool hasDocumentTimestamp,
        bool isDocumentTimestampValid,
        PdfSignatureLevel level,
        PdfChainStatus chainStatus,
        string? chainError,
        string? error)
    {
        SignerSubject = signerSubject;
        SignerIssuer = signerIssuer;
        SignerCertificate = signerCertificate;
        ClaimedSigningTime = claimedSigningTime;
        TimestampedSigningTime = timestampedSigningTime;
        IsIntact = isIntact;
        CoversWholeFile = coversWholeFile;
        HasDocumentTimestamp = hasDocumentTimestamp;
        IsDocumentTimestampValid = isDocumentTimestampValid;
        Level = level;
        ChainStatus = chainStatus;
        ChainError = chainError;
        Error = error;
    }

    /// <summary>The subject of the signing certificate, as an X.500 string. Null when it was not embedded.</summary>
    public string? SignerSubject { get; }

    /// <summary>The issuer of the signing certificate. Null when it was not embedded.</summary>
    public string? SignerIssuer { get; }

    /// <summary>The signing certificate itself, as it was embedded in the CMS. Null when it was not.</summary>
    public X509Certificate2? SignerCertificate { get; }

    /// <summary>The start of the certificate's validity window, in UTC.</summary>
    public DateTimeOffset? CertificateNotBefore => SignerCertificate is { } certificate
        ? new DateTimeOffset(certificate.NotBefore.ToUniversalTime(), TimeSpan.Zero)
        : null;

    /// <summary>The end of the certificate's validity window, in UTC.</summary>
    public DateTimeOffset? CertificateNotAfter => SignerCertificate is { } certificate
        ? new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero)
        : null;

    /// <summary>The moment the signature dictionary claims, from its <c>/M</c>. Not evidence.</summary>
    public DateTimeOffset? ClaimedSigningTime { get; }

    /// <summary>
    /// The moment a timestamp authority attests, from the signature timestamp token. This is the
    /// moment the signature is known to have existed; null when it carries no timestamp.
    /// </summary>
    public DateTimeOffset? TimestampedSigningTime { get; }

    /// <summary>Whether the CMS verifies over the bytes the signature covers.</summary>
    public bool IsIntact { get; }

    /// <summary>
    /// Whether the byte range reaches the last byte of the file. A signature made before a later
    /// incremental update covers its own revision, not the file, which is normal and not a defect.
    /// </summary>
    public bool CoversWholeFile { get; }

    /// <summary>Whether the file carries a document timestamp at all.</summary>
    public bool HasDocumentTimestamp { get; }

    /// <summary>Whether a document timestamp verifies and reaches the end of the file.</summary>
    public bool IsDocumentTimestampValid { get; }

    /// <summary>The highest PAdES baseline level the verifier could establish.</summary>
    public PdfSignatureLevel Level { get; }

    /// <summary>The chain's standing against the supplied trust store.</summary>
    public PdfChainStatus ChainStatus { get; }

    /// <summary>Why the chain was not trusted, when it was not. Null otherwise.</summary>
    public string? ChainError { get; }

    /// <summary>Why the signature is not intact, when it is not. Null otherwise.</summary>
    public string? Error { get; }
}
