using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

#pragma warning disable CA5350 // OCSP identifies an issuer by a SHA-1 hash; the algorithm is not a choice here.

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// What the verifier proves about the signatures on the revocation evidence a store embeds: the
/// answer's signer, the list's authority, and the three ways a signature fails to be evidence.
/// </summary>
/// <remarks>
/// Every response is built here, in memory, with certificates the test generated; no responder is
/// ever contacted, and a tampered answer is tampered on purpose so the report has something to say.
/// </remarks>
public sealed class RevocationSignatureTests
{
    private const string Sha256WithRsa = "1.2.840.113549.1.1.11";
    private const string RsaPss = "1.2.840.113549.1.1.10";

    private static readonly DateTimeOffset SignedAt = new(2026, 2, 1, 10, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ProducedAt = new(2026, 2, 1, 9, 0, 0, TimeSpan.Zero);

    private const string ProducedStem = "20260201090000";
    private const string ProducedShifted = "20260201090001";

    [Fact]
    public void A_good_answer_signed_by_its_issuer_is_reported_valid()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);

        var reading = Assert.Single(Read(signer, root, [Ocsp(signer, root, root)]));

        Assert.Equal(PdfRevocationKind.Ocsp, reading.Kind);
        Assert.Equal(PdfRevocationSignatureStatus.Valid, reading.Status);
        Assert.True(reading.IsValid);
        Assert.Null(reading.Error);
        Assert.Equal(signer.Subject, reading.Subject);
        Assert.Equal(root.Subject, reading.Issuer);
        Assert.Equal(Convert.ToHexString(signer.SerialNumberBytes.Span), reading.SerialNumber);
        Assert.Equal(root.Subject, reading.SignerSubject);
        Assert.Equal(root.SerialNumber, reading.SignerSerialNumber);
    }

    [Fact]
    public void An_answer_whose_signature_was_altered_is_reported_invalid_with_the_certificate_it_names()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);

        var reading = Assert.Single(Read(signer, root, [TamperSignature(Ocsp(signer, root, root))]));

        Assert.Equal(PdfRevocationSignatureStatus.Invalid, reading.Status);
        Assert.Equal(signer.Subject, reading.Subject);
        Assert.Equal(Convert.ToHexString(signer.SerialNumberBytes.Span), reading.SerialNumber);
        Assert.Null(reading.SignerSubject);
        Assert.Contains("does not verify", reading.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_answer_whose_signed_bytes_changed_is_reported_invalid()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);

        // One digit of producedAt, inside tbsResponseData: the signature no longer covers the bytes.
        var tampered = Replace(Ocsp(signer, root, root), ProducedStem, ProducedShifted);
        var reading = Assert.Single(Read(signer, root, [tampered]));

        Assert.Equal(PdfRevocationSignatureStatus.Invalid, reading.Status);
        Assert.Equal(signer.Subject, reading.Subject);
        Assert.Contains("does not verify", reading.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_answer_signed_by_another_authority_is_reported_with_an_unexpected_signer()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);
        using var impostor = TestCertificates.Root("CN=M3scalinum Test Root, O=Impostor, C=FR");

        var reading = Assert.Single(Read(signer, root, [Ocsp(signer, root, impostor, embed: [impostor])]));

        Assert.Equal(PdfRevocationSignatureStatus.UnexpectedSigner, reading.Status);
        Assert.Equal(impostor.Subject, reading.SignerSubject);
        Assert.Contains("did not authorise", reading.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_delegated_responder_the_issuer_authorised_is_accepted()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);
        using var responder = Responder(root, authorised: true);

        var answer = Ocsp(signer, root, responder, embed: [responder]);
        var reading = Assert.Single(Read(signer, root, [answer]));

        Assert.Equal(PdfRevocationSignatureStatus.Valid, reading.Status);
        Assert.Equal(responder.Subject, reading.SignerSubject);
        Assert.Null(reading.Error);
    }

    [Fact]
    public void A_delegated_responder_without_the_ocsp_signing_usage_is_not_accepted()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);
        using var responder = Responder(root, authorised: false);

        var answer = Ocsp(signer, root, responder, embed: [responder]);
        var reading = Assert.Single(Read(signer, root, [answer]));

        Assert.Equal(PdfRevocationSignatureStatus.UnexpectedSigner, reading.Status);
        Assert.Equal(responder.Subject, reading.SignerSubject);
        Assert.Contains("id-kp-OCSPSigning", reading.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_delegated_answer_is_evidence_the_store_can_use()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);
        using var responder = Responder(root, authorised: true);

        var pdf = Sign(signer, root, [Ocsp(signer, root, responder, embed: [responder])]);

        Assert.True(PdfSignatureVerifier.ReadRevocation(pdf)[0].OcspCovers);
        Assert.Equal(
            PdfRevocationSignatureStatus.Valid,
            Assert.Single(PdfSignatureVerifier.ReadRevocationSignatures(pdf)).Status);
    }

    [Fact]
    public void An_answer_without_a_usable_algorithm_is_reported_without_a_signature()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);

        var reading = Assert.Single(Read(signer, root, [Ocsp(signer, root, root, algorithm: RsaPss)]));

        Assert.Equal(PdfRevocationSignatureStatus.NoSignature, reading.Status);
        Assert.Contains("no signature this reader can check", reading.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_correct_crl_is_reported_valid()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);

        var reading = Assert.Single(Read(signer, root, crls: [TestRevocation.Crl(root)]));

        Assert.Equal(PdfRevocationKind.Crl, reading.Kind);
        Assert.Equal(PdfRevocationSignatureStatus.Valid, reading.Status);
        Assert.Equal(root.Subject, reading.Issuer);
        Assert.Equal(root.Subject, reading.SignerSubject);
        Assert.Null(reading.Subject);
        Assert.Null(reading.SerialNumber);
        Assert.Null(reading.Error);
    }

    [Fact]
    public void A_crl_signed_by_another_key_is_reported_invalid()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);
        using var impostor = TestCertificates.Root();

        // The list names the root's subject and is signed with a key of the same name.
        var forged = TestRevocation.ForgedCrl(root, impostor);
        var reading = Assert.Single(Read(signer, root, crls: [forged]));

        Assert.Equal(PdfRevocationSignatureStatus.Invalid, reading.Status);
        Assert.Equal(root.Subject, reading.Issuer);
        Assert.Contains("does not verify", reading.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_crl_naming_an_authority_the_store_does_not_embed_is_reported_with_an_unexpected_signer()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);

        // Only the signer is embedded: nothing in the store is the authority the list names.
        var pdf = PdfSigner.Sign(
            TestPdf.OnePage(),
            signer,
            new PdfSignatureOptions
            {
                SignedAt = SignedAt,
                Dss = new PdfDssOptions
                {
                    Certificates = [TestCertificates.Public(signer)],
                    Crls = [TestRevocation.Crl(root)],
                },
            });

        var reading = Assert.Single(PdfSignatureVerifier.ReadRevocationSignatures(pdf));

        Assert.Equal(PdfRevocationSignatureStatus.UnexpectedSigner, reading.Status);
        Assert.Equal(root.Subject, reading.Issuer);
        Assert.Contains("embeds no certificate of that name", reading.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_crl_with_an_algorithm_this_reader_cannot_check_is_reported_without_a_signature()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);

        var reading = Assert.Single(Read(signer, root, crls: [CrlWithAlgorithm(root, RsaPss)]));

        Assert.Equal(PdfRevocationSignatureStatus.NoSignature, reading.Status);
    }

    [Fact]
    public void A_document_without_a_store_reports_no_revocation_signature()
    {
        using var signer = TestCertificates.Signer();

        var pdf = PdfSigner.Sign(TestPdf.OnePage(), signer, new PdfSignatureOptions { SignedAt = SignedAt });

        Assert.Empty(PdfSignatureVerifier.ReadRevocationSignatures(pdf));
    }

    [Fact]
    public void An_empty_document_is_refused()
    {
        Assert.Throws<PdfSignatureException>(() => PdfSignatureVerifier.ReadRevocationSignatures([]));
    }

    [Fact]
    public void An_answer_that_names_no_certificate_is_still_reported()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);

        var reading = Assert.Single(Read(signer, root, [Ocsp(signer, root, root, withEntry: false)]));

        Assert.Equal(PdfRevocationKind.Ocsp, reading.Kind);
        // The answer names no certificate, so no issuer resolves: the key that signed it verifies,
        // but nothing ties it to an authority the file can name.
        Assert.Equal(PdfRevocationSignatureStatus.UnexpectedSigner, reading.Status);
        Assert.Equal(root.Subject, reading.SignerSubject);
        Assert.Null(reading.Subject);
        Assert.Null(reading.SerialNumber);
    }

    private static byte[] Sign(X509Certificate2 signer, X509Certificate2 issuer, byte[][] answers, byte[][]? crls = null)
        => PdfSigner.Sign(
            TestPdf.OnePage(),
            signer,
            new PdfSignatureOptions
            {
                SignedAt = SignedAt,
                Dss = new PdfDssOptions
                {
                    Certificates = [TestCertificates.Public(signer), TestCertificates.Public(issuer)],
                    OcspResponses = answers.Select(bytes => (ReadOnlyMemory<byte>)bytes).ToList(),
                    Crls = (crls ?? []).Select(bytes => (ReadOnlyMemory<byte>)bytes).ToList(),
                },
            });

    private static IReadOnlyList<PdfRevocationSignatureReading> Read(
        X509Certificate2 signer,
        X509Certificate2 issuer,
        byte[][]? answers = null,
        byte[][]? crls = null)
        => PdfSignatureVerifier.ReadRevocationSignatures(Sign(signer, issuer, answers ?? [], crls));

    /// <summary>A responder certificate, with or without the usage RFC 6960 requires.</summary>
    private static X509Certificate2 Responder(X509Certificate2 issuer, bool authorised)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=M3scalinum Sample Responder, O=M3scalinum, C=FR",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));

        if (authorised)
        {
            request.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.9") }, critical: true));
        }

        var serial = RandomNumberGenerator.GetBytes(9);
        serial[0] = 0x01;

        using var created = request.Create(
            issuer,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
            serial);

        return created.CopyWithPrivateKey(key);
    }

    /// <summary>An OCSP answer saying "good" about <paramref name="subject"/>, signed by <paramref name="signer"/>.</summary>
    private static byte[] Ocsp(
        X509Certificate2 subject,
        X509Certificate2 issuer,
        X509Certificate2 signer,
        X509Certificate2[]? embed = null,
        string algorithm = Sha256WithRsa,
        bool withEntry = true)
    {
        var tbs = new AsnWriter(AsnEncodingRules.DER);

        using (tbs.PushSequence())
        {
            // responderID ::= byName [1] Name — the signer's own name.
            using (tbs.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 1, isConstructed: true)))
            {
                tbs.WriteEncodedValue(signer.SubjectName.RawData);
            }

            tbs.WriteGeneralizedTime(ProducedAt);

            using (tbs.PushSequence())
            {
                if (withEntry)
                {
                using (tbs.PushSequence())
                {
                    using (tbs.PushSequence())
                    {
                        using (tbs.PushSequence())
                        {
                            tbs.WriteObjectIdentifier("1.3.14.3.2.26");
                            tbs.WriteNull();
                        }

                        tbs.WriteOctetString(SHA1.HashData(issuer.SubjectName.RawData));
                        tbs.WriteOctetString(SHA1.HashData(issuer.PublicKey.EncodedKeyValue.RawData));
                        tbs.WriteInteger(subject.SerialNumberBytes.Span);
                    }

                    tbs.WriteNull(new Asn1Tag(TagClass.ContextSpecific, 0));
                    tbs.WriteGeneralizedTime(ProducedAt);
                }
                }
            }
        }

        var encoded = tbs.Encode();
        var signature = Sign(signer, encoded);

        var basic = new AsnWriter(AsnEncodingRules.DER);

        using (basic.PushSequence())
        {
            basic.WriteEncodedValue(encoded);

            using (basic.PushSequence())
            {
                basic.WriteObjectIdentifier(algorithm);
                basic.WriteNull();
            }

            basic.WriteBitString(signature);

            if (embed is { Length: > 0 })
            {
                using (basic.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))
                {
                    foreach (var certificate in embed)
                    {
                        basic.WriteEncodedValue(certificate.RawData);
                    }
                }
            }
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

    /// <summary>A CRL that declares an algorithm this reader has no implementation for.</summary>
    private static byte[] CrlWithAlgorithm(X509Certificate2 issuer, string algorithm)
    {
        var tbs = new AsnWriter(AsnEncodingRules.DER);

        using (tbs.PushSequence())
        {
            tbs.WriteInteger(1);

            using (tbs.PushSequence())
            {
                tbs.WriteObjectIdentifier(algorithm);
                tbs.WriteNull();
            }

            tbs.WriteEncodedValue(issuer.SubjectName.RawData);
            tbs.WriteUtcTime(DateTimeOffset.UtcNow.AddDays(-1));
            tbs.WriteUtcTime(DateTimeOffset.UtcNow.AddDays(30));
        }

        var encoded = tbs.Encode();
        var outer = new AsnWriter(AsnEncodingRules.DER);

        using (outer.PushSequence())
        {
            outer.WriteEncodedValue(encoded);

            using (outer.PushSequence())
            {
                outer.WriteObjectIdentifier(algorithm);
                outer.WriteNull();
            }

            outer.WriteBitString(Sign(issuer, encoded));
        }

        return outer.Encode();
    }

    private static byte[] Sign(X509Certificate2 certificate, byte[] data)
    {
        using var key = certificate.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("the test certificate has no RSA key");

        return key.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    /// <summary>Flips one bit of the answer's last byte, which sits inside the signature value.</summary>
    private static byte[] TamperSignature(byte[] der)
    {
        var tampered = der.ToArray();
        tampered[^1] ^= 0xFF;
        return tampered;
    }

    /// <summary>Rewrites one ASCII run inside the DER, keeping every length unchanged.</summary>
    private static byte[] Replace(byte[] der, string from, string to)
    {
        Assert.Equal(from.Length, to.Length);

        var source = System.Text.Encoding.ASCII.GetBytes(from);
        var replacement = System.Text.Encoding.ASCII.GetBytes(to);
        var tampered = der.ToArray();

        for (var index = 0; index + source.Length <= tampered.Length; index++)
        {
            if (tampered.AsSpan(index, source.Length).SequenceEqual(source))
            {
                replacement.CopyTo(tampered, index);
                return tampered;
            }
        }

        throw new InvalidOperationException($"the answer does not carry {from}");
    }
}
