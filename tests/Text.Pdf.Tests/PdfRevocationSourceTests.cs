using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// The revocation seam: the caller names where OCSP answers and CRLs come from, the suite embeds
/// what it is given, and it refuses — by name — when a PAdES-B-LT would be missing some of it.
/// </summary>
/// <remarks>
/// Every test here uses a fake source: no test in this file opens a socket, and none of them needs
/// one, which is the point of the seam.
/// </remarks>
public sealed class PdfRevocationSourceTests
{
    private static readonly Uri Authority = new("https://tsa.example.test/rfc3161");

    [Fact]
    public void A_source_that_answers_for_the_whole_chain_produces_baseline_LT()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);
        using var tsa = new FakeTsa();
        using var client = tsa.Client();
        var source = FakeRevocationSource.Ocsp(root);

        var result = PdfSigner.SignWithReport(
            TestPdf.OnePage(),
            signer,
            new PdfSignatureOptions
            {
                TimestampAuthority = Authority,
                TimestampClient = client,
                RevocationSource = source,
                Dss = new PdfDssOptions
                {
                    Certificates = [TestCertificates.Public(signer), TestCertificates.Public(root)],
                },
            });

        var signature = Assert.Single(PdfSignatureVerifier.Verify(result.Content).Signatures);

        Assert.True(signature.IsIntact, signature.Error);
        Assert.Equal(PdfSignatureLevel.BaselineLT, signature.Level);
        Assert.Equal(PdfSignatureLevel.BaselineLT, result.Level);
        Assert.Empty(result.MissingRevocation);

        var use = Assert.Single(result.Revocation);
        Assert.Equal(PdfRevocationKind.Ocsp, use.Kind);
        Assert.Equal(signer.Subject, use.Subject);
        Assert.Equal(root.Subject, use.Issuer);
        Assert.NotNull(use.IssuedAt);

        // The answer went into the store, as the DER the source returned.
        var text = SignedPdf.Text(result.Content);
        Assert.Contains("/OCSPs[", text, StringComparison.Ordinal);

        var stored = PdfDss.Read(PdfDocument.Parse(text));
        Assert.Single(stored.Ocsp);
        Assert.True(stored.Ocsp[0].GivesGoodAnswer(signer, root));

        // The anchor is its own authority: the source is never asked about it.
        var call = Assert.Single(source.Calls);
        Assert.Equal(signer.Subject, call.Subject);
        Assert.Equal(root.Subject, call.Issuer);
    }

    [Fact]
    public void A_source_that_omits_an_answer_is_refused_naming_the_certificate()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);
        using var tsa = new FakeTsa();
        using var client = tsa.Client();

        var error = Assert.Throws<PdfRevocationException>(() => PdfSigner.SignWithReport(
            TestPdf.OnePage(),
            signer,
            new PdfSignatureOptions
            {
                TimestampAuthority = Authority,
                TimestampClient = client,
                RevocationSource = FakeRevocationSource.Nothing(),
                Dss = new PdfDssOptions
                {
                    Certificates = [TestCertificates.Public(signer), TestCertificates.Public(root)],
                },
            }));

        Assert.IsAssignableFrom<PdfSignatureException>(error);
        Assert.Equal(signer.Subject, error.Subject);
        Assert.Equal(root.Subject, error.Issuer);
        Assert.Equal(Convert.ToHexString(signer.SerialNumberBytes.Span), error.SerialNumber);
        Assert.Contains(signer.Subject, error.Message, StringComparison.Ordinal);
        Assert.Contains(error.SerialNumber, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_explicitly_requested_baseline_T_is_honoured_and_lists_what_is_missing()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);
        using var tsa = new FakeTsa();
        using var client = tsa.Client();

        var result = PdfSigner.SignWithReport(
            TestPdf.OnePage(),
            signer,
            new PdfSignatureOptions
            {
                Level = PdfSignatureLevel.BaselineT,
                TimestampAuthority = Authority,
                TimestampClient = client,
                RevocationSource = FakeRevocationSource.Nothing(),
                Dss = new PdfDssOptions
                {
                    Certificates = [TestCertificates.Public(signer), TestCertificates.Public(root)],
                },
            });

        Assert.Equal(PdfSignatureLevel.BaselineT, result.Level);
        Assert.Empty(result.Revocation);

        var missing = Assert.Single(result.MissingRevocation);
        Assert.Equal(signer.Subject, missing.Subject);
        Assert.Equal(root.Subject, missing.Issuer);
        Assert.Equal(Convert.ToHexString(signer.SerialNumberBytes.Span), missing.SerialNumber);

        var signature = Assert.Single(PdfSignatureVerifier.Verify(result.Content).Signatures);
        Assert.True(signature.IsIntact, signature.Error);
        Assert.Equal(PdfSignatureLevel.BaselineT, signature.Level);
    }

    [Fact]
    public void A_source_that_returns_only_a_crl_still_proves_baseline_LT()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);
        using var tsa = new FakeTsa();
        using var client = tsa.Client();

        var source = new FakeRevocationSource(
            (_, _) => new PdfRevocationAnswer { Crl = TestRevocation.Crl(root) });

        var result = PdfSigner.SignWithReport(
            TestPdf.OnePage(),
            signer,
            new PdfSignatureOptions
            {
                TimestampAuthority = Authority,
                TimestampClient = client,
                RevocationSource = source,
                Dss = new PdfDssOptions
                {
                    Certificates = [TestCertificates.Public(signer), TestCertificates.Public(root)],
                },
            });

        var signature = Assert.Single(PdfSignatureVerifier.Verify(result.Content).Signatures);

        Assert.True(signature.IsIntact, signature.Error);
        Assert.Equal(PdfSignatureLevel.BaselineLT, signature.Level);

        var use = Assert.Single(result.Revocation);
        Assert.Equal(PdfRevocationKind.Crl, use.Kind);
        Assert.NotNull(use.IssuedAt);
        Assert.Contains("/CRLs[", SignedPdf.Text(result.Content), StringComparison.Ordinal);
    }

    [Fact]
    public void An_explicitly_requested_baseline_LT_is_satisfied_by_the_store_the_caller_supplies()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);
        using var tsa = new FakeTsa();
        using var client = tsa.Client();

        var result = PdfSigner.SignWithReport(
            TestPdf.OnePage(),
            signer,
            new PdfSignatureOptions
            {
                Level = PdfSignatureLevel.BaselineLT,
                TimestampAuthority = Authority,
                TimestampClient = client,
                Dss = new PdfDssOptions
                {
                    Certificates = [TestCertificates.Public(signer), TestCertificates.Public(root)],
                    Crls = [TestRevocation.Crl(root)],
                },
            });

        Assert.Empty(result.MissingRevocation);
        Assert.Equal(PdfRevocationKind.Crl, Assert.Single(result.Revocation).Kind);

        var signature = Assert.Single(PdfSignatureVerifier.Verify(result.Content).Signatures);
        Assert.Equal(PdfSignatureLevel.BaselineLT, signature.Level);
    }

    [Fact]
    public void An_explicitly_requested_baseline_LT_without_evidence_is_refused()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);
        using var tsa = new FakeTsa();
        using var client = tsa.Client();

        var error = Assert.Throws<PdfRevocationException>(() => PdfSigner.SignWithReport(
            TestPdf.OnePage(),
            signer,
            new PdfSignatureOptions
            {
                Level = PdfSignatureLevel.BaselineLT,
                TimestampAuthority = Authority,
                TimestampClient = client,
                Dss = new PdfDssOptions
                {
                    Certificates = [TestCertificates.Public(signer), TestCertificates.Public(root)],
                },
            }));

        Assert.Equal(signer.Subject, error.Subject);
    }

    [Fact]
    public void The_verifier_reports_which_answer_covers_each_certificate_and_when_it_was_produced()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);
        using var tsa = new FakeTsa();
        using var client = tsa.Client();

        var result = PdfSigner.SignWithReport(
            TestPdf.OnePage(),
            signer,
            new PdfSignatureOptions
            {
                TimestampAuthority = Authority,
                TimestampClient = client,
                RevocationSource = FakeRevocationSource.Ocsp(root),
                Dss = new PdfDssOptions
                {
                    Certificates = [TestCertificates.Public(signer), TestCertificates.Public(root)],
                },
            });

        var coverage = PdfSignatureVerifier.ReadRevocation(result.Content);

        Assert.Collection(
            coverage,
            entry =>
            {
                Assert.Equal(signer.Subject, entry.Subject);
                Assert.Equal(root.Subject, entry.Issuer);
                Assert.True(entry.OcspCovers);
                Assert.True(entry.IsCovered);
                Assert.NotNull(entry.OcspIssuedAt);
                Assert.True(entry.OcspIssuedAt <= DateTimeOffset.UtcNow);
            },
            entry =>
            {
                Assert.Equal(root.Subject, entry.Subject);
                Assert.False(entry.IsCovered);
                Assert.Null(entry.OcspIssuedAt);
                Assert.Null(entry.CrlIssuedAt);
            });
    }

    [Fact]
    public void The_verifier_reports_a_crl_and_the_moment_its_issuer_dated_it()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);
        using var tsa = new FakeTsa();
        using var client = tsa.Client();

        var crl = TestRevocation.Crl(root);
        var issued = PdfCrl.Parse(crl)!.ThisUpdate;

        var result = PdfSigner.SignWithReport(
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

        var coverage = PdfSignatureVerifier.ReadRevocation(result.Content);

        var entry = Assert.Single(coverage, item => item.Subject == signer.Subject);
        Assert.True(entry.CrlCovers);
        Assert.False(entry.OcspCovers);
        Assert.Equal(issued, entry.CrlIssuedAt);
        Assert.Equal(root.Subject, entry.Issuer);
        Assert.Equal(Convert.ToHexString(signer.SerialNumberBytes.Span), entry.SerialNumber);
    }

    [Fact]
    public void A_level_the_writer_cannot_produce_is_refused_before_anything_is_written()
    {
        using var signer = TestCertificates.Signer();

        Assert.Throws<PdfSignatureException>(() => PdfSigner.SignWithReport(
            TestPdf.OnePage(),
            signer,
            new PdfSignatureOptions { Level = PdfSignatureLevel.BaselineLTA }));

        Assert.Throws<PdfSignatureException>(() => PdfSigner.SignWithReport(
            TestPdf.OnePage(),
            signer,
            new PdfSignatureOptions { Level = PdfSignatureLevel.None }));
    }

    [Fact]
    public void Reading_the_revocation_of_an_empty_document_is_refused()
        => Assert.Throws<PdfSignatureException>(() => PdfSignatureVerifier.ReadRevocation([]));

    /// <summary>A source that answers from a function and records what it was asked about.</summary>
    private sealed class FakeRevocationSource : IRevocationSource
    {
        private readonly Func<X509Certificate2, X509Certificate2, PdfRevocationAnswer> _answer;

        public FakeRevocationSource(Func<X509Certificate2, X509Certificate2, PdfRevocationAnswer> answer)
            => _answer = answer;

        public List<(string Subject, string Issuer)> Calls { get; } = [];

        /// <summary>A source holding a "good" OCSP answer for every certificate it is asked about.</summary>
        /// <param name="issuer">The authority that signs the answers, with its private key.</param>
        public static FakeRevocationSource Ocsp(X509Certificate2 issuer)
            => new((subject, _) => new PdfRevocationAnswer { OcspResponse = TestRevocation.OcspGood(subject, issuer) });

        /// <summary>A source that holds nothing.</summary>
        public static FakeRevocationSource Nothing() => new((_, _) => PdfRevocationAnswer.None);

        public PdfRevocationAnswer GetRevocation(X509Certificate2 certificate, X509Certificate2 issuer)
        {
            Calls.Add((certificate.Subject, issuer.Subject));
            return _answer(certificate, issuer);
        }
    }
}
