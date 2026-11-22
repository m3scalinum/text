using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Xunit;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// Batch verification: one verdict per document, counted by chain status, with the certificates a
/// chain could not accept named and the report rendered as a stable text.
/// </summary>
public sealed class PdfBatchVerificationTests
{
    private const string SignerSubject = "CN=Invoice Signer, O=Factures SARL, C=FR";

    private const string StrangerSubject = "CN=Some Other Root, O=Elsewhere, C=FR";

    private static readonly DateTimeOffset SignedAt = new(2026, 2, 1, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_batch_of_three_documents_counts_one_per_chain_status()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.WithKey(TestCertificates.Issued(SignerSubject, root));
        using var strangerRoot = TestCertificates.Root(StrangerSubject);
        using var strangerSigner = TestCertificates.WithKey(TestCertificates.Issued("CN=Other Signer, C=FR", strangerRoot));

        var trustStore = new PdfTrustStore { Anchors = [TestCertificates.Public(root)] };

        var report = PdfBatchSignatureVerifier.Verify(
            [Sign(TestPdf.OnePage(), signer), Sign(TestPdf.OnePage(), strangerSigner), TestPdf.OnePage()],
            new PdfBatchSignatureOptions { TrustStore = trustStore });

        Assert.Equal(3, report.Documents);
        Assert.Equal(3, report.Read);
        Assert.Equal(0, report.Unreadable);
        Assert.Equal(2, report.SignedDocuments);

        Assert.Equal(1, report.CountsByStatus[PdfChainStatus.Trusted]);
        Assert.Equal(1, report.CountsByStatus[PdfChainStatus.Untrusted]);
        Assert.Equal(1, report.CountsByStatus[PdfChainStatus.NotEvaluated]);
        Assert.Equal(Enum.GetValues<PdfChainStatus>().Length, report.CountsByStatus.Count);

        Assert.Equal([0], report.DocumentsByStatus[PdfChainStatus.Trusted]);
        Assert.Equal([1], report.DocumentsByStatus[PdfChainStatus.Untrusted]);
        Assert.Equal([2], report.DocumentsByStatus[PdfChainStatus.NotEvaluated]);
    }

    [Fact]
    public void The_report_of_a_mixed_batch_is_pinned_line_for_line()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.WithKey(TestCertificates.Issued(SignerSubject, root));
        using var strangerRoot = TestCertificates.Root(StrangerSubject);
        using var strangerSigner = TestCertificates.WithKey(TestCertificates.Issued("CN=Other Signer, C=FR", strangerRoot));

        var report = PdfBatchSignatureVerifier.Verify(
            [Sign(TestPdf.OnePage(), signer), Sign(TestPdf.OnePage(), strangerSigner), TestPdf.OnePage()],
            new PdfBatchSignatureOptions { TrustStore = new PdfTrustStore { Anchors = [TestCertificates.Public(root)] } });

        var expected = string.Join(
            '\n',
            "M3SCALINUM-BATCH-SIGNATURE-REPORT 1",
            "documents\t3",
            "read\t3",
            "unreadable\t0",
            "signed\t2",
            "status\tNotEvaluated\t1",
            "status\tTrusted\t1",
            "status\tUntrusted\t1",
            "document\t0\tTrusted\t1\t0",
            "document\t1\tUntrusted\t1\t1",
            "missing\t1\t\t" + StrangerSubject + "\t\tnamed",
            "document\t2\tNotEvaluated\t0\t0",
            string.Empty);

        Assert.Equal(expected, report.ToText());
        Assert.Equal(Encoding.UTF8.GetBytes(expected), report.ToBytes());
    }

    [Fact]
    public void An_unreadable_document_becomes_an_entry_and_leaves_the_others_verified()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.WithKey(TestCertificates.Issued(SignerSubject, root));

        var signed = Sign(TestPdf.OnePage(), signer);

        var report = PdfBatchSignatureVerifier.Verify(
            [signed, Encoding.Latin1.GetBytes("this is not a pdf"), signed],
            new PdfBatchSignatureOptions { TrustStore = new PdfTrustStore { Anchors = [TestCertificates.Public(root)] } });

        Assert.Equal(3, report.Documents);
        Assert.Equal(2, report.Read);
        Assert.Equal(1, report.Unreadable);
        Assert.Equal(2, report.SignedDocuments);

        var broken = report.Entries[1];
        Assert.False(broken.IsRead);
        Assert.Contains("not a PDF", broken.Error, StringComparison.Ordinal);
        Assert.Empty(broken.MissingCertificates);

        Assert.True(report.Entries[0].IsRead);
        Assert.True(report.Entries[2].IsRead);
        Assert.Equal(PdfChainStatus.Trusted, report.Entries[0].ChainStatus);
        Assert.Equal(PdfChainStatus.Trusted, report.Entries[2].ChainStatus);

        // An unreadable document is not filed under a chain status: nothing was judged.
        Assert.Equal(2, report.CountsByStatus[PdfChainStatus.Trusted]);
        Assert.Equal(0, report.CountsByStatus[PdfChainStatus.NotEvaluated]);
        Assert.Contains(report.ToText().Split('\n'), line => line.StartsWith("error\t1\t", StringComparison.Ordinal));
    }

    [Fact]
    public void The_certificates_a_chain_could_not_accept_are_named()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.WithKey(TestCertificates.Issued(SignerSubject, root));
        using var strangerRoot = TestCertificates.Root(StrangerSubject);
        using var selfSigned = TestCertificates.Signer();

        var strangerStore = new PdfTrustStore { Anchors = [TestCertificates.Public(strangerRoot)] };

        // The chain stopped at the signer: the missing certificate is named by the issuer its child
        // states, and neither a subject nor a serial number is invented for it.
        var stopped = Assert.Single(PdfSignatureVerifier.VerifyDetailed(Sign(TestPdf.OnePage(), signer), strangerStore).Certificates);

        Assert.Null(stopped.Subject);
        Assert.Null(stopped.SerialNumber);
        Assert.Equal(root.Subject, stopped.Issuer);
        Assert.False(stopped.IsEmbedded);

        // The certificate is in hand but not anchored: its own subject, issuer and serial number say
        // which one to add to the store.
        var rejected = Assert.Single(PdfSignatureVerifier.VerifyDetailed(Sign(TestPdf.OnePage(), selfSigned), strangerStore).Certificates);

        Assert.True(rejected.IsEmbedded);
        Assert.Equal(selfSigned.Subject, rejected.Subject);
        Assert.Equal(selfSigned.Issuer, rejected.Issuer);
        Assert.Equal(selfSigned.SerialNumber, rejected.SerialNumber);
        Assert.False(string.IsNullOrWhiteSpace(rejected.Reason));

        // Two signatures over the same missing root name it once, not once per signature.
        var twice = PdfSignatureVerifier.VerifyDetailed(Sign(Sign(TestPdf.OnePage(), signer), signer), strangerStore);

        Assert.Equal(2, twice.Verification.Signatures.Count);
        Assert.Equal(root.Subject, Assert.Single(twice.Certificates).Issuer);

        var report = PdfBatchSignatureVerifier.Verify(
            [Sign(TestPdf.OnePage(), signer)],
            new PdfBatchSignatureOptions { TrustStore = strangerStore });

        Assert.Equal(root.Subject, Assert.Single(report.Entries[0].MissingCertificates).Issuer);
        Assert.Contains(
            report.ToText().Split('\n'),
            line => line.StartsWith("missing\t0\t\t", StringComparison.Ordinal)
                && line.Contains(root.Subject, StringComparison.Ordinal));
    }

    [Fact]
    public void A_chain_that_was_not_judged_names_no_missing_certificate()
    {
        using var signer = TestCertificates.WithKey(TestCertificates.Signer());

        var report = PdfBatchSignatureVerifier.Verify([Sign(TestPdf.OnePage(), signer)]);

        Assert.Equal(PdfChainStatus.NotEvaluated, report.Entries[0].ChainStatus);
        Assert.Empty(report.Entries[0].MissingCertificates);
    }

    [Fact]
    public void Two_runs_and_any_degree_of_parallelism_render_the_same_report()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.WithKey(TestCertificates.Issued(SignerSubject, root));
        using var strangerRoot = TestCertificates.Root(StrangerSubject);

        var trustStore = new PdfTrustStore { Anchors = [TestCertificates.Public(root)] };
        var strangerStore = new PdfTrustStore { Anchors = [TestCertificates.Public(strangerRoot)] };

        var documents = new List<byte[]>();

        for (var index = 0; index < 12; index++)
        {
            documents.Add(index % 3 == 0
                ? Encoding.Latin1.GetBytes("not a pdf " + index.ToString(CultureInfo.InvariantCulture))
                : Sign(WithText(TestPdf.OnePage(), index.ToString(CultureInfo.InvariantCulture)), signer, index));
        }

        var first = PdfBatchSignatureVerifier.Verify(documents, new PdfBatchSignatureOptions { TrustStore = trustStore });
        var second = PdfBatchSignatureVerifier.Verify(documents, new PdfBatchSignatureOptions { TrustStore = trustStore });
        var parallel = PdfBatchSignatureVerifier.Verify(
            documents,
            new PdfBatchSignatureOptions { TrustStore = trustStore, MaxDegreeOfParallelism = 4 });
        var other = PdfBatchSignatureVerifier.Verify(
            documents,
            new PdfBatchSignatureOptions { TrustStore = strangerStore, MaxDegreeOfParallelism = 8 });
        var otherSequential = PdfBatchSignatureVerifier.Verify(
            documents,
            new PdfBatchSignatureOptions { TrustStore = strangerStore });

        Assert.Equal(first.ToText(), second.ToText());
        Assert.Equal(first.ToText(), parallel.ToText());
        Assert.Equal(otherSequential.ToText(), other.ToText());
        Assert.NotEqual(first.ToText(), other.ToText());
        Assert.Equal(Enumerable.Range(0, documents.Count), first.Entries.Select(entry => entry.Index));
        Assert.Equal(4, first.Unreadable);
    }

    [Fact]
    public void An_empty_batch_is_a_report_that_still_lists_every_chain_status()
    {
        var report = PdfBatchSignatureVerifier.Verify([]);

        Assert.Equal(0, report.Documents);
        Assert.Empty(report.Entries);

        foreach (var status in Enum.GetValues<PdfChainStatus>())
        {
            Assert.Equal(0, report.CountsByStatus[status]);
            Assert.Empty(report.DocumentsByStatus[status]);
        }

        Assert.Equal(Enum.GetValues<PdfChainStatus>().Length, report.CountsByStatus.Count);
    }

    [Fact]
    public void The_report_does_not_keep_the_documents_bytes()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.WithKey(TestCertificates.Issued(SignerSubject, root));

        var documents = Enumerable.Range(0, 20)
            .Select(index => Sign(WithText(TestPdf.OnePage(), index.ToString(CultureInfo.InvariantCulture)), signer, index))
            .ToArray();

        var report = PdfBatchSignatureVerifier.Verify(
            documents,
            new PdfBatchSignatureOptions { TrustStore = new PdfTrustStore { Anchors = [TestCertificates.Public(root)] } });

        // The report carries verdicts, not files: no page dictionary of any document reaches the text,
        // and twenty signed documents render as fewer bytes than one of them.
        Assert.DoesNotContain("/MediaBox", report.ToText(), StringComparison.Ordinal);
        Assert.True(report.ToText().Length < documents[0].Length);
    }

    [Fact]
    public void A_degree_of_parallelism_below_one_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PdfBatchSignatureVerifier.Verify([], new PdfBatchSignatureOptions { MaxDegreeOfParallelism = 0 }));

        Assert.Throws<ArgumentNullException>(() => PdfBatchSignatureVerifier.Verify(null!));
    }

    [Fact]
    public void A_position_that_carries_no_document_is_an_unreadable_entry()
    {
        byte[][] documents = [TestPdf.OnePage(), null!];

        var report = PdfBatchSignatureVerifier.Verify(documents);

        Assert.Equal(1, report.Read);
        Assert.Equal(1, report.Unreadable);
        Assert.Equal("the batch carries no document at this position", report.Entries[1].Error);
    }

    [Fact]
    public void A_thousand_documents_cover_every_chain_status()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.WithKey(TestCertificates.Issued(SignerSubject, root));
        using var strangerRoot = TestCertificates.Root(StrangerSubject);
        using var strangerSigner = TestCertificates.WithKey(TestCertificates.Issued("CN=Other Signer, C=FR", strangerRoot));

        var trustStore = new PdfTrustStore { Anchors = [TestCertificates.Public(root)] };
        var strangerStore = new PdfTrustStore { Anchors = [TestCertificates.Public(strangerRoot)] };

        var documents = new List<byte[]>(1000);

        for (var index = 0; index < 400; index++)
        {
            documents.Add(Sign(WithText(TestPdf.OnePage(), "trusted " + index.ToString(CultureInfo.InvariantCulture)), signer, index));
        }

        for (var index = 400; index < 700; index++)
        {
            documents.Add(Sign(WithText(TestPdf.OnePage(), "untrusted " + index.ToString(CultureInfo.InvariantCulture)), strangerSigner, index));
        }

        for (var index = 0; index < 299; index++)
        {
            documents.Add(TestPdf.WithPages(1 + (index % 3)));
        }

        documents.Add(Encoding.Latin1.GetBytes("%PDF-1.7 truncated"));

        var known = PdfBatchSignatureVerifier.Verify(
            documents,
            new PdfBatchSignatureOptions { TrustStore = trustStore, MaxDegreeOfParallelism = 4 });

        var unknown = PdfBatchSignatureVerifier.Verify(
            documents,
            new PdfBatchSignatureOptions { TrustStore = strangerStore, MaxDegreeOfParallelism = 4 });

        Assert.Equal(1000, known.Documents);
        Assert.Equal(999, known.Read);
        Assert.Equal(1, known.Unreadable);
        Assert.Equal(700, known.SignedDocuments);

        Assert.Equal(400, known.CountsByStatus[PdfChainStatus.Trusted]);
        Assert.Equal(300, known.CountsByStatus[PdfChainStatus.Untrusted]);
        Assert.Equal(299, known.CountsByStatus[PdfChainStatus.NotEvaluated]);
        Assert.Equal(400, known.DocumentsByStatus[PdfChainStatus.Trusted].Count);
        Assert.Equal(299, known.DocumentsByStatus[PdfChainStatus.NotEvaluated].Count);

        Assert.Equal(300, unknown.CountsByStatus[PdfChainStatus.Trusted]);
        Assert.Equal(400, unknown.CountsByStatus[PdfChainStatus.Untrusted]);
        Assert.Equal(299, unknown.CountsByStatus[PdfChainStatus.NotEvaluated]);
        Assert.Equal(300, known.Entries.Count(entry => entry.MissingCertificates.Count > 0));
        Assert.Equal(400, unknown.Entries.Count(entry => entry.MissingCertificates.Count > 0));

        // Every defined status is present in the report, whether or not the batch carried one.
        Assert.Equal(Enum.GetValues<PdfChainStatus>().Length, known.CountsByStatus.Count);
    }

    private static byte[] Sign(byte[] document, X509Certificate2 signer, int offset = 0)
        => PdfSigner.Sign(document, signer, new PdfSignatureOptions { SignedAt = SignedAt.AddMinutes(offset) });

    /// <summary>
    /// A copy of <paramref name="document"/> carrying <paramref name="text"/> as a comment before its
    /// last end-of-file marker, so two documents of a batch are different files rather than one twice.
    /// </summary>
    private static byte[] WithText(byte[] document, string text)
    {
        var body = Encoding.Latin1.GetString(document);
        var marker = body.IndexOf("%%EOF", StringComparison.Ordinal);

        return Encoding.Latin1.GetBytes(body.Insert(marker, "% " + text + "\n"));
    }
}
