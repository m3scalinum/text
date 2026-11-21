using System;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Xunit;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// PAdES-B-T: an RFC 3161 token over the signature value. The tests use a stub transport that
/// produces real tokens, so what is verified is the mechanism, not a mock of it.
/// </summary>
public sealed class PdfTimestampSigningTests
{
    private static readonly Uri Authority = new("https://tsa.example.test/rfc3161");

    private static readonly DateTimeOffset SignedAt = new(2026, 2, 1, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_timestamp_turns_baseline_B_into_baseline_T()
    {
        using var tsa = new FakeTsa { Time = new DateTimeOffset(2026, 2, 1, 10, 31, 0, TimeSpan.Zero) };
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

        var signature = Assert.Single(PdfSignatureVerifier.Verify(signed).Signatures);

        Assert.True(signature.IsIntact, signature.Error);
        Assert.Equal(PdfSignatureLevel.BaselineT, signature.Level);
        Assert.Equal(SignedAt, signature.ClaimedSigningTime);
        Assert.Equal(tsa.Time, signature.TimestampedSigningTime);
        Assert.Equal(tsa.Time, signature.TimestampedSigningTime!.Value.ToUniversalTime());
        Assert.True(signature.CoversWholeFile);
    }

    [Fact]
    public void The_authority_receives_a_time_stamp_req_over_the_supplied_client()
    {
        using var tsa = new FakeTsa();
        using var client = tsa.Client();

        PdfSigner.Sign(
            TestPdf.OnePage(),
            TestCertificates.Signer(),
            new PdfSignatureOptions { TimestampAuthority = Authority, TimestampClient = client });

        Assert.Equal(1, tsa.Calls);
        Assert.Equal("application/timestamp-query", tsa.LastContentType);
        Assert.Equal(PdfTimestamp.Sha256Oid, tsa.LastAlgorithm);
        Assert.Equal(32, tsa.LastImprint!.Length);
    }

    [Fact]
    public void The_token_is_attached_as_the_signature_timestamp_unsigned_attribute()
    {
        using var tsa = new FakeTsa();
        using var client = tsa.Client();

        var signed = PdfSigner.Sign(
            TestPdf.OnePage(),
            TestCertificates.Signer(),
            new PdfSignatureOptions { TimestampAuthority = Authority, TimestampClient = client });

        var cms = SignedPdf.Cms(signed);
        var attributes = cms.SignerInfos[0].UnsignedAttributes;

        Assert.Single(attributes);
        Assert.Equal(PdfTimestamp.SignatureTimestampOid, attributes[0].Oid?.Value);

        var token = PdfTimestamp.Parse(attributes[0].Values[0].RawData);
        Assert.True(token.Matches(cms.SignerInfos[0].GetSignature()));
        Assert.Equal(tsa.Certificate.Subject, token.Signer?.Subject);
    }

    [Fact]
    public void The_imprint_the_authority_sees_is_the_signature_value()
    {
        using var tsa = new FakeTsa();
        using var client = tsa.Client();

        var signed = PdfSigner.Sign(
            TestPdf.OnePage(),
            TestCertificates.Signer(),
            new PdfSignatureOptions { TimestampAuthority = Authority, TimestampClient = client });

        // Removing the token leaves the signature value untouched, so the imprint still matches.
        var cms = SignedPdf.Cms(signed);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(cms.SignerInfos[0].GetSignature())),
            Convert.ToHexString(tsa.LastImprint!));
    }

    [Fact]
    public void An_authority_that_answers_an_error_status_is_reported()
    {
        using var handler = new StubHandler(_ => StubHandler.Body([], HttpStatusCode.ServiceUnavailable));
        using var client = handler.Client();

        var exception = Assert.Throws<PdfSignatureException>(() => PdfSigner.Sign(
            TestPdf.OnePage(),
            TestCertificates.Signer(),
            new PdfSignatureOptions { TimestampAuthority = Authority, TimestampClient = client }));

        Assert.Contains("503", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Service Unavailable", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_authority_that_rejects_the_request_is_reported()
    {
        using var handler = new StubHandler(_ => StubHandler.Body(FakeTsa.Response(null, status: 2)));
        using var client = handler.Client();

        var exception = Assert.Throws<PdfSignatureException>(() => PdfSigner.Sign(
            TestPdf.OnePage(),
            TestCertificates.Signer(),
            new PdfSignatureOptions { TimestampAuthority = Authority, TimestampClient = client }));

        Assert.Contains("status 2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_answer_without_a_token_is_reported()
    {
        using var handler = new StubHandler(_ => StubHandler.Body(FakeTsa.Response(null)));
        using var client = handler.Client();

        var exception = Assert.Throws<PdfSignatureException>(() => PdfSigner.Sign(
            TestPdf.OnePage(),
            TestCertificates.Signer(),
            new PdfSignatureOptions { TimestampAuthority = Authority, TimestampClient = client }));

        Assert.Contains("without a token", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_token_over_another_imprint_is_refused()
    {
        using var tsa = new FakeTsa
        {
            TokenFactory = _ => new FakeTsa().Token(RandomNumberGenerator.GetBytes(32)),
        };

        using var client = tsa.Client();

        var exception = Assert.Throws<PdfSignatureException>(() => PdfSigner.Sign(
            TestPdf.OnePage(),
            TestCertificates.Signer(),
            new PdfSignatureOptions { TimestampAuthority = Authority, TimestampClient = client }));

        Assert.Contains("imprint is not this signature", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_token_that_is_not_a_timestamp_is_refused()
    {
        using var handler = new StubHandler(_ => StubHandler.Body(FakeTsa.Response([0x30, 0x00])));
        using var client = handler.Client();

        var exception = Assert.Throws<PdfSignatureException>(() => PdfSigner.Sign(
            TestPdf.OnePage(),
            TestCertificates.Signer(),
            new PdfSignatureOptions { TimestampAuthority = Authority, TimestampClient = client }));

        Assert.Contains("timestamp", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_unreachable_authority_is_reported()
    {
        using var handler = new StubHandler(new HttpRequestException("name resolution failed"));
        using var client = handler.Client();

        var exception = Assert.Throws<PdfSignatureException>(() => PdfSigner.Sign(
            TestPdf.OnePage(),
            TestCertificates.Signer(),
            new PdfSignatureOptions { TimestampAuthority = Authority, TimestampClient = client }));

        Assert.Contains("could not be reached", exception.Message, StringComparison.Ordinal);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public void A_timestamp_authority_without_a_client_is_refused()
    {
        var exception = Assert.Throws<PdfSignatureException>(() => PdfSigner.Sign(
            TestPdf.OnePage(),
            TestCertificates.Signer(),
            new PdfSignatureOptions { TimestampAuthority = Authority }));

        Assert.Contains("HttpClient", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_client_without_an_authority_is_refused()
    {
        using var handler = new StubHandler(_ => StubHandler.Body([]));
        using var client = handler.Client();

        var exception = Assert.Throws<PdfSignatureException>(() => PdfSigner.Sign(
            TestPdf.OnePage(),
            TestCertificates.Signer(),
            new PdfSignatureOptions { TimestampClient = client }));

        Assert.Contains("no authority", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_signature_without_a_timestamp_stops_at_baseline_B()
    {
        var signed = PdfSigner.Sign(TestPdf.OnePage(), TestCertificates.Signer(), new PdfSignatureOptions { SignedAt = SignedAt });

        var signature = Assert.Single(PdfSignatureVerifier.Verify(signed).Signatures);

        Assert.Null(signature.TimestampedSigningTime);
        Assert.Equal(PdfSignatureLevel.BaselineB, signature.Level);

        // No unsigned attribute carrying a timestamp OID.
        Assert.DoesNotContain(
            PdfTimestamp.SignatureTimestampOid,
            SignedPdf.Text(signed),
            StringComparison.Ordinal);
        Assert.Empty(SignedPdf.Cms(signed).SignerInfos[0].UnsignedAttributes);
    }

    [Fact]
    public void A_timestamped_signature_is_still_written_as_an_incremental_update()
    {
        using var tsa = new FakeTsa();
        using var client = tsa.Client();

        var original = TestPdf.OnePage();
        var signed = PdfSigner.Sign(
            original,
            TestCertificates.Signer(),
            new PdfSignatureOptions { TimestampAuthority = Authority, TimestampClient = client });

        Assert.Equal(original, signed.AsSpan(0, original.Length).ToArray());
        Assert.True(signed.Length > original.Length);

        // The token fits in the 8 KiB reserved for the CMS.
        var hex = Regex.Match(SignedPdf.Text(signed), @"/Contents<([0-9A-F]+)>").Groups[1].Value;
        Assert.True(hex.Length <= 8192 * 2);
    }

    [Fact]
    public void A_request_and_a_token_round_trip_through_asn1()
    {
        using var tsa = new FakeTsa { Time = new DateTimeOffset(2026, 5, 6, 7, 8, 9, TimeSpan.Zero) };

        var imprint = SHA256.HashData("payload"u8);
        var request = PdfTimestamp.BuildRequest(imprint);
        var (algorithm, requested) = FakeTsa.ReadRequest(request);

        Assert.Equal(PdfTimestamp.Sha256Oid, algorithm);
        Assert.Equal(imprint, requested);

        var token = PdfTimestamp.Parse(tsa.Token(requested, algorithm));

        Assert.Equal(PdfTimestamp.Sha256Oid, token.HashAlgorithm);
        Assert.Equal(tsa.Time, token.GeneratedAt);
        // An imprint is the digest of the data the token attests, so the data is what is hashed.
        Assert.True(token.Matches("payload"u8));
        Assert.False(token.Matches("other"u8));
    }

    [Fact]
    public void A_token_that_does_not_verify_is_refused()
    {
        using var tsa = new FakeTsa();
        var token = tsa.Token(SHA256.HashData([1]));

        // Flip a byte of the signature: the CMS still parses, and its signature no longer verifies.
        token[^1] ^= 0xFF;

        var exception = Assert.Throws<PdfSignatureException>(() => PdfTimestamp.Parse(token));

        Assert.Contains("does not verify", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_structure_that_is_not_a_cms_is_refused()
    {
        var exception = Assert.Throws<PdfSignatureException>(() => PdfTimestamp.Parse([0x30, 0x03, 0x02, 0x01, 0x01]));

        Assert.Contains("not a CMS", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_cms_that_is_not_a_tst_info_is_refused()
    {
        using var certificate = TestCertificates.Signer();
        var cms = new System.Security.Cryptography.Pkcs.SignedCms(
            new System.Security.Cryptography.Pkcs.ContentInfo([1, 2, 3]),
            detached: false);

        cms.ComputeSignature(new System.Security.Cryptography.Pkcs.CmsSigner(
            System.Security.Cryptography.Pkcs.SubjectIdentifierType.IssuerAndSerialNumber,
            certificate));

        var exception = Assert.Throws<PdfSignatureException>(() => PdfTimestamp.Parse(cms.Encode()));

        Assert.Contains("TSTInfo", exception.Message, StringComparison.Ordinal);
    }
}
