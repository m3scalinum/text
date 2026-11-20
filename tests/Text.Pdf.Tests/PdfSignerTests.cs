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
            "CN=M3scalinum.Text.SharpTypst Test, O=M3scalinum.Text.SharpTypst, C=FR",
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

    /// <summary>Reads the byte range a reader would use, and returns the two slices.</summary>
    private static (byte[] Signed, byte[] Signature) Extract(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);

        var range = Regex.Match(text, @"/ByteRange\[(\d+) (\d+) (\d+) (\d+)\]");
        Assert.True(range.Success, "the signature dictionary carries no /ByteRange");

        var first = int.Parse(range.Groups[1].Value, CultureInfo.InvariantCulture);
        var firstLength = int.Parse(range.Groups[2].Value, CultureInfo.InvariantCulture);
        var second = int.Parse(range.Groups[3].Value, CultureInfo.InvariantCulture);
        var secondLength = int.Parse(range.Groups[4].Value, CultureInfo.InvariantCulture);

        Assert.Equal(0, first);
        Assert.Equal(pdf.Length, second + secondLength);

        var signed = new byte[firstLength + secondLength];
        Buffer.BlockCopy(pdf, first, signed, 0, firstLength);
        Buffer.BlockCopy(pdf, second, signed, firstLength, secondLength);

        var contents = Regex.Match(text, @"/Contents<([0-9A-Fa-f]+)>");
        Assert.True(contents.Success, "the signature dictionary carries no /Contents");

        var hex = contents.Groups[1].Value.TrimEnd('0');
        if (hex.Length % 2 != 0)
        {
            hex += "0";
        }

        return (signed, Convert.FromHexString(hex));
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

        // And the second one covers the whole file, first signature included.
        var text = Encoding.Latin1.GetString(second);
        Assert.Equal(2, Regex.Count(text, @"/SubFilter/ETSI\.CAdES\.detached"));
    }
}
