using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace M3scalinum.Text.Pdf;

/// <summary>A list a list-of-lists points at: the national trust list of one territory.</summary>
/// <remarks>
/// The pointer carries what the list-of-lists knows about the other list — where it is published,
/// which territory it covers, and the certificate its operator signs it with — and nothing else.
/// Fetching that URL is the caller's decision, made with the caller's network policy; this reader
/// never follows a pointer, because a verification that downloaded a document to trust a document
/// would be a verification that can be steered by whoever wrote the first one.
/// </remarks>
public sealed class PdfTrustListPointer
{
    internal PdfTrustListPointer(
        string location,
        string? territory,
        string? operatorName,
        IReadOnlyList<X509Certificate2> certificates)
    {
        Location = location;
        Territory = territory;
        OperatorName = operatorName;
        Certificates = certificates;
    }

    /// <summary>The URL the list is published at, as the pointer writes it. Never followed here.</summary>
    public string Location { get; }

    /// <summary>The territory the pointed list covers, when the pointer states it.</summary>
    public string? Territory { get; }

    /// <summary>The operator of the pointed list, when the pointer states it.</summary>
    public string? OperatorName { get; }

    /// <summary>
    /// The certificates the pointer binds to the pointed list's operator — the ones its signature
    /// is expected to chain to. Empty when the pointer names none.
    /// </summary>
    public IReadOnlyList<X509Certificate2> Certificates { get; }
}

/// <summary>
/// A trust list read from ETSI TS 119 612 XML: a territory's statement, at one moment, of which
/// services are granted and what they cover.
/// </summary>
/// <remarks>
/// The list is data, not evidence: this type parses the XML and answers questions about what it
/// says. It does not verify the list's own XAdES signature, does not fetch anything, and does not
/// check that the list is current — <see cref="IssueDateTime"/>, <see cref="NextUpdate"/> and
/// <see cref="IsExpiredAt"/> are exposed so the caller can, and
/// <see cref="PdfSignatureVerifier.Verify(ReadOnlySpan{byte}, PdfTrustStore, PdfTrustedList)"/> is
/// how the caller puts the list's own signature to the same test.
/// </remarks>
public sealed class PdfTrustedList
{
    internal PdfTrustedList(
        string territory,
        int version,
        long sequenceNumber,
        DateTimeOffset issueDateTime,
        DateTimeOffset? nextUpdate,
        string? listType,
        string? location,
        IReadOnlyList<PdfTrustService> services,
        IReadOnlyList<PdfTrustListPointer> nationalLists)
    {
        Territory = territory;
        Version = version;
        SequenceNumber = sequenceNumber;
        IssueDateTime = issueDateTime;
        NextUpdate = nextUpdate;
        ListType = listType;
        Location = location;
        Services = services;
        NationalLists = nationalLists;
    }

    /// <summary>The territory the list speaks for, from <c>SchemeTerritory</c> — <c>EU</c> for a list of lists.</summary>
    public string Territory { get; }

    /// <summary>The format version the list was written in, from <c>TSLVersionIdentifier</c>.</summary>
    public int Version { get; }

    /// <summary>The sequence number of this edition, from <c>TSLSequenceNumber</c>. It only ever grows.</summary>
    public long SequenceNumber { get; }

    /// <summary>When the list was published, from <c>ListIssueDateTime</c>.</summary>
    public DateTimeOffset IssueDateTime { get; }

    /// <summary>When the next edition is due, when the list states it.</summary>
    public DateTimeOffset? NextUpdate { get; }

    /// <summary>The <c>TSLType</c> URI the list declares, when it declares one.</summary>
    public string? ListType { get; }

    /// <summary>
    /// Whether this is a list of lists (a LOTL): its services describe lists, and
    /// <see cref="NationalLists"/> says where they are published.
    /// </summary>
    public bool IsListOfLists => ListType is not null && ListType.EndsWith("/EUlistofthelists", StringComparison.Ordinal);

    /// <summary>
    /// Where the caller obtained these bytes, passed to <see cref="PdfLotl.Read(ReadOnlySpan{byte}, string)"/>.
    /// The format does not carry a list's own URL, so the caller records it; null otherwise.
    /// </summary>
    public string? Location { get; }

    /// <summary>Every service the list declares, in document order, whatever its status.</summary>
    public IReadOnlyList<PdfTrustService> Services { get; }

    /// <summary>The other trust lists this one points at, in document order. Empty for a national list.</summary>
    public IReadOnlyList<PdfTrustListPointer> NationalLists { get; }

    /// <summary>Whether <paramref name="at"/> is past <see cref="NextUpdate"/>.</summary>
    /// <param name="at">The instant to judge.</param>
    /// <remarks>
    /// A stale list is not refused — its statements about the past are still true — but a caller
    /// that keeps one should say so, which is why the answer is exposed rather than enforced.
    /// </remarks>
    public bool IsExpiredAt(DateTimeOffset at) => NextUpdate is { } next && at > next;

    /// <summary>
    /// The services a granted period of which covers <paramref name="certificate"/> at
    /// <paramref name="at"/>, in document order.
    /// </summary>
    /// <param name="certificate">The certificate to look for.</param>
    /// <param name="at">The instant the coverage is judged at, normally the signing time.</param>
    public IReadOnlyList<PdfTrustService> FindServices(X509Certificate2 certificate, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        return FindServices(SHA256.HashData(certificate.RawData), at);
    }

    /// <summary>
    /// The services a granted period of which covers the certificate whose DER bytes hash to
    /// <paramref name="sha256Thumbprint"/> at <paramref name="at"/>, in document order.
    /// </summary>
    /// <param name="sha256Thumbprint">The SHA-256 digest of the certificate's DER bytes.</param>
    /// <param name="at">The instant the coverage is judged at, normally the signing time.</param>
    public IReadOnlyList<PdfTrustService> FindServices(ReadOnlySpan<byte> sha256Thumbprint, DateTimeOffset at)
    {
        var matches = new List<PdfTrustService>();

        foreach (var service in Services)
        {
            if (service.Covers(sha256Thumbprint) && service.Covers(at))
            {
                matches.Add(service);
            }
        }

        return matches;
    }

    /// <summary>
    /// The first service covering <paramref name="certificate"/> at <paramref name="at"/>, or null
    /// when the list grants none.
    /// </summary>
    /// <param name="certificate">The certificate to look for.</param>
    /// <param name="at">The instant the coverage is judged at.</param>
    public PdfTrustService? FindService(X509Certificate2 certificate, DateTimeOffset at)
        => FirstOrNull(FindServices(certificate, at));

    /// <summary>
    /// The first service covering the certificate with this SHA-256 digest at
    /// <paramref name="at"/>, or null.
    /// </summary>
    /// <param name="sha256Thumbprint">The SHA-256 digest of the certificate's DER bytes.</param>
    /// <param name="at">The instant the coverage is judged at.</param>
    public PdfTrustService? FindService(ReadOnlySpan<byte> sha256Thumbprint, DateTimeOffset at)
        => FirstOrNull(FindServices(sha256Thumbprint, at));

    /// <summary>
    /// Says, for every signature of <paramref name="verification"/>, which service of this list
    /// covers its signing certificate, and whether that service is qualified.
    /// </summary>
    /// <param name="verification">What a verifier already established about the document.</param>
    /// <returns>One entry per signature, in the same order as
    /// <see cref="PdfSignatureVerification.Signatures"/>.</returns>
    /// <remarks>
    /// The instant each signature is judged at is its timestamp when the verifier established one,
    /// and its claimed time otherwise — a claim, which is why the explanation says which was used.
    /// The signing certificate is matched, and nothing else: a certificate issued by a qualified CA
    /// is not itself qualified, and inferring that would be inventing a fact the list does not state.
    /// </remarks>
    public IReadOnlyList<PdfSignatureTrustCoverage> Evaluate(PdfSignatureVerification verification)
    {
        ArgumentNullException.ThrowIfNull(verification);

        var coverage = new List<PdfSignatureTrustCoverage>();

        for (var index = 0; index < verification.Signatures.Count; index++)
        {
            coverage.Add(Evaluate(index, verification.Signatures[index]));
        }

        return coverage;
    }

    private PdfSignatureTrustCoverage Evaluate(int index, PdfSignatureInfo signature)
    {
        var at = signature.TimestampedSigningTime ?? signature.ClaimedSigningTime;
        var evidenced = signature.TimestampedSigningTime is not null;

        if (signature.SignerCertificate is null)
        {
            return new PdfSignatureTrustCoverage(
                index, signature.SignerSubject, null, false, at,
                "not qualified: the signature carries no signing certificate, so no service can cover it");
        }

        if (at is null)
        {
            return new PdfSignatureTrustCoverage(
                index, signature.SignerSubject, null, false, null,
                "not qualified: the signature carries no time this reader can judge a service period against");
        }

        var service = FindService(signature.SignerCertificate, at.Value);

        if (service is null)
        {
            return new PdfSignatureTrustCoverage(
                index, signature.SignerSubject, null, false, at,
                $"not qualified: the signing certificate is not found in any granted service of {Territory} at {at.Value:u}"
                + (evidenced ? " (timestamped time)" : " (claimed time, not evidence)"));
        }

        if (!service.IsQualified)
        {
            return new PdfSignatureTrustCoverage(
                index, signature.SignerSubject, service, false, at,
                $"not qualified: {Territory} covers the signing certificate with a service of type {service.TypeIdentifier},"
                + " which the list does not mark qualified");
        }

        return new PdfSignatureTrustCoverage(
            index, signature.SignerSubject, service, true, at,
            $"qualified: {Territory} lists {service.ProviderName} as granting {service.TypeIdentifier}"
            + $" from {service.ValidFrom:u}"
            + (evidenced ? " (timestamped time)" : " (claimed time, not evidence)"));
    }

    private static PdfTrustService? FirstOrNull(IReadOnlyList<PdfTrustService> services)
        => services.Count > 0 ? services[0] : null;
}

/// <summary>What a trust list says about one signature's signing certificate.</summary>
/// <remarks>
/// The entry never turns a signature invalid: a certificate the list does not know gives
/// <see cref="IsQualified"/> false with an explanation, while the signature itself keeps whatever
/// <see cref="PdfSignatureInfo"/> established about its bytes and its chain.
/// </remarks>
public sealed class PdfSignatureTrustCoverage
{
    internal PdfSignatureTrustCoverage(
        int signatureIndex,
        string? signerSubject,
        PdfTrustService? service,
        bool isQualified,
        DateTimeOffset? evaluatedAt,
        string explanation)
    {
        SignatureIndex = signatureIndex;
        SignerSubject = signerSubject;
        Service = service;
        IsQualified = isQualified;
        EvaluatedAt = evaluatedAt;
        Explanation = explanation;
    }

    /// <summary>The position of the signature in <see cref="PdfSignatureVerification.Signatures"/>.</summary>
    public int SignatureIndex { get; }

    /// <summary>The subject of the signing certificate the entry is about.</summary>
    public string? SignerSubject { get; }

    /// <summary>The service that covers the signing certificate, or null when none does.</summary>
    public PdfTrustService? Service { get; }

    /// <summary>
    /// Whether the list states that a qualified service covered the certificate at the moment the
    /// entry was judged.
    /// </summary>
    public bool IsQualified { get; }

    /// <summary>The instant the service's period was judged against, when there was one.</summary>
    public DateTimeOffset? EvaluatedAt { get; }

    /// <summary>Why the certificate is or is not qualified, in one sentence naming the facts used.</summary>
    public string Explanation { get; }
}

/// <summary>A verification of a document, and what a trust list says about each of its signatures.</summary>
/// <remarks>
/// The two answers are kept apart on purpose: <see cref="Signatures"/> is what the verifier checked
/// about the PDF's bytes, and <see cref="Coverage"/> is what a list published by a territory
/// declares. A signature can be intact and untrusted while qualified, or intact and trusted while
/// no list knows its signer.
/// </remarks>
public sealed class PdfTrustListVerification
{
    internal PdfTrustListVerification(
        PdfSignatureVerification signatures,
        IReadOnlyList<PdfSignatureTrustCoverage> coverage)
    {
        Signatures = signatures;
        Coverage = coverage;
    }

    /// <summary>What the verifier established about the document, unchanged.</summary>
    public PdfSignatureVerification Signatures { get; }

    /// <summary>One entry per signature, in signature order; empty when no list was supplied.</summary>
    public IReadOnlyList<PdfSignatureTrustCoverage> Coverage { get; }

    /// <summary>Whether every signature is covered by a qualified service of the list.</summary>
    public bool AllQualified
    {
        get
        {
            foreach (var entry in Coverage)
            {
                if (!entry.IsQualified)
                {
                    return false;
                }
            }

            return Coverage.Count > 0;
        }
    }
}
