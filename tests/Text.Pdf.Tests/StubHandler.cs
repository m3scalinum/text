using System;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>A transport that answers whatever the test tells it to.</summary>
internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _answer;

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) => _answer = answer;

    public StubHandler(Exception error)
        => _answer = _ => throw error;

    public int Calls { get; private set; }

    public HttpClient Client() => new(this, disposeHandler: false);

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        => SendAsync(request, cancellationToken).GetAwaiter().GetResult();

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(_answer(request));
    }

    /// <summary>An answer carrying a body.</summary>
    public static HttpResponseMessage Body(byte[] payload, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new ByteArrayContent(payload) };
}
