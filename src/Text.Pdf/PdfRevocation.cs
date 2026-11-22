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

    private PdfOcspResponse(List<PdfOcspEntry> entries, DateTimeOffset producedAt, byte[] tbs, byte[] signature, string algorithm)
    {
        Entries = entries;
        ProducedAt = producedAt;
        _tbs = tbs;
        _signature = signature;
        _algorithm = algorithm;
    }

    /// <summary>The single responses the answer carries.</summary>
    public IReadOnlyList<PdfOcspEntry> Entries { get; }

    /// <summary>The moment the responder says it produced the answer, from its <c>producedAt</c>.</summary>
    public DateTimeOffset ProducedAt { get; }

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

            return new PdfOcspResponse(entries, producedAt, tbsEncoded.ToArray(), signature, algorithm);
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
    /// Whether the answer says "good" about <paramref name="subject"/>, for an issuer
    /// <paramref name="issuer"/>. The certificate identifier is matched both ways the standard
    /// allows — by the hash of the issuer's name and by the hash of its key — because responders
    /// disagree on which one they fill in.
    /// </summary>
    /// <remarks>
    /// A delegated responder — an answer signed by a certificate other than the issuer — is not
    /// recognized: its certificate would have to be embedded and authorised by the issuer, and this
    /// reader does not follow that path.
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
