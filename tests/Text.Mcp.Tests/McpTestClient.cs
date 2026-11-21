using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using M3scalinum.Text.Mcp;

namespace M3scalinum.Text.Mcp.Tests;

/// <summary>
/// Drives a server over a pair of in-memory streams with real newline-delimited JSON-RPC frames.
/// </summary>
/// <remarks>
/// The whole request stream is written before the server runs, and the whole response stream is read
/// after it returns: with sequential framing and a sequential loop this is the same exchange a pipe
/// carries, without a scheduler deciding whether a test passes. Running to completion also asserts
/// the shutdown contract — end of input ends <c>RunAsync</c>, it does not hang.
/// </remarks>
internal static class McpTestClient
{
    /// <summary>Joins frames with the newline separator the transport uses.</summary>
    internal static string Frames(params string[] frames)
        => string.Join("\n", frames) + "\n";

    /// <summary>Sends the frames and returns the responses, parsed and detached from their documents.</summary>
    internal static async Task<IReadOnlyList<JsonElement>> ExchangeAsync(
        string frames,
        McpServerOptions? options = null)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(frames));
        using var output = new MemoryStream();

        await new McpServer(options).RunAsync(input, output);

        var text = Encoding.UTF8.GetString(output.ToArray());
        var messages = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .ToArray();

        if (messages.Length == 0)
        {
            return [];
        }

        // Parsed as one array, so a response that is not a JSON object fails here rather than in
        // every test that reaches into it.
        using var document = JsonDocument.Parse("[" + string.Join(",", messages) + "]");

        return document.RootElement.EnumerateArray().Select(element => element.Clone()).ToArray();
    }

    /// <summary>Builds a <c>tools/call</c> frame from a tool name and a raw JSON argument object.</summary>
    internal static string ToolCall(int id, string name, string arguments)
        => "{\"jsonrpc\":\"2.0\",\"id\":"
            + id.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ",\"method\":\"tools/call\",\"params\":{\"name\":\""
            + name
            + "\",\"arguments\":"
            + arguments
            + "}}";

    /// <summary>The base64 PDF a successful render returned, decoded.</summary>
    internal static byte[] RenderedPdf(JsonElement response, out JsonElement summary)
    {
        if (!response.TryGetProperty("result", out var result))
        {
            throw new InvalidOperationException($"the call failed: {response.GetRawText()}");
        }

        foreach (var block in result.GetProperty("content").EnumerateArray())
        {
            if (block.GetProperty("type").GetString() == "resource")
            {
                summary = result.GetProperty("content")[0];
                return Convert.FromBase64String(block.GetProperty("resource").GetProperty("blob").GetString()!);
            }
        }

        throw new InvalidOperationException(
            $"the response carries no embedded resource: {response.GetRawText()}");
    }
}
