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

    private PdfCrl(byte[] issuerName, List<byte[]> revoked, byte[] tbs, byte[] signature, string algorithm)
    {
        IssuerName = issuerName;
        Revoked = revoked;
        _tbs = tbs;
        _signature = signature;
        _algorithm = algorithm;
    }

    /// <summary>The DER of the <c>issuer</c> name of the CRL.</summary>
    public byte[] IssuerName { get; }

    /// <summary>The serial numbers the list names, as DER integers' two's-complement bytes.</summary>
    public IReadOnlyList<byte[]> Revoked { get; }

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
            ReadTime(tbs);

            if (tbs.HasData && IsTime(tbs.PeekTag()))
            {
                ReadTime(tbs);
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

            return new PdfCrl(issuer, revoked, tbsEncoded.ToArray(), signature, algorithm);
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

    private static void ReadTime(AsnReader reader)
    {
        if (reader.PeekTag().TagValue == (int)UniversalTagNumber.GeneralizedTime)
        {
            reader.ReadGeneralizedTime();
        }
        else
        {
            reader.ReadUtcTime();
        }
    }

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

    private PdfOcspResponse(List<PdfOcspEntry> entries, byte[] tbs, byte[] signature, string algorithm)
    {
        Entries = entries;
        _tbs = tbs;
        _signature = signature;
        _algorithm = algorithm;
    }

    /// <summary>The single responses the answer carries.</summary>
    public IReadOnlyList<PdfOcspEntry> Entries { get; }

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
            tbs.ReadGeneralizedTime();     // producedAt

            var entries = new List<PdfOcspEntry>();
            var responses = tbs.ReadSequence();

            while (responses.HasData)
            {
                entries.Add(PdfOcspEntry.Read(responses.ReadSequence()));
            }

            return new PdfOcspResponse(entries, tbsEncoded.ToArray(), signature, algorithm);
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
    {
        var nameHash = PdfAsn.Sha1(issuer.SubjectName.RawData);
        var keyHash = PdfAsn.Sha1(issuer.PublicKey.EncodedKeyValue.RawData);

        foreach (var entry in Entries)
        {
            if (entry.Status != 0
                || !entry.Serial.AsSpan().SequenceEqual(subject.SerialNumberBytes.Span))
            {
                continue;
            }

            if (entry.IssuerNameHash.AsSpan().SequenceEqual(nameHash)
                || entry.IssuerKeyHash.AsSpan().SequenceEqual(keyHash))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>One <c>SingleResponse</c> of an OCSP answer.</summary>
/// <param name="HashAlgorithm">The digest the identifier hashes with.</param>
/// <param name="IssuerNameHash">The hash of the issuer's name.</param>
/// <param name="IssuerKeyHash">The hash of the issuer's public key.</param>
/// <param name="Serial">The certificate's serial number, as DER bytes.</param>
/// <param name="Status"><c>0</c> good, <c>1</c> revoked, <c>2</c> unknown.</param>
internal sealed record PdfOcspEntry(
    string HashAlgorithm,
    byte[] IssuerNameHash,
    byte[] IssuerKeyHash,
    byte[] Serial,
    int Status)
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

        // The status itself is a CHOICE of empty or complex values; nothing to read for "good".
        if (status == 0)
        {
            response.ReadEncodedValue();
        }

        return new PdfOcspEntry(hash, nameHash, keyHash, serial, status);
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
