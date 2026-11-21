using System;
using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// What a verifier may and may not conclude. Each test states one fact the result has to carry, and
/// one it must refuse to invent.
/// </summary>
public sealed class PdfSignatureVerifierTests
{
    private static readonly DateTimeOffset SignedAt = new(2026, 2, 1, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_signed_document_carries_one_intact_signature_that_covers_the_file()
    {
        var pdf = TestPdf.OnePage();
        var signed = PdfSigner.Sign(pdf, TestCertificates.Signer(), new PdfSignatureOptions { SignedAt = SignedAt });

        var result = PdfSignatureVerifier.Verify(signed);

        Assert.True(result.IsSigned);
        var signature = Assert.Single(result.Signatures);
        Assert.True(signature.IsIntact, signature.Error);
        Assert.Null(signature.Error);
        Assert.True(signature.CoversWholeFile);
        Assert.True(result.AllIntact);
        Assert.True(result.AllCoverWholeFile);
        Assert.Equal(PdfSignatureLevel.BaselineB, signature.Level);
        Assert.Equal(PdfSignatureLevel.BaselineB, result.ProvenLevel);
        Assert.Empty(result.DocumentTimestamps);
        Assert.False(result.HasDocumentTimestamp);
    }

    [Fact]
    public void The_signer_is_reported_with_its_validity_window()
    {
        using var certificate = TestCertificates.Signer("CN=Facture SARL, O=Facture, C=FR");
        var signed = PdfSigner.Sign(TestPdf.OnePage(), certificate, new PdfSignatureOptions { SignedAt = SignedAt });

        var signature = Assert.Single(PdfSignatureVerifier.Verify(signed).Signatures);

        Assert.Equal(certificate.Subject, signature.SignerSubject);
        Assert.Equal(certificate.Issuer, signature.SignerIssuer);
        Assert.NotNull(signature.SignerCertificate);
        Assert.Equal(
            certificate.Thumbprint,
            signature.SignerCertificate!.Thumbprint);

        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), signature.CertificateNotBefore);
        Assert.Equal(new DateTimeOffset(2036, 1, 1, 0, 0, 0, TimeSpan.Zero), signature.CertificateNotAfter);
    }

    [Fact]
    public void The_claimed_time_is_read_from_the_signature_dictionary()
    {
        var signed = PdfSigner.Sign(
            TestPdf.OnePage(),
            TestCertificates.Signer(),
            new PdfSignatureOptions { SignedAt = SignedAt });

        var signature = Assert.Single(PdfSignatureVerifier.Verify(signed).Signatures);

        Assert.Equal(SignedAt, signature.ClaimedSigningTime);
        Assert.Null(signature.TimestampedSigningTime);
        Assert.False(signature.HasDocumentTimestamp);
    }

    [Fact]
    public void A_trust_store_decides_the_chain()
    {
        using var certificate = TestCertificates.Signer();

        var signed = PdfSigner.Sign(TestPdf.OnePage(), certificate, new PdfSignatureOptions { SignedAt = SignedAt });

        var trusted = PdfSignatureVerifier.Verify(
            signed,
            new PdfTrustStore { Anchors = [TestCertificates.Public(certificate)] });

        var signature = Assert.Single(trusted.Signatures);
        Assert.Equal(PdfChainStatus.Trusted, signature.ChainStatus);
        Assert.Null(signature.ChainError);
    }

    [Fact]
    public void Without_a_trust_store_the_chain_is_not_evaluated()
    {
        var signed = PdfSigner.Sign(TestPdf.OnePage(), TestCertificates.Signer(), new PdfSignatureOptions { SignedAt = SignedAt });

        var implicitStore = Assert.Single(PdfSignatureVerifier.Verify(signed).Signatures);
        var emptyStore = Assert.Single(PdfSignatureVerifier.Verify(signed, PdfTrustStore.Empty).Signatures);
        var nullStore = Assert.Single(PdfSignatureVerifier.Verify(signed, null).Signatures);

        Assert.Equal(PdfChainStatus.NotEvaluated, implicitStore.ChainStatus);
        Assert.Equal(PdfChainStatus.NotEvaluated, emptyStore.ChainStatus);
        Assert.Equal(PdfChainStatus.NotEvaluated, nullStore.ChainStatus);
        Assert.Null(implicitStore.ChainError);
    }

    [Fact]
    public void An_unknown_anchor_leaves_the_chain_untrusted()
    {
        using var certificate = TestCertificates.Signer();
        using var stranger = TestCertificates.Root("CN=Some Other Root, O=Elsewhere, C=FR");

        var signed = PdfSigner.Sign(TestPdf.OnePage(), certificate, new PdfSignatureOptions { SignedAt = SignedAt });

        var result = PdfSignatureVerifier.Verify(
            signed,
            new PdfTrustStore { Anchors = [TestCertificates.Public(stranger)] });

        var signature = Assert.Single(result.Signatures);
        Assert.Equal(PdfChainStatus.Untrusted, signature.ChainStatus);
        Assert.False(string.IsNullOrWhiteSpace(signature.ChainError));
        Assert.True(signature.IsIntact);
    }

    [Fact]
    public void A_tampered_byte_breaks_the_signature()
    {
        var signed = PdfSigner.Sign(TestPdf.OnePage(), TestCertificates.Signer(), new PdfSignatureOptions { SignedAt = SignedAt });

        // A byte of the page content, well before the revision the signature appended.
        var tampered = (byte[])signed.Clone();
        var page = Encoding.Latin1.GetString(tampered).IndexOf("/MediaBox", StringComparison.Ordinal);
        tampered[page] = (byte)'X';

        var result = PdfSignatureVerifier.Verify(tampered);

        var signature = Assert.Single(result.Signatures);
        Assert.False(signature.IsIntact);
        Assert.NotNull(signature.Error);
        Assert.Contains("does not verify", signature.Error, StringComparison.Ordinal);
        Assert.Equal(PdfSignatureLevel.None, signature.Level);
        Assert.False(result.AllIntact);
    }

    [Fact]
    public void A_tampered_signature_value_breaks_the_signature()
    {
        var signed = PdfSigner.Sign(TestPdf.OnePage(), TestCertificates.Signer(), new PdfSignatureOptions { SignedAt = SignedAt });
        var text = Encoding.Latin1.GetString(signed);

        // One hex digit of the CMS itself: the digest no longer matches the signed attributes.
        var contents = Regex.Match(text, @"/Contents<([0-9A-Fa-f]{10})").Groups[1].Index;
        var tampered = (byte[])signed.Clone();
        tampered[contents] = tampered[contents] == (byte)'0' ? (byte)'1' : (byte)'0';

        var signature = Assert.Single(PdfSignatureVerifier.Verify(tampered).Signatures);

        Assert.False(signature.IsIntact);
        Assert.Equal(PdfSignatureLevel.None, signature.Level);
    }

    [Fact]
    public void A_signature_made_before_a_later_update_covers_only_its_revision()
    {
        var first = PdfSigner.Sign(TestPdf.OnePage(), TestCertificates.Signer(), new PdfSignatureOptions { SignedAt = SignedAt });
        var second = PdfSigner.Sign(first, TestCertificates.Signer(), new PdfSignatureOptions { SignedAt = SignedAt });

        var result = PdfSignatureVerifier.Verify(second);

        Assert.Equal(2, result.Signatures.Count);
        Assert.All(result.Signatures, signature => Assert.True(signature.IsIntact, signature.Error));
        Assert.False(result.Signatures[0].CoversWholeFile);
        Assert.True(result.Signatures[1].CoversWholeFile);
        Assert.False(result.AllCoverWholeFile);
        Assert.True(result.AllIntact);
    }

    [Fact]
    public void A_document_without_a_signature_is_reported_unsigned()
    {
        var result = PdfSignatureVerifier.Verify(TestPdf.OnePage());

        Assert.False(result.IsSigned);
        Assert.Empty(result.Signatures);
        Assert.Empty(result.DocumentTimestamps);
        Assert.Equal(PdfSignatureLevel.None, result.ProvenLevel);
        Assert.True(result.AllIntact);
        Assert.True(result.AllCoverWholeFile);
        Assert.False(result.HasDocumentTimestamp);
        Assert.True(result.AllDocumentTimestampsValid);
    }

    [Fact]
    public void An_empty_document_is_refused()
    {
        Assert.Throws<PdfSignatureException>(() => PdfSignatureVerifier.Verify(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Data_that_is_not_a_pdf_is_refused()
    {
        var exception = Assert.Throws<PdfSignatureException>(() =>
            PdfSignatureVerifier.Verify(Encoding.ASCII.GetBytes("this is not a PDF")));

        Assert.Contains("startxref", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_byte_range_that_runs_past_the_file_is_reported_rather_than_thrown()
    {
        var signed = PdfSigner.Sign(TestPdf.OnePage(), TestCertificates.Signer(), new PdfSignatureOptions { SignedAt = SignedAt });

        // Keep the width: the offset of every object written after it must not move.
        var broken = Regex.Replace(
            Encoding.Latin1.GetString(signed),
            @"/ByteRange\[0 (\d{10}) (\d{10}) (\d{10})\]",
            match => "/ByteRange[0 " + match.Groups[1].Value + " " + match.Groups[2].Value + " 9999999999]",
            RegexOptions.CultureInvariant);

        var signature = Assert.Single(PdfSignatureVerifier.Verify(Encoding.Latin1.GetBytes(broken)).Signatures);

        Assert.False(signature.IsIntact);
        Assert.Contains("does not fit", signature.Error, StringComparison.Ordinal);
        Assert.Equal(PdfSignatureLevel.None, signature.Level);
    }

    [Fact]
    public void A_byte_range_that_does_not_start_at_zero_is_reported()
    {
        var signed = PdfSigner.Sign(TestPdf.OnePage(), TestCertificates.Signer(), new PdfSignatureOptions { SignedAt = SignedAt });

        var broken = Regex.Replace(
            Encoding.Latin1.GetString(signed),
            @"/ByteRange\[0 (\d{10}) (\d{10}) (\d{10})\]",
            match => "/ByteRange[1 " + match.Groups[1].Value + " " + match.Groups[2].Value + " " + match.Groups[3].Value + "]",
            RegexOptions.CultureInvariant);

        var signature = Assert.Single(PdfSignatureVerifier.Verify(Encoding.Latin1.GetBytes(broken)).Signatures);

        Assert.False(signature.IsIntact);
        Assert.Contains("first byte", signature.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_claimed_time_with_an_offset_is_read_in_that_offset()
    {
        var pdf = Encoding.Latin1.GetBytes("""
            %PDF-1.7
            1 0 obj
            <</Type/Catalog/Pages 2 0 R>>
            endobj
            2 0 obj
            <</Type/Pages/Count 0/Kids[]>>
            endobj
            3 0 obj
            <</Type/Sig/ByteRange[0 0 0 0]/Contents<00>/M(D:20260101120000+02'00')>>
            endobj
            trailer
            <</Size 4/Root 1 0 R>>
            startxref
            0
            %%EOF
            """);

        var signature = Assert.Single(PdfSignatureVerifier.Verify(pdf).Signatures);

        Assert.False(signature.IsIntact);
        Assert.Contains("CMS", signature.Error, StringComparison.Ordinal);
        Assert.Equal(
            new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.FromHours(2)),
            signature.ClaimedSigningTime);
        Assert.Equal(
            new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero),
            signature.ClaimedSigningTime!.Value.ToUniversalTime());
    }

    [Fact]
    public void An_object_without_a_byte_range_is_not_a_signature()
    {
        var pdf = Encoding.Latin1.GetBytes(TestPdf.MinimalText.Replace(
            "/Type/Pages",
            "/Type/Pages/Note(not a signature)",
            StringComparison.Ordinal));

        var result = PdfSignatureVerifier.Verify(pdf);

        Assert.False(result.IsSigned);
    }

    [Fact]
    public void The_reported_levels_are_ordered()
    {
        // Guards the comparison PdfSignatureVerification.ProvenLevel relies on.
        Assert.True(PdfSignatureLevel.None < PdfSignatureLevel.BaselineB);
        Assert.True(PdfSignatureLevel.BaselineB < PdfSignatureLevel.BaselineT);
        Assert.True(PdfSignatureLevel.BaselineT < PdfSignatureLevel.BaselineLT);
        Assert.Equal("0", ((int)PdfSignatureLevel.None).ToString(CultureInfo.InvariantCulture));
    }
}
