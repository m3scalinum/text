using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using M3scalinum.Text.Mcp;
using M3scalinum.Text.Mcp.Internal;
using Xunit;

namespace M3scalinum.Text.Mcp.Tests;

/// <summary>
/// The HTTP transport: a real round trip over a loopback socket the operating system assigned, and
/// the network posture that comes with it — the token, the session lifecycle, the body cap and the
/// single served path.
/// </summary>
/// <remarks>
/// These run without the native compiler except <see cref="A_tools_call_returns_a_pdf_over_http"/>,
/// which needs <c>SHARPTYPST_NATIVE_PATH</c> like the stdio render tests.
/// </remarks>
public sealed class HttpTransportTests
{
    private const string Initialize = TransportSuiteTests.Initialize;

    private const string ToolsList = """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""";

    private const string Ping = """{"jsonrpc":"2.0","id":3,"method":"ping"}""";

    private const string SkipReason =
        "set SHARPTYPST_NATIVE_PATH to native/sharp-typst/target/release/sharp_typst.dll "
        + "(or libsharp_typst.so) to run the render tests";

    private static bool NativeAvailable
        => Environment.GetEnvironmentVariable("SHARPTYPST_NATIVE_PATH") is { Length: > 0 } path
            && File.Exists(path);

    [Fact]
    public async Task Initialize_issues_a_session_id_and_reports_the_protocol_revision()
    {
        await using var server = await HttpTestServer.StartAsync();

        var response = await server.PostAsync(Initialize);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var sessionId = Assert.Single(response.Headers.GetValues(McpHttpServer.SessionHeader));

        Assert.Equal(32, sessionId.Length);

        var body = await HttpTestServer.ReadJsonAsync(response);

        Assert.Equal(McpServer.ProtocolVersion, body.GetProperty("result").GetProperty("protocolVersion").GetString());
    }

    [Fact]
    public async Task A_request_without_the_token_is_rejected_with_401()
    {
        await using var server = await HttpTestServer.StartAsync();

        var response = await server.PostAsync(Initialize, token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_request_with_the_wrong_token_is_rejected_with_401()
    {
        await using var server = await HttpTestServer.StartAsync();

        var response = await server.PostAsync(Initialize, token: "not-the-token");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_message_with_a_session_id_the_server_never_issued_is_rejected_with_404()
    {
        await using var server = await HttpTestServer.StartAsync();

        var response = await server.PostAsync(ToolsList, sessionId: new string('0', 32));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_message_after_initialize_without_the_session_header_is_rejected_with_404()
    {
        await using var server = await HttpTestServer.StartAsync();

        await server.OpenSessionAsync();

        var response = await server.PostAsync(ToolsList);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_message_with_another_session_id_is_rejected_with_404()
    {
        await using var server = await HttpTestServer.StartAsync();

        var sessionId = await server.OpenSessionAsync();
        var other = await server.OpenSessionAsync();

        Assert.NotEqual(sessionId, other);

        var response = await server.PostAsync(ToolsList, sessionId: other + "x");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Tools_list_answers_over_the_session()
    {
        await using var server = await HttpTestServer.StartAsync();

        var sessionId = await server.OpenSessionAsync();
        var body = await server.ExchangeAsync(ToolsList, sessionId);
        var tools = body.GetProperty("result").GetProperty("tools").EnumerateArray().ToArray();

        Assert.Equal(6, tools.Length);
        Assert.Equal(McpToolNames.RenderDocument, tools[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task An_oversized_body_is_rejected_with_413_before_it_is_buffered()
    {
        // The ceiling is 256 bytes, so a 4 KiB body is refused on its declared length: the server
        // never stores more than the limit, and it answers instead of reading the whole body.
        var options = new McpServerOptions { MaxDocumentSize = 256, MaxRequestSize = 256 };

        await using var server = await HttpTestServer.StartAsync(options);

        var sessionId = await server.OpenSessionAsync();
        var oversized = "{\"jsonrpc\":\"2.0\",\"id\":9,\"method\":\"ping\",\"params\":{\"pad\":\""
            + new string('x', 4096)
            + "\"}}";

        var response = await server.PostAsync(oversized, sessionId);

        Assert.Equal(413, (int)response.StatusCode);

        // The refusal belongs to the request, not to the session: the next message is served.
        var next = await server.PostAsync(Ping, sessionId);

        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
    }

    [Fact]
    public async Task Delete_closes_the_session_and_a_later_post_is_rejected_with_404()
    {
        await using var server = await HttpTestServer.StartAsync();

        var sessionId = await server.OpenSessionAsync();

        var deleted = await server.SendAsync(HttpMethod.Delete, sessionId);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var after = await server.PostAsync(Ping, sessionId);

        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }

    [Fact]
    public async Task Delete_without_a_known_session_is_rejected_with_404()
    {
        await using var server = await HttpTestServer.StartAsync();

        var response = await server.SendAsync(HttpMethod.Delete);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_path_other_than_the_endpoint_is_rejected_with_404()
    {
        await using var server = await HttpTestServer.StartAsync();

        var response = await server.PostAsync(Initialize, path: "/elsewhere");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_get_is_rejected_with_405_and_the_allowed_methods()
    {
        await using var server = await HttpTestServer.StartAsync();

        var sessionId = await server.OpenSessionAsync();
        var response = await server.SendAsync(HttpMethod.Get, sessionId);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);

        var allow = response.Content.Headers.GetValues("Allow").SelectMany(value => value.Split(", ")).ToArray();

        Assert.Contains("POST", allow);
        Assert.Contains("DELETE", allow);
    }

    [Fact]
    public async Task A_client_that_accepts_only_an_event_stream_receives_the_message_as_an_event()
    {
        await using var server = await HttpTestServer.StartAsync();

        var sessionId = await server.OpenSessionAsync();
        var response = await server.PostAsync(Ping, sessionId, accept: "text/event-stream");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.StartsWith("event: message\ndata: ", text, StringComparison.Ordinal);
        Assert.EndsWith("\n\n", text, StringComparison.Ordinal);

        var data = text.Split('\n')[1]["data: ".Length..];

        using var frame = JsonDocument.Parse(data);

        Assert.Equal(3, frame.RootElement.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task A_client_that_accepts_json_and_events_receives_json()
    {
        await using var server = await HttpTestServer.StartAsync();

        var sessionId = await server.OpenSessionAsync();
        var response = await server.PostAsync(Ping, sessionId, accept: "application/json, text/event-stream");

        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_notification_is_accepted_with_no_body()
    {
        await using var server = await HttpTestServer.StartAsync();

        var sessionId = await server.OpenSessionAsync();
        var response = await server.PostAsync(
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""", sessionId);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_session_cap_is_enforced()
    {
        await using var server = await HttpTestServer.StartAsync(
            httpOptions: new McpHttpServerOptions
            {
                BearerToken = HttpTestServer.Token,
                MaxSessions = 1,
            });

        await server.OpenSessionAsync();

        var response = await server.PostAsync(Initialize);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task The_server_binds_loopback_on_a_port_the_operating_system_chose()
    {
        await using var server = await HttpTestServer.StartAsync();

        Assert.True(server.Port > 0);
        Assert.Equal("127.0.0.1", server.Endpoint.Host);
        Assert.Equal(McpHttpServerOptions.DefaultPath, server.Endpoint.AbsolutePath);
        Assert.Equal(IPAddress.Loopback, server.Server.Options.Address);
    }

    [Fact]
    public async Task The_wire_refuses_an_oversized_body_without_reading_it()
    {
        // Only the head is in the stream: a reader that tried to consume the body first would end at
        // EOF and answer 400. A 413 here is the proof the declared length was checked before any byte
        // of the body was buffered.
        var head = Encoding.ASCII.GetBytes("POST /mcp HTTP/1.1\r\nContent-Length: 4096\r\n\r\n");

        using var stream = new MemoryStream(head);

        var wire = new HttpWireReader(stream);
        var parsed = await wire.ReadHeadAsync(McpHttpServerOptions.DefaultMaxHeaderBytes,
            TestContext.Current.CancellationToken);

        Assert.NotNull(parsed);

        var exception = await Assert.ThrowsAsync<HttpWireException>(
            () => wire.ReadBodyAsync(parsed!.ContentLength!.Value, 128, TestContext.Current.CancellationToken));

        Assert.Equal(413, exception.StatusCode);
        Assert.Contains("128", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_wire_parses_a_head_that_arrives_with_a_larger_body_in_the_same_read()
    {
        // A single socket read can carry the head and several kilobytes of body. Scanning only the
        // tail of the accumulated block for the blank line missed the terminator in that case, and
        // the connection then waited for a head that had already arrived.
        var body = new string('x', 8192);
        var head = $"POST /mcp HTTP/1.1\r\nContent-Length: {body.Length}\r\n\r\n";

        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(head + body));

        var wire = new HttpWireReader(stream);
        var parsed = await wire.ReadHeadAsync(McpHttpServerOptions.DefaultMaxHeaderBytes,
            TestContext.Current.CancellationToken);

        Assert.NotNull(parsed);
        Assert.Equal(body.Length, parsed!.ContentLength);

        var read = await wire.ReadBodyAsync(parsed.ContentLength!.Value, 16 * 1024,
            TestContext.Current.CancellationToken);

        Assert.Equal(body.Length, read.Length);
    }

    [Fact]
    public async Task The_wire_refuses_a_header_block_over_the_cap_with_431()
    {
        var head = Encoding.ASCII.GetBytes("POST /mcp HTTP/1.1\r\nX-Pad: " + new string('x', 512) + "\r\n");

        using var stream = new MemoryStream(head);

        var wire = new HttpWireReader(stream);

        var exception = await Assert.ThrowsAsync<HttpWireException>(
            () => wire.ReadHeadAsync(256, TestContext.Current.CancellationToken));

        Assert.Equal(431, exception.StatusCode);
    }

    [Fact]
    public async Task A_tools_call_returns_a_pdf_over_http()
    {
        Assert.SkipUnless(NativeAvailable, SkipReason);

        await using var server = await HttpTestServer.StartAsync();

        var sessionId = await server.OpenSessionAsync();

        var body = await server.ExchangeAsync(
            McpTestClient.ToolCall(
                5,
                McpToolNames.RenderDocument,
                """{"source":"= Hello over HTTP\nRendered by the MCP server."}"""),
            sessionId);

        Assert.True(body.TryGetProperty("result", out var result), body.GetRawText());
        Assert.False(result.TryGetProperty("isError", out _));

        var resource = result.GetProperty("content").EnumerateArray()
            .Single(block => block.GetProperty("type").GetString() == "resource");

        var pdf = Convert.FromBase64String(
            resource.GetProperty("resource").GetProperty("blob").GetString()!);

        Assert.Equal("%PDF"u8.ToArray(), pdf[..4]);
        Assert.True(pdf.Length > 1000, $"the PDF is only {pdf.Length} bytes");
    }
}
