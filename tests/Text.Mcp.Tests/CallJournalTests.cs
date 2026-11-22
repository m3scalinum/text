using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using M3scalinum.Text.Mcp;
using Xunit;

namespace M3scalinum.Text.Mcp.Tests;

/// <summary>
/// The call journal: one structured line per tool call, off unless the host supplies a writer, and
/// made of sizes and identifiers rather than document text.
/// </summary>
public sealed class CallJournalTests
{
    [Fact]
    public async Task No_journal_is_written_when_the_host_did_not_supply_a_writer()
    {
        // The default server has no journal: one request, one line on the wire, and no writer to
        // throw on. Nothing is appended anywhere by the server itself.
        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames("""{"jsonrpc":"2.0","id":1,"method":"ping"}"""));

        Assert.Single(responses);
        Assert.Equal(JsonValueKind.Object, responses[0].GetProperty("result").ValueKind);
    }

    [Fact]
    public async Task One_line_is_written_per_call_with_its_tool_sizes_and_outcome()
    {
        using var writer = new StringWriter();

        var options = new McpServerOptions { CallJournal = writer };

        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames(
                McpTestClient.ToolCall(1, McpToolNames.ListTemplates, "{}"),
                McpTestClient.ToolCall(2, McpToolNames.RenderDocument, "{}")),
            options);

        // The journal never appears on the wire: the client sees two JSON-RPC messages, and nothing
        // else, whatever the journal does.
        Assert.Equal(2, responses.Count);

        var lines = Lines(writer);

        Assert.Equal(2, lines.Length);

        using var first = JsonDocument.Parse(lines[0]);
        using var second = JsonDocument.Parse(lines[1]);

        var listed = first.RootElement;

        Assert.Equal(McpToolNames.ListTemplates, listed.GetProperty("tool").GetString());
        Assert.Equal(2, listed.GetProperty("inputBytes").GetInt32());
        Assert.True(listed.GetProperty("outputBytes").GetInt32() > 0);
        Assert.Equal("ok", listed.GetProperty("outcome").GetString());
        Assert.False(listed.TryGetProperty("errorCode", out _));
        Assert.False(listed.TryGetProperty("session", out _));
        Assert.True(listed.GetProperty("timestamp").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(-1));

        var refused = second.RootElement;

        Assert.Equal(McpToolNames.RenderDocument, refused.GetProperty("tool").GetString());
        Assert.Equal(2, refused.GetProperty("inputBytes").GetInt32());
        Assert.Equal(0, refused.GetProperty("outputBytes").GetInt32());
        Assert.Equal("invalid_params", refused.GetProperty("outcome").GetString());
        Assert.Equal(-32602, refused.GetProperty("errorCode").GetInt32());
    }

    [Fact]
    public async Task A_tool_failure_is_journaled_as_a_tool_error()
    {
        using var writer = new StringWriter();

        var options = new McpServerOptions { CallJournal = writer };

        // Not a PDF: the work fails inside the tool, which is a result carrying isError, not a
        // JSON-RPC error.
        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames(McpTestClient.ToolCall(1, McpToolNames.ExtractText, """{"pdf":"bWFya2Vy"}""")),
            options);

        Assert.True(responses[0].GetProperty("result").TryGetProperty("isError", out var isError));
        Assert.True(isError.GetBoolean());

        using var entry = JsonDocument.Parse(Assert.Single(Lines(writer)));

        Assert.Equal("tool_error", entry.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(-32603, entry.RootElement.GetProperty("errorCode").GetInt32());

        // The client did receive a result — one that says the work failed — so its size is recorded.
        Assert.True(entry.RootElement.GetProperty("outputBytes").GetInt32() > 0);
    }

    [Fact]
    public async Task A_quota_refusal_is_journaled_with_its_code()
    {
        using var writer = new StringWriter();

        var options = new McpServerOptions { CallJournal = writer, MaxRendersPerMinute = 1 };

        await McpTestClient.ExchangeAsync(
            McpTestClient.Frames(
                McpTestClient.ToolCall(1, McpToolNames.RenderDocument, "{}"),
                McpTestClient.ToolCall(2, McpToolNames.RenderDocument, "{}")),
            options);

        var lines = Lines(writer);

        Assert.Equal(2, lines.Length);

        using var entry = JsonDocument.Parse(lines[1]);

        Assert.Equal("quota_exceeded", entry.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(McpServer.QuotaExceeded, entry.RootElement.GetProperty("errorCode").GetInt32());
    }

    [Fact]
    public async Task The_journal_carries_no_document_text()
    {
        using var writer = new StringWriter();

        var options = new McpServerOptions { CallJournal = writer };

        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames(McpTestClient.ToolCall(1, McpToolNames.ExtractText, """{"pdf":"bWFya2Vy"}""")),
            options);

        Assert.Single(responses);

        var line = Assert.Single(Lines(writer));

        // The argument decoded to the bytes of "marker": the journal records its size, never its
        // value.
        Assert.DoesNotContain("marker", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bWFya2Vy", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_http_call_is_journaled_under_its_session()
    {
        using var writer = new StringWriter();

        await using var server = await HttpTestServer.StartAsync(
            new McpServerOptions { CallJournal = writer });

        var sessionId = await server.OpenSessionAsync();

        await server.ExchangeAsync(McpTestClient.ToolCall(1, McpToolNames.ListTemplates, "{}"), sessionId);

        using var entry = JsonDocument.Parse(Assert.Single(Lines(writer)));

        Assert.Equal(sessionId, entry.RootElement.GetProperty("session").GetString());
    }

    [Fact]
    public async Task A_writer_that_throws_does_not_fail_the_call()
    {
        var options = new McpServerOptions { CallJournal = new FailingWriter() };

        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames(McpTestClient.ToolCall(1, McpToolNames.ListTemplates, "{}")),
            options);

        var result = Assert.Single(responses).GetProperty("result");

        Assert.False(result.TryGetProperty("isError", out _));
    }

    private static string[] Lines(StringWriter writer)
        => writer
            .ToString()
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>A journal the host misconfigured: every write fails.</summary>
    private sealed class FailingWriter : TextWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

        public override void Write(char value) => throw new IOException("the journal is on a full disk");

        public override void Write(string? value) => throw new IOException("the journal is on a full disk");
    }
}
