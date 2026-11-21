using System;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Xunit;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// PAdES-B-LTA: a document timestamp over the whole file, the store included, and a chain the
/// verifier builds entry by entry instead of assuming.
/// </summary>
/// <remarks>
/// Every level assertion here is read through <see cref="PdfSignatureVerifier"/>, which is the public
/// answer; <see cref="PdfArchiveTimestamp.Inspect"/> is used only for the facts the verifier does not
/// expose, such as what a given timestamp covers.
/// </remarks>
public sealed class PdfArchiveLtaTests
{
    private static readonly Uri Authority = new("https://tsa.example.test/rfc3161");

    private static readonly DateTimeOffset SignedAt = new(2026, 2, 1, 10, 30, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset ArchivedAt = new(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A PAdES-B-LT document: the signer's chain and a CRL in the store, and a signature timestamp.
    /// The authority's certificate goes into the store only when the caller says so, which is what
    /// separates "the store carries the signature's evidence" from "the store carries the timestamps
    /// evidence too".
    /// </summary>
    private static byte[] SignLongTerm(
        X509Certificate2 root,
        X509Certificate2 signer,
        FakeTsa tsa,
        HttpClient client,
        bool storeTheAuthority = true)
    {
        var certificates = new System.Collections.Generic.List<X509Certificate2>
        {
            TestCertificates.Public(signer),
            TestCertificates.Public(root),
        };

        if (storeTheAuthority)
        {
            certificates.Add(TestCertificates.Public(tsa.Certificate));
        }

        return PdfSigner.Sign(
            TestPdf.OnePage(),
            signer,
            new PdfSignatureOptions
            {
                SignedAt = SignedAt,
                TimestampAuthority = Authority,
                TimestampClient = client,
                Dss = new PdfDssOptions
                {
                    Certificates = certificates,
                    Crls = [TestRevocation.Crl(root)],
                },
            });
    }

    private static byte[] Archive(byte[] pdf, HttpClient client)
        => PdfArchiveTimestamp.Refresh(
            pdf,
            new PdfArchiveTimestampOptions
            {
                TimestampAuthority = Authority,
                TimestampClient = client,
            });

    [Fact]
    public void A_long_term_document_archived_once_is_proven_LTA()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);
        using var tsa = new FakeTsa { Time = ArchivedAt };
        using var client = tsa.Client();

        var signed = SignLongTerm(root, signer, tsa, client);

        // Before the timestamp the document proves LT, and only LT.
        Assert.Equal(PdfSignatureLevel.BaselineLT, Assert.Single(PdfSignatureVerifier.Verify(signed).Signatures).Level);

        var archived = Archive(signed, client);

        Assert.True(PdfArchiveTimestamp.IsArchived(archived));

        var verification = PdfSignatureVerifier.Verify(archived);
        var signature = Assert.Single(verification.Signatures);

        Assert.True(signature.IsIntact, signature.Error);
        Assert.Equal(PdfSignatureLevel.BaselineLTA, signature.Level);
        Assert.Equal(PdfSignatureLevel.BaselineLTA, verification.ProvenLevel);

        var chain = PdfArchiveTimestamp.Inspect(archived);
        Assert.Empty(chain.Breaks);
        Assert.Equal(2, chain.Entries.Count);

        var signedEntry = chain.Entries[0];
        Assert.Equal(PdfArchiveEntryKind.Signature, signedEntry.Kind);
        Assert.True(signedEntry.IsIntact, signedEntry.Error);
        Assert.True(signedEntry.CoversItsRevision);
        Assert.False(signedEntry.CoversWholeFile);

        var stamp = chain.Entries[1];
        Assert.Equal(PdfArchiveEntryKind.DocumentTimestamp, stamp.Kind);
        Assert.True(stamp.IsIntact, stamp.Error);
        Assert.True(stamp.CoversItsRevision);
        Assert.True(stamp.CoversWholeFile);
        Assert.True(stamp.CoversDocumentSecurityStore);
        Assert.Equal(archived.Length, (int)stamp.CoveredEnd);
        Assert.Equal(ArchivedAt, stamp.AttestedTime);
        Assert.Equal(tsa.Certificate.Subject, stamp.SignerSubject);

        // An incremental update: the signed revision is still there, byte for byte.
        Assert.Equal(signed, archived.AsSpan(0, signed.Length).ToArray());
    }

    [Fact]
    public void A_document_archived_twice_carries_two_intact_timestamps_and_the_second_covers_the_first()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);
        using var tsa = new FakeTsa { Time = ArchivedAt };
        using var client = tsa.Client();

        var once = Archive(SignLongTerm(root, signer, tsa, client), client);

        tsa.Time = ArchivedAt.AddHours(1);
        var twice = Archive(once, client);

        var verification = PdfSignatureVerifier.Verify(twice);
        Assert.Equal(2, verification.DocumentTimestamps.Count);
        Assert.True(verification.AllDocumentTimestampsValid);
        Assert.All(verification.DocumentTimestamps, entry => Assert.True(entry.IsIntact, entry.Error));
        Assert.True(PdfArchiveTimestamp.IsArchived(twice));

        var chain = PdfArchiveTimestamp.Inspect(twice);
        Assert.Empty(chain.Breaks);
        Assert.Equal(3, chain.Entries.Count);
        Assert.All(chain.Entries, entry => Assert.True(entry.IsIntact, entry.Error));
        Assert.All(chain.Entries, entry => Assert.True(entry.CoversItsRevision, entry.Error));

        var firstStamp = chain.Entries[1];
        var secondStamp = chain.Entries[2];

        Assert.Equal(PdfArchiveEntryKind.DocumentTimestamp, firstStamp.Kind);
        Assert.Equal(PdfArchiveEntryKind.DocumentTimestamp, secondStamp.Kind);

        // The second covers the first: a prefix from byte zero, past the first timestamp's dictionary.
        Assert.Equal(0L, secondStamp.CoveredStart);
        Assert.True(secondStamp.CoveredEnd > firstStamp.CoveredEnd);
        Assert.True(secondStamp.CoveredEnd > firstStamp.Offset);
        Assert.Equal(twice.Length, (int)secondStamp.CoveredEnd);

        // The first still protects its own revision, and no longer reaches the end of the file.
        Assert.True(firstStamp.CoversItsRevision);
        Assert.False(firstStamp.CoversWholeFile);

        Assert.Equal(PdfSignatureLevel.BaselineLTA, Assert.Single(verification.Signatures).Level);
        Assert.Equal(PdfSignatureLevel.BaselineLTA, verification.ProvenLevel);
    }

    [Fact]
    public void A_byte_changed_after_archiving_is_detected()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);
        using var tsa = new FakeTsa { Time = ArchivedAt };
        using var client = tsa.Client();

        var archived = Archive(SignLongTerm(root, signer, tsa, client), client);
        Assert.True(PdfArchiveTimestamp.IsArchived(archived));

        // One byte of the page content, inside every range that covers it.
        var tampered = (byte[])archived.Clone();
        var at = Encoding.Latin1.GetString(tampered).IndexOf("/MediaBox", StringComparison.Ordinal);
        tampered[at] = (byte)'X';

        Assert.False(PdfArchiveTimestamp.IsArchived(tampered));

        var chain = PdfArchiveTimestamp.Inspect(tampered);
        Assert.NotEmpty(chain.Breaks);
        Assert.All(chain.Entries, entry => Assert.False(entry.IsIntact));

        var verification = PdfSignatureVerifier.Verify(tampered);
        var signature = Assert.Single(verification.Signatures);

        Assert.False(signature.IsIntact);
        Assert.Equal(PdfSignatureLevel.None, signature.Level);
        Assert.NotEqual(PdfSignatureLevel.BaselineLTA, verification.ProvenLevel);
        Assert.False(verification.AllDocumentTimestampsValid);
    }

    [Fact]
    public void A_timestamp_whose_authority_is_missing_from_the_store_is_not_archived()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);
        using var tsa = new FakeTsa { Time = ArchivedAt };
        using var client = tsa.Client();

        var archived = Archive(SignLongTerm(root, signer, tsa, client, storeTheAuthority: false), client);

        Assert.False(PdfArchiveTimestamp.IsArchived(archived));

        var chain = PdfArchiveTimestamp.Inspect(archived);

        // The timestamp itself is fine; what is missing is the evidence that it was issued by someone
        // the store vouches for.
        Assert.All(chain.Entries, entry => Assert.True(entry.IsIntact, entry.Error));
        Assert.True(chain.Entries[1].CoversWholeFile);
        Assert.True(chain.Entries[1].CoversDocumentSecurityStore);
        Assert.Contains(chain.Breaks, reason => reason.Contains("does not carry the certificate of", StringComparison.Ordinal));

        var verification = PdfSignatureVerifier.Verify(archived);
        var signature = Assert.Single(verification.Signatures);

        // LT is still proven — the store carries the signer's chain and its revocation data.
        Assert.Equal(PdfSignatureLevel.BaselineLT, signature.Level);
        Assert.Equal(PdfSignatureLevel.BaselineLT, verification.ProvenLevel);
        Assert.True(verification.AllDocumentTimestampsValid);
    }

    [Fact]
    public void A_timestamped_document_without_a_store_proves_no_more_than_T()
    {
        using var tsa = new FakeTsa { Time = ArchivedAt };
        using var client = tsa.Client();

        var signed = PdfSigner.Sign(
            TestPdf.OnePage(),
            TestCertificates.Signer(),
            new PdfSignatureOptions
            {
                SignedAt = SignedAt,
                TimestampAuthority = Authority,
                TimestampClient = client,
            });

        var archived = Archive(signed, client);

        Assert.False(PdfArchiveTimestamp.IsArchived(archived));

        var verification = PdfSignatureVerifier.Verify(archived);
        var signature = Assert.Single(verification.Signatures);

        Assert.Equal(PdfSignatureLevel.BaselineT, signature.Level);
        Assert.Equal(PdfSignatureLevel.BaselineT, verification.ProvenLevel);

        var chain = PdfArchiveTimestamp.Inspect(archived);
        Assert.Contains(chain.Breaks, reason => reason.Contains("no document security store", StringComparison.Ordinal));
    }

    [Fact]
    public void A_signature_added_after_the_timestamp_leaves_the_document_unarchived()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);
        using var tsa = new FakeTsa { Time = ArchivedAt };
        using var client = tsa.Client();

        var archived = Archive(SignLongTerm(root, signer, tsa, client), client);

        // A second signature, after the timestamp: the newest revision is now signed but not timestamped.
        var extended = PdfSigner.Sign(archived, TestCertificates.Signer(), new PdfSignatureOptions { SignedAt = SignedAt });
        Assert.True(PdfArchiveTimestamp.Inspect(archived).IsArchived);

        Assert.False(PdfArchiveTimestamp.IsArchived(extended));

        var chain = PdfArchiveTimestamp.Inspect(extended);
        Assert.Equal(PdfArchiveEntryKind.Signature, chain.Entries[^1].Kind);
        Assert.Contains(chain.Breaks, reason => reason.Contains("not a document timestamp", StringComparison.Ordinal));

        // The timestamp still protects the revision it was written into: that is not the break.
        Assert.True(chain.Entries[1].CoversItsRevision);
        Assert.True(chain.Entries[1].IsIntact, chain.Entries[1].Error);

        var verification = PdfSignatureVerifier.Verify(extended);

        // The archived signature still proves LT; the new one, made with a bare self-signed
        // certificate and no timestamp, proves B — and the document is only as strong as that.
        Assert.Equal(PdfSignatureLevel.BaselineLT, verification.Signatures[0].Level);
        Assert.Equal(PdfSignatureLevel.BaselineB, verification.Signatures[1].Level);
        Assert.Equal(PdfSignatureLevel.BaselineB, verification.ProvenLevel);
    }

    [Fact]
    public void A_timestamp_that_stops_before_the_end_of_its_revision_is_not_archived()
    {
        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);
        using var tsa = new FakeTsa { Time = ArchivedAt };
        using var client = tsa.Client();

        var archived = Archive(SignLongTerm(root, signer, tsa, client), client);
        Assert.True(PdfArchiveTimestamp.IsArchived(archived));

        // Rewrite the timestamp so it covers a shorter range — and re-issue its token over exactly
        // those bytes, so it is intact but no longer reaches the end of the revision it sits in.
        // This is the shape a refresh must refuse to call archived.
        var dictionary = PdfLtaSignatureSlice.Dictionaries(archived)[^1];
        var range = dictionary.ByteRange;

        var text = Encoding.Latin1.GetString(archived);
        var start = text.IndexOf("/ByteRange[", dictionary.Offset, StringComparison.Ordinal);
        var end = text.IndexOf(']', start) + 1;

        var shortened = range[3] - 64;
        var replacement = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"/ByteRange[0 {range[1]:D10} {range[2]:D10} {shortened:D10}]");

        Assert.Equal(end - start, replacement.Length);

        var edited = Encoding.Latin1.GetBytes(string.Concat(text.AsSpan(0, start), replacement, text.AsSpan(end)));

        var covered = new byte[range[1] + shortened];
        Buffer.BlockCopy(edited, 0, covered, 0, (int)range[1]);
        Buffer.BlockCopy(edited, (int)range[2], covered, (int)range[1], (int)shortened);

        var token = tsa.Token(System.Security.Cryptography.SHA256.HashData(covered));
        var hex = Convert.ToHexString(token).PadRight(dictionary.ContentsLength, '0');
        Assert.Equal(dictionary.ContentsLength, hex.Length);

        var rebuilt = (byte[])edited.Clone();
        Encoding.ASCII.GetBytes(hex).CopyTo(rebuilt, dictionary.ContentsStart);

        var chain = PdfArchiveTimestamp.Inspect(rebuilt);
        var stamp = chain.Entries[^1];

        Assert.True(stamp.IsIntact, stamp.Error);
        Assert.True(stamp.CoversDocumentSecurityStore);
        Assert.False(stamp.CoversWholeFile);
        Assert.False(stamp.CoversItsRevision);
        Assert.True(stamp.CoveredEnd < stamp.RevisionEnd);
        Assert.False(chain.IsArchived);
        Assert.Contains(chain.Breaks, reason => reason.Contains("no longer covers the revision", StringComparison.Ordinal));

        // The signature itself is untouched, so the document still proves LT — and no more.
        var signature = Assert.Single(PdfSignatureVerifier.Verify(rebuilt).Signatures);
        Assert.True(signature.IsIntact, signature.Error);
        Assert.Equal(PdfSignatureLevel.BaselineLT, signature.Level);
    }

    [Fact]
    public void An_unsigned_document_is_not_archived()
    {
        var chain = PdfArchiveTimestamp.Inspect(TestPdf.OnePage());

        Assert.False(chain.IsArchived);
        Assert.Empty(chain.Entries);
        Assert.Contains(chain.Breaks, reason => reason.Contains("neither a signature", StringComparison.Ordinal));
    }

    [Fact]
    public void A_document_without_a_signature_cannot_be_archived()
    {
        using var tsa = new FakeTsa();
        using var client = tsa.Client();

        var exception = Assert.Throws<PdfSignatureException>(() =>
            Archive(TestPdf.OnePage(), client));

        Assert.Contains("carries no signature", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_authority_and_the_client_are_required()
    {
        using var tsa = new FakeTsa();
        using var client = tsa.Client();

        var signed = PdfSigner.Sign(TestPdf.OnePage(), TestCertificates.Signer());

        Assert.Throws<ArgumentNullException>(() => PdfArchiveTimestamp.Refresh(signed, null!));
        Assert.Throws<ArgumentNullException>(() => PdfArchiveTimestamp.Refresh(null!, new PdfArchiveTimestampOptions()));

        Assert.Contains(
            "no timestamp authority",
            Assert.Throws<PdfSignatureException>(() =>
                PdfArchiveTimestamp.Refresh(signed, new PdfArchiveTimestampOptions { TimestampClient = client })).Message,
            StringComparison.Ordinal);

        Assert.Contains(
            "without an HttpClient",
            Assert.Throws<PdfSignatureException>(() =>
                PdfArchiveTimestamp.Refresh(signed, new PdfArchiveTimestampOptions { TimestampAuthority = Authority })).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_document_is_refused()
    {
        using var tsa = new FakeTsa();
        using var client = tsa.Client();

        Assert.Throws<PdfSignatureException>(() =>
            PdfArchiveTimestamp.Refresh([], new PdfArchiveTimestampOptions
            {
                TimestampAuthority = Authority,
                TimestampClient = client,
            }));

        Assert.Throws<PdfSignatureException>(() => PdfArchiveTimestamp.IsArchived([]));
        Assert.Throws<PdfSignatureException>(() => PdfArchiveTimestamp.Inspect([]));
    }

    [Fact]
    public void An_authority_that_fails_archives_nothing()
    {
        using var handler = new StubHandler(_ => StubHandler.Body([], System.Net.HttpStatusCode.ServiceUnavailable));
        using var client = handler.Client();

        using var root = TestCertificates.Root();
        using var signer = TestCertificates.Issued("CN=Facture SARL, O=Facture, C=FR", root);

        var signed = PdfSigner.Sign(TestPdf.OnePage(), signer, new PdfSignatureOptions { SignedAt = SignedAt });

        var exception = Assert.Throws<PdfSignatureException>(() => Archive(signed, client));

        Assert.Contains("503", exception.Message, StringComparison.Ordinal);
    }
}
