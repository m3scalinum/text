using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace M3scalinum.Text.Pdf;

/// <summary>The kind of service a trust list entry describes.</summary>
/// <remarks>
/// The enum is the coarse answer; <see cref="PdfTrustService.TypeIdentifier"/> keeps the URI the list
/// actually wrote, so a caller can tell <c>CA</c> from a type this reader does not know.
/// </remarks>
public enum PdfTrustServiceType
{
    /// <summary>A type this reader does not classify, or none the list declared.</summary>
    Other = 0,

    /// <summary>A certification authority, <c>…/Svctype/CA</c>.</summary>
    CertificateAuthority = 1,

    /// <summary>A qualified certification authority, <c>…/Svctype/CA/QC</c>.</summary>
    QualifiedCertificateAuthority = 2,

    /// <summary>An electronic signature service, <c>…/Svctype/AdESig</c> or <c>AdESigQC</c>.</summary>
    Signature = 3,

    /// <summary>An electronic seal service, <c>…/Svctype/AdESeal</c> or <c>AdESealQC</c>.</summary>
    Seal = 4,

    /// <summary>A timestamping service, <c>…/Svctype/TSA</c>.</summary>
    Timestamp = 5,
}

/// <summary>The standing of a service, as the list states it at the time it was published.</summary>
public enum PdfTrustServiceStatus
{
    /// <summary>A status this reader does not classify.</summary>
    Unknown = 0,

    /// <summary>The service is granted (or accredited): it may be relied on.</summary>
    Granted = 1,

    /// <summary>The service was withdrawn: it may no longer be relied on.</summary>
    Withdrawn = 2,

    /// <summary>The service was deprecated: it is kept for the signatures it already covers.</summary>
    Deprecated = 3,
}

/// <summary>
/// One status of a service and the window it held, derived from the service's status history.
/// </summary>
/// <remarks>
/// A trust list does not write an end date for its current status; it writes the start of the next
/// one. A period therefore ends where the following period begins, and the current period has no
/// end. <see cref="Covers"/> is the only judgement this type makes, and it needs both facts: the
/// status is granted and the moment falls inside the window.
/// </remarks>
public sealed class PdfTrustServicePeriod
{
    internal PdfTrustServicePeriod(PdfTrustServiceStatus status, DateTimeOffset from, DateTimeOffset? to)
    {
        Status = status;
        From = from;
        To = to;
    }

    /// <summary>The status the service held during this period.</summary>
    public PdfTrustServiceStatus Status { get; }

    /// <summary>When the status started, as the list states it.</summary>
    public DateTimeOffset From { get; }

    /// <summary>When the next status started, or null for the current period.</summary>
    public DateTimeOffset? To { get; }

    /// <summary>
    /// Whether a service with this period could be relied on at <paramref name="at"/>: it was
    /// granted, and the moment is inside the window.
    /// </summary>
    /// <param name="at">The instant to judge, normally the signing time.</param>
    public bool Covers(DateTimeOffset at)
        => Status == PdfTrustServiceStatus.Granted && at >= From && (To is not { } to || at < to);
}

/// <summary>One service a trust list entry declares, with the certificates it names.</summary>
/// <remarks>
/// The certificates are the digital identities the list binds to the service — for a CA/QC service,
/// the authority's own certificates, not the ones it issues. A service whose digital identity names
/// no certificate this reader could decode is still reported, with an empty
/// <see cref="Certificates"/>: it exists in the list, and it covers nothing.
/// </remarks>
public sealed class PdfTrustService
{
    private readonly List<byte[]> _thumbprints = [];

    internal PdfTrustService(
        string providerName,
        string name,
        PdfTrustServiceType type,
        string typeIdentifier,
        PdfTrustServiceStatus status,
        string statusIdentifier,
        bool isQualified,
        IReadOnlyList<X509Certificate2> certificates,
        IReadOnlyList<PdfTrustServicePeriod> periods)
    {
        ProviderName = providerName;
        Name = name;
        Type = type;
        TypeIdentifier = typeIdentifier;
        Status = status;
        StatusIdentifier = statusIdentifier;
        IsQualified = isQualified;
        Certificates = certificates;
        Periods = periods;

        foreach (var certificate in certificates)
        {
            _thumbprints.Add(SHA256.HashData(certificate.RawData));
        }
    }

    /// <summary>The name of the provider that operates the service, as the list gives it.</summary>
    public string ProviderName { get; }

    /// <summary>The name of the service, as the list gives it. Empty when the list gives none.</summary>
    public string Name { get; }

    /// <summary>The kind of service, classified from <see cref="TypeIdentifier"/>.</summary>
    public PdfTrustServiceType Type { get; }

    /// <summary>The service type URI exactly as the list wrote it.</summary>
    public string TypeIdentifier { get; }

    /// <summary>The service's current status, classified from <see cref="StatusIdentifier"/>.</summary>
    public PdfTrustServiceStatus Status { get; }

    /// <summary>The service status URI exactly as the list wrote it.</summary>
    public string StatusIdentifier { get; }

    /// <summary>
    /// Whether the list marks the service as qualified: a <c>/QC</c> service type, or a
    /// <c>QC…</c> qualifier.
    /// </summary>
    /// <remarks>
    /// This is the list's claim about the service, not about a signature made with one of its
    /// certificates. <see cref="PdfSignatureTrustCoverage"/> is where the two are put together.
    /// </remarks>
    public bool IsQualified { get; }

    /// <summary>The certificates the service's digital identity names, in list order.</summary>
    public IReadOnlyList<X509Certificate2> Certificates { get; }

    /// <summary>The first named certificate, or null when the service names none.</summary>
    public X509Certificate2? Certificate => Certificates.Count > 0 ? Certificates[0] : null;

    /// <summary>
    /// The status history oldest first, the current status last. Never empty for a service this
    /// reader accepted.
    /// </summary>
    public IReadOnlyList<PdfTrustServicePeriod> Periods { get; }

    /// <summary>When the current status started.</summary>
    public DateTimeOffset? ValidFrom => Periods.Count > 0 ? Periods[^1].From : null;

    /// <summary>
    /// When the current status ends. Null for every service a list publishes: the end of the
    /// current status is the start of the next one, which the list does not know yet.
    /// </summary>
    public DateTimeOffset? ValidTo => Periods.Count > 0 ? Periods[^1].To : null;

    /// <summary>Whether a granted period of this service covers <paramref name="at"/>.</summary>
    /// <param name="at">The instant to judge.</param>
    public bool Covers(DateTimeOffset at)
    {
        foreach (var period in Periods)
        {
            if (period.Covers(at))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether one of the service's certificates is <paramref name="certificate"/>.</summary>
    /// <param name="certificate">The certificate to look for, compared by its DER bytes.</param>
    public bool Covers(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        var raw = certificate.RawData;

        foreach (var listed in Certificates)
        {
            if (listed.RawData.AsSpan().SequenceEqual(raw))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether one of the service's certificates hashes to <paramref name="sha256Thumbprint"/>.</summary>
    /// <param name="sha256Thumbprint">The SHA-256 digest of the certificate's DER bytes.</param>
    public bool Covers(ReadOnlySpan<byte> sha256Thumbprint)
    {
        foreach (var thumbprint in _thumbprints)
        {
            if (thumbprint.AsSpan().SequenceEqual(sha256Thumbprint))
            {
                return true;
            }
        }

        return false;
    }
}
