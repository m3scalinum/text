using System.Text;
using System.Text.Json;
using M3scalinum.Text.Mcp;

// Drives the MCP server in process: initialize, tools/list, then a real tools/call. The server is
// the package's, the layouts it lists come from its Templates dependency, and the compiler is only
// touched by render_document, which this example does not call.

var server = new McpServer();

var initialize = await AskAsync("""
    {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"samples","version":"1.0.0"}}}
    """);

Console.WriteLine("M3scalinum.Text.Mcp");
Console.WriteLine($"  initialize   {initialize.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString()}");

var tools = await AskAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
var names = tools.GetProperty("result").GetProperty("tools")
    .EnumerateArray()
    .Select(tool => tool.GetProperty("name").GetString()!)
    .ToList();

Console.WriteLine($"  tools/list   {names.Count}: {string.Join(", ", names)}");

if (names.Count != 5
    || !names.Contains(McpToolNames.RenderDocument)
    || !names.Contains(McpToolNames.ExtractText))
{
    throw new InvalidOperationException($"the tool list is wrong: {string.Join(", ", names)}");
}

var call = await AskAsync(
    "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\""
    + McpToolNames.ListTemplates + "\",\"arguments\":{}}}");

var result = call.GetProperty("result");

if (result.TryGetProperty("isError", out var isError) && isError.ValueKind == JsonValueKind.True)
{
    throw new InvalidOperationException("list_templates answered with an error");
}

var content = result.GetProperty("content").EnumerateArray().First().GetProperty("text").GetString()!;
var layoutCount = JsonDocument.Parse(content).RootElement.GetProperty("templates").GetArrayLength();

Console.WriteLine($"  tools/call   list_templates returned {layoutCount} layouts");

if (layoutCount == 0)
{
    throw new InvalidOperationException("the server listed no template");
}

Console.WriteLine();
Console.WriteLine("M3scalinum.Text.Mcp: the server, its protocol surface and its layout pack ran from the package.");

async Task<JsonElement> AskAsync(string message)
{
    var response = await server.HandleMessageAsync(message, CancellationToken.None)
        ?? throw new InvalidOperationException($"the server did not answer: {message}");

    var json = JsonDocument.Parse(Encoding.UTF8.GetString(response)).RootElement.Clone();

    if (json.TryGetProperty("error", out var error))
    {
        throw new InvalidOperationException($"the server refused the message: {error}");
    }

    return json;
}
