using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using M3scalinum.Text.Mcp;
using Xunit;

namespace M3scalinum.Text.Mcp.Tests;

/// <summary>
/// The protocol suite every transport must pass: the same messages, the same answers, whichever
/// framing carries them.
/// </summary>
/// <remarks>
/// <para>
/// The point is divergence. A transport that changes a response — a status code leaking into a
/// result, a message quietly dropped — fails here even though both implementations pass their own
/// tests. <see cref="StdioTransportSuiteTests"/> runs this over an in-memory stream pair and
/// <see cref="HttpTransportSuiteTests"/> over a real loopback socket.
/// </para>
/// <para>
/// Only the message-level contract is shared. Framing, authentication and session lifecycle belong
/// to the transport and are tested where they live.
/// </para>
/// </remarks>
public abstract class TransportSuiteTests
{
    internal const string Initialize =
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"tests","version":"1.0.0"}}}""";

    /// <summary>Opens a transport, ready to carry messages.</summary>
    protected abstract Task<IProtocolClient> ConnectAsync();

    [Fact]
    public async Task Every_transport_reports_the_same_protocol_revision_capabilities_and_server_info()
    {
        await using var client = await ConnectAsync();

        var response = await client.SendAsync(Initialize);

        Assert.NotNull(response);

        var result = response!.Value.GetProperty("result");

        Assert.Equal(McpServer.ProtocolVersion, result.GetProperty("protocolVersion").GetString());
        Assert.False(result.GetProperty("capabilities").GetProperty("tools").GetProperty("listChanged").GetBoolean());
        Assert.Equal("M3scalinum.Text.Mcp", result.GetProperty("serverInfo").GetProperty("name").GetString());
        Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("serverInfo").GetProperty("version").GetString()));
    }

    [Fact]
    public async Task Every_transport_answers_ping_with_an_empty_result()
    {
        await using var client = await ConnectAsync();

        var response = await client.SendAsync("""{"jsonrpc":"2.0","id":7,"method":"ping"}""");

        Assert.NotNull(response);
        Assert.Equal(7, response!.Value.GetProperty("id").GetInt32());
        Assert.Empty(response.Value.GetProperty("result").EnumerateObject());
    }

    [Fact]
    public async Task Every_transport_lists_the_six_tools_with_their_schemas()
    {
        await using var client = await ConnectAsync();

        var response = await client.SendAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");

        Assert.NotNull(response);

        var tools = response!.Value.GetProperty("result").GetProperty("tools").EnumerateArray().ToArray();

        Assert.Equal(
            new[]
            {
                McpToolNames.RenderDocument,
                McpToolNames.ListTemplates,
                McpToolNames.SignDocument,
                McpToolNames.MergeDocuments,
                McpToolNames.ExtractText,
            },
            tools.Select(tool => tool.GetProperty("name").GetString()).ToArray());

        foreach (var tool in tools)
        {
            Assert.Equal("object", tool.GetProperty("inputSchema").GetProperty("type").GetString());
            Assert.False(string.IsNullOrWhiteSpace(tool.GetProperty("description").GetString()));
        }
    }

    [Fact]
    public async Task Every_transport_answers_an_unknown_method_with_method_not_found()
    {
        await using var client = await ConnectAsync();

        var response = await client.SendAsync("""{"jsonrpc":"2.0","id":"abc","method":"prompts/list"}""");

        Assert.NotNull(response);

        var error = response!.Value.GetProperty("error");

        Assert.Equal(-32601, error.GetProperty("code").GetInt32());
        Assert.Contains("prompts/list", error.GetProperty("message").GetString(), System.StringComparison.Ordinal);
        Assert.Equal("abc", response.Value.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Every_transport_answers_an_unknown_tool_with_invalid_params()
    {
        await using var client = await ConnectAsync();

        var response = await client.SendAsync(McpTestClient.ToolCall(4, "delete_everything", "{}"));

        Assert.NotNull(response);

        var error = response!.Value.GetProperty("error");

        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Contains("delete_everything", error.GetProperty("message").GetString(), System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_transport_answers_malformed_json_with_a_parse_error_and_a_null_id()
    {
        await using var client = await ConnectAsync();

        var response = await client.SendAsync("""{"jsonrpc":"2.0","id":1,"method":""");

        Assert.NotNull(response);
        Assert.Equal(JsonValueKind.Null, response!.Value.GetProperty("id").ValueKind);
        Assert.Equal(-32700, response.Value.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Every_transport_leaves_a_notification_unanswered()
    {
        await using var client = await ConnectAsync();

        var response = await client.SendAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");

        Assert.Null(response);
    }

    [Fact]
    public async Task Every_transport_refuses_a_batch_request()
    {
        await using var client = await ConnectAsync();

        var response = await client.SendAsync("""[{"jsonrpc":"2.0","id":1,"method":"ping"}]""");

        Assert.NotNull(response);

        var error = response!.Value.GetProperty("error");

        Assert.Equal(-32600, error.GetProperty("code").GetInt32());
        Assert.Contains("batch", error.GetProperty("message").GetString(), System.StringComparison.Ordinal);
    }
}

/// <summary>The shared suite over newline-delimited JSON on an in-memory stream pair.</summary>
public sealed class StdioTransportSuiteTests : TransportSuiteTests
{
    protected override Task<IProtocolClient> ConnectAsync()
        => Task.FromResult<IProtocolClient>(StdioProtocolClient.Instance);
}

/// <summary>The shared suite over a real HTTP round trip on a loopback port.</summary>
public sealed class HttpTransportSuiteTests : TransportSuiteTests
{
    protected override async Task<IProtocolClient> ConnectAsync()
    {
        var client = await HttpProtocolClient.StartAsync();

        return client;
    }
}
