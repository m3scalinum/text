using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

#pragma warning disable CA5350 // OCSP identifies an issuer by a SHA-1 hash; the algorithm is not a choice here.

namespace M3scalinum.Text.Pdf;

/// <summary>
/// A certificate revocation list, read far enough to answer two questions: was it issued by this
/// certificate's issuer, and does it list this certificate?
/// </summary>
/// <remarks>
/// The signature is verified against the issuer's public key, so a CRL that was not signed by the
/// issuer it names is not evidence of anything. This is deliberately not a general-purpose CRL
/// engine: no delta CRLs, no indirect CRLs, no reason codes.
/// </remarks>
internal sealed class PdfCrl
{
    private readonly byte[] _tbs;
    private readonly byte[] _signature;
    private readonly string _algorithm;

    private PdfCrl(
        byte[] issuerName,
        List<byte[]> revoked,
        DateTimeOffset thisUpdate,
        DateTimeOffset? nextUpdate,
        byte[] tbs,
        byte[] signature,
        string algorithm)
    {
        IssuerName = issuerName;
        Revoked = revoked;
        ThisUpdate = thisUpdate;
        NextUpdate = nextUpdate;
        _tbs = tbs;
        _signature = signature;
        _algorithm = algorithm;
    }

    /// <summary>The DER of the <c>issuer</c> name of the CRL.</summary>
    public byte[] IssuerName { get; }

    /// <summary>The serial numbers the list names, as DER integers' two's-complement bytes.</summary>
    public IReadOnlyList<byte[]> Revoked { get; }

    /// <summary>The moment the list says it was issued, from its <c>thisUpdate</c>.</summary>
    public DateTimeOffset ThisUpdate { get; }

    /// <summary>The moment the issuer wants the list replaced, from its <c>nextUpdate</c>, when it has one.</summary>
    public DateTimeOffset? NextUpdate { get; }

    /// <summary>Reads a DER CRL, or returns <see langword="null"/> when it cannot be read.</summary>
    public static PdfCrl? Parse(ReadOnlySpan<byte> der)
    {
        try
        {
            var outer = new AsnReader(der.ToArray(), AsnEncodingRules.DER).ReadSequence();
            var tbsEncoded = outer.ReadEncodedValue();
            var tbs = new AsnReader(tbsEncoded, AsnEncodingRules.DER).ReadSequence();

            if (tbs.PeekTag().HasSameClassAndValue(Asn1Tag.Integer))
            {
                tbs.ReadInteger();
            }

            var signatureAlgorithm = tbs.ReadSequence();
            var algorithm = signatureAlgorithm.ReadObjectIdentifier();
            ReadOptionalNull(signatureAlgorithm);

            var issuer = tbs.ReadEncodedValue().ToArray();
            var thisUpdate = ReadTime(tbs);

            DateTimeOffset? nextUpdate = null;
            if (tbs.HasData && IsTime(tbs.PeekTag()))
            {
                nextUpdate = ReadTime(tbs);
            }

            var revoked = new List<byte[]>();
            if (tbs.HasData && tbs.PeekTag().HasSameClassAndValue(Asn1Tag.Sequence))
            {
                var entries = tbs.ReadSequence();
                while (entries.HasData)
                {
                    var entry = entries.ReadSequence();
                    revoked.Add(entry.ReadIntegerBytes().ToArray());
                }
            }

            var value = outer.ReadSequence();
            value.ReadObjectIdentifier();
            var signature = outer.ReadBitString(out _);

            return new PdfCrl(issuer, revoked, thisUpdate, nextUpdate, tbsEncoded.ToArray(), signature, algorithm);
        }
        catch (AsnContentException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Whether the list names <paramref name="issuer"/> as the authority that issued it.</summary>
    public bool IssuedBy(X509Certificate2 issuer)
        => IssuerName.AsSpan().SequenceEqual(issuer.SubjectName.RawData);

    /// <summary>Whether the list's signature verifies with the issuer's key.</summary>
    public bool SignatureVerifies(X509Certificate2 issuer)
        => PdfAsn.Verify(issuer, _tbs, _signature, _algorithm);

    /// <summary>
    /// What the list's own signature proves, given the authority that publishes it.
    /// </summary>
    /// <param name="authority">The certificate the list names as its issuer.</param>
    /// <param name="error">Why the signature is not valid, when it is not.</param>
    /// <remarks>
    /// A CRL carries no responder identity of its own: the name in its <c>tbsCertList</c> is the
    /// authority, and the only question is whether that authority's key made the signature. A list
    /// that names one authority and is signed by another is reported
    /// <see cref="PdfRevocationSignatureStatus.Invalid"/>, not as a valid list with a wrong name.
    /// </remarks>
    public PdfRevocationSignatureStatus SignatureStatus(X509Certificate2 authority, out string? error)
    {
        if (_tbs.Length == 0 || _signature.Length == 0 || !PdfAsn.Supports(_algorithm))
        {
            error = "the list carries no signature this reader can check";
            return PdfRevocationSignatureStatus.NoSignature;
        }

        if (!IssuedBy(authority))
        {
            error = $"the list names {IssuedAs()} as its issuer, not {authority.Subject}";
            return PdfRevocationSignatureStatus.Invalid;
        }

        if (!PdfAsn.Verify(authority, _tbs, _signature, _algorithm))
        {
            error = $"the list names {authority.Subject} as its issuer but its signature does not verify with that key";
            return PdfRevocationSignatureStatus.Invalid;
        }

        error = null;
        return PdfRevocationSignatureStatus.Valid;
    }

    /// <summary>The issuer name the list carries, rendered for a report.</summary>
    public string IssuedAs()
    {
        try
        {
            return new X500DistinguishedName(IssuerName).Name;
        }
        catch (CryptographicException)
        {
            return "an unreadable name";
        }
    }

    /// <summary>Whether the serial number appears among the revoked certificates.</summary>
    public bool Lists(ReadOnlySpan<byte> serial)
    {
        foreach (var entry in Revoked)
        {
            if (entry.AsSpan().SequenceEqual(serial))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTime(Asn1Tag tag)
        => tag.TagValue is (int)UniversalTagNumber.UtcTime or (int)UniversalTagNumber.GeneralizedTime;

    private static DateTimeOffset ReadTime(AsnReader reader)
        => reader.PeekTag().TagValue == (int)UniversalTagNumber.GeneralizedTime
            ? reader.ReadGeneralizedTime()
            : reader.ReadUtcTime();

    private static void ReadOptionalNull(AsnReader reader)
    {
        if (reader.HasData && reader.PeekTag().HasSameClassAndValue(Asn1Tag.Null))
        {
            reader.ReadNull();
        }
    }
}

/// <summary>An OCSP response, read far enough to find a good answer for a certificate.</summary>
internal sealed class PdfOcspResponse
{
    private readonly byte[] _tbs;
    private readonly byte[] _signature;
    private readonly string _algorithm;
    private readonly IReadOnlyList<X509Certificate2> _certificates;

    private PdfOcspResponse(
        List<PdfOcspEntry> entries,
        DateTimeOffset producedAt,
        byte[] tbs,
        byte[] signature,
        string algorithm,
        IReadOnlyList<X509Certificate2> certificates)
    {
        Entries = entries;
        ProducedAt = producedAt;
        _tbs = tbs;
        _signature = signature;
        _algorithm = algorithm;
        _certificates = certificates;
    }

    /// <summary>The single responses the answer carries.</summary>
    public IReadOnlyList<PdfOcspEntry> Entries { get; }

    /// <summary>The moment the responder says it produced the answer, from its <c>producedAt</c>.</summary>
    public DateTimeOffset ProducedAt { get; }

    /// <summary>
    /// The responder certificates the answer embeds, from <c>BasicOCSPResponse.certs</c>. A delegated
    /// responder's certificate travels here; the issuer's never has to.
    /// </summary>
    public IReadOnlyList<X509Certificate2> Certificates => _certificates;

    /// <summary>Reads a DER <c>OCSPResponse</c>, or returns <see langword="null"/> when it cannot be read.</summary>
    public static PdfOcspResponse? Parse(ReadOnlySpan<byte> der)
    {
        try
        {
            var outer = new AsnReader(der.ToArray(), AsnEncodingRules.DER).ReadSequence();
            var status = (int)outer.ReadEnumeratedValue<OcspResponseStatus>();

            if (status != 0 || !outer.HasData)
            {
                // 0 is "successful"; anything else carries no single response to read.
                return null;
            }

            var responseBytes = outer.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true));
            var responseType = responseBytes.ReadObjectIdentifier();

            if (responseType != "1.3.6.1.5.5.7.48.1.1")
            {
                return null;
            }

            var basic = new AsnReader(responseBytes.ReadOctetString(), AsnEncodingRules.DER).ReadSequence();
            var tbsEncoded = basic.ReadEncodedValue();
            var signatureAlgorithm = basic.ReadSequence();
            var algorithm = signatureAlgorithm.ReadObjectIdentifier();
            var signature = basic.ReadBitString(out _);

            var certificates = new List<X509Certificate2>();
            var certificatesTag = new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true);

            if (basic.HasData && basic.PeekTag().HasSameClassAndValue(certificatesTag))
            {
                var embedded = basic.ReadSequence(certificatesTag);

                while (embedded.HasData)
                {
                    // A certificate that does not decode is dropped: the answer is still readable
                    // without it, and the signature status then says no responder was identified.
                    if (PdfAsn.LoadCertificate(embedded.ReadEncodedValue().ToArray()) is { } certificate)
                    {
                        certificates.Add(certificate);
                    }
                }
            }

            var tbs = new AsnReader(tbsEncoded, AsnEncodingRules.DER).ReadSequence();

            if (tbs.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
            {
                tbs.ReadInteger(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true));
            }

            tbs.ReadEncodedValue();        // responderID
            var producedAt = tbs.ReadGeneralizedTime();

            var entries = new List<PdfOcspEntry>();
            var responses = tbs.ReadSequence();

            while (responses.HasData)
            {
                entries.Add(PdfOcspEntry.Read(responses.ReadSequence()));
            }

            return new PdfOcspResponse(entries, producedAt, tbsEncoded.ToArray(), signature, algorithm, certificates);
        }
        catch (AsnContentException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Whether the answer's signature verifies with <paramref name="signer"/>'s key.</summary>
    public bool SignatureVerifies(X509Certificate2 signer)
        => PdfAsn.Verify(signer, _tbs, _signature, _algorithm);

    /// <summary>
    /// What the answer's signature proves, and who made it.
    /// </summary>
    /// <param name="issuer">The certificate that issued the certificate this answer speaks about, or
    /// null when the store does not carry it.</param>
    /// <param name="embedded">Certificates the store carries, considered after the answer's own
    /// <c>certs</c> when looking for a delegated responder.</param>
    /// <param name="signer">The certificate whose key verified the signature, when one did.</param>
    /// <param name="error">Why the signature is not valid, when it is not.</param>
    /// <remarks>
    /// <para>
    /// The issuer is tried first: an answer an issuer signed itself needs no delegation. When that
    /// key does not verify, each responder certificate the answer embeds — then each certificate the
    /// store embeds — is tried. A key that verifies identifies the signer; whether that signer was
    /// allowed to answer is a separate question, and a responder that is not the issuer is accepted
    /// only when the issuer issued its certificate and that certificate carries
    /// <c>id-kp-OCSPSigning</c>, as RFC 6960 requires.
    /// </para>
    /// <para>
    /// A key that verifies nothing is <see cref="PdfRevocationSignatureStatus.Invalid"/>: the bytes
    /// were changed, or the signature was made by a key the file does not carry. The two are not
    /// distinguishable from the file, which is why the report says both.
    /// </para>
    /// </remarks>
    public PdfRevocationSignatureStatus SignatureStatus(
        X509Certificate2? issuer,
        IReadOnlyList<X509Certificate2> embedded,
        out X509Certificate2? signer,
        out string? error)
    {
        signer = null;

        if (_tbs.Length == 0 || _signature.Length == 0 || !PdfAsn.Supports(_algorithm))
        {
            error = "the answer carries no signature this reader can check";
            return PdfRevocationSignatureStatus.NoSignature;
        }

        if (issuer is not null && PdfAsn.Verify(issuer, _tbs, _signature, _algorithm))
        {
            signer = issuer;
            error = null;
            return PdfRevocationSignatureStatus.Valid;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in Candidates(embedded))
        {
            if (!seen.Add(candidate.Thumbprint)
                || !PdfAsn.Verify(candidate, _tbs, _signature, _algorithm))
            {
                continue;
            }

            signer = candidate;

            if (PdfAsn.IsAuthorisedResponder(candidate, issuer, ProducedAt))
            {
                error = null;
                return PdfRevocationSignatureStatus.Valid;
            }

            error = issuer is null
                ? $"the answer is signed by {candidate.Subject}, and the store does not carry the issuer it speaks for"
                : $"the answer is signed by {candidate.Subject}, which {issuer.Subject} did not authorise as an"
                  + " OCSP responder (no id-kp-OCSPSigning extended key usage issued by that authority)";
            return PdfRevocationSignatureStatus.UnexpectedSigner;
        }

        error = $"the signature does not verify over tbsResponseData with "
                + (issuer is null ? "any certificate the file carries" : $"the key of {issuer.Subject} or of any responder certificate the answer embeds");
        return PdfRevocationSignatureStatus.Invalid;
    }

    private IEnumerable<X509Certificate2> Candidates(IReadOnlyList<X509Certificate2> embedded)
    {
        foreach (var certificate in _certificates)
        {
            yield return certificate;
        }

        foreach (var certificate in embedded)
        {
            yield return certificate;
        }
    }

    /// <summary>
    /// Whether the answer says "good" about <paramref name="subject"/>, for an issuer
    /// <paramref name="issuer"/>. The certificate identifier is matched both ways the standard
    /// allows — by the hash of the issuer's name and by the hash of its key — because responders
    /// disagree on which one they fill in.
    /// </summary>
    /// <remarks>
    /// Status alone is not evidence: a "good" answer signed by nobody the issuer authorised proves
    /// nothing, which is why <see cref="PdfOcspResponse.SignatureStatus"/> is the half that decides
    /// whether the answer counts.
    /// </remarks>
    public bool GivesGoodAnswer(X509Certificate2 subject, X509Certificate2 issuer)
        => EntryFor(subject, issuer) is { Status: 0 };

    /// <summary>
    /// Whether the answer carries a response about <paramref name="subject"/>, whatever its status.
    /// Used to say a certificate is covered even when the answer is not "good".
    /// </summary>
    public bool Answers(X509Certificate2 subject, X509Certificate2 issuer)
        => EntryFor(subject, issuer) is not null;

    private PdfOcspEntry? EntryFor(X509Certificate2 subject, X509Certificate2 issuer)
    {
        var nameHash = PdfAsn.Sha1(issuer.SubjectName.RawData);
        var keyHash = PdfAsn.Sha1(issuer.PublicKey.EncodedKeyValue.RawData);

        foreach (var entry in Entries)
        {
            if (!entry.Serial.AsSpan().SequenceEqual(subject.SerialNumberBytes.Span))
            {
                continue;
            }

            if (entry.IssuerNameHash.AsSpan().SequenceEqual(nameHash)
                || entry.IssuerKeyHash.AsSpan().SequenceEqual(keyHash))
            {
                return entry;
            }
        }

        return null;
    }
}

/// <summary>One <c>SingleResponse</c> of an OCSP answer.</summary>
/// <param name="HashAlgorithm">The digest the identifier hashes with.</param>
/// <param name="IssuerNameHash">The hash of the issuer's name.</param>
/// <param name="IssuerKeyHash">The hash of the issuer's public key.</param>
/// <param name="Serial">The certificate's serial number, as DER bytes.</param>
/// <param name="Status"><c>0</c> good, <c>1</c> revoked, <c>2</c> unknown.</param>
/// <param name="ThisUpdate">The moment the responder says this status is current.</param>
internal sealed record PdfOcspEntry(
    string HashAlgorithm,
    byte[] IssuerNameHash,
    byte[] IssuerKeyHash,
    byte[] Serial,
    int Status,
    DateTimeOffset ThisUpdate)
{
    public static PdfOcspEntry Read(AsnReader response)
    {
        var id = response.ReadSequence();
        var algorithm = id.ReadSequence();
        var hash = algorithm.ReadObjectIdentifier();
        var nameHash = id.ReadOctetString();
        var keyHash = id.ReadOctetString();
        var serial = id.ReadIntegerBytes().ToArray();

        var tag = response.PeekTag();
        var status = tag.HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)) ? 0
            : tag.HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 1)) ? 1
            : 2;

        // The status is a CHOICE — empty for "good", a revocation time for "revoked", nothing for
        // "unknown" — but it is one encoded element either way, and only its tag matters here.
        response.ReadEncodedValue();
        var thisUpdate = response.PeekTag().TagValue == (int)UniversalTagNumber.GeneralizedTime
            ? response.ReadGeneralizedTime()
            : response.ReadUtcTime();

        return new PdfOcspEntry(hash, nameHash, keyHash, serial, status, thisUpdate);
    }
}

/// <summary>Signature checks shared by the revocation readers.</summary>
internal static class PdfAsn
{
    /// <summary>Whether <paramref name="signature"/> over <paramref name="data"/> verifies with <paramref name="owner"/>'s key.</summary>
    public static bool Verify(
        X509Certificate2 owner,
        ReadOnlySpan<byte> data,
        ReadOnlySpan<byte> signature,
        string algorithm)
    {
        var (hash, isEcdsa) = algorithm switch
        {
            "1.2.840.113549.1.1.11" => (HashAlgorithmName.SHA256, false),
            "1.2.840.113549.1.1.12" => (HashAlgorithmName.SHA384, false),
            "1.2.840.113549.1.1.13" => (HashAlgorithmName.SHA512, false),
            "1.2.840.113549.1.1.5" => (HashAlgorithmName.SHA1, false),
            "1.2.840.10045.4.3.2" => (HashAlgorithmName.SHA256, true),
            "1.2.840.10045.4.3.3" => (HashAlgorithmName.SHA384, true),
            "1.2.840.10045.4.3.4" => (HashAlgorithmName.SHA512, true),
            _ => (default, false),
        };

        if (hash == default)
        {
            return false;
        }

        try
        {
            if (isEcdsa)
            {
                using var ecdsa = owner.GetECDsaPublicKey();
                return ecdsa is not null && ecdsa.VerifyData(data, signature, hash, DSASignatureFormat.Rfc3279DerSequence);
            }

            using var rsa = owner.GetRSAPublicKey();
            return rsa is not null && rsa.VerifyData(data, signature, hash, RSASignaturePadding.Pkcs1);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    public static byte[] Sha1(ReadOnlySpan<byte> data) => SHA1.HashData(data);

    /// <summary>The digest <paramref name="algorithm"/> names, or an empty array when this reader has no
    /// implementation of it (an OCSP certificate identifier with an unknown hash matches nothing).</summary>
    public static byte[] Digest(string algorithm, ReadOnlySpan<byte> data) => algorithm switch
    {
        "1.3.14.3.2.26" => SHA1.HashData(data),
        "2.16.840.1.101.3.4.2.1" => SHA256.HashData(data),
        "2.16.840.1.101.3.4.2.2" => SHA384.HashData(data),
        "2.16.840.1.101.3.4.2.3" => SHA512.HashData(data),
        _ => [],
    };

    /// <summary>The OID of the extended key usage a delegated OCSP responder must carry, RFC 6960.</summary>
    public const string OcspSigningUsage = "1.3.6.1.5.5.7.3.9";

    /// <summary>Whether this reader can check a signature made with <paramref name="algorithm"/>.</summary>
    public static bool Supports(string algorithm)
        => algorithm is "1.2.840.113549.1.1.11" or "1.2.840.113549.1.1.12" or "1.2.840.113549.1.1.13"
            or "1.2.840.10045.4.3.2" or "1.2.840.10045.4.3.3" or "1.2.840.10045.4.3.4";

    /// <summary>A certificate from its DER bytes, or null when they are not one.</summary>
    public static X509Certificate2? LoadCertificate(byte[] der)
    {
        try
        {
#if NET9_0_OR_GREATER
            return X509CertificateLoader.LoadCertificate(der);
#else
            return new X509Certificate2(der);
#endif
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether <paramref name="responder"/> is a responder certificate <paramref name="issuer"/>
    /// authorised: its certificate chains to that issuer, it carries <c>id-kp-OCSPSigning</c>, and
    /// its validity window covers the moment the answer was produced. No download is attempted.
    /// </summary>
    public static bool IsAuthorisedResponder(
        X509Certificate2 responder,
        X509Certificate2? issuer,
        DateTimeOffset producedAt)
    {
        if (issuer is null || !HasExtendedKeyUsage(responder, OcspSigningUsage))
        {
            return false;
        }

        if (responder.NotBefore.ToUniversalTime() > producedAt.UtcDateTime
            || responder.NotAfter.ToUniversalTime() < producedAt.UtcDateTime)
        {
            return false;
        }

        try
        {
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.DisableCertificateDownloads = true;
            chain.ChainPolicy.CustomTrustStore.Add(issuer);

            return chain.Build(responder);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>Whether the certificate carries <paramref name="usage"/> in its extended key usage.</summary>
    public static bool HasExtendedKeyUsage(X509Certificate2 certificate, string usage)
    {
        foreach (var extension in certificate.Extensions)
        {
            if (extension is X509EnhancedKeyUsageExtension enhanced)
            {
                foreach (var oid in enhanced.EnhancedKeyUsages)
                {
                    if (oid.Value == usage)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>Whether two DER integers name the same number, whatever their leading zero octets.</summary>
    public static bool SameInteger(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
        => new BigInteger(left, isUnsigned: true, isBigEndian: true)
            == new BigInteger(right, isUnsigned: true, isBigEndian: true);
}

/// <summary>The status field of an OCSP response. Only <c>Successful</c> carries an answer.</summary>
internal enum OcspResponseStatus
{
    Successful = 0,
}

/// <summary>
/// What a document security store carries for one certificate: whether revocation data covers it,
/// and when that data was issued.
/// </summary>
/// <remarks>
/// The report exists so a caller can keep — or refresh — its own cache: an answer issued yesterday
/// is still good for the certificate it names, and the moment it was issued is what says how long
/// that will last. It says nothing about whether the certificate's chain is trusted, and nothing
/// about whether the signer's own policy accepts the responder that signed the answer.
/// </remarks>
public sealed class PdfRevocationCoverage
{
    internal PdfRevocationCoverage(
        string subject,
        string issuer,
        string serialNumber,
        bool ocspCovers,
        DateTimeOffset? ocspIssuedAt,
        bool crlCovers,
        DateTimeOffset? crlIssuedAt)
    {
        Subject = subject;
        Issuer = issuer;
        SerialNumber = serialNumber;
        OcspCovers = ocspCovers;
        OcspIssuedAt = ocspIssuedAt;
        CrlCovers = crlCovers;
        CrlIssuedAt = crlIssuedAt;
    }

    /// <summary>The subject of the certificate, as an X.500 string.</summary>
    public string Subject { get; }

    /// <summary>The issuer of the certificate, as an X.500 string.</summary>
    public string Issuer { get; }

    /// <summary>The certificate's serial number, as uppercase hexadecimal, in the DER byte order.</summary>
    public string SerialNumber { get; }

    /// <summary>Whether an embedded OCSP answer names this certificate and was signed by its issuer.</summary>
    public bool OcspCovers { get; }

    /// <summary>The moment the covering OCSP answer was produced; null when none covers.</summary>
    public DateTimeOffset? OcspIssuedAt { get; }

    /// <summary>Whether an embedded CRL was signed by this certificate's issuer.</summary>
    public bool CrlCovers { get; }

    /// <summary>The moment the covering CRL says it was issued; null when none covers.</summary>
    public DateTimeOffset? CrlIssuedAt { get; }

    /// <summary>Whether either kind of evidence covers this certificate.</summary>
    public bool IsCovered => OcspCovers || CrlCovers;
}
