using System;
using System.Formats.Asn1;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

#pragma warning disable CA5350 // A token may carry a SHA-1 imprint; the verifier reads it, it does not choose it.

namespace M3scalinum.Text.Pdf;

/// <summary>What a timestamp token attests, once it has been read.</summary>
/// <param name="Imprint">The digest the token was issued over.</param>
/// <param name="HashAlgorithm">The digest algorithm, as an OID.</param>
/// <param name="GeneratedAt">The time the TSA asserts.</param>
/// <param name="Signer">The TSA certificate the token carries, when it carries one.</param>
/// <param name="Token">The token as it was issued.</param>
internal sealed record PdfTimestampToken(
    byte[] Imprint,
    string HashAlgorithm,
    DateTimeOffset GeneratedAt,
    X509Certificate2? Signer,
    byte[] Token)
{
    /// <summary>Whether <paramref name="data"/> hashes to the imprint the token carries.</summary>
    public bool Matches(ReadOnlySpan<byte> data)
    {
        var digest = HashAlgorithm switch
        {
            "2.16.840.1.101.3.4.2.1" => SHA256.HashData(data),
            "2.16.840.1.101.3.4.2.2" => SHA384.HashData(data),
            "2.16.840.1.101.3.4.2.3" => SHA512.HashData(data),
            "1.3.14.3.2.26" => SHA1.HashData(data),
            _ => null,
        };

        return digest is not null && CryptographicOperations.FixedTimeEquals(digest, Imprint);
    }
}

/// <summary>
/// RFC 3161 timestamp requests and responses, built and read with <see cref="AsnWriter"/> and
/// <see cref="AsnReader"/>.
/// </summary>
/// <remarks>
/// Nothing here reaches the network on its own: the caller passes an <see cref="HttpClient"/> and a
/// URL, and only <see cref="Request"/> writes to the wire.
/// </remarks>
internal static class PdfTimestamp
{
    /// <summary>The signed attribute that carries a signature timestamp: <c>id-aa-signatureTimeStampToken</c>.</summary>
    public const string SignatureTimestampOid = "1.2.840.113549.1.9.16.2.14";

    /// <summary>The content type of a TSTInfo: <c>id-ct-TSTInfo</c>.</summary>
    public const string TstInfoOid = "1.2.840.113549.1.9.16.1.4";

    /// <summary>The OID of SHA-256.</summary>
    public const string Sha256Oid = "2.16.840.1.101.3.4.2.1";

    /// <summary>Builds the <c>TimeStampReq</c> for a digest, asking the TSA to include its certificate.</summary>
    /// <remarks>
    /// No nonce: the token is bound to this signature value by its imprint, so a replayed token
    /// could only ever attest a signature that is already identical to this one.
    /// </remarks>
    public static byte[] BuildRequest(ReadOnlySpan<byte> imprint)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);

        using (writer.PushSequence())
        {
            writer.WriteInteger(1);

            using (writer.PushSequence())
            {
                WriteAlgorithm(writer, Sha256Oid);
                writer.WriteOctetString(imprint);
            }

            writer.WriteBoolean(true);
        }

        return writer.Encode();
    }

    /// <summary>Posts <paramref name="request"/> to <paramref name="authority"/> and returns the token.</summary>
    /// <exception cref="PdfSignatureException">The TSA refused, or answered with something that is not a token.</exception>
    public static byte[] Request(Uri authority, HttpClient client, byte[] request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, authority)
        {
            Content = new ByteArrayContent(request),
        };

        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/timestamp-query");
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/timestamp-reply"));

        byte[] body;
        try
        {
            using var response = client.Send(message);
            if (!response.IsSuccessStatusCode)
            {
                throw new PdfSignatureException(
                    $"the timestamp authority answered {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            body = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        }
        catch (HttpRequestException exception)
        {
            throw new PdfSignatureException($"the timestamp authority could not be reached: {exception.Message}", exception);
        }
        catch (TaskCanceledException exception)
        {
            throw new PdfSignatureException("the timestamp authority did not answer in time", exception);
        }

        return ReadResponse(body);
    }

    /// <summary>Reads a <c>TimeStampResp</c> and returns the token it carries.</summary>
    public static byte[] ReadResponse(ReadOnlySpan<byte> response)
    {
        var reader = new AsnReader(response.ToArray(), AsnEncodingRules.DER);
        var outer = reader.ReadSequence();

        var status = outer.ReadSequence();
        var code = (int)status.ReadInteger();
        var detail = status.HasData && status.PeekTag().HasSameClassAndValue(Asn1Tag.Sequence)
            ? ReadStatusText(status.ReadSequence())
            : string.Empty;

        if (code is not (0 or 1))
        {
            throw new PdfSignatureException(
                $"the timestamp authority rejected the request with status {code}{(detail.Length > 0 ? ": " + detail : string.Empty)}");
        }

        if (!outer.HasData)
        {
            throw new PdfSignatureException("the timestamp authority answered without a token");
        }

        return outer.ReadEncodedValue().ToArray();
    }

    private static string ReadStatusText(AsnReader statusString)
    {
        var parts = new System.Collections.Generic.List<string>();

        while (statusString.HasData)
        {
            // PKIFreeText ::= SEQUENCE OF UTF8String
            parts.Add(statusString.ReadCharacterString(UniversalTagNumber.UTF8String));
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Reads a timestamp token: verifies its own CMS signature, and returns what it attests.
    /// </summary>
    /// <param name="token">The DER token, as a TSA issues it.</param>
    /// <exception cref="PdfSignatureException">The token is not a CMS structure, or its signature does not verify.</exception>
    public static PdfTimestampToken Parse(ReadOnlySpan<byte> token)
    {
        var bytes = token.ToArray();
        var cms = new SignedCms();

        try
        {
            cms.Decode(bytes);
        }
        catch (CryptographicException exception)
        {
            throw new PdfSignatureException($"the timestamp token is not a CMS structure: {exception.Message}", exception);
        }

        if (cms.ContentInfo.ContentType.Value != TstInfoOid)
        {
            throw new PdfSignatureException(
                $"the timestamp token carries {cms.ContentInfo.ContentType.Value} instead of a TSTInfo");
        }

        try
        {
            cms.CheckSignature(verifySignatureOnly: true);
        }
        catch (CryptographicException exception)
        {
            throw new PdfSignatureException($"the timestamp token's signature does not verify: {exception.Message}", exception);
        }

        var content = cms.ContentInfo.Content;
        var info = new AsnReader(content, AsnEncodingRules.DER).ReadSequence();
        info.ReadInteger();                                    // version
        info.ReadObjectIdentifier();                           // policy
        var imprint = info.ReadSequence();
        var hash = imprint.ReadSequence();
        var algorithm = hash.ReadObjectIdentifier();
        var digest = imprint.ReadOctetString();
        info.ReadInteger();                                    // serialNumber
        var generatedAt = info.ReadGeneralizedTime();

        return new PdfTimestampToken(digest, algorithm, generatedAt, cms.SignerInfos.Count > 0 ? cms.SignerInfos[0].Certificate : null, bytes);
    }

    private static void WriteAlgorithm(AsnWriter writer, string oid)
    {
        using (writer.PushSequence())
        {
            writer.WriteObjectIdentifier(oid);
            writer.WriteNull();
        }
    }
}
