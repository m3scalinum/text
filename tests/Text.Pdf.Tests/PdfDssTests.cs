using System;
using System.Linq;
using System.Text;
using Xunit;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// PAdES-B-LT: the document security store. The tests build the chain, a CRL and an OCSP answer,
/// and check that the verifier reaches its conclusion only when the evidence actually holds.
/// </summary>
public sealed class PdfDssTests
{
    private static readonly Uri Authority = new("https://tsa.example.test/rfc3161");

    private static readonly DateTimeOffset SignedAt = new(2026, 2, 1, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_store_with_the_chain_and_a_crl_proves_baseline_LT()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);
        using var tsa = new FakeTsa();
        using var client = tsa.Client();

        var signed = PdfSigner.Sign(
            TestPdf.OnePage(),
            signer,
            new PdfSignatureOptions
            {
                SignedAt = SignedAt,
                TimestampAuthority = Authority,
                TimestampClient = client,
                Dss = new PdfDssOptions
                {
                    Certificates = [TestCertificates.Public(signer), TestCertificates.Public(root)],
                    Crls = [TestRevocation.Crl(root)],
                },
            });

        var signature = Assert.Single(PdfSignatureVerifier.Verify(signed).Signatures);

        Assert.True(signature.IsIntact, signature.Error);
        Assert.Equal(PdfSignatureLevel.BaselineLT, signature.Level);
        Assert.True(signature.CoversWholeFile);
    }

    [Fact]
    public void The_store_round_trips_through_the_file()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);
        using var tsa = new FakeTsa();
        using var client = tsa.Client();
        var crl = TestRevocation.Crl(root);

        var signed = PdfSigner.Sign(
            TestPdf.OnePage(),
            signer,
            new PdfSignatureOptions
            {
                TimestampAuthority = Authority,
                TimestampClient = client,
                Dss = new PdfDssOptions
                {
                    Certificates = [TestCertificates.Public(signer), TestCertificates.Public(root)],
                    Crls = [crl],
                },
            });

        var text = SignedPdf.Text(signed);
        var document = PdfDocument.Parse(text);
        var store = PdfDss.Read(document);

        Assert.Collection(
            store.Certificates,
            entry => Assert.Equal(signer.Subject, entry.Subject),
            entry => Assert.Equal(root.Subject, entry.Subject));

        var stored = Assert.Single(store.Crls);
        Assert.True(stored.IssuedBy(root));
        Assert.True(stored.SignatureVerifies(root));
        Assert.Empty(stored.Revoked);

        Assert.Contains("/Certs[", text, StringComparison.Ordinal);
        Assert.Contains("/CRLs[", text, StringComparison.Ordinal);
        Assert.Contains(Encoding.Latin1.GetString(crl), text, StringComparison.Ordinal);
        Assert.Matches(@"/DSS \d+ 0 R", text);
    }

    [Fact]
    public void A_store_without_revocation_stops_at_baseline_T()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);
        using var tsa = new FakeTsa();
        using var client = tsa.Client();

        var signed = PdfSigner.Sign(
            TestPdf.OnePage(),
            signer,
            new PdfSignatureOptions
            {
                TimestampAuthority = Authority,
                TimestampClient = client,
                Dss = new PdfDssOptions
                {
                    Certificates = [TestCertificates.Public(signer), TestCertificates.Public(root)],
                },
            });

        var signature = Assert.Single(PdfSignatureVerifier.Verify(signed).Signatures);

        Assert.Equal(PdfSignatureLevel.BaselineT, signature.Level);
    }

    [Fact]
    public void A_crl_that_revokes_the_signer_is_not_evidence()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);
        using var tsa = new FakeTsa();
        using var client = tsa.Client();

        var signed = PdfSigner.Sign(
            TestPdf.OnePage(),
            signer,
            new PdfSignatureOptions
            {
                TimestampAuthority = Authority,
                TimestampClient = client,
                Dss = new PdfDssOptions
                {
                    Certificates = [TestCertificates.Public(signer), TestCertificates.Public(root)],
                    Crls = [TestRevocation.Crl(root, signer.SerialNumberBytes.ToArray())],
                },
            });

        var signature = Assert.Single(PdfSignatureVerifier.Verify(signed).Signatures);

        Assert.Equal(PdfSignatureLevel.BaselineT, signature.Level);
    }

    [Fact]
    public void A_crl_signed_by_someone_else_is_not_evidence()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);
        using var impostor = TestCertificates.Root();
        using var tsa = new FakeTsa();
        using var client = tsa.Client();

        // The list names the root but is signed with another key of the same name.
        var forged = TestRevocation.ForgedCrl(root, impostor);
        var parsed = PdfCrl.Parse(forged);

        Assert.NotNull(parsed);
        Assert.True(parsed!.IssuedBy(root));
        Assert.False(parsed.SignatureVerifies(root));

        var signed = PdfSigner.Sign(
            TestPdf.OnePage(),
            signer,
            new PdfSignatureOptions
            {
                TimestampAuthority = Authority,
                TimestampClient = client,
                Dss = new PdfDssOptions
                {
                    Certificates = [TestCertificates.Public(signer), TestCertificates.Public(root)],
                    Crls = [forged],
                },
            });

        var signature = Assert.Single(PdfSignatureVerifier.Verify(signed).Signatures);

        Assert.Equal(PdfSignatureLevel.BaselineT, signature.Level);
    }

    [Fact]
    public void An_ocsp_answer_proves_the_chain()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);
        using var tsa = new FakeTsa();
        using var client = tsa.Client();

        var signed = PdfSigner.Sign(
            TestPdf.OnePage(),
            signer,
            new PdfSignatureOptions
            {
                TimestampAuthority = Authority,
                TimestampClient = client,
                Dss = new PdfDssOptions
                {
                    Certificates = [TestCertificates.Public(signer), TestCertificates.Public(root)],
                    OcspResponses = [TestRevocation.OcspGood(signer, root)],
                },
            });

        var signature = Assert.Single(PdfSignatureVerifier.Verify(signed).Signatures);

        Assert.Equal(PdfSignatureLevel.BaselineLT, signature.Level);
    }

    [Fact]
    public void An_ocsp_answer_for_another_certificate_is_not_evidence()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);
        using var colleague = TestCertificates.Issued("CN=Colleague, O=Facture, C=FR", root, serial: 44);
        using var tsa = new FakeTsa();
        using var client = tsa.Client();

        var signed = PdfSigner.Sign(
            TestPdf.OnePage(),
            signer,
            new PdfSignatureOptions
            {
                TimestampAuthority = Authority,
                TimestampClient = client,
                Dss = new PdfDssOptions
                {
                    Certificates = [TestCertificates.Public(signer), TestCertificates.Public(root)],
                    OcspResponses = [TestRevocation.OcspGood(colleague, root)],
                },
            });

        var signature = Assert.Single(PdfSignatureVerifier.Verify(signed).Signatures);

        Assert.Equal(PdfSignatureLevel.BaselineT, signature.Level);
    }

    [Fact]
    public void A_store_without_the_root_does_not_prove_the_chain()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);
        using var tsa = new FakeTsa();
        using var client = tsa.Client();

        var signed = PdfSigner.Sign(
            TestPdf.OnePage(),
            signer,
            new PdfSignatureOptions
            {
                TimestampAuthority = Authority,
                TimestampClient = client,
                Dss = new PdfDssOptions
                {
                    Certificates = [TestCertificates.Public(signer)],
                    Crls = [TestRevocation.Crl(root)],
                },
            });

        var signature = Assert.Single(PdfSignatureVerifier.Verify(signed).Signatures);

        Assert.Equal(PdfSignatureLevel.BaselineT, signature.Level);
    }

    [Fact]
    public void A_store_without_a_timestamp_never_reaches_LT()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);

        var signed = PdfSigner.Sign(
            TestPdf.OnePage(),
            signer,
            new PdfSignatureOptions
            {
                Dss = new PdfDssOptions
                {
                    Certificates = [TestCertificates.Public(signer), TestCertificates.Public(root)],
                    Crls = [TestRevocation.Crl(root)],
                },
            });

        var signature = Assert.Single(PdfSignatureVerifier.Verify(signed).Signatures);

        Assert.Equal(PdfSignatureLevel.BaselineB, signature.Level);
        Assert.Contains("/Certs[", SignedPdf.Text(signed), StringComparison.Ordinal);
    }

    [Fact]
    public void A_second_store_is_merged_with_the_first()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);
        using var tsa = new FakeTsa();
        using var client = tsa.Client();

        var first = PdfSigner.Sign(
            TestPdf.OnePage(),
            signer,
            new PdfSignatureOptions
            {
                Dss = new PdfDssOptions { Certificates = [TestCertificates.Public(signer)] },
            });

        var second = PdfSigner.Sign(
            first,
            TestCertificates.Signer(),
            new PdfSignatureOptions
            {
                Dss = new PdfDssOptions { Certificates = [TestCertificates.Public(root)] },
            });

        var store = PdfDss.Read(PdfDocument.Parse(SignedPdf.Text(second)));

        Assert.Collection(
            store.Certificates,
            entry => Assert.Equal(signer.Subject, entry.Subject),
            entry => Assert.Equal(root.Subject, entry.Subject));

        // One store, pointed at by the last catalog, and it merged rather than replaced.
        var catalog = PdfDocument.Parse(SignedPdf.Text(second)).Catalog;
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(catalog, @"/DSS \d+ 0 R"));
    }

    [Fact]
    public void A_store_survives_a_verification_that_carries_no_trust_anchor()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);
        using var tsa = new FakeTsa();
        using var client = tsa.Client();

        var signed = PdfSigner.Sign(
            TestPdf.OnePage(),
            signer,
            new PdfSignatureOptions
            {
                TimestampAuthority = Authority,
                TimestampClient = client,
                Dss = new PdfDssOptions
                {
                    Certificates = [TestCertificates.Public(signer), TestCertificates.Public(root)],
                    Crls = [TestRevocation.Crl(root)],
                },
            });

        var signature = Assert.Single(PdfSignatureVerifier.Verify(signed).Signatures);

        // The level says what the store proves; the trust store says whether the caller believes it.
        Assert.Equal(PdfSignatureLevel.BaselineLT, signature.Level);
        Assert.Equal(PdfChainStatus.NotEvaluated, signature.ChainStatus);

        var trusted = Assert.Single(PdfSignatureVerifier.Verify(
            signed,
            new PdfTrustStore { Anchors = [TestCertificates.Public(root)] }).Signatures);

        Assert.Equal(PdfChainStatus.Trusted, trusted.ChainStatus);
        Assert.Equal(PdfSignatureLevel.BaselineLT, trusted.Level);
    }

    [Fact]
    public void A_stream_that_is_not_a_certificate_is_ignored()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);

        var signed = PdfSigner.Sign(
            TestPdf.OnePage(),
            signer,
            new PdfSignatureOptions
            {
                Dss = new PdfDssOptions
                {
                    Certificates = [TestCertificates.Public(signer), TestCertificates.Public(root)],
                    Crls = [new byte[] { 0x30, 0x00 }],
                    OcspResponses = [new byte[] { 0x30, 0x00 }],
                },
            });

        var store = PdfDss.Read(PdfDocument.Parse(SignedPdf.Text(signed)));

        Assert.Equal(
            [signer.Subject, root.Subject],
            store.Certificates.Select(entry => entry.Subject).ToList());
        Assert.Empty(store.Crls);
        Assert.Empty(store.Ocsp);
    }
}
