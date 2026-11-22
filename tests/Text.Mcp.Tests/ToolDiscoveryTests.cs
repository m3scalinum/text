using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using M3scalinum.Text.Mcp;
using Xunit;

namespace M3scalinum.Text.Mcp.Tests;

/// <summary>
/// Tool discovery agrees with dispatch: what <c>tools/list</c> announces is exactly what
/// <c>tools/call</c> accepts.
/// </summary>
/// <remarks>
/// The two are the same table, and these tests compare the two sets through the protocol rather than
/// through the type, so a refactor that reintroduces a hand-written list fails here.
/// </remarks>
public sealed class ToolDiscoveryTests
{
    [Fact]
    public async Task Every_tool_the_list_announces_is_accepted_by_tools_call()
    {
        var announced = await AnnouncedToolsAsync();

        Assert.NotEmpty(announced);

        var accepted = new List<string>();

        foreach (var name in announced)
        {
            // An empty argument object: a tool that needs more refuses on its arguments, which is
            // still an answer to a tool that exists. A tool that does not exist is a different
            // message, and it is the one this test rules out.
            var response = await CallAsync(name);

            if (!response.TryGetProperty("result", out _))
            {
                var message = response.GetProperty("error").GetProperty("message").GetString() ?? string.Empty;

                Assert.DoesNotContain("unknown tool", message, StringComparison.Ordinal);
            }

            accepted.Add(name);
        }

        Assert.Equal(announced, accepted);
    }

    [Fact]
    public async Task A_tool_the_list_does_not_announce_is_refused()
    {
        var response = await CallAsync("delete_everything");

        var error = response.GetProperty("error");

        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Contains("delete_everything", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_unknown_tool_message_names_every_tool_the_list_announces()
    {
        var announced = await AnnouncedToolsAsync();
        var response = await CallAsync("delete_everything");
        var message = response.GetProperty("error").GetProperty("message").GetString()!;

        foreach (var name in announced)
        {
            Assert.Contains(name, message, StringComparison.Ordinal);
        }
    }

    private static async Task<string[]> AnnouncedToolsAsync()
    {
        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}"""));

        return
        [
            .. responses[0]
                .GetProperty("result")
                .GetProperty("tools")
                .EnumerateArray()
                .Select(tool => tool.GetProperty("name").GetString()!),
        ];
    }

    private static async Task<JsonElement> CallAsync(string name)
    {
        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames(McpTestClient.ToolCall(2, name, "{}")));

        return responses[0];
    }
}
