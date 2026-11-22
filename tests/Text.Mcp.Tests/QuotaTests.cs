using System;
using System.Text.Json;
using System.Threading.Tasks;
using M3scalinum.Text.Mcp;
using M3scalinum.Text.Mcp.Internal;
using Xunit;

namespace M3scalinum.Text.Mcp.Tests;

/// <summary>
/// The per-client render quota: a sliding one-minute window, a typed JSON-RPC error when it is
/// reached, and a counter that a later request does not wipe.
/// </summary>
/// <remarks>
/// None of these calls needs the native compiler: the ceiling is applied when a call is admitted,
/// before its arguments are read, which is also why a malformed call spends a slot.
/// </remarks>
public sealed class QuotaTests
{
    private const string Ping = """{"jsonrpc":"2.0","id":90,"method":"ping"}""";

    private static string Render(int id) => McpTestClient.ToolCall(id, McpToolNames.RenderDocument, "{}");

    private static string ListTemplates(int id) => McpTestClient.ToolCall(id, McpToolNames.ListTemplates, "{}");

    [Fact]
    public void A_ceiling_below_one_is_refused_when_the_server_is_built()
    {
        Assert.Throws<ArgumentException>(() => new McpServer(new McpServerOptions { MaxRendersPerMinute = 0 }));
    }

    [Fact]
    public void The_window_admits_the_ceiling_then_frees_a_slot_once_the_oldest_call_expires()
    {
        var quota = new RenderQuota(2);
        var start = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        Assert.True(quota.TryAcquire("session", start, out _));
        Assert.True(quota.TryAcquire("session", start.AddSeconds(10), out _));

        Assert.False(quota.TryAcquire("session", start.AddSeconds(59), out var retryAfter));

        // Fifty seconds after the second call, the first one is a minute old and its slot is free.
        Assert.Equal(1, retryAfter);
        Assert.True(quota.TryAcquire("session", start.AddSeconds(60), out _));
        Assert.False(quota.TryAcquire("session", start.AddSeconds(61), out _));
    }

    [Fact]
    public void A_window_below_one_call_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RenderQuota(0));
    }

    [Fact]
    public void The_window_is_per_client()
    {
        var quota = new RenderQuota(1);
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        Assert.True(quota.TryAcquire("first", now, out _));
        Assert.False(quota.TryAcquire("first", now, out _));
        Assert.True(quota.TryAcquire("second", now, out _));
    }

    [Fact]
    public void An_expired_window_is_forgotten_rather_than_kept_forever()
    {
        var quota = new RenderQuota(1);
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        Assert.True(quota.TryAcquire("first", now, out _));

        // Admitting another client prunes the first one's window, which is empty and dropped; the
        // first client then starts a fresh window instead of inheriting a stale key.
        Assert.True(quota.TryAcquire("second", now.AddSeconds(61), out _));
        Assert.True(quota.TryAcquire("first", now.AddSeconds(62), out _));
        Assert.False(quota.TryAcquire("first", now.AddSeconds(63), out _));
    }

    [Fact]
    public async Task The_call_over_the_ceiling_is_a_typed_quota_error_and_the_session_continues()
    {
        var options = new McpServerOptions { MaxRendersPerMinute = 2 };

        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames(Render(1), Render(2), Render(3), Ping),
            options);

        // The first two calls spend their slot even though their arguments are refused: the ceiling
        // bounds attempts, not successes.
        Assert.Equal(-32602, responses[0].GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(-32602, responses[1].GetProperty("error").GetProperty("code").GetInt32());

        var error = responses[2].GetProperty("error");

        Assert.Equal(McpServer.QuotaExceeded, error.GetProperty("code").GetInt32());
        Assert.Contains("render quota exceeded", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("stdio", error.GetProperty("message").GetString(), StringComparison.Ordinal);

        var data = error.GetProperty("data");

        Assert.Equal("render_quota_exceeded", data.GetProperty("reason").GetString());
        Assert.Equal(2, data.GetProperty("limit").GetInt32());
        Assert.Equal(60, data.GetProperty("windowSeconds").GetInt32());
        Assert.True(data.GetProperty("retryAfterSeconds").GetInt32() >= 1);

        // The refusal is a message, not a dropped connection: the next request is answered.
        Assert.Equal(JsonValueKind.Object, responses[3].GetProperty("result").ValueKind);
    }

    [Fact]
    public async Task A_request_between_two_document_calls_does_not_reset_the_window()
    {
        var options = new McpServerOptions { MaxRendersPerMinute = 1 };

        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames(Render(1), Ping, Render(3)),
            options);

        Assert.Equal(-32602, responses[0].GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(JsonValueKind.Object, responses[1].GetProperty("result").ValueKind);
        Assert.Equal(McpServer.QuotaExceeded, responses[2].GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Listing_templates_is_metadata_and_never_spends_a_slot()
    {
        var options = new McpServerOptions { MaxRendersPerMinute = 1 };

        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames(Render(1), ListTemplates(2), ListTemplates(3), Render(4)),
            options);

        Assert.Equal(-32602, responses[0].GetProperty("error").GetProperty("code").GetInt32());

        foreach (var index in new[] { 1, 2 })
        {
            var result = responses[index].GetProperty("result");

            Assert.False(result.TryGetProperty("isError", out _));
            Assert.Contains(
                "\"templates\"",
                result.GetProperty("content")[0].GetProperty("text").GetString(),
                StringComparison.Ordinal);
        }

        Assert.Equal(McpServer.QuotaExceeded, responses[3].GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Two_http_sessions_have_their_own_window()
    {
        var options = new McpServerOptions { MaxRendersPerMinute = 1 };

        await using var server = await HttpTestServer.StartAsync(options);

        var first = await server.OpenSessionAsync();
        var second = await server.OpenSessionAsync();

        Assert.Equal(-32602, Fail(await server.ExchangeAsync(Render(1), first)).GetProperty("code").GetInt32());
        Assert.Equal(
            McpServer.QuotaExceeded,
            Fail(await server.ExchangeAsync(Render(2), first)).GetProperty("code").GetInt32());

        Assert.Equal(-32602, Fail(await server.ExchangeAsync(Render(3), second)).GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task The_quota_error_names_the_session_that_reached_it()
    {
        var options = new McpServerOptions { MaxRendersPerMinute = 1 };

        await using var server = await HttpTestServer.StartAsync(options);

        var sessionId = await server.OpenSessionAsync();

        await server.ExchangeAsync(Render(1), sessionId);

        var error = Fail(await server.ExchangeAsync(Render(2), sessionId));

        Assert.Contains(sessionId, error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    private static JsonElement Fail(JsonElement response) => response.GetProperty("error");
}
