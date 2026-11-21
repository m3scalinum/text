using System;
using System.Formats.Asn1;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// An RFC 3161 authority that answers over a stub transport: it reads the <c>TimeStampReq</c> the
/// signer wrote and returns a real, signed token over the imprint it found.
/// </summary>
internal sealed class FakeTsa : HttpMessageHandler
{
    public FakeTsa(X509Certificate2? certificate = null)
        => Certificate = certificate ?? TestCertificates.Signer("CN=M3scalinum Test TSA, O=M3scalinum.Text.Pdf, C=FR");

    public X509Certificate2 Certificate { get; }

    public DateTimeOffset Time { get; set; } = new(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);

    public int Calls { get; private set; }

    public string? LastContentType { get; private set; }

    public string? LastAlgorithm { get; private set; }

    public byte[]? LastImprint { get; private set; }

    /// <summary>Replaces the token the authority answers with, to model a misbehaving TSA.</summary>
    public Func<byte[], byte[]>? TokenFactory { get; set; }

    /// <summary>The HTTP status to answer with.</summary>
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

    /// <summary>Builds a signed token over <paramref name="imprint"/>.</summary>
    public byte[] Token(byte[] imprint, string algorithm = PdfTimestamp.Sha256Oid)
    {
        var info = new AsnWriter(AsnEncodingRules.DER);

        using (info.PushSequence())
        {
            info.WriteInteger(1);
            info.WriteObjectIdentifier("1.3.6.1.4.1.99999.1");

            using (info.PushSequence())
            {
                using (info.PushSequence())
                {
                    info.WriteObjectIdentifier(algorithm);
                    info.WriteNull();
                }

                info.WriteOctetString(imprint);
            }

            info.WriteInteger(new System.Numerics.BigInteger(RandomNumberGenerator.GetBytes(8), isUnsigned: true, isBigEndian: true));
            info.WriteGeneralizedTime(Time);
        }

        var cms = new SignedCms(new ContentInfo(new Oid(PdfTimestamp.TstInfoOid), info.Encode()), detached: false);
        cms.ComputeSignature(new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, Certificate)
        {
            IncludeOption = X509IncludeOption.EndCertOnly,
        });

        return cms.Encode();
    }

    /// <summary>Builds a <c>TimeStampResp</c> around a token.</summary>
    public static byte[] Response(byte[]? token, int status = 0)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);

        using (writer.PushSequence())
        {
            using (writer.PushSequence())
            {
                writer.WriteInteger(status);
            }

            if (token is not null)
            {
                writer.WriteEncodedValue(token);
            }
        }

        return writer.Encode();
    }

    /// <summary>Reads the imprint a <c>TimeStampReq</c> asks to have timestamped.</summary>
    public static (string Algorithm, byte[] Imprint) ReadRequest(byte[] request)
    {
        var outer = new AsnReader(request, AsnEncodingRules.DER).ReadSequence();
        outer.ReadInteger();
        var imprint = outer.ReadSequence();
        var algorithm = imprint.ReadSequence();
        var oid = algorithm.ReadObjectIdentifier();

        return (oid, imprint.ReadOctetString());
    }

    /// <summary>An <see cref="HttpClient"/> wired to this authority.</summary>
    public HttpClient Client() => new(this, disposeHandler: false);

    protected override HttpResponseMessage Send(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
        => SendAsync(request, cancellationToken).GetAwaiter().GetResult();

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Calls++;
        LastContentType = request.Content?.Headers.ContentType?.MediaType;

        var body = request.Content!.ReadAsByteArrayAsync(cancellationToken).GetAwaiter().GetResult();
        var (algorithm, imprint) = ReadRequest(body);
        LastAlgorithm = algorithm;
        LastImprint = imprint;

        var token = TokenFactory is { } factory ? factory(imprint) : Token(imprint, algorithm);
        var payload = Response(token);

        return Task.FromResult(new HttpResponseMessage(Status)
        {
            Content = new ByteArrayContent(payload),
        });
    }
}
