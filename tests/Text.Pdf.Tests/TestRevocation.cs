using System;
using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

#pragma warning disable CA5350 // OCSP identifies an issuer by a SHA-1 hash; the algorithm is not a choice here.

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// Builds the revocation material a document security store carries, as DER, so the tests can prove
/// the verifier reads it rather than only counting streams.
/// </summary>
internal static class TestRevocation
{
    private const string Sha256WithRsa = "1.2.840.113549.1.1.11";

    /// <summary>A CRL issued by <paramref name="issuer"/>, listing <paramref name="revoked"/>.</summary>
    public static byte[] Crl(X509Certificate2 issuer, params byte[][] revoked)
        => Build(issuer.SubjectName.RawData, issuer, revoked);

    /// <summary>
    /// A CRL that names <paramref name="issuer"/> but is signed by <paramref name="signer"/>, to model
    /// a list somebody forged.
    /// </summary>
    public static byte[] ForgedCrl(X509Certificate2 issuer, X509Certificate2 signer, params byte[][] revoked)
        => Build(issuer.SubjectName.RawData, signer, revoked);

    private static byte[] Build(byte[] issuerName, X509Certificate2 signer, byte[][] revoked)
    {
        var tbs = new AsnWriter(AsnEncodingRules.DER);
        var thisUpdate = DateTimeOffset.UtcNow.AddDays(-1);
        var nextUpdate = DateTimeOffset.UtcNow.AddDays(30);

        using (tbs.PushSequence())
        {
            tbs.WriteInteger(1);

            using (tbs.PushSequence())
            {
                tbs.WriteObjectIdentifier(Sha256WithRsa);
                tbs.WriteNull();
            }

            tbs.WriteEncodedValue(issuerName);
            tbs.WriteUtcTime(thisUpdate);
            tbs.WriteUtcTime(nextUpdate);

            if (revoked.Length > 0)
            {
                using (tbs.PushSequence())
                {
                    foreach (var serial in revoked)
                    {
                        using (tbs.PushSequence())
                        {
                            tbs.WriteInteger(serial.AsSpan());
                            tbs.WriteUtcTime(thisUpdate);
                        }
                    }
                }
            }
        }

        var encoded = tbs.Encode();
        using var key = signer.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("the CRL signer has no RSA key");

        var signature = key.SignData(encoded, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var outer = new AsnWriter(AsnEncodingRules.DER);

        using (outer.PushSequence())
        {
            outer.WriteEncodedValue(encoded);

            using (outer.PushSequence())
            {
                outer.WriteObjectIdentifier(Sha256WithRsa);
                outer.WriteNull();
            }

            outer.WriteBitString(signature);
        }

        return outer.Encode();
    }

    /// <summary>An OCSP answer, signed by <paramref name="issuer"/>, saying "good" about <paramref name="subject"/>.</summary>
    public static byte[] OcspGood(X509Certificate2 subject, X509Certificate2 issuer)
    {
        var tbs = new AsnWriter(AsnEncodingRules.DER);

        using (tbs.PushSequence())
        {
            // responderID ::= byName [1] Name
            using (tbs.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 1, isConstructed: true)))
            {
                tbs.WriteEncodedValue(issuer.SubjectName.RawData);
            }

            tbs.WriteGeneralizedTime(DateTimeOffset.UtcNow.AddMinutes(-5));

            using (tbs.PushSequence())                       // responses
            {
                using (tbs.PushSequence())                   // SingleResponse
                {
                    using (tbs.PushSequence())               // CertID
                    {
                        using (tbs.PushSequence())           // AlgorithmIdentifier
                        {
                            tbs.WriteObjectIdentifier("1.3.14.3.2.26");
                            tbs.WriteNull();
                        }

                        tbs.WriteOctetString(SHA1.HashData(issuer.SubjectName.RawData));
                        tbs.WriteOctetString(SHA1.HashData(issuer.PublicKey.EncodedKeyValue.RawData));
                        tbs.WriteInteger(subject.SerialNumberBytes.Span);
                    }

                    tbs.WriteNull(new Asn1Tag(TagClass.ContextSpecific, 0));   // certStatus good
                    tbs.WriteGeneralizedTime(DateTimeOffset.UtcNow.AddMinutes(-5));
                }
            }
        }

        var encoded = tbs.Encode();
        using var key = issuer.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("the OCSP signer has no RSA key");

        var signature = key.SignData(encoded, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var basic = new AsnWriter(AsnEncodingRules.DER);

        using (basic.PushSequence())
        {
            basic.WriteEncodedValue(encoded);

            using (basic.PushSequence())
            {
                basic.WriteObjectIdentifier(Sha256WithRsa);
                basic.WriteNull();
            }

            basic.WriteBitString(signature);
        }

        var response = new AsnWriter(AsnEncodingRules.DER);

        using (response.PushSequence())
        {
            response.WriteEnumeratedValue((OcspResponseStatus)0);

            using (response.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))
            {
                response.WriteObjectIdentifier("1.3.6.1.5.5.7.48.1.1");
                response.WriteOctetString(basic.Encode());
            }
        }

        return response.Encode();
    }
}
