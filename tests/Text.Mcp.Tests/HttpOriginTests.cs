using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using M3scalinum.Text.Mcp;
using Xunit;

namespace M3scalinum.Text.Mcp.Tests;

/// <summary>
/// The <c>Origin</c> check of the HTTP transport: the MCP streamable-HTTP spec requires it on every
/// incoming connection, to keep a web page from driving a loopback port through DNS rebinding.
/// </summary>
/// <remarks>
/// A request with no <c>Origin</c> is not a browser request and stays served; a request that carries
/// one must name an origin the host listed, and an empty list serves none.
/// </remarks>
public sealed class HttpOriginTests
{
    private const string Initialize = TransportSuiteTests.Initialize;

    private const string Allowed = "https://app.example.com";

    [Fact]
    public async Task A_request_without_an_origin_is_served()
    {
        await using var server = await HttpTestServer.StartAsync(
            httpOptions: new McpHttpServerOptions { BearerToken = HttpTestServer.Token });

        var response = await server.PostAsync(Initialize);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_request_from_an_allowed_origin_is_served()
    {
        await using var server = await HttpTestServer.StartAsync(httpOptions: Options(Allowed));

        var response = await server.PostAsync(Initialize, origin: Allowed);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_origin_is_matched_case_insensitively_and_ignores_a_default_port()
    {
        await using var server = await HttpTestServer.StartAsync(httpOptions: Options("https://app.example.com:443"));

        var response = await server.PostAsync(Initialize, origin: "HTTPS://App.Example.com");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_origin_the_list_does_not_carry_is_rejected_with_403()
    {
        await using var server = await HttpTestServer.StartAsync(httpOptions: Options(Allowed));

        var response = await server.PostAsync(Initialize, origin: "https://evil.example");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains("https://evil.example", body, StringComparison.Ordinal);
        Assert.Contains(Allowed, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_no_allowed_origin_every_browser_origin_is_rejected()
    {
        await using var server = await HttpTestServer.StartAsync();

        var response = await server.PostAsync(Initialize, origin: Allowed);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains(Allowed, body, StringComparison.Ordinal);
        Assert.Contains("no browser origin", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_delete_from_a_refused_origin_is_rejected_with_403()
    {
        await using var server = await HttpTestServer.StartAsync(httpOptions: Options(Allowed));

        var sessionId = await server.OpenSessionAsync();

        var response = await server.SendAsync(HttpMethod.Delete, sessionId, origin: "https://evil.example");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // The refused DELETE never ran: the session is still there.
        var ping = await server.PostAsync("""{"jsonrpc":"2.0","id":2,"method":"ping"}""", sessionId);

        Assert.Equal(HttpStatusCode.OK, ping.StatusCode);
    }

    [Fact]
    public void An_origin_that_names_a_path_fails_when_the_server_is_built()
    {
        var options = Options("https://app.example.com/console");

        Assert.Throws<ArgumentException>(
            () => new McpHttpServer(new McpServerOptions(), options));
    }

    private static McpHttpServerOptions Options(string origin) => new()
    {
        BearerToken = HttpTestServer.Token,
        AllowedOrigins = [origin],
    };
}
