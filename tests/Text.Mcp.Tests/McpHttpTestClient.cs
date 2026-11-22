using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using M3scalinum.Text.Mcp;
using Xunit;

namespace M3scalinum.Text.Mcp.Tests;

/// <summary>A protocol client the shared transport suite can drive, whatever the framing is.</summary>
public interface IProtocolClient : IAsyncDisposable
{
    /// <summary>Sends one message and returns its answer, or <see langword="null"/> when there is none.</summary>
    Task<JsonElement?> SendAsync(string message);
}

/// <summary>Drives the stdio transport: one message in, at most one message out.</summary>
internal sealed class StdioProtocolClient : IProtocolClient
{
    internal static readonly StdioProtocolClient Instance = new();

    public async Task<JsonElement?> SendAsync(string message)
    {
        var responses = await McpTestClient.ExchangeAsync(McpTestClient.Frames(message));

        return responses.Count == 0 ? null : responses[0];
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// A real <see cref="McpHttpServer"/> on a loopback port the operating system chose, plus the
/// <see cref="HttpClient"/> that talks to it.
/// </summary>
/// <remarks>
/// The client is a real HTTP client over a real socket: the requests are framed by
/// <see cref="HttpClient"/> and parsed by the server's own HTTP/1.1 reader, so a test that passes here
/// has crossed the network surface, not a mock of it.
/// </remarks>
internal sealed class HttpTestServer : IAsyncDisposable
{
    internal const string Token = "test-token-5f2c9a4e7b1d";

    private readonly McpHttpServer _server;
    private readonly HttpClient _client;

    private HttpTestServer(McpHttpServer server, HttpClient client)
    {
        _server = server;
        _client = client;
    }

    internal McpHttpServer Server => _server;

    internal HttpClient Client => _client;

    internal int Port => _server.Port;

    internal Uri Endpoint => _server.Endpoint;

    internal static async Task<HttpTestServer> StartAsync(
        McpServerOptions? options = null,
        McpHttpServerOptions? httpOptions = null)
    {
        var serverOptions = options ?? new McpServerOptions();

        httpOptions ??= new McpHttpServerOptions { BearerToken = Token };

        var server = new McpHttpServer(serverOptions, httpOptions);

        await server.StartAsync(new McpServer(serverOptions));

        var client = new HttpClient { BaseAddress = server.Endpoint, Timeout = TimeSpan.FromSeconds(30) };

        return new HttpTestServer(server, client);
    }

    /// <summary>Posts one JSON-RPC message, with the session and the bearer token when given.</summary>
    internal async Task<HttpResponseMessage> PostAsync(
        string message,
        string? sessionId = null,
        string? token = Token,
        string? accept = null,
        string? path = null,
        string? origin = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path is null ? _server.Endpoint : Absolute(path));

        request.Content = new StringContent(message, Encoding.UTF8, "application/json");

        if (sessionId is not null)
        {
            request.Headers.TryAddWithoutValidation(McpHttpServer.SessionHeader, sessionId);
        }

        if (origin is not null)
        {
            request.Headers.TryAddWithoutValidation("Origin", origin);
        }

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (accept is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept", accept);
        }

        return await _client.SendAsync(request);
    }

    /// <summary>Issues a request with a method other than POST.</summary>
    internal async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string? sessionId = null,
        string? token = Token,
        string? path = null,
        string? origin = null)
    {
        var request = new HttpRequestMessage(method, path is null ? _server.Endpoint : Absolute(path));

        if (sessionId is not null)
        {
            request.Headers.TryAddWithoutValidation(McpHttpServer.SessionHeader, sessionId);
        }

        if (origin is not null)
        {
            request.Headers.TryAddWithoutValidation("Origin", origin);
        }

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await _client.SendAsync(request);
    }

    /// <summary>Initializes a session and returns its id, asserting the exchange succeeded.</summary>
    internal async Task<string> OpenSessionAsync()
    {
        var response = await PostAsync(TransportSuiteTests.Initialize);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var session = response.Headers.TryGetValues(McpHttpServer.SessionHeader, out var values)
            ? Assert.Single(values)
            : throw new InvalidOperationException("initialize answered without a session id");

        return session;
    }

    /// <summary>Sends a request and returns the JSON-RPC message the server answered.</summary>
    internal async Task<JsonElement> ExchangeAsync(
        string message,
        string sessionId,
        string? accept = null)
    {
        var response = await PostAsync(message, sessionId, accept: accept);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.Clone();
    }

    /// <summary>The JSON body of a response, parsed and detached.</summary>
    internal static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.Clone();
    }

    private Uri Absolute(string path) => new($"http://127.0.0.1:{Port}{path}");

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _server.DisposeAsync();
    }
}

/// <summary>Drives an <see cref="McpHttpServer"/> as an <see cref="IProtocolClient"/>.</summary>
internal sealed class HttpProtocolClient : IProtocolClient
{
    private readonly HttpTestServer _server;
    private string? _sessionId;

    private HttpProtocolClient(HttpTestServer server) => _server = server;

    internal static async Task<HttpProtocolClient> StartAsync()
        => new(await HttpTestServer.StartAsync());

    public async Task<JsonElement?> SendAsync(string message)
    {
        if (_sessionId is null && !IsInitialize(message))
        {
            _sessionId = await _server.OpenSessionAsync();
        }

        var response = await _server.PostAsync(message, _sessionId);

        if (response.StatusCode == System.Net.HttpStatusCode.Accepted)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"the server answered {(int)response.StatusCode} to a message the suite expected to succeed");
        }

        if (response.Headers.TryGetValues(McpHttpServer.SessionHeader, out var values))
        {
            _sessionId = Assert.Single(values);
        }

        return await HttpTestServer.ReadJsonAsync(response);
    }

    private static bool IsInitialize(string message)
    {
        try
        {
            using var document = JsonDocument.Parse(message);

            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("method", out var method)
                && method.GetString() == "initialize"
                && document.RootElement.TryGetProperty("id", out var id)
                && id.ValueKind != JsonValueKind.Null;
        }
        catch (JsonException)
        {
            // A malformed message is not an initialize: the session is opened first, and the server
            // answers the malformed body with a parse error, which is what the suite asserts.
            return false;
        }
    }

    public async ValueTask DisposeAsync() => await _server.DisposeAsync();
}
