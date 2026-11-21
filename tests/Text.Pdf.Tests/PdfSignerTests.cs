using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// A PAdES signature is a detached CMS over the whole file minus the bytes that hold it. These
/// tests verify the signature the way a reader does — by slicing the byte range and checking the
/// CMS — rather than by looking for strings.
/// </summary>
public sealed class PdfSignerTests
{
    /// <summary>The smallest PDF the signer accepts: a catalog, a page tree, a trailer.</summary>
    private const string Minimal = """
        %PDF-1.7
        1 0 obj
        <</Type/Catalog/Pages 2 0 R>>
        endobj
        2 0 obj
        <</Type/Pages/Count 0/Kids[]>>
        endobj
        trailer
        <</Size 3/Root 1 0 R>>
        startxref
        0
        %%EOF
        """;

    private static X509Certificate2 Certificate()
    {
        using var key = RSA.Create(2048);

        var request = new CertificateRequest(
            "CN=M3scalinum.Text.Typst Test, O=M3scalinum.Text.Typst, C=FR",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, critical: true));

        using var created = request.CreateSelfSigned(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2036, 1, 1, 0, 0, 0, TimeSpan.Zero));

        // Round-trip through PKCS#12 so the private key is usable by the CMS builder on every
        // platform, which is what a caller loading a .pfx gets.
        return X509CertificateLoader.LoadPkcs12(
            created.Export(X509ContentType.Pkcs12),
            password: null,
            X509KeyStorageFlags.Exportable);
    }

    private static byte[] Sign(string pdf, PdfSignatureOptions? options = null)
        => PdfSigner.Sign(Encoding.Latin1.GetBytes(pdf), Certificate(), options);

    /// <summary>
    /// Reads the byte range a reader would use, and returns the two slices. The signature is located
    /// through the object table and the range it declares, not by a regex over the file as text: the
    /// hex placeholder is zero-padded, and trimming those zeros cuts real DER bytes whenever the CMS
    /// happens to end in <c>0x00</c> — a flake that shows up in one run out of a hundred and
    /// twenty-eight. <see cref="PdfLtaSignatureSlice"/> takes the length from the ASN.1 header.
    /// </summary>
    private static (byte[] Signed, byte[] Signature) Extract(byte[] pdf)
    {
        var dictionary = Assert.Single(PdfLtaSignatureSlice.Dictionaries(pdf));
        var range = dictionary.ByteRange;

        Assert.Equal(0, range[0]);
        Assert.Equal(pdf.Length, range[2] + range[3]);

        return (PdfLtaSignatureSlice.Covered(pdf, dictionary), PdfLtaSignatureSlice.Contents(pdf, dictionary));
    }

    [Fact]
    public void The_signature_verifies_over_the_byte_range()
    {
        var signed = Sign(Minimal);
        var (content, signature) = Extract(signed);

        var cms = new SignedCms(new ContentInfo(content), detached: true);
        cms.Decode(signature);

        // Throws when the digest does not match, which is the whole point of the test.
        cms.CheckSignature(verifySignatureOnly: true);

        Assert.Single(cms.SignerInfos);
    }

    [Fact]
    public void The_document_declares_a_pades_signature()
    {
        var text = Encoding.Latin1.GetString(Sign(Minimal));

        Assert.Contains("/SubFilter/ETSI.CAdES.detached", text, StringComparison.Ordinal);
        Assert.Contains("/Type/Sig", text, StringComparison.Ordinal);
        Assert.Contains("/AcroForm<</Fields[", text, StringComparison.Ordinal);
        Assert.Contains("/SigFlags 3", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_original_bytes_are_kept_as_a_prefix()
    {
        var original = Encoding.Latin1.GetBytes(Minimal);
        var signed = PdfSigner.Sign(original, Certificate());

        Assert.True(signed.Length > original.Length);
        Assert.Equal(original, signed.AsSpan(0, original.Length).ToArray());
    }

    [Fact]
    public void The_signature_declares_what_the_caller_asked()
    {
        var text = Encoding.Latin1.GetString(Sign(Minimal, new PdfSignatureOptions
        {
            FieldName = "Facture",
            Reason = "Facture émise",
            Location = "Paris",
            SignedAt = new DateTimeOffset(2026, 2, 1, 10, 30, 0, TimeSpan.Zero),
        }));

        Assert.Contains("/T(Facture)", text, StringComparison.Ordinal);
        Assert.Contains("/Reason(Facture émise)", text, StringComparison.Ordinal);
        Assert.Contains("/Location(Paris)", text, StringComparison.Ordinal);
        Assert.Contains("/M(D:20260201103000+00'00')", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_certificate_without_a_private_key_is_refused()
    {
        using var certificate = X509CertificateLoader.LoadCertificate(
            Certificate().Export(X509ContentType.Cert));

        var exception = Assert.Throws<PdfSignatureException>(() =>
            PdfSigner.Sign(Encoding.Latin1.GetBytes(Minimal), certificate));

        Assert.Contains("private key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Data_that_is_not_a_pdf_is_refused()
    {
        var exception = Assert.Throws<PdfSignatureException>(() =>
            PdfSigner.Sign(Encoding.ASCII.GetBytes("not a pdf at all"), Certificate()));

        Assert.Contains("startxref", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_document_is_refused()
    {
        Assert.Throws<PdfSignatureException>(() =>
            PdfSigner.Sign(ReadOnlySpan<byte>.Empty, Certificate()));
    }

    [Fact]
    public void A_second_signature_leaves_the_first_one_valid()
    {
        var first = Sign(Minimal);
        var second = PdfSigner.Sign(first, Certificate());

        // The first signature still covers its own byte range, which the second one appended after.
        var (content, signature) = Extract(first);
        var cms = new SignedCms(new ContentInfo(content), detached: true);
        cms.Decode(signature);
        cms.CheckSignature(verifySignatureOnly: true);

        // And the second one covers the whole file, first signature included. Both dictionaries are
        // counted from the object table, not by matching a string in the file.
        var dictionaries = PdfLtaSignatureSlice.Dictionaries(second);
        Assert.Equal(2, dictionaries.Count);
        Assert.Equal(second.Length, dictionaries[1].ByteRange[2] + dictionaries[1].ByteRange[3]);
        Assert.True(first.Length <= dictionaries[1].ByteRange[2]);

        var last = new SignedCms(
            new ContentInfo(PdfLtaSignatureSlice.Covered(second, dictionaries[1])),
            detached: true);
        last.Decode(PdfLtaSignatureSlice.Contents(second, 1));
        last.CheckSignature(verifySignatureOnly: true);
    }
}
