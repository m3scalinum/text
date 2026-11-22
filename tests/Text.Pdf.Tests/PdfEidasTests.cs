using System;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Xunit;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// The two eIDAS acts that are not a person's signature: the electronic seal, and the qualified
/// timestamp — what the writer produces for them, and what a reader can and cannot conclude.
/// </summary>
/// <remarks>
/// The fake trust lists are hand-written ETSI TS 119 612 documents carrying the DER bytes of the
/// certificates the tests generated, so a match is a match of bytes rather than a string this suite
/// controls. Nothing here reaches a network: the timestamp authority answers over a stub transport.
/// </remarks>
public sealed class PdfEidasTests
{
    private const string Granted = "http://uri.etsi.org/TrstSvc/TrustedList/Svcstatus/granted";
    private const string Withdrawn = "http://uri.etsi.org/TrstSvc/TrustedList/Svcstatus/withdrawn";
    private const string SealService = "http://uri.etsi.org/TrstSvc/Svctype/AdESealQC";
    private const string SignatureService = "http://uri.etsi.org/TrstSvc/Svctype/AdESigQC";
    private const string QualifiedTimestampService = "http://uri.etsi.org/TrstSvc/Svctype/TSA/QC";
    private const string PlainTimestampService = "http://uri.etsi.org/TrstSvc/Svctype/TSA";
    private const string CaService = "http://uri.etsi.org/TrstSvc/Svctype/CA/QC";

    private static readonly DateTimeOffset SignedAt = new(2026, 2, 1, 10, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset StampedAt = new(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly Uri Authority = new("https://tsa.example.test/rfc3161");

    [Fact]
    public void A_seal_is_signed_with_the_document_signing_usage_and_a_claimed_signing_time()
    {
        using var certificate = SealCertificate();

        var signed = PdfSigner.Sign(TestPdf.OnePage(), certificate, new PdfSignatureOptions
        {
            Kind = PdfSignatureKind.Seal,
            SignedAt = SignedAt,
        });

        // The claim is in the document, and in the CMS as the CAdES signing-time attribute.
        Assert.Contains("/M(D:20260201103000+00'00')", Encoding.Latin1.GetString(signed), StringComparison.Ordinal);

        var signer = Signer(signed);
        var attribute = Assert.IsType<CryptographicAttributeObject>(SigningTimeAttribute(signer));

        var value = new System.Formats.Asn1.AsnReader(attribute.Values[0].RawData, System.Formats.Asn1.AsnEncodingRules.BER);
        Assert.Equal(SignedAt, value.ReadUtcTime());

        var verdict = Assert.Single(PdfSignatureVerifier.ReadEidas(signed).Signatures);

        Assert.Equal(PdfSignatureKind.Seal, verdict.Kind);
        Assert.True(verdict.HasSigningTimeAttribute);
        Assert.Equal(SignedAt, verdict.SigningTime);
        Assert.False(verdict.HasTimestamp);
        Assert.True(PdfSignatureVerifier.Verify(signed).AllIntact);
    }

    [Fact]
    public void A_seal_is_refused_when_the_certificate_lacks_the_document_signing_usage()
    {
        using var certificate = TestCertificates.Signer();

        var exception = Assert.Throws<PdfSignatureException>(() => PdfSigner.Sign(
            TestPdf.OnePage(),
            certificate,
            new PdfSignatureOptions { Kind = PdfSignatureKind.Seal, SignedAt = SignedAt }));

        Assert.Contains("1.3.6.1.5.5.7.3.36", exception.Message, StringComparison.Ordinal);
        Assert.Contains("id-kp-documentSigning", exception.Message, StringComparison.Ordinal);
        Assert.Contains("extended key usage", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_seal_timestamped_by_a_listed_service_reads_as_seal_timestamped_by_a_qualified_service()
    {
        using var seal = SealCertificate();
        using var tsa = new FakeTsa { Time = StampedAt };
        using var client = tsa.Client();

        var signed = PdfSigner.Sign(TestPdf.OnePage(), seal, new PdfSignatureOptions
        {
            Kind = PdfSignatureKind.Seal,
            SignedAt = SignedAt,
            TimestampAuthority = Authority,
            TimestampClient = client,
        });

        var list = PdfLotl.Read(Bytes(List(
            Service(seal, SealService) + Service(tsa.Certificate, QualifiedTimestampService))));

        var report = PdfSignatureVerifier.ReadEidas(signed, null, list);

        Assert.True(report.TrustListSupplied);
        var verdict = Assert.Single(report.Signatures);

        Assert.Equal(PdfSignatureKind.Seal, verdict.Kind);
        Assert.True(verdict.HasTimestamp);
        Assert.Equal(StampedAt, verdict.TimestampedTime);
        Assert.Equal(tsa.Certificate.Subject, verdict.TimestampAuthoritySubject);
        Assert.Equal(PdfEidasQualification.Qualified, verdict.TimestampAuthorityQualification);
        Assert.Contains("qualified", verdict.TimestampAuthorityExplanation, StringComparison.Ordinal);
        Assert.Contains(StampedAt.ToString("u"), verdict.TimestampAuthorityExplanation, StringComparison.Ordinal);
        Assert.Equal(PdfEidasQualification.Qualified, verdict.SignerQualification);
        Assert.Equal(PdfSignatureLevel.BaselineT, verdict.Level);
        Assert.Equal("seal, timestamped by a qualified service", verdict.Summary);
    }

    [Fact]
    public void Without_a_trust_list_the_timestamp_authority_is_not_evaluated()
    {
        using var seal = SealCertificate();
        using var tsa = new FakeTsa { Time = StampedAt };
        using var client = tsa.Client();

        var signed = PdfSigner.Sign(TestPdf.OnePage(), seal, new PdfSignatureOptions
        {
            Kind = PdfSignatureKind.Seal,
            SignedAt = SignedAt,
            TimestampAuthority = Authority,
            TimestampClient = client,
        });

        var report = PdfSignatureVerifier.ReadEidas(signed);
        var verdict = Assert.Single(report.Signatures);

        Assert.False(report.TrustListSupplied);
        Assert.True(verdict.HasTimestamp);
        Assert.Equal(PdfEidasQualification.NotEvaluated, verdict.TimestampAuthorityQualification);
        Assert.Equal(PdfEidasQualification.NotEvaluated, verdict.SignerQualification);
        Assert.Contains("no trust list", verdict.TimestampAuthorityExplanation, StringComparison.Ordinal);
        Assert.DoesNotContain("not qualified", verdict.TimestampAuthorityExplanation, StringComparison.Ordinal);
        Assert.Equal("seal, timestamped; the authority's qualification was not evaluated", verdict.Summary);
    }

    [Fact]
    public void A_listed_timestamp_service_that_is_withdrawn_is_reported_as_not_qualified()
    {
        using var seal = SealCertificate();
        using var tsa = new FakeTsa { Time = StampedAt };
        using var client = tsa.Client();

        var signed = PdfSigner.Sign(TestPdf.OnePage(), seal, new PdfSignatureOptions
        {
            Kind = PdfSignatureKind.Seal,
            SignedAt = SignedAt,
            TimestampAuthority = Authority,
            TimestampClient = client,
        });

        var list = PdfLotl.Read(Bytes(List(
            Service(tsa.Certificate, QualifiedTimestampService, status: Withdrawn))));

        var verdict = Assert.Single(PdfSignatureVerifier.ReadEidas(signed, null, list).Signatures);

        Assert.Equal(PdfEidasQualification.NotQualified, verdict.TimestampAuthorityQualification);
        Assert.Equal("seal, timestamped by a service the list does not mark qualified", verdict.Summary);
    }

    [Fact]
    public void A_timestamp_service_the_list_does_not_mark_qualified_is_not_qualified()
    {
        using var seal = SealCertificate();
        using var tsa = new FakeTsa { Time = StampedAt };
        using var client = tsa.Client();

        var signed = PdfSigner.Sign(TestPdf.OnePage(), seal, new PdfSignatureOptions
        {
            Kind = PdfSignatureKind.Seal,
            SignedAt = SignedAt,
            TimestampAuthority = Authority,
            TimestampClient = client,
        });

        var list = PdfLotl.Read(Bytes(List(Service(tsa.Certificate, PlainTimestampService))));

        var verdict = Assert.Single(PdfSignatureVerifier.ReadEidas(signed, null, list).Signatures);

        Assert.Equal(PdfEidasQualification.NotQualified, verdict.TimestampAuthorityQualification);
        Assert.Contains("does not mark qualified", verdict.TimestampAuthorityExplanation, StringComparison.Ordinal);
    }

    [Fact]
    public void A_service_that_is_not_a_timestamp_service_does_not_qualify_the_authority()
    {
        using var seal = SealCertificate();
        using var tsa = new FakeTsa { Time = StampedAt };
        using var client = tsa.Client();

        var signed = PdfSigner.Sign(TestPdf.OnePage(), seal, new PdfSignatureOptions
        {
            Kind = PdfSignatureKind.Seal,
            SignedAt = SignedAt,
            TimestampAuthority = Authority,
            TimestampClient = client,
        });

        var list = PdfLotl.Read(Bytes(List(Service(tsa.Certificate, CaService))));
        var verdict = Assert.Single(PdfSignatureVerifier.ReadEidas(signed, null, list).Signatures);

        Assert.Equal(PdfEidasQualification.NotQualified, verdict.TimestampAuthorityQualification);
        Assert.Contains("not a timestamp service", verdict.TimestampAuthorityExplanation, StringComparison.Ordinal);
    }

    [Fact]
    public void A_seal_covered_by_a_signature_service_is_not_a_qualified_seal()
    {
        using var seal = SealCertificate();
        var signed = PdfSigner.Sign(TestPdf.OnePage(), seal, new PdfSignatureOptions
        {
            Kind = PdfSignatureKind.Seal,
            SignedAt = SignedAt,
        });

        var list = PdfLotl.Read(Bytes(List(Service(seal, SignatureService))));
        var verdict = Assert.Single(PdfSignatureVerifier.ReadEidas(signed, null, list).Signatures);

        Assert.Equal(PdfEidasQualification.NotQualified, verdict.SignerQualification);
        Assert.Contains("not a seal service", verdict.SignerExplanation, StringComparison.Ordinal);
    }

    [Fact]
    public void A_person_signature_is_written_exactly_as_before_and_reads_as_a_person()
    {
        using var certificate = TestCertificates.Signer();

        var options = new PdfSignatureOptions { SignedAt = SignedAt };
        var first = PdfSigner.Sign(TestPdf.OnePage(), certificate, options);
        var second = PdfSigner.Sign(TestPdf.OnePage(), certificate, options);

        // Same inputs still give the same bytes, so the seal support did not change the person path.
        Assert.Equal(first, second);
        Assert.Null(SigningTimeAttribute(Signer(first)));

        var verdict = Assert.Single(PdfSignatureVerifier.ReadEidas(first).Signatures);

        Assert.Equal(PdfSignatureKind.Person, verdict.Kind);
        Assert.False(verdict.HasSigningTimeAttribute);
        Assert.Null(verdict.SigningTime);
        Assert.False(verdict.HasTimestamp);
        Assert.Equal(PdfSignatureLevel.BaselineB, verdict.Level);
        Assert.Equal("person, not timestamped", verdict.Summary);
    }

    [Fact]
    public void The_certificate_decides_the_kind_even_when_the_caller_says_person()
    {
        using var certificate = SealCertificate();

        var signed = PdfSigner.Sign(TestPdf.OnePage(), certificate, new PdfSignatureOptions { SignedAt = SignedAt });

        var verdict = Assert.Single(PdfSignatureVerifier.ReadEidas(signed).Signatures);

        Assert.Equal(PdfSignatureKind.Seal, verdict.Kind);
        Assert.False(verdict.HasSigningTimeAttribute);
    }

    [Fact]
    public void A_document_timestamp_is_read_with_the_same_list_and_the_same_reserve()
    {
        using var seal = SealCertificate();
        using var tsa = new FakeTsa { Time = StampedAt };
        using var client = tsa.Client();

        var signed = PdfSigner.Sign(TestPdf.OnePage(), seal, new PdfSignatureOptions
        {
            Kind = PdfSignatureKind.Seal,
            SignedAt = SignedAt,
        });

        var stamped = PdfDocumentTimestamp.Add(signed, Authority, client);
        var list = PdfLotl.Read(Bytes(List(Service(tsa.Certificate, QualifiedTimestampService))));

        var evaluated = PdfSignatureVerifier.ReadEidas(stamped, null, list);
        var timestamp = Assert.Single(evaluated.DocumentTimestamps);

        Assert.True(timestamp.IsIntact);
        Assert.Equal(StampedAt, timestamp.TimestampedTime);
        Assert.Equal(tsa.Certificate.Subject, timestamp.AuthoritySubject);
        Assert.Equal(PdfEidasQualification.Qualified, timestamp.Qualification);

        var unevaluated = Assert.Single(PdfSignatureVerifier.ReadEidas(stamped).DocumentTimestamps);
        Assert.Equal(PdfEidasQualification.NotEvaluated, unevaluated.Qualification);
    }

    [Fact]
    public void A_seal_service_the_list_does_not_mark_qualified_is_not_qualified()
    {
        using var seal = SealCertificate();
        var signed = PdfSigner.Sign(TestPdf.OnePage(), seal, new PdfSignatureOptions
        {
            Kind = PdfSignatureKind.Seal,
            SignedAt = SignedAt,
        });

        var list = PdfLotl.Read(Bytes(List(
            Service(seal, "http://uri.etsi.org/TrstSvc/Svctype/AdESeal"))));
        var verdict = Assert.Single(PdfSignatureVerifier.ReadEidas(signed, null, list).Signatures);

        Assert.Equal(PdfEidasQualification.NotQualified, verdict.SignerQualification);
        Assert.Contains("does not mark qualified", verdict.SignerExplanation, StringComparison.Ordinal);
    }

    [Fact]
    public void A_signature_without_a_signer_certificate_cannot_be_qualified_by_a_list()
    {
        using var certificate = SealCertificate();
        var broken = Broken(PdfSigner.Sign(TestPdf.OnePage(), certificate, new PdfSignatureOptions
        {
            Kind = PdfSignatureKind.Seal,
            SignedAt = SignedAt,
        }));

        var list = PdfLotl.Read(Bytes(List(Service(certificate, SealService))));
        var verdict = Assert.Single(PdfSignatureVerifier.ReadEidas(broken, null, list).Signatures);

        Assert.Equal(PdfEidasQualification.NotQualified, verdict.SignerQualification);
        Assert.Contains("no signing certificate", verdict.SignerExplanation, StringComparison.Ordinal);
    }

    [Fact]
    public void A_byte_range_that_does_not_fit_the_file_is_reported_without_a_verdict()
    {
        using var certificate = SealCertificate();
        var signed = PdfSigner.Sign(TestPdf.OnePage(), certificate, new PdfSignatureOptions
        {
            Kind = PdfSignatureKind.Seal,
            SignedAt = SignedAt,
        });

        var text = Encoding.Latin1.GetString(signed);
        var corrupted = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"/ByteRange\[0 (\d{10}) (\d{10}) (\d{10})\]",
            "/ByteRange[0 $1 $2 9999999999]",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        Assert.NotEqual(text, corrupted);

        var verdict = Assert.Single(PdfSignatureVerifier.ReadEidas(Encoding.Latin1.GetBytes(corrupted)).Signatures);

        Assert.False(verdict.HasTimestamp);
        Assert.Equal(PdfEidasQualification.NotEvaluated, verdict.TimestampAuthorityQualification);
        Assert.Contains("not evaluated", verdict.TimestampAuthorityExplanation, StringComparison.Ordinal);
    }

    [Fact]
    public void A_signature_whose_bytes_do_not_decode_is_still_reported()
    {
        using var certificate = SealCertificate();
        var signed = PdfSigner.Sign(TestPdf.OnePage(), certificate, new PdfSignatureOptions
        {
            Kind = PdfSignatureKind.Seal,
            SignedAt = SignedAt,
        });

        var verdict = Assert.Single(PdfSignatureVerifier.ReadEidas(Broken(signed)).Signatures);

        Assert.False(verdict.HasTimestamp);
        Assert.Equal(PdfEidasQualification.NotEvaluated, verdict.TimestampAuthorityQualification);
        Assert.Contains("not evaluated", verdict.TimestampAuthorityExplanation, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_signature_kind_is_refused()
    {
        using var certificate = TestCertificates.Signer();

        var exception = Assert.Throws<PdfSignatureException>(() => PdfSigner.Sign(
            TestPdf.OnePage(),
            certificate,
            new PdfSignatureOptions { Kind = (PdfSignatureKind)99, SignedAt = SignedAt }));

        Assert.Contains("not one this writer knows", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>A certificate issued for document signing: the usage an eSeal needs.</summary>
    private static X509Certificate2 SealCertificate(string subject = "CN=M3scalinum Test Seal, O=M3scalinum, C=FR")
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, critical: true));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid(PdfEidasCertificate.DocumentSigningOid, "documentSigning") },
                critical: false));

        using var created = request.CreateSelfSigned(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2036, 1, 1, 0, 0, 0, TimeSpan.Zero));

        return TestCertificates.WithKey(created);
    }

    /// <summary>The signer whose CMS a document's first signature carries.</summary>
    private static SignerInfo Signer(byte[] pdf)
    {
        var document = PdfDocument.Parse(Encoding.Latin1.GetString(pdf));
        var dictionary = document.SignatureDictionaries()[0];

        Assert.True(PdfSignatureVerifier.TryCovered(pdf, dictionary, out var content, out var error), error);

        var cms = new SignedCms(new ContentInfo(content), detached: true);
        cms.Decode(PdfSignatureVerifier.ReadDer(pdf, dictionary));

        return cms.SignerInfos[0];
    }

    /// <summary>A copy of the document whose first signature value no longer decodes: 0x30 becomes 0x00.</summary>
    private static byte[] Broken(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        var start = text.IndexOf("/Contents<", StringComparison.Ordinal) + "/Contents<".Length;
        var hex = text[start] == '3' ? '0' : '3';

        return Encoding.Latin1.GetBytes(text[..start] + hex + text[(start + 1)..]);
    }

    /// <summary>The CAdES <c>signing-time</c> signed attribute, when the CMS carries one.</summary>
    private static CryptographicAttributeObject? SigningTimeAttribute(SignerInfo signer)
    {
        foreach (var attribute in signer.SignedAttributes)
        {
            if (attribute.Oid?.Value == "1.2.840.113549.1.9.5")
            {
                return attribute;
            }
        }

        return null;
    }

    private static byte[] Bytes(string xml) => Encoding.UTF8.GetBytes(xml);

    private static string List(string services, string territory = "FR") => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <TrustServiceStatusList xmlns="http://uri.etsi.org/02231/v2#">
          <SchemeInformation>
            <TSLVersionIdentifier>5</TSLVersionIdentifier>
            <TSLSequenceNumber>12</TSLSequenceNumber>
            <TSLType>http://uri.etsi.org/TrstSvc/TrustedList/TSLType/EUgeneric</TSLType>
            <SchemeOperatorName><Name xml:lang="en">Test scheme operator</Name></SchemeOperatorName>
            <SchemeTerritory>{{territory}}</SchemeTerritory>
            <ListIssueDateTime>2026-01-01T00:00:00Z</ListIssueDateTime>
            <NextUpdate><dateTime>2026-07-01T00:00:00Z</dateTime></NextUpdate>
          </SchemeInformation>
          <TrustServiceProviderList>
            <TrustServiceProvider>
              <TSPInformation>
                <TSPName><Name xml:lang="en">Test provider</Name></TSPName>
              </TSPInformation>
              <TSPServices>
                {{services}}
              </TSPServices>
            </TrustServiceProvider>
          </TrustServiceProviderList>
        </TrustServiceStatusList>
        """;

    private static string Service(
        X509Certificate2 certificate,
        string type,
        string status = Granted,
        string since = "2020-01-01T00:00:00Z") => $"""
        <TSPService>
          <ServiceInformation>
            <ServiceTypeIdentifier>{type}</ServiceTypeIdentifier>
            <ServiceName><Name xml:lang="en">Test service</Name></ServiceName>
            <ServiceDigitalIdentity>
              <DigitalId><X509Certificate>{Convert.ToBase64String(certificate.RawData)}</X509Certificate></DigitalId>
            </ServiceDigitalIdentity>
            <ServiceStatus>{status}</ServiceStatus>
            <StatusStartingTime>{since}</StatusStartingTime>
          </ServiceInformation>
        </TSPService>
        """;
}
