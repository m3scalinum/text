using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// The document timestamp: an RFC 3161 token over the whole file, added after the signature. It is
/// what makes a signature readable years later, and it must never disturb the signature it follows.
/// </summary>
public sealed class PdfDocumentTimestampTests
{
    private static readonly Uri Authority = new("https://tsa.example.test/rfc3161");

    private static byte[] Sign()
        => PdfSigner.Sign(TestPdf.OnePage(), TestCertificates.Signer(), new PdfSignatureOptions
        {
            SignedAt = new DateTimeOffset(2026, 2, 1, 10, 30, 0, TimeSpan.Zero),
        });

    [Fact]
    public void A_document_timestamp_is_read_back_with_the_time_the_authority_attests()
    {
        using var tsa = new FakeTsa { Time = new DateTimeOffset(2026, 2, 2, 9, 0, 0, TimeSpan.Zero) };
        using var client = tsa.Client();

        var stamped = PdfDocumentTimestamp.Add(Sign(), Authority, client);
        var result = PdfSignatureVerifier.Verify(stamped);

        Assert.True(result.HasDocumentTimestamp);
        var timestamp = Assert.Single(result.DocumentTimestamps);

        Assert.True(timestamp.IsIntact, timestamp.Error);
        Assert.Null(timestamp.Error);
        Assert.Equal(tsa.Time, timestamp.TimestampedTime);
        Assert.True(timestamp.CoversWholeFile);
        Assert.Equal(tsa.Certificate.Subject, timestamp.SignerSubject);
        Assert.Equal(tsa.Certificate.Issuer, timestamp.SignerIssuer);
        Assert.True(result.AllDocumentTimestampsValid);

        // The signature it follows is untouched, and knows a timestamp now covers it.
        var signature = Assert.Single(result.Signatures);
        Assert.True(signature.IsIntact, signature.Error);
        Assert.True(signature.HasDocumentTimestamp);
        Assert.True(signature.IsDocumentTimestampValid);
    }

    [Fact]
    public void The_signature_no_longer_covers_the_whole_file_once_a_timestamp_follows()
    {
        using var tsa = new FakeTsa();
        using var client = tsa.Client();

        var signed = Sign();
        var stamped = PdfDocumentTimestamp.Add(signed, Authority, client);

        var result = PdfSignatureVerifier.Verify(stamped);
        var signature = Assert.Single(result.Signatures);

        Assert.True(signature.IsIntact, signature.Error);
        Assert.False(signature.CoversWholeFile);
        Assert.False(result.AllCoverWholeFile);
        Assert.Equal(PdfSignatureLevel.BaselineB, signature.Level);
    }

    [Fact]
    public void A_tampered_signature_is_reported_by_the_timestamp_that_covers_it()
    {
        using var tsa = new FakeTsa();
        using var client = tsa.Client();

        var stamped = PdfDocumentTimestamp.Add(Sign(), Authority, client);
        var text = SignedPdf.Text(stamped);
        var tampered = (byte[])stamped.Clone();
        var contents = Regex.Match(text, @"/Contents<([0-9A-F])").Groups[1].Index;
        tampered[contents] = tampered[contents] == (byte)'0' ? (byte)'1' : (byte)'0';

        var result = PdfSignatureVerifier.Verify(tampered);

        var timestamp = Assert.Single(result.DocumentTimestamps);
        Assert.False(timestamp.IsIntact);
        Assert.Contains("imprint", timestamp.Error, StringComparison.Ordinal);
        Assert.NotNull(timestamp.TimestampedTime);
    }

    [Fact]
    public void A_document_timestamp_whose_token_is_corrupt_is_reported()
    {
        using var tsa = new FakeTsa();
        using var client = tsa.Client();

        var stamped = PdfDocumentTimestamp.Add(Sign(), Authority, client);
        var text = SignedPdf.Text(stamped);

        // The second /Contents belongs to the timestamp.
        var matches = Regex.Matches(text, @"/Contents<([0-9A-F])");
        var tampered = (byte[])stamped.Clone();
        var at = matches[1].Groups[1].Index;
        tampered[at] = (byte)'0';

        var timestamp = Assert.Single(PdfSignatureVerifier.Verify(tampered).DocumentTimestamps);

        Assert.False(timestamp.IsIntact);
        Assert.NotNull(timestamp.Error);
        Assert.False(PdfSignatureVerifier.Verify(tampered).AllDocumentTimestampsValid);
    }

    [Fact]
    public void A_timestamp_that_does_not_verify_is_present_but_not_valid()
    {
        using var tsa = new FakeTsa();
        using var client = tsa.Client();

        var stamped = PdfDocumentTimestamp.Add(Sign(), Authority, client);
        var matches = Regex.Matches(SignedPdf.Text(stamped), @"/Contents<([0-9A-F])");
        var tampered = (byte[])stamped.Clone();
        tampered[matches[1].Groups[1].Index] = (byte)'0';

        var result = PdfSignatureVerifier.Verify(tampered);
        var signature = Assert.Single(result.Signatures);

        // The file carries a timestamp — that is a fact — but it is not one this signature can lean on.
        Assert.True(signature.HasDocumentTimestamp);
        Assert.False(signature.IsDocumentTimestampValid);
    }

    [Fact]
    public void Two_document_timestamps_are_both_reported()
    {
        using var tsa = new FakeTsa();
        using var client = tsa.Client();

        var first = PdfDocumentTimestamp.Add(Sign(), Authority, client);
        tsa.Time = tsa.Time.AddHours(1);
        var second = PdfDocumentTimestamp.Add(first, Authority, client);

        var result = PdfSignatureVerifier.Verify(second);

        Assert.Equal(2, result.DocumentTimestamps.Count);
        Assert.All(result.DocumentTimestamps, entry => Assert.True(entry.IsIntact, entry.Error));
        Assert.True(result.DocumentTimestamps[0].TimestampedTime < result.DocumentTimestamps[1].TimestampedTime);

        // The last stamp covers the file; the first one only its own revision.
        Assert.False(result.DocumentTimestamps[0].CoversWholeFile);
        Assert.True(result.DocumentTimestamps[1].CoversWholeFile);
    }

    [Fact]
    public void The_stamp_declares_a_document_timestamp_over_the_escaped_token()
    {
        using var tsa = new FakeTsa();
        using var client = tsa.Client();

        var signed = Sign();
        var stamped = PdfDocumentTimestamp.Add(signed, Authority, client);
        var text = SignedPdf.Text(stamped);

        Assert.Contains("/Type/DocTimeStamp", text, StringComparison.Ordinal);
        Assert.Contains("/SubFilter/ETSI.RFC3161", text, StringComparison.Ordinal);
        Assert.Contains("/Filter/Adobe.PPKLite", text, StringComparison.Ordinal);

        // An incremental update: the signed revision is still there, byte for byte.
        Assert.Equal(signed, stamped.AsSpan(0, signed.Length).ToArray());

        // The token is stored as a CMS structure, not as a bare signature.
        var token = PdfTimestamp.Parse(SignedPdf.Contents(stamped, 1));
        Assert.Equal(tsa.Time, token.GeneratedAt);
    }

    [Fact]
    public void An_empty_document_is_refused()
    {
        using var tsa = new FakeTsa();
        using var client = tsa.Client();

        Assert.Throws<PdfSignatureException>(() =>
            PdfDocumentTimestamp.Add(ReadOnlySpan<byte>.Empty, Authority, client));
    }

    [Fact]
    public void The_authority_and_the_client_are_required()
    {
        using var tsa = new FakeTsa();
        using var client = tsa.Client();

        Assert.Throws<ArgumentNullException>(() => PdfDocumentTimestamp.Add(Sign(), null!, client));
        Assert.Throws<ArgumentNullException>(() => PdfDocumentTimestamp.Add(Sign(), Authority, null!));
    }

    [Fact]
    public void An_authority_that_refuses_is_reported()
    {
        using var handler = new StubHandler(_ => StubHandler.Body([], System.Net.HttpStatusCode.BadGateway));
        using var client = handler.Client();

        var exception = Assert.Throws<PdfSignatureException>(() => PdfDocumentTimestamp.Add(Sign(), Authority, client));

        Assert.Contains("502", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_token_over_another_imprint_is_refused()
    {
        using var tsa = new FakeTsa();
        var other = new FakeTsa().Token(SHA256.HashData(Encoding.ASCII.GetBytes("something else")));
        using var handler = new StubHandler(_ => StubHandler.Body(FakeTsa.Response(other)));
        using var client = handler.Client();

        tsa.Time = tsa.Time.AddMinutes(1);

        var exception = Assert.Throws<PdfSignatureException>(() =>
            PdfDocumentTimestamp.Add(Sign(), Authority, client));

        Assert.Contains("no timestamp was written", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stamp_on_a_document_that_is_not_a_pdf_is_refused()
    {
        using var tsa = new FakeTsa();
        using var client = tsa.Client();

        var exception = Assert.Throws<PdfSignatureException>(() =>
            PdfDocumentTimestamp.Add(Encoding.ASCII.GetBytes("not a pdf"), Authority, client));

        Assert.Contains("startxref", exception.Message, StringComparison.Ordinal);
    }
}
