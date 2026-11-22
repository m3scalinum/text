using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;
using Xunit;

#pragma warning disable CA5350 // One test signs a list with SHA-1, which a legacy list may name; the algorithm is read, not chosen.

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// What a trust list's own signature proves: the references it covers, the signer, and what the
/// qualification verdict is allowed to say when the list is not verified or has expired.
/// </summary>
/// <remarks>
/// The fixtures are ETSI TS 119 612 documents this test writes and signs in memory, with a key it
/// generated. The digest and signature are produced through the package's own canonicalisation so
/// the bytes signed are the bytes verified; one test pins that canonical form against a literal, so
/// the rules are checked rather than assumed.
/// </remarks>
public sealed class TrustListSignatureTests
{
    private const string InclusiveC14N = "http://www.w3.org/TR/2001/REC-xml-c14n-20010315";
    private const string ExclusiveC14N = "http://www.w3.org/2001/10/xml-exc-c14n#";
    private const string Enveloped = "http://www.w3.org/2000/09/xmldsig#enveloped-signature";
    private const string RsaSha256 = "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256";
    private const string Sha256 = "http://www.w3.org/2001/04/xmlenc#sha256";

    private static readonly DateTimeOffset SignedAt = new(2026, 2, 1, 10, 30, 0, TimeSpan.Zero);

    private static readonly XNamespace Ds = "http://www.w3.org/2000/09/xmldsig#";
    private static readonly XNamespace Xades = "http://uri.etsi.org/01903/v1.3.2#";
    private static readonly XNamespace Tsl = "http://uri.etsi.org/02231/v2#";

    [Fact]
    public void A_signed_list_whose_signature_verifies_against_the_anchors_is_reported_verified()
    {
        using var signer = TestCertificates.Root("CN=FR National List Signer, O=M3scalinum, C=FR");
        using var certificate = TestCertificates.Signer();

        var xml = Signed(List(certificate), signer);
        var verdict = PdfLotl.VerifySignature(xml, new PdfTrustStore { Anchors = [signer] });

        Assert.Equal(PdfTrustListSignatureStatus.Valid, verdict.Status);
        Assert.True(verdict.IsVerified);
        Assert.Null(verdict.Error);
        Assert.Equal(signer.Subject, verdict.SignerSubject);
        Assert.Equal(signer.SerialNumber, verdict.SignerSerialNumber);
        Assert.Equal(SignedAt, verdict.SignedAt);
        Assert.True(verdict.CoversDocument);
        Assert.Equal(PdfChainStatus.Trusted, verdict.SignerChain);
        Assert.Equal(new[] { string.Empty, "#SignedProperties-1" }, verdict.References);

        var list = PdfLotl.Read(xml, null, new PdfTrustStore { Anchors = [signer] });
        Assert.NotNull(list.Signature);
        Assert.True(list.Signature!.IsVerified);
    }

    [Fact]
    public void A_list_whose_signed_bytes_changed_by_one_byte_is_refused_with_the_reason()
    {
        using var signer = TestCertificates.Root("CN=FR National List Signer, O=M3scalinum, C=FR");
        using var certificate = TestCertificates.Signer();

        var xml = Signed(List(certificate), signer);

        // The territory is inside the signed document; swapping two letters keeps the length, so the
        // DER-free XML still parses and only the digest can notice.
        var tampered = Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(xml).Replace(">FR<", ">DE<", StringComparison.Ordinal));

        var verdict = PdfLotl.VerifySignature(tampered, new PdfTrustStore { Anchors = [signer] });

        Assert.Equal(PdfTrustListSignatureStatus.Invalid, verdict.Status);
        Assert.False(verdict.IsVerified);
        Assert.Contains("digest of the reference to the document does not match", verdict.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_list_without_a_signature_is_reported_unsigned()
    {
        using var certificate = TestCertificates.Signer();

        var verdict = PdfLotl.VerifySignature(Bytes(List(certificate).ToString(SaveOptions.DisableFormatting)), null);

        Assert.Equal(PdfTrustListSignatureStatus.NoSignature, verdict.Status);
        Assert.False(verdict.IsVerified);
        Assert.Contains("no ds:Signature", verdict.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unreadable_document_is_refused_before_any_signature_is_looked_for()
    {
        var error = Assert.Throws<PdfTrustListException>(
            () => PdfLotl.VerifySignature(Bytes("<TrustServiceStatusList"), null));

        Assert.Equal(PdfTrustListError.Malformed, error.Error);
    }

    [Fact]
    public void A_list_signed_by_a_certificate_the_anchors_do_not_cover_is_not_verified()
    {
        using var signer = TestCertificates.Root("CN=FR National List Signer, O=M3scalinum, C=FR");
        using var other = TestCertificates.Root("CN=Another Root, O=M3scalinum, C=FR");
        using var certificate = TestCertificates.Signer();

        var verdict = PdfLotl.VerifySignature(
            Signed(List(certificate), signer),
            new PdfTrustStore { Anchors = [other] });

        Assert.Equal(PdfTrustListSignatureStatus.Valid, verdict.Status);
        Assert.Equal(PdfChainStatus.Untrusted, verdict.SignerChain);
        Assert.False(verdict.IsVerified);
    }

    [Fact]
    public void A_list_verified_without_anchors_is_not_verified()
    {
        using var signer = TestCertificates.Root("CN=FR National List Signer, O=M3scalinum, C=FR");
        using var certificate = TestCertificates.Signer();

        var verdict = PdfLotl.VerifySignature(Signed(List(certificate), signer), null);

        Assert.Equal(PdfTrustListSignatureStatus.Valid, verdict.Status);
        Assert.Equal(PdfChainStatus.NotEvaluated, verdict.SignerChain);
        Assert.False(verdict.IsVerified);
    }

    [Fact]
    public void A_signature_that_covers_only_a_detached_element_is_not_verified()
    {
        using var signer = TestCertificates.Root("CN=FR National List Signer, O=M3scalinum, C=FR");
        using var certificate = TestCertificates.Signer();

        var xml = Signed(List(certificate), signer, coversDocument: false);
        var verdict = PdfLotl.VerifySignature(xml, new PdfTrustStore { Anchors = [signer] });

        Assert.Equal(PdfTrustListSignatureStatus.Valid, verdict.Status);
        Assert.False(verdict.CoversDocument);
        Assert.False(verdict.IsVerified);
    }

    [Fact]
    public void An_unsupported_canonicalisation_is_refused_by_name()
    {
        using var signer = TestCertificates.Root("CN=FR National List Signer, O=M3scalinum, C=FR");
        using var certificate = TestCertificates.Signer();

        var xml = Signed(List(certificate), signer, canonicalization: "http://example.test/c14n");
        var verdict = PdfLotl.VerifySignature(xml, new PdfTrustStore { Anchors = [signer] });

        Assert.Equal(PdfTrustListSignatureStatus.Invalid, verdict.Status);
        Assert.Contains("http://example.test/c14n", verdict.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unsupported_signature_method_is_refused_by_name()
    {
        using var signer = TestCertificates.Root("CN=FR National List Signer, O=M3scalinum, C=FR");
        using var certificate = TestCertificates.Signer();

        var xml = Signed(List(certificate), signer, signatureMethod: "http://example.test/hmac");
        var verdict = PdfLotl.VerifySignature(xml, new PdfTrustStore { Anchors = [signer] });

        Assert.Equal(PdfTrustListSignatureStatus.Invalid, verdict.Status);
        Assert.Contains("http://example.test/hmac", verdict.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_reference_to_an_identifier_the_document_does_not_carry_is_refused()
    {
        using var signer = TestCertificates.Root("CN=FR National List Signer, O=M3scalinum, C=FR");
        using var certificate = TestCertificates.Signer();

        var xml = Signed(List(certificate), signer, referenceIdentifier: "nowhere");
        var verdict = PdfLotl.VerifySignature(xml, new PdfTrustStore { Anchors = [signer] });

        Assert.Equal(PdfTrustListSignatureStatus.Invalid, verdict.Status);
        Assert.Contains("#nowhere", verdict.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_qualification_on_a_list_whose_signature_does_not_hold_is_never_qualified()
    {
        using var signer = TestCertificates.Root("CN=FR National List Signer, O=M3scalinum, C=FR");
        using var certificate = TestCertificates.Signer();

        var xml = Signed(List(certificate), signer);
        var tampered = Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(xml).Replace(">FR<", ">DE<", StringComparison.Ordinal));

        var list = PdfLotl.Read(tampered, null, new PdfTrustStore { Anchors = [signer] });
        var result = PdfSignatureVerifier.Verify(PdfSigned(certificate), PdfTrustStore.Empty, list);

        var coverage = Assert.Single(result.Coverage);

        Assert.Equal(PdfQualificationBasis.NotVerified, coverage.Basis);
        Assert.False(coverage.IsQualified);
        Assert.False(result.AllQualified);
        Assert.Contains("not verified", coverage.Explanation, StringComparison.Ordinal);
        Assert.Equal(PdfTrustListSignatureStatus.Invalid, result.ListSignature!.Status);
    }

    [Fact]
    public void A_qualification_on_a_verified_current_list_names_the_verification()
    {
        using var signer = TestCertificates.Root("CN=FR National List Signer, O=M3scalinum, C=FR");
        using var certificate = TestCertificates.Signer();

        var list = PdfLotl.Read(Signed(List(certificate), signer), null, new PdfTrustStore { Anchors = [signer] });
        var result = PdfSignatureVerifier.Verify(PdfSigned(certificate), PdfTrustStore.Empty, list);

        var coverage = Assert.Single(result.Coverage);

        Assert.Equal(PdfQualificationBasis.Verified, coverage.Basis);
        Assert.True(coverage.IsQualified, coverage.Explanation);
        Assert.True(result.AllQualified);
        Assert.True(result.ListSignature!.IsVerified);
    }

    [Fact]
    public void A_qualification_on_a_verified_list_past_its_next_update_says_the_list_expired()
    {
        using var signer = TestCertificates.Root("CN=FR National List Signer, O=M3scalinum, C=FR");
        using var certificate = TestCertificates.Signer();

        // The list says it must be replaced on 15 January; the signature is claimed on 1 February.
        var expired = List(certificate, nextUpdate: "2026-01-15T00:00:00Z");
        var list = PdfLotl.Read(Signed(expired, signer), null, new PdfTrustStore { Anchors = [signer] });

        var coverage = Assert.Single(PdfSignatureVerifier.Verify(PdfSigned(certificate), PdfTrustStore.Empty, list).Coverage);

        Assert.Equal(PdfQualificationBasis.VerifiedExpired, coverage.Basis);
        Assert.True(coverage.IsQualified);
        Assert.Contains("NextUpdate passed", coverage.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void A_list_read_without_anchors_reports_the_qualification_as_unchecked()
    {
        using var signer = TestCertificates.Root("CN=FR National List Signer, O=M3scalinum, C=FR");
        using var certificate = TestCertificates.Signer();

        var list = PdfLotl.Read(Signed(List(certificate), signer));
        var result = PdfSignatureVerifier.Verify(PdfSigned(certificate), PdfTrustStore.Empty, list);

        var coverage = Assert.Single(result.Coverage);

        Assert.Null(list.Signature);
        Assert.Null(result.ListSignature);
        Assert.Equal(PdfQualificationBasis.NotChecked, coverage.Basis);
        Assert.Contains("read without anchors", coverage.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void A_national_list_is_verified_with_the_anchors_the_lotl_publishes()
    {
        using var signer = TestCertificates.Root("CN=FR National List Signer, O=M3scalinum, C=FR");
        using var certificate = TestCertificates.Signer();

        var lotl = PdfLotl.Read(Bytes(ListOfLists(signer).ToString(SaveOptions.DisableFormatting)));
        var anchors = PdfTrustStore.ForNationalList(lotl, "FR");

        var verdict = PdfLotl.VerifySignature(Signed(List(certificate), signer), anchors);

        Assert.True(verdict.IsVerified, verdict.Error);
        Assert.Equal(PdfChainStatus.Trusted, verdict.SignerChain);

        // Another territory's pointer names another operator, so it anchors nothing of this one.
        Assert.False(PdfLotl.VerifySignature(Signed(List(certificate), signer), PdfTrustStore.ForNationalList(lotl, "DE")).IsVerified);
    }

    [Fact]
    public void An_exclusive_canonicalisation_is_verified_the_same_way()
    {
        using var signer = TestCertificates.Root("CN=FR National List Signer, O=M3scalinum, C=FR");
        using var certificate = TestCertificates.Signer();

        var xml = Signed(List(certificate), signer, canonicalization: ExclusiveC14N, exclusive: true);
        var verdict = PdfLotl.VerifySignature(xml, new PdfTrustStore { Anchors = [signer] });

        Assert.True(verdict.IsVerified, verdict.Error);
    }

    [Fact]
    public void The_canonical_form_sorts_attributes_escapes_text_and_keeps_carriage_returns()
    {
        var element = new XElement(
            "a",
            new XAttribute("z", "second"),
            new XAttribute("b", "\"quoted\" & <tag>"),
            new XAttribute("tab", "\t"),
            new XAttribute("nl", "line\nbreak"),
            new XAttribute("cr", "a\rb"),
            new XElement("child", "x\ry < z > a & b"),
            new XElement("empty", new XAttribute("k", "  ")),
            new XProcessingInstruction("keep", "the data"),
            new XComment("dropped"));

        var canonical = Encoding.UTF8.GetString(PdfXmlSignature.Canonicalize(element, exclusive: false));

        Assert.Equal(
            "<a b=\"&quot;quoted&quot; &amp; &lt;tag&gt;\" cr=\"a&#xD;b\" nl=\"line&#xA;break\" tab=\"&#x9;\" z=\"second\">"
            + "<child>x&#xD;y &lt; z &gt; a &amp; b</child><empty k=\"  \"></empty><?keep the data?></a>",
            canonical);
    }

    [Fact]
    public void Verifying_a_list_that_declares_a_document_type_is_refused()
    {
        var error = Assert.Throws<PdfTrustListException>(
            () => PdfLotl.VerifySignature(Bytes("<!DOCTYPE x []><TrustServiceStatusList/>"), null));

        Assert.Equal(PdfTrustListError.XmlPolicy, error.Error);
    }

    [Fact]
    public void Verifying_an_empty_list_is_refused()
    {
        Assert.Throws<PdfTrustListException>(() => PdfLotl.VerifySignature([], null));
    }



    [Fact]
    public void A_signature_without_signedinfo_is_refused()
    {
        using var signer = TestCertificates.Root("CN=FR National List Signer, O=M3scalinum, C=FR");
        using var certificate = TestCertificates.Signer();

        var document = Parsed(Signed(List(certificate), signer));
        document.Descendants().Single(element => element.Name.LocalName == "SignedInfo").Remove();

        var verdict = PdfLotl.VerifySignature(Bytes(document), new PdfTrustStore { Anchors = [signer] });

        Assert.Equal(PdfTrustListSignatureStatus.Invalid, verdict.Status);
        Assert.Contains("no SignedInfo", verdict.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_signature_without_a_certificate_is_refused()
    {
        using var signer = TestCertificates.Root("CN=FR National List Signer, O=M3scalinum, C=FR");
        using var certificate = TestCertificates.Signer();

        var document = Parsed(Signed(List(certificate), signer));
        document.Descendants().Single(element => element.Name.LocalName == "Signature")
            .Descendants().Single(element => element.Name.LocalName == "X509Certificate").Remove();

        var verdict = PdfLotl.VerifySignature(Bytes(document), new PdfTrustStore { Anchors = [signer] });

        Assert.Equal(PdfTrustListSignatureStatus.Invalid, verdict.Status);
        Assert.Contains("no signing certificate", verdict.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_signature_without_a_signature_value_is_refused()
    {
        using var signer = TestCertificates.Root("CN=FR National List Signer, O=M3scalinum, C=FR");
        using var certificate = TestCertificates.Signer();

        var document = Parsed(Signed(List(certificate), signer));
        document.Descendants().Single(element => element.Name.LocalName == "SignatureValue").Value = string.Empty;

        var verdict = PdfLotl.VerifySignature(Bytes(document), new PdfTrustStore { Anchors = [signer] });

        Assert.Equal(PdfTrustListSignatureStatus.Invalid, verdict.Status);
        Assert.Contains("no SignatureValue", verdict.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_signature_that_covers_nothing_is_refused()
    {
        using var signer = TestCertificates.Root("CN=FR National List Signer, O=M3scalinum, C=FR");
        using var certificate = TestCertificates.Signer();

        var document = Parsed(Signed(List(certificate), signer));

        foreach (var reference in document.Descendants().Where(element => element.Name.LocalName == "Reference").ToList())
        {
            reference.Remove();
        }

        var verdict = PdfLotl.VerifySignature(Resigned(document, signer), new PdfTrustStore { Anchors = [signer] });

        Assert.Equal(PdfTrustListSignatureStatus.Invalid, verdict.Status);
        Assert.Contains("no reference", verdict.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_reference_that_points_outside_the_document_is_refused()
    {
        using var signer = TestCertificates.Root("CN=FR National List Signer, O=M3scalinum, C=FR");
        using var certificate = TestCertificates.Signer();

        var document = Parsed(Signed(List(certificate), signer));
        DocumentReference(document).SetAttributeValue("URI", "https://example.test/other.xml");

        var verdict = PdfLotl.VerifySignature(Resigned(document, signer), new PdfTrustStore { Anchors = [signer] });

        Assert.Equal(PdfTrustListSignatureStatus.Invalid, verdict.Status);
        Assert.Contains("points outside the document", verdict.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_reference_without_the_enveloped_transform_is_refused()
    {
        using var signer = TestCertificates.Root("CN=FR National List Signer, O=M3scalinum, C=FR");
        using var certificate = TestCertificates.Signer();

        var document = Parsed(Signed(List(certificate), signer));
        DocumentReference(document).Descendants()
            .Single(element => element.Name.LocalName == "Transform"
                              && element.Attribute("Algorithm")!.Value == Enveloped)
            .Remove();

        var verdict = PdfLotl.VerifySignature(Resigned(document, signer), new PdfTrustStore { Anchors = [signer] });

        Assert.Equal(PdfTrustListSignatureStatus.Invalid, verdict.Status);
        Assert.Contains("no enveloped-signature transform", verdict.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_transform_this_reader_does_not_implement_is_refused_by_name()
    {
        using var signer = TestCertificates.Root("CN=FR National List Signer, O=M3scalinum, C=FR");
        using var certificate = TestCertificates.Signer();

        var document = Parsed(Signed(List(certificate), signer));
        DocumentReference(document).Descendants()
            .First(element => element.Name.LocalName == "Transform")
            .SetAttributeValue("Algorithm", "http://www.w3.org/2000/09/xmldsig#base64");

        var verdict = PdfLotl.VerifySignature(Resigned(document, signer), new PdfTrustStore { Anchors = [signer] });

        Assert.Equal(PdfTrustListSignatureStatus.Invalid, verdict.Status);
        Assert.Contains("http://www.w3.org/2000/09/xmldsig#base64", verdict.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_list_signed_with_sha1_is_verified()
    {
        using var signer = TestCertificates.Root("CN=FR National List Signer, O=M3scalinum, C=FR");
        using var certificate = TestCertificates.Signer();

        var xml = Signed(
            List(certificate),
            signer,
            digestMethod: "http://www.w3.org/2000/09/xmldsig#sha1",
            signatureMethod: "http://www.w3.org/2000/09/xmldsig#rsa-sha1");

        var verdict = PdfLotl.VerifySignature(xml, new PdfTrustStore { Anchors = [signer] });

        Assert.True(verdict.IsVerified, verdict.Error);
    }

    [Fact]
    public void A_list_signed_with_sha384_is_verified()
    {
        using var signer = TestCertificates.Root("CN=FR National List Signer, O=M3scalinum, C=FR");
        using var certificate = TestCertificates.Signer();

        var xml = Signed(
            List(certificate),
            signer,
            digestMethod: "http://www.w3.org/2001/04/xmldsig-more#sha384",
            signatureMethod: "http://www.w3.org/2001/04/xmldsig-more#rsa-sha384");

        var verdict = PdfLotl.VerifySignature(xml, new PdfTrustStore { Anchors = [signer] });

        Assert.True(verdict.IsVerified, verdict.Error);
    }

    [Fact]
    public void A_digest_algorithm_this_reader_does_not_implement_is_refused_by_name()
    {
        using var signer = TestCertificates.Root("CN=FR National List Signer, O=M3scalinum, C=FR");
        using var certificate = TestCertificates.Signer();

        var document = Parsed(Signed(List(certificate), signer));
        DocumentReference(document).Descendants()
            .Single(element => element.Name.LocalName == "DigestMethod")
            .SetAttributeValue("Algorithm", "http://example.test/md5");

        var verdict = PdfLotl.VerifySignature(Resigned(document, signer), new PdfTrustStore { Anchors = [signer] });

        Assert.Equal(PdfTrustListSignatureStatus.Invalid, verdict.Status);
        Assert.Contains("http://example.test/md5", verdict.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_list_signed_with_sha512_is_verified()
    {
        using var signer = TestCertificates.Root("CN=FR National List Signer, O=M3scalinum, C=FR");
        using var certificate = TestCertificates.Signer();

        var xml = Signed(
            List(certificate),
            signer,
            digestMethod: "http://www.w3.org/2001/04/xmlenc#sha512",
            signatureMethod: "http://www.w3.org/2001/04/xmldsig-more#rsa-sha512");

        var verdict = PdfLotl.VerifySignature(xml, new PdfTrustStore { Anchors = [signer] });

        Assert.True(verdict.IsVerified, verdict.Error);
    }

    [Fact]
    public void A_digest_value_that_is_not_base64_is_refused()
    {
        using var signer = TestCertificates.Root("CN=FR National List Signer, O=M3scalinum, C=FR");
        using var certificate = TestCertificates.Signer();

        var document = Parsed(Signed(List(certificate), signer));
        DocumentReference(document).Descendants()
            .Single(element => element.Name.LocalName == "DigestValue")
            .Value = "not base64!";

        var verdict = PdfLotl.VerifySignature(Resigned(document, signer), new PdfTrustStore { Anchors = [signer] });

        Assert.Equal(PdfTrustListSignatureStatus.Invalid, verdict.Status);
        Assert.Contains("does not match", verdict.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_signing_certificate_that_is_not_base64_leaves_no_signer()
    {
        using var signer = TestCertificates.Root("CN=FR National List Signer, O=M3scalinum, C=FR");
        using var certificate = TestCertificates.Signer();

        var document = Parsed(Signed(List(certificate), signer));
        document.Descendants().Single(element => element.Name.LocalName == "Signature")
            .Descendants().Single(element => element.Name.LocalName == "X509Certificate").Value = "not a certificate";

        var verdict = PdfLotl.VerifySignature(Bytes(document), new PdfTrustStore { Anchors = [signer] });

        Assert.Equal(PdfTrustListSignatureStatus.Invalid, verdict.Status);
        Assert.Contains("no signing certificate", verdict.Error, StringComparison.Ordinal);
    }

    private static XDocument Parsed(byte[] xml)
        => XDocument.Load(new System.IO.MemoryStream(xml), LoadOptions.PreserveWhitespace);

    private static byte[] Bytes(XDocument document)
        => Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting));

    private static XElement DocumentReference(XDocument document)
        => document.Descendants().First(element => element.Name.LocalName == "Reference");

    /// <summary>Signs the SignedInfo of an edited tree again, so a structural edit stays the only fault.</summary>
    private static byte[] Resigned(XDocument document, X509Certificate2 signer)
    {
        var signedInfo = document.Descendants().Single(element => element.Name.LocalName == "SignedInfo");
        var toSign = PdfXmlSignature.Canonicalize(signedInfo, exclusive: false);

        using var key = signer.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("the test signer has no RSA key");

        document.Descendants().Single(element => element.Name.LocalName == "SignatureValue").Value =
            Convert.ToBase64String(key.SignData(toSign, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

        return Bytes(document);
    }

    private static byte[] Bytes(string xml) => Encoding.UTF8.GetBytes(xml);

    /// <summary>A signed PDF whose signing certificate the fixture list names.</summary>
    private static byte[] PdfSigned(X509Certificate2 certificate)
        => PdfSigner.Sign(TestPdf.OnePage(), certificate, new PdfSignatureOptions { SignedAt = SignedAt });

    /// <summary>A national trust list naming <paramref name="certificate"/>, unsigned.</summary>
    private static XDocument List(
        X509Certificate2 certificate,
        string territory = "FR",
        string nextUpdate = "2026-07-01T00:00:00Z")
    {
        return new XDocument(
            new XElement(
                Tsl + "TrustServiceStatusList",
                new XAttribute(XNamespace.Xmlns + "ds", Ds),
                new XAttribute(XNamespace.Xmlns + "xades", Xades),
                new XElement(
                    Tsl + "SchemeInformation",
                    new XElement(Tsl + "TSLVersionIdentifier", "5"),
                    new XElement(Tsl + "TSLSequenceNumber", "12"),
                    new XElement(Tsl + "TSLType", "http://uri.etsi.org/TrstSvc/TrustedList/TSLType/EUgeneric"),
                    new XElement(Tsl + "SchemeTerritory", territory),
                    new XElement(Tsl + "ListIssueDateTime", "2026-01-01T00:00:00Z"),
                    new XElement(Tsl + "NextUpdate", new XElement(Tsl + "dateTime", nextUpdate))),
                new XElement(
                    Tsl + "TrustServiceProviderList",
                    new XElement(
                        Tsl + "TrustServiceProvider",
                        new XElement(
                            Tsl + "TSPInformation",
                            new XElement(
                                Tsl + "TSPName",
                                new XElement(Tsl + "Name", new XAttribute(XNamespace.Xml + "lang", "en"), "Test provider"))),
                        new XElement(
                            Tsl + "TSPServices",
                            new XElement(
                                Tsl + "TSPService",
                                new XElement(
                                    Tsl + "ServiceInformation",
                                    new XElement(
                                        Tsl + "ServiceTypeIdentifier",
                                        "http://uri.etsi.org/TrstSvc/Svctype/CA/QC"),
                                    new XElement(
                                        Tsl + "ServiceName",
                                        new XElement(Tsl + "Name", new XAttribute(XNamespace.Xml + "lang", "en"), "Test service")),
                                    new XElement(
                                        Tsl + "ServiceDigitalIdentity",
                                        new XElement(
                                            Tsl + "DigitalId",
                                            new XElement(Tsl + "X509Certificate", Convert.ToBase64String(certificate.RawData)))),
                                    new XElement(
                                        Tsl + "ServiceStatus",
                                        "http://uri.etsi.org/TrstSvc/TrustedList/Svcstatus/granted"),
                                    new XElement(Tsl + "StatusStartingTime", "2020-01-01T00:00:00Z"))))))));
    }

    /// <summary>A list-of-lists whose FR pointer binds <paramref name="signer"/> to the national list.</summary>
    private static XDocument ListOfLists(X509Certificate2 signer)
    {
        var pointer = new XElement(
            Tsl + "OtherTSLPointer",
            new XElement(
                Tsl + "ServiceDigitalIdentities",
                new XElement(
                    Tsl + "ServiceDigitalIdentity",
                    new XElement(
                        Tsl + "DigitalId",
                        new XElement(Tsl + "X509Certificate", Convert.ToBase64String(signer.RawData))))),
            new XElement(Tsl + "TSLLocation", "https://example.test/FR.xml"),
            new XElement(
                Tsl + "AdditionalInformation",
                new XElement(
                    Tsl + "OtherInformation",
                    new XElement(Tsl + "SchemeTerritory", "FR"),
                    new XElement(
                        Tsl + "SchemeOperatorName",
                        new XElement(Tsl + "Name", new XAttribute(XNamespace.Xml + "lang", "en"), "FR operator")))));

        return new XDocument(
            new XElement(
                Tsl + "TrustServiceStatusList",
                new XElement(
                    Tsl + "SchemeInformation",
                    new XElement(Tsl + "TSLVersionIdentifier", "5"),
                    new XElement(Tsl + "TSLSequenceNumber", "1"),
                    new XElement(Tsl + "TSLType", "http://uri.etsi.org/TrstSvc/TrustedList/TSLType/EUlistofthelists"),
                    new XElement(Tsl + "SchemeTerritory", "EU"),
                    new XElement(Tsl + "ListIssueDateTime", "2026-01-01T00:00:00Z"),
                    new XElement(Tsl + "PointersToOtherTSL", pointer))));
    }

    /// <summary>
    /// Appends an enveloped XAdES signature to <paramref name="document"/> and signs it.
    /// </summary>
    private static byte[] Signed(
        XDocument document,
        X509Certificate2 signer,
        bool coversDocument = true,
        bool exclusive = false,
        string canonicalization = InclusiveC14N,
        string signatureMethod = RsaSha256,
        string propertiesIdentifier = "SignedProperties-1",
        string? referenceIdentifier = null,
        string digestMethod = Sha256)
    {
        var root = document.Root!;
        var propertiesUri = "#" + (referenceIdentifier ?? propertiesIdentifier);

        var references = new List<XElement>();

        if (coversDocument)
        {
            references.Add(Reference(string.Empty, enveloped: true, canonicalization, digestMethod));
        }

        var propertiesReference = Reference(propertiesUri, enveloped: false, canonicalization, digestMethod);
        references.Add(propertiesReference);

        var signature = new XElement(
            Ds + "Signature",
            new XElement(
                Ds + "SignedInfo",
                new XElement(Ds + "CanonicalizationMethod", new XAttribute("Algorithm", canonicalization)),
                new XElement(Ds + "SignatureMethod", new XAttribute("Algorithm", signatureMethod)),
                references),
            new XElement(Ds + "SignatureValue"),
            new XElement(
                Ds + "KeyInfo",
                new XElement(
                    Ds + "X509Data",
                    new XElement(Ds + "X509Certificate", Convert.ToBase64String(signer.RawData)))),
            new XElement(
                Ds + "Object",
                new XElement(
                    Xades + "QualifyingProperties",
                    new XElement(
                        Xades + "SignedProperties",
                        new XAttribute("Id", propertiesIdentifier),
                        new XElement(
                            Xades + "SignedSignatureProperties",
                            new XElement(Xades + "SigningTime", "2026-02-01T10:30:00Z"))))));

        root.Add(signature);

        // The document reference: the whole document with the signature element removed, which is
        // what the enveloped-signature transform leaves behind.
        var copy = new XElement(root);

        foreach (var element in copy.Descendants().Where(PdfXmlSignature.IsSignature).ToList())
        {
            element.Remove();
        }

        if (coversDocument)
        {
            SetDigest(references[0], PdfXmlSignature.Canonicalize(copy, exclusive), digestMethod);
        }

        var properties = signature.Descendants().Single(element => element.Name.LocalName == "SignedProperties");
        SetDigest(propertiesReference, PdfXmlSignature.Canonicalize(properties, exclusive), digestMethod);

        var signedInfo = signature.Element(Ds + "SignedInfo")!;
        var toSign = PdfXmlSignature.Canonicalize(signedInfo, exclusive);

        using var key = signer.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("the test signer has no RSA key");

        signature.Element(Ds + "SignatureValue")!.Value =
            Convert.ToBase64String(key.SignData(toSign, HashOf(signatureMethod), RSASignaturePadding.Pkcs1));

        return Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting));
    }

    private static XElement Reference(string uri, bool enveloped, string canonicalization, string digestMethod)
        => new(
            Ds + "Reference",
            new XAttribute("URI", uri),
            new XElement(
                Ds + "Transforms",
                enveloped ? new XElement(Ds + "Transform", new XAttribute("Algorithm", Enveloped)) : null,
                new XElement(Ds + "Transform", new XAttribute("Algorithm", canonicalization))),
            new XElement(Ds + "DigestMethod", new XAttribute("Algorithm", digestMethod)),
            new XElement(Ds + "DigestValue"));

    private static void SetDigest(XElement reference, byte[] canonical, string digestMethod)
        => reference.Element(Ds + "DigestValue")!.Value =
            Convert.ToBase64String(Digest(digestMethod, canonical));

    private static HashAlgorithmName HashOf(string signatureMethod) => signatureMethod switch
    {
        "http://www.w3.org/2000/09/xmldsig#rsa-sha1" => HashAlgorithmName.SHA1,
        "http://www.w3.org/2001/04/xmldsig-more#rsa-sha384" => HashAlgorithmName.SHA384,
        "http://www.w3.org/2001/04/xmldsig-more#rsa-sha512" => HashAlgorithmName.SHA512,
        _ => HashAlgorithmName.SHA256,
    };

    private static byte[] Digest(string method, byte[] data) => method switch
    {
        "http://www.w3.org/2000/09/xmldsig#sha1" => SHA1.HashData(data),
        "http://www.w3.org/2001/04/xmldsig-more#sha384" => SHA384.HashData(data),
        "http://www.w3.org/2001/04/xmlenc#sha512" => SHA512.HashData(data),
        _ => SHA256.HashData(data),
    };
}
