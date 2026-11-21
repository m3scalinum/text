using System;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// The arithmetic that makes a PAdES signature what it is: the range must exclude the hex string and
/// nothing else, and it must add up to the file exactly.
/// </summary>
public sealed class PdfByteRangeTests
{
    /// <summary>8 KiB of CMS room, twice as many hex digits, and the two angle brackets.</summary>
    private const int ReservedHex = 1 + (8192 * 2) + 1;

    [Fact]
    public void The_range_excludes_exactly_the_hex_string_of_the_signature()
    {
        var signed = PdfSigner.Sign(TestPdf.OnePage(), TestCertificates.Signer());
        var text = SignedPdf.Text(signed);

        var range = Assert.Single(SignedPdf.Ranges(signed));

        Assert.Equal(0, range[0]);
        Assert.Equal(signed.Length, range[2] + range[3]);
        Assert.True(range[1] < range[2]);

        // The first range stops on the '<' that opens the hex string...
        Assert.Equal("Contents", text.Substring((int)range[1] - 8, 8));
        Assert.Equal('<', text[(int)range[1]]);

        // ...and the second starts just after the '>' that closes it.
        Assert.Equal('>', text[(int)range[2] - 1]);
        Assert.Equal(ReservedHex, range[2] - range[1]);

        // Nothing in between is part of the signed content: every byte of it is hex or a bracket.
        var excluded = text.Substring((int)range[1], (int)(range[2] - range[1]));
        Assert.Matches("^<[0-9A-F]+>$", excluded);

        // The CMS starts with the SEQUENCE tag, which is what a reader decodes.
        Assert.Equal("30", text.Substring((int)range[1] + 1, 2));
    }

    [Fact]
    public void The_signed_content_is_the_file_minus_the_hex_string()
    {
        var signed = PdfSigner.Sign(TestPdf.OnePage(), TestCertificates.Signer());
        var range = Assert.Single(SignedPdf.Ranges(signed));

        Assert.Equal(signed.Length - ReservedHex, range[1] + range[3]);
        Assert.Equal(range[1] + range[3], SignedPdf.Covered(signed, range).Length);
    }

    [Fact]
    public void The_range_holds_when_the_signature_carries_an_appearance_and_a_store()
    {
        using var certificate = TestCertificates.Signer();
        using var root = TestCertificates.Root();

        var signed = PdfSigner.Sign(
            TestPdf.OnePage(),
            certificate,
            new PdfSignatureOptions
            {
                SignedAt = new DateTimeOffset(2026, 2, 1, 10, 30, 0, TimeSpan.Zero),
                Appearance = new PdfSignatureAppearance
                {
                    Width = 200,
                    Height = 60,
                    TextLines = ["Signé par M3scalinum"],
                },
                Dss = new PdfDssOptions
                {
                    Certificates = [TestCertificates.Public(certificate), TestCertificates.Public(root)],
                    Crls = [TestRevocation.Crl(root)],
                },
            });

        var range = Assert.Single(SignedPdf.Ranges(signed));
        var text = SignedPdf.Text(signed);

        Assert.Equal(signed.Length, range[2] + range[3]);
        Assert.Equal('<', text[(int)range[1]]);
        Assert.Equal(ReservedHex, range[2] - range[1]);

        // The store and the appearance are inside the signed content, not appended after it.
        Assert.Contains("/DSS", text, StringComparison.Ordinal);
        Assert.Contains("/BBox[0 0 200 60]", text, StringComparison.Ordinal);
        Assert.Contains("/AP<</N ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_second_signature_gets_a_range_that_covers_the_first_revision()
    {
        var first = PdfSigner.Sign(TestPdf.OnePage(), TestCertificates.Signer());
        var second = PdfSigner.Sign(first, TestCertificates.Signer());

        var ranges = SignedPdf.Ranges(second);
        Assert.Equal(2, ranges.Count);

        // The first signature still covers its own revision, byte for byte.
        Assert.Equal(first.Length, ranges[0][2] + ranges[0][3]);

        // The second covers everything, including the first signature's dictionary.
        Assert.Equal(second.Length, ranges[1][2] + ranges[1][3]);
        Assert.True(ranges[1][2] > ranges[0][2]);

        // And the earlier revision is untouched, so the first range still names the same bytes.
        Assert.Equal(first.Length, SignedPdf.Ranges(first)[0][2] + SignedPdf.Ranges(first)[0][3]);
    }

    [Fact]
    public void A_signature_whose_byte_range_crosses_its_own_contents_is_detected()
    {
        var signed = PdfSigner.Sign(TestPdf.OnePage(), TestCertificates.Signer());
        var text = SignedPdf.Text(signed);
        var range = Assert.Single(SignedPdf.Ranges(signed));

        // Move the second range back so it swallows part of the hex string: the digest no longer
        // matches what was signed, which is exactly what the range exists to prevent.
        var broken = text.Replace(
            $"/ByteRange[0 {range[1]:D10} {range[2]:D10} {range[3]:D10}]",
            $"/ByteRange[0 {range[1]:D10} {range[2] - 10:D10} {range[3] + 10:D10}]",
            StringComparison.Ordinal);

        var signature = Assert.Single(PdfSignatureVerifier.Verify(Encoding.Latin1.GetBytes(broken)).Signatures);

        Assert.False(signature.IsIntact);
    }

    [Fact]
    public void The_byte_range_of_a_document_timestamp_excludes_its_token()
    {
        using var tsa = new FakeTsa();
        using var client = tsa.Client();

        var signed = PdfSigner.Sign(TestPdf.OnePage(), TestCertificates.Signer());
        var stamped = PdfDocumentTimestamp.Add(signed, new Uri("https://tsa.example.test/"), client);

        var ranges = SignedPdf.Ranges(stamped);
        Assert.Equal(2, ranges.Count);
        Assert.Equal(stamped.Length, ranges[1][2] + ranges[1][3]);
        Assert.Equal(ReservedHex, ranges[1][2] - ranges[1][1]);

        // The token starts with a SEQUENCE tagged ContentInfo, like the CMS it sits next to.
        var text = SignedPdf.Text(stamped);
        Assert.Equal("30", text.Substring((int)ranges[1][1] + 1, 2));
        Assert.Matches(@"^\d+ \d+$", Regex.Match(text, @"xref\n(\d+ \d+)").Groups[1].Value);
    }
}
