using System;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Xunit;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// Reading <c>/Contents</c> back deterministically: from the object graph and the byte range, with
/// the DER length taken from the ASN.1 header rather than from trimming zeros off the padding.
/// </summary>
public sealed class PdfLtaExtractionTests
{
    private static byte[] Sign(X509Certificate2 certificate)
        => PdfSigner.Sign(
            TestPdf.OnePage(),
            certificate,
            new PdfSignatureOptions { SignedAt = new DateTimeOffset(2026, 2, 1, 10, 30, 0, TimeSpan.Zero) });

    /// <summary>A certificate whose DER size can be varied, to show the extraction follows the header.</summary>
    private static X509Certificate2 Certificate(int extensionBytes)
    {
        using var key = RSA.Create(2048);

        var request = new CertificateRequest(
            "CN=M3scalinum LTA Test, O=M3scalinum.Text.Pdf, C=FR",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, critical: true));

        if (extensionBytes > 0)
        {
            request.CertificateExtensions.Add(
                new X509Extension("1.3.6.1.4.1.99999.7", RandomNumberGenerator.GetBytes(extensionBytes), critical: false));
        }

        using var created = request.CreateSelfSigned(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2036, 1, 1, 0, 0, 0, TimeSpan.Zero));

        return TestCertificates.WithKey(created);
    }

    [Fact]
    public void The_contents_sit_in_the_gap_the_byte_range_names()
    {
        using var certificate = TestCertificates.Signer();
        var pdf = Sign(certificate);

        var dictionary = Assert.Single(PdfLtaSignatureSlice.Dictionaries(pdf));
        var hex = PdfLtaSignatureSlice.Hex(pdf, dictionary);

        // The placeholder is fixed-width, so the gap is exactly the hex string and its delimiters.
        Assert.Equal(PdfContents.ReservedBytes * 2, hex.Length);
        Assert.Equal(dictionary.ByteRange[1] + 1, dictionary.ContentsStart);
        Assert.Equal(dictionary.ByteRange[2], dictionary.ContentsStart + hex.Length + 1);
        Assert.All(hex, digit => Assert.True("0123456789ABCDEF".Contains(digit, StringComparison.Ordinal)));
    }

    [Fact]
    public void The_extraction_returns_the_length_the_header_declares_for_differently_sized_certificates()
    {
        using var small = Certificate(extensionBytes: 0);
        using var large = Certificate(extensionBytes: 600);

        var smallPdf = Sign(small);
        var largePdf = Sign(large);

        var smallSignature = PdfLtaSignatureSlice.Contents(smallPdf);
        var largeSignature = PdfLtaSignatureSlice.Contents(largePdf);

        // A certificate that is 600 bytes longer makes the CMS longer, by more than its own difference
        // once the signature over it is counted.
        Assert.NotEqual(smallSignature.Length, largeSignature.Length);
        Assert.True(largeSignature.Length > smallSignature.Length + 600);

        // Both are exactly what their header says: no trailing zero was trimmed, no padding was kept.
        Assert.Equal(smallSignature.Length, PdfLtaSignatureSlice.DeclaredLength(smallSignature));
        Assert.Equal(largeSignature.Length, PdfLtaSignatureSlice.DeclaredLength(largeSignature));
        Assert.Equal(0x30, smallSignature[0]);
        Assert.Equal(0x30, largeSignature[0]);

        // And both verify, which is the fact a truncated DER would break.
        foreach (var (pdf, der) in new[] { (smallPdf, smallSignature), (largePdf, largeSignature) })
        {
            var dictionary = Assert.Single(PdfLtaSignatureSlice.Dictionaries(pdf));
            var cms = new SignedCms(new ContentInfo(PdfLtaSignatureSlice.Covered(pdf, dictionary)), detached: true);
            cms.Decode(der);

            cms.CheckSignature(verifySignatureOnly: true);
            Assert.Single(cms.SignerInfos);
        }
    }

    [Fact]
    public void The_padding_after_the_der_is_zeros_and_is_not_part_of_it()
    {
        using var certificate = TestCertificates.Signer();
        var pdf = Sign(certificate);

        var dictionary = Assert.Single(PdfLtaSignatureSlice.Dictionaries(pdf));
        var raw = Convert.FromHexString(PdfLtaSignatureSlice.Hex(pdf, dictionary));
        var der = PdfLtaSignatureSlice.Contents(pdf, dictionary);

        Assert.Equal(PdfContents.ReservedBytes, raw.Length);
        Assert.True(der.Length < raw.Length);
        Assert.Equal(der, raw.AsSpan(0, der.Length).ToArray());
        Assert.All(raw.AsSpan(der.Length).ToArray(), value => Assert.Equal(0, value));
    }

    [Fact]
    public void The_second_signature_of_a_document_is_read_without_assuming_a_layout()
    {
        var first = Sign(TestCertificates.Signer());
        var second = PdfSigner.Sign(first, TestCertificates.Signer(), new PdfSignatureOptions
        {
            SignedAt = new DateTimeOffset(2026, 2, 2, 10, 30, 0, TimeSpan.Zero),
        });

        var dictionaries = PdfLtaSignatureSlice.Dictionaries(second);
        Assert.Equal(2, dictionaries.Count);

        // The first signature still covers its own revision, which the second one appended after.
        var firstCms = new SignedCms(
            new ContentInfo(PdfLtaSignatureSlice.Covered(second, dictionaries[0])),
            detached: true);
        firstCms.Decode(PdfLtaSignatureSlice.Contents(second, 0));
        firstCms.CheckSignature(verifySignatureOnly: true);

        var last = PdfLtaSignatureSlice.Dictionaries(second)[1];
        Assert.Equal(second.Length, last.ByteRange[2] + last.ByteRange[3]);
    }

    [Fact]
    public void A_document_without_a_signature_has_no_dictionary_to_read()
    {
        Assert.Empty(PdfLtaSignatureSlice.Dictionaries(TestPdf.OnePage()));
    }
}
