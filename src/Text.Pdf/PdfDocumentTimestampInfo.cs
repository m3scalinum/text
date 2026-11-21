using System;
using System.Security.Cryptography.X509Certificates;

namespace M3scalinum.Text.Pdf;

/// <summary>What was established about one document timestamp.</summary>
public sealed class PdfDocumentTimestampInfo
{
    internal PdfDocumentTimestampInfo(
        DateTimeOffset? timestampedTime,
        string? signerSubject,
        string? signerIssuer,
        X509Certificate2? signerCertificate,
        bool isIntact,
        bool coversWholeFile,
        PdfChainStatus chainStatus,
        string? chainError,
        string? error)
    {
        TimestampedTime = timestampedTime;
        SignerSubject = signerSubject;
        SignerIssuer = signerIssuer;
        SignerCertificate = signerCertificate;
        IsIntact = isIntact;
        CoversWholeFile = coversWholeFile;
        ChainStatus = chainStatus;
        ChainError = chainError;
        Error = error;
    }

    /// <summary>The time the authority attests, when the token was readable.</summary>
    public DateTimeOffset? TimestampedTime { get; }

    /// <summary>The subject of the authority's certificate, when the token embeds it.</summary>
    public string? SignerSubject { get; }

    /// <summary>The issuer of the authority's certificate.</summary>
    public string? SignerIssuer { get; }

    /// <summary>The authority's certificate, as embedded in the token.</summary>
    public X509Certificate2? SignerCertificate { get; }

    /// <summary>Whether the token's CMS verifies, and its imprint is this document.</summary>
    public bool IsIntact { get; }

    /// <summary>Whether the timestamp covers the last byte of the file.</summary>
    public bool CoversWholeFile { get; }

    /// <summary>The authority certificate's standing against the supplied trust store.</summary>
    public PdfChainStatus ChainStatus { get; }

    /// <summary>Why the chain was not trusted, when it was not.</summary>
    public string? ChainError { get; }

    /// <summary>Why the timestamp is not intact, when it is not.</summary>
    public string? Error { get; }
}
