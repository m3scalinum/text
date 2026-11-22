using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Xunit;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// What the trust list reader accepts, what it refuses, and what the verifier then concludes.
/// </summary>
/// <remarks>
/// Every fixture is a hand-written ETSI TS 119 612 document carrying a real certificate the test
/// generated, so a match is a match of DER bytes rather than of a string this suite controls.
/// </remarks>
public sealed class PdfLotlTests
{
    private const string QualifiedCa = "http://uri.etsi.org/TrstSvc/Svctype/CA/QC";
    private const string PlainCa = "http://uri.etsi.org/TrstSvc/Svctype/CA";
    private const string Granted = "http://uri.etsi.org/TrstSvc/TrustedList/Svcstatus/granted";
    private const string Withdrawn = "http://uri.etsi.org/TrstSvc/TrustedList/Svcstatus/withdrawn";
    private const string ListOfLists = "http://uri.etsi.org/TrstSvc/TrustedList/TSLType/EUlistofthelists";

    private static readonly DateTimeOffset SignedAt = new(2026, 2, 1, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_minimal_list_is_read_with_its_territory_edition_and_one_service()
    {
        using var certificate = TestCertificates.Signer();

        var list = PdfLotl.Read(Bytes(List(Service(certificate))));

        Assert.Equal("FR", list.Territory);
        Assert.Equal(5, list.Version);
        Assert.Equal(12, list.SequenceNumber);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), list.IssueDateTime);
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero), list.NextUpdate);
        Assert.False(list.IsExpiredAt(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.True(list.IsExpiredAt(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.False(list.IsListOfLists);
        Assert.Empty(list.NationalLists);

        var service = Assert.Single(list.Services);
        Assert.Equal("Test provider", service.ProviderName);
        Assert.Equal("Test service", service.Name);
        Assert.Equal(PdfTrustServiceType.QualifiedCertificateAuthority, service.Type);
        Assert.Equal(QualifiedCa, service.TypeIdentifier);
        Assert.Equal(PdfTrustServiceStatus.Granted, service.Status);
        Assert.True(service.IsQualified);
        Assert.Equal(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), service.ValidFrom);
        Assert.Null(service.ValidTo);
        Assert.Equal(certificate.Thumbprint, service.Certificate!.Thumbprint);
    }

    [Fact]
    public void Several_services_with_overlapping_periods_are_all_reported()
    {
        using var certificate = TestCertificates.Signer();

        // The first service was granted in 2020 and never withdrawn. The second was granted in
        // 2022 and withdrawn in 2024 — the format writes the second fact as the start of the next
        // status, which is what turns the history into a window that ends.
        var permanent = Service(certificate);
        var retired = Service(certificate, status: Withdrawn, since: "2024-01-01T00:00:00Z", history: History(Granted, "2022-01-01T00:00:00Z"));

        var list = PdfLotl.Read(Bytes(List(permanent + retired)));

        Assert.Equal(2, list.Services.Count);

        var retiredService = list.Services[1];
        Assert.Equal(2, retiredService.Periods.Count);
        Assert.Equal(PdfTrustServiceStatus.Granted, retiredService.Periods[0].Status);
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), retiredService.Periods[0].To);
        Assert.Equal(PdfTrustServiceStatus.Withdrawn, retiredService.Status);

        // 2023 is inside both services' granted windows.
        var overlapping = list.FindServices(certificate, new DateTimeOffset(2023, 6, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(2, overlapping.Count);

        // 2025 is inside the permanent service only: the retired one is withdrawn.
        var later = list.FindServices(certificate, new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var only = Assert.Single(later);
        Assert.Equal(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), only.ValidFrom);

        // 2019 is before either service was granted.
        Assert.Empty(list.FindServices(certificate, new DateTimeOffset(2019, 6, 1, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void A_withdrawn_service_is_read_but_never_covers_a_later_signature()
    {
        using var certificate = TestCertificates.Signer();

        var list = PdfLotl.Read(Bytes(List(Service(certificate, status: Withdrawn, since: "2024-01-01T00:00:00Z"))));

        var service = Assert.Single(list.Services);
        Assert.Equal(PdfTrustServiceStatus.Withdrawn, service.Status);

        Assert.Null(list.FindService(certificate, new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.Empty(list.FindServices(certificate, new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void A_certificate_is_found_by_its_sha256_thumbprint()
    {
        using var certificate = TestCertificates.Signer();
        using var other = TestCertificates.Signer("CN=Other, O=M3scalinum.Text.Pdf, C=FR");

        var list = PdfLotl.Read(Bytes(List(Service(certificate))));
        var at = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

        var thumbprint = SHA256.HashData(certificate.RawData);

        Assert.Same(Assert.Single(list.Services), list.FindService(thumbprint, at));
        Assert.Null(list.FindService(SHA256.HashData(other.RawData), at));
        Assert.True(Assert.Single(list.Services).Covers(thumbprint));
    }

    [Fact]
    public void A_certificate_absent_from_the_list_is_reported_as_not_found()
    {
        using var listed = TestCertificates.Signer();
        using var signer = TestCertificates.Signer("CN=Not listed, O=M3scalinum.Text.Pdf, C=FR");

        var list = PdfLotl.Read(Bytes(List(Service(listed))));

        Assert.Null(list.FindService(signer, SignedAt));
        Assert.Empty(list.FindServices(signer, SignedAt));
    }

    [Fact]
    public void A_service_that_names_no_certificate_is_read_and_covers_nothing()
    {
        var list = PdfLotl.Read(Bytes(List(Service(certificate: null))));

        var service = Assert.Single(list.Services);
        Assert.Empty(service.Certificates);
        Assert.Null(service.Certificate);
    }

    [Fact]
    public void A_document_type_declaration_is_refused_as_an_xml_policy_breach()
    {
        var xml = """
            <!DOCTYPE TrustServiceStatusList [<!ELEMENT TrustServiceStatusList ANY>]>
            <TrustServiceStatusList/>
            """;

        var error = Assert.Throws<PdfTrustListException>(() => PdfLotl.Read(Bytes(xml)));

        Assert.Equal(PdfTrustListError.XmlPolicy, error.Error);
        Assert.Contains("document type", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_external_entity_is_refused_as_an_xml_policy_breach()
    {
        var xml = """
            <?xml version="1.0"?>
            <!DOCTYPE TrustServiceStatusList SYSTEM "file:///c:/windows/win.ini">
            <TrustServiceStatusList/>
            """;

        var error = Assert.Throws<PdfTrustListException>(() => PdfLotl.Read(Bytes(xml)));

        Assert.Equal(PdfTrustListError.XmlPolicy, error.Error);
    }

    [Fact]
    public void A_list_nested_past_the_depth_cap_is_refused()
    {
        var deep = new StringBuilder("<?xml version=\"1.0\"?><TrustServiceStatusList>");

        for (var level = 0; level <= PdfLotl.MaxXmlDepth; level++)
        {
            deep.Append("<d>");
        }

        for (var level = 0; level <= PdfLotl.MaxXmlDepth; level++)
        {
            deep.Append("</d>");
        }

        deep.Append("</TrustServiceStatusList>");

        var error = Assert.Throws<PdfTrustListException>(() => PdfLotl.Read(Bytes(deep.ToString())));

        Assert.Equal(PdfTrustListError.Limit, error.Error);
        Assert.Contains("nests past", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_list_over_the_size_cap_is_refused_before_it_is_scanned()
    {
        var error = Assert.Throws<PdfTrustListException>(
            () => PdfLotl.Read(Enumerable.Repeat((byte)' ', PdfLotl.MaxXmlBytes + 1).ToArray()));

        Assert.Equal(PdfTrustListError.Limit, error.Error);
        Assert.Contains($"{PdfLotl.MaxXmlBytes}", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Bytes_that_are_not_a_trust_list_are_refused_as_malformed()
    {
        var notXml = Assert.Throws<PdfTrustListException>(() => PdfLotl.Read(Bytes("this is not XML")));
        Assert.Equal(PdfTrustListError.Malformed, notXml.Error);

        var wrongRoot = Assert.Throws<PdfTrustListException>(() => PdfLotl.Read(Bytes("<List/>")));
        Assert.Equal(PdfTrustListError.Malformed, wrongRoot.Error);

        var empty = Assert.Throws<PdfTrustListException>(() => PdfLotl.Read(ReadOnlySpan<byte>.Empty));
        Assert.Equal(PdfTrustListError.Malformed, empty.Error);
    }

    [Fact]
    public void A_list_reads_the_same_from_a_stream()
    {
        using var certificate = TestCertificates.Signer();

        using var stream = new MemoryStream(Bytes(List(Service(certificate))));
        var list = PdfLotl.Read(stream);

        Assert.Single(list.Services);
        Assert.Equal("FR", list.Territory);
    }

    [Fact]
    public void A_pointer_to_a_national_list_is_exposed_and_never_followed()
    {
        using var operatorCertificate = TestCertificates.Signer("CN=FR TSL Operator, O=M3scalinum, C=FR");

        var lotl = PdfLotl.Read(Bytes(List(
            services: string.Empty,
            pointers: Pointer(operatorCertificate),
            territory: "EU",
            type: ListOfLists)));

        Assert.True(lotl.IsListOfLists);

        // A list of lists carries no service of its own: the national list is the caller's to fetch.
        Assert.Empty(lotl.Services);

        var pointer = Assert.Single(lotl.NationalLists);
        Assert.Equal("https://example.test/FR.xml", pointer.Location);
        Assert.Equal("FR", pointer.Territory);
        Assert.Equal("FR operator", pointer.OperatorName);
        Assert.Equal(operatorCertificate.Thumbprint, Assert.Single(pointer.Certificates).Thumbprint);

        var store = PdfTrustStore.ForNationalList(lotl, "FR");

        Assert.Equal(operatorCertificate.Thumbprint, Assert.Single(store.Anchors).Thumbprint);
        Assert.Empty(PdfTrustStore.ForNationalList(lotl, "DE").Anchors);
    }

    [Fact]
    public void A_signature_covered_by_a_qualified_service_is_reported_as_qualified()
    {
        using var certificate = TestCertificates.Signer();

        var signed = PdfSigner.Sign(TestPdf.OnePage(), certificate, new PdfSignatureOptions { SignedAt = SignedAt });
        var list = PdfLotl.Read(Bytes(List(Service(certificate))));

        var result = PdfSignatureVerifier.Verify(signed, PdfTrustStore.Empty, list);

        var signature = Assert.Single(result.Signatures.Signatures);
        Assert.True(signature.IsIntact, signature.Error);

        var coverage = Assert.Single(result.Coverage);
        Assert.True(coverage.IsQualified, coverage.Explanation);
        Assert.Equal(0, coverage.SignatureIndex);
        Assert.Equal(signature.SignerSubject, coverage.SignerSubject);
        Assert.Equal(SignedAt, coverage.EvaluatedAt);
        Assert.NotNull(coverage.Service);
        Assert.Equal(PdfTrustServiceType.QualifiedCertificateAuthority, coverage.Service!.Type);
        Assert.True(result.AllQualified);
    }

    [Fact]
    public void A_signature_whose_signer_is_absent_from_the_list_stays_intact_and_is_not_qualified()
    {
        using var listed = TestCertificates.Signer("CN=Listed, O=M3scalinum.Text.Pdf, C=FR");
        using var signer = TestCertificates.Signer("CN=Not listed, O=M3scalinum.Text.Pdf, C=FR");

        var signed = PdfSigner.Sign(TestPdf.OnePage(), signer, new PdfSignatureOptions { SignedAt = SignedAt });
        var list = PdfLotl.Read(Bytes(List(Service(listed))));

        var result = PdfSignatureVerifier.Verify(signed, PdfTrustStore.Empty, list);

        Assert.True(Assert.Single(result.Signatures.Signatures).IsIntact);

        var coverage = Assert.Single(result.Coverage);
        Assert.False(coverage.IsQualified);
        Assert.Null(coverage.Service);
        Assert.Contains("not found", coverage.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.False(result.AllQualified);
    }

    [Fact]
    public void A_service_the_list_does_not_mark_qualified_covers_the_signer_without_qualifying_it()
    {
        using var certificate = TestCertificates.Signer();

        var signed = PdfSigner.Sign(TestPdf.OnePage(), certificate, new PdfSignatureOptions { SignedAt = SignedAt });
        var list = PdfLotl.Read(Bytes(List(Service(certificate, type: PlainCa))));

        var coverage = Assert.Single(PdfSignatureVerifier.Verify(signed, PdfTrustStore.Empty, list).Coverage);

        Assert.False(coverage.IsQualified);
        Assert.NotNull(coverage.Service);
        Assert.Equal(PdfTrustServiceType.CertificateAuthority, coverage.Service!.Type);
        Assert.Contains("does not mark qualified", coverage.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void A_verification_without_a_list_carries_no_coverage()
    {
        using var certificate = TestCertificates.Signer();
        var signed = PdfSigner.Sign(TestPdf.OnePage(), certificate, new PdfSignatureOptions { SignedAt = SignedAt });

        var result = PdfSignatureVerifier.Verify(signed, PdfTrustStore.Empty, trustList: null);

        Assert.Single(result.Signatures.Signatures);
        Assert.Empty(result.Coverage);
        Assert.False(result.AllQualified);
    }

    [Fact]
    public void A_qualified_ca_certificate_in_the_store_still_judges_the_chain()
    {
        // Qualification is not trust: the list says the service is qualified, the store says the
        // chain is rooted. Both answers reach the caller, and neither replaces the other.
        using var certificate = TestCertificates.Signer();

        var signed = PdfSigner.Sign(TestPdf.OnePage(), certificate, new PdfSignatureOptions { SignedAt = SignedAt });
        var list = PdfLotl.Read(Bytes(List(Service(certificate))));

        var untrusted = PdfSignatureVerifier.Verify(signed, PdfTrustStore.Empty, list);
        var trusted = PdfSignatureVerifier.Verify(signed, new PdfTrustStore { Anchors = [certificate] }, list);

        Assert.Equal(PdfChainStatus.NotEvaluated, Assert.Single(untrusted.Signatures.Signatures).ChainStatus);
        Assert.Equal(PdfChainStatus.Trusted, Assert.Single(trusted.Signatures.Signatures).ChainStatus);
        Assert.True(Assert.Single(trusted.Coverage).IsQualified);
    }

    [Fact]
    public void A_list_records_where_the_caller_found_it()
    {
        using var certificate = TestCertificates.Signer();

        var list = PdfLotl.Read(Bytes(List(Service(certificate))), "https://example.test/FR.xml");

        Assert.Equal("https://example.test/FR.xml", list.Location);
    }

    [Fact]
    public void Service_types_and_statuses_are_classified_from_their_identifiers()
    {
        using var certificate = TestCertificates.Signer();

        var services = string.Concat(
            Service(certificate, type: "http://uri.etsi.org/TrstSvc/Svctype/CA"),
            Service(certificate, type: "http://uri.etsi.org/TrstSvc/Svctype/AdESig"),
            Service(certificate, type: "http://uri.etsi.org/TrstSvc/Svctype/AdESealQC"),
            Service(certificate, type: "http://uri.etsi.org/TrstSvc/Svctype/TSA"),
            Service(certificate, type: "http://example.test/Svctype/SomethingElse",
                status: "http://uri.etsi.org/TrstSvc/TrustedList/Svcstatus/deprecated"),
            Service(certificate, status: "http://example.test/Svcstatus/UnheardOf"));

        var list = PdfLotl.Read(Bytes(List(services)));

        Assert.Equal(PdfTrustServiceType.CertificateAuthority, list.Services[0].Type);
        Assert.False(list.Services[0].IsQualified);
        Assert.Equal(PdfTrustServiceType.Signature, list.Services[1].Type);
        Assert.Equal(PdfTrustServiceType.Seal, list.Services[2].Type);
        Assert.True(list.Services[2].IsQualified);
        Assert.Equal(PdfTrustServiceType.Timestamp, list.Services[3].Type);
        Assert.Equal(PdfTrustServiceType.Other, list.Services[4].Type);
        Assert.Equal(PdfTrustServiceStatus.Deprecated, list.Services[4].Status);
        Assert.Equal(PdfTrustServiceStatus.Unknown, list.Services[5].Status);
        Assert.Equal("http://example.test/Svcstatus/UnheardOf", list.Services[5].StatusIdentifier);
    }

    [Fact]
    public void A_qualifier_whose_name_starts_with_QC_marks_the_service_qualified()
    {
        using var certificate = TestCertificates.Signer();

        var qualified = Service(
            certificate,
            type: "http://uri.etsi.org/TrstSvc/Svctype/AdESig",
            extensions: Qualifier("http://uri.etsi.org/TrstSvc/TrustedList/SvcInfoExt/QCWithSSCD"));

        var plain = Service(
            certificate,
            type: "http://uri.etsi.org/TrstSvc/Svctype/AdESig",
            extensions: Qualifier("http://uri.etsi.org/TrstSvc/TrustedList/SvcInfoExt/NotQualified"));

        var list = PdfLotl.Read(Bytes(List(qualified + plain)));

        Assert.True(list.Services[0].IsQualified);
        Assert.False(list.Services[1].IsQualified);
    }

    [Fact]
    public void A_deprecated_or_unknown_status_covers_nothing()
    {
        using var certificate = TestCertificates.Signer();

        var deprecated = Service(certificate, status: "http://uri.etsi.org/TrstSvc/TrustedList/Svcstatus/deprecated");
        var unknown = Service(certificate, status: "http://example.test/Svcstatus/UnheardOf");

        var list = PdfLotl.Read(Bytes(List(deprecated + unknown)));

        Assert.Empty(list.FindServices(certificate, SignedAt));
        Assert.Null(list.FindService(certificate, SignedAt));
    }

    [Fact]
    public void A_service_is_matched_by_its_certificate_bytes()
    {
        using var certificate = TestCertificates.Signer();
        using var other = TestCertificates.Signer("CN=Other, O=M3scalinum.Text.Pdf, C=FR");

        var service = Assert.Single(PdfLotl.Read(Bytes(List(Service(certificate)))).Services);

        Assert.True(service.Covers(certificate));
        Assert.False(service.Covers(other));
    }

    [Fact]
    public void A_certificate_that_does_not_decode_is_dropped_and_the_service_still_reads()
    {
        using var certificate = TestCertificates.Signer();

        const string Broken = "<X509Certificate>this is not base64!</X509Certificate>"
            + "<X509Certificate>AAAA</X509Certificate>"
            + "<X509SubjectName>CN=Named only</X509SubjectName>";

        var list = PdfLotl.Read(Bytes(List(Service(certificate, identity: Broken))));

        var service = Assert.Single(list.Services);
        Assert.Equal(certificate.Thumbprint, Assert.Single(service.Certificates).Thumbprint);
    }

    [Fact]
    public void A_service_naming_more_certificates_than_the_cap_is_refused()
    {
        using var certificate = TestCertificates.Signer();

        var element = $"<X509Certificate>{Convert.ToBase64String(certificate.RawData)}</X509Certificate>";
        var identity = string.Concat(Enumerable.Repeat(element, PdfLotl.MaxCertificates + 1));

        var error = Assert.Throws<PdfTrustListException>(
            () => PdfLotl.Read(Bytes(List(Service(certificate: null, identity: identity)))));

        Assert.Equal(PdfTrustListError.Limit, error.Error);
        Assert.Contains($"{PdfLotl.MaxCertificates}", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_list_without_a_territory_is_refused()
    {
        using var certificate = TestCertificates.Signer();

        var error = Assert.Throws<PdfTrustListException>(
            () => PdfLotl.Read(Bytes(List(Service(certificate), territory: string.Empty))));

        Assert.Equal(PdfTrustListError.Malformed, error.Error);
    }

    [Fact]
    public void A_pointer_without_a_location_is_refused()
    {
        using var operatorCertificate = TestCertificates.Signer();

        var error = Assert.Throws<PdfTrustListException>(() => PdfLotl.Read(Bytes(List(
            services: string.Empty,
            pointers: Pointer(operatorCertificate, location: string.Empty),
            territory: "EU",
            type: ListOfLists))));

        Assert.Equal(PdfTrustListError.Malformed, error.Error);
        Assert.Contains("TSLLocation", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pointer_without_additional_information_states_no_territory()
    {
        using var operatorCertificate = TestCertificates.Signer();

        var pointer = $$"""
            <PointersToOtherTSL>
              <OtherTSLPointer>
                <TSLLocation>https://example.test/DE.xml</TSLLocation>
              </OtherTSLPointer>
            </PointersToOtherTSL>
            """;

        var lotl = PdfLotl.Read(Bytes(List(
            services: string.Empty,
            pointers: pointer,
            territory: "EU",
            type: ListOfLists)));

        var entry = Assert.Single(lotl.NationalLists);
        Assert.Equal("https://example.test/DE.xml", entry.Location);
        Assert.Null(entry.Territory);
        Assert.Null(entry.OperatorName);
        Assert.Empty(entry.Certificates);
    }

    [Fact]
    public void A_list_without_a_provider_list_has_no_services()
    {
        var xml = """
            <TrustServiceStatusList>
              <SchemeInformation>
                <SchemeTerritory>FR</SchemeTerritory>
                <ListIssueDateTime>2026-01-01T00:00:00Z</ListIssueDateTime>
              </SchemeInformation>
            </TrustServiceStatusList>
            """;

        var list = PdfLotl.Read(Bytes(xml));

        Assert.Empty(list.Services);
        Assert.Equal("FR", list.Territory);
        Assert.Equal(0, list.Version);
    }

    [Fact]
    public void A_stream_over_the_size_cap_is_refused()
    {
        using var stream = new MemoryStream(new byte[PdfLotl.MaxXmlBytes + 1]);

        var error = Assert.Throws<PdfTrustListException>(() => PdfLotl.Read(stream));

        Assert.Equal(PdfTrustListError.Limit, error.Error);
    }

    [Fact]
    public void A_signature_with_no_certificate_or_no_time_is_not_qualified()
    {
        using var certificate = TestCertificates.Signer();

        var verification = new PdfSignatureVerification(
            [
                new PdfSignatureInfo(
                    null, null, null, SignedAt, null, true, true, false, false,
                    PdfSignatureLevel.BaselineB, PdfChainStatus.NotEvaluated, null, null),
                new PdfSignatureInfo(
                    "CN=No time", "CN=No time", certificate, null, null, true, true, false, false,
                    PdfSignatureLevel.BaselineB, PdfChainStatus.NotEvaluated, null, null),
            ],
            []);

        var list = PdfLotl.Read(Bytes(List(Service(certificate))));
        var coverage = list.Evaluate(verification);

        Assert.Equal(2, coverage.Count);
        Assert.False(coverage[0].IsQualified);
        Assert.Contains("no signing certificate", coverage[0].Explanation, StringComparison.Ordinal);
        Assert.False(coverage[1].IsQualified);
        Assert.Contains("no time", coverage[1].Explanation, StringComparison.Ordinal);
        Assert.Null(coverage[1].EvaluatedAt);
    }

    private static byte[] Bytes(string xml) => Encoding.UTF8.GetBytes(xml);

    private static string List(
        string services,
        string pointers = "",
        string territory = "FR",
        string type = "http://uri.etsi.org/TrstSvc/TrustedList/TSLType/EUgeneric",
        int version = 5,
        int sequence = 12) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <TrustServiceStatusList xmlns="http://uri.etsi.org/02231/v2#">
          <SchemeInformation>
            <TSLVersionIdentifier>{{version}}</TSLVersionIdentifier>
            <TSLSequenceNumber>{{sequence}}</TSLSequenceNumber>
            <TSLType>{{type}}</TSLType>
            <SchemeOperatorName><Name xml:lang="en">Test scheme operator</Name></SchemeOperatorName>
            <SchemeTerritory>{{territory}}</SchemeTerritory>
            <ListIssueDateTime>2026-01-01T00:00:00Z</ListIssueDateTime>
            <NextUpdate><dateTime>2026-07-01T00:00:00Z</dateTime></NextUpdate>
            {{pointers}}
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
        X509Certificate2? certificate,
        string status = Granted,
        string since = "2020-01-01T00:00:00Z",
        string type = QualifiedCa,
        string history = "",
        string identity = "",
        string extensions = "")
    {
        var element = certificate is null
            ? string.Empty
            : $"<X509Certificate>{Convert.ToBase64String(certificate.RawData)}</X509Certificate>";

        return $$"""
            <TSPService>
              <ServiceInformation>
                <ServiceTypeIdentifier>{{type}}</ServiceTypeIdentifier>
                <ServiceName><Name xml:lang="en">Test service</Name></ServiceName>
                <ServiceDigitalIdentity>
                  <DigitalId>{{identity}}{{element}}</DigitalId>
                </ServiceDigitalIdentity>
                <ServiceStatus>{{status}}</ServiceStatus>
                <StatusStartingTime>{{since}}</StatusStartingTime>
                {{extensions}}
              </ServiceInformation>
              {{history}}
            </TSPService>
            """;
    }

    private static string History(string status, string since) => $$"""
        <ServiceHistory>
          <ServiceHistoryInstance>
            <ServiceName><Name xml:lang="en">Test service</Name></ServiceName>
            <ServiceStatus>{{status}}</ServiceStatus>
            <StatusStartingTime>{{since}}</StatusStartingTime>
          </ServiceHistoryInstance>
        </ServiceHistory>
        """;

    private static string Qualifier(string uri) => $$"""
        <ServiceInformationExtensions>
          <Extension>
            <Qualifiers>
              <Qualifier URI="{{uri}}">A qualifier</Qualifier>
            </Qualifiers>
          </Extension>
        </ServiceInformationExtensions>
        """;

    private static string Pointer(
        X509Certificate2 certificate,
        string location = "https://example.test/FR.xml",
        string territory = "FR") => $$"""
        <PointersToOtherTSL>
          <OtherTSLPointer>
            <ServiceDigitalIdentities>
              <ServiceDigitalIdentity>
                <DigitalId><X509Certificate>{{Convert.ToBase64String(certificate.RawData)}}</X509Certificate></DigitalId>
              </ServiceDigitalIdentity>
            </ServiceDigitalIdentities>
            <TSLLocation>{{location}}</TSLLocation>
            <AdditionalInformation>
              <OtherInformation>
                <SchemeTerritory>{{territory}}</SchemeTerritory>
                <SchemeOperatorName><Name xml:lang="en">FR operator</Name></SchemeOperatorName>
                <MimeType>application/vnd.etsi.tsl+xml</MimeType>
              </OtherInformation>
            </AdditionalInformation>
          </OtherTSLPointer>
        </PointersToOtherTSL>
        """;
}
