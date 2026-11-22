using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using M3scalinum.Text.Mcp;
using M3scalinum.Text.Templates;
using Xunit;

namespace M3scalinum.Text.Mcp.Tests;

/// <summary>
/// The template pack as MCP resources: one read-only URI per layout, and its manifest as the
/// contents.
/// </summary>
public sealed class ResourceTests
{
    private const string Uri = McpServer.TemplateResourcePrefix + TemplateIds.Invoice;

    private const string List = """{"jsonrpc":"2.0","id":1,"method":"resources/list"}""";

    [Fact]
    public async Task Resources_list_names_one_uri_per_layout_of_the_pack()
    {
        var uris = await ListedUrisAsync();

        Assert.Equal(
            new[]
            {
                TemplateIds.Invoice,
                TemplateIds.Quotation,
                TemplateIds.Statement,
                TemplateIds.Attestation,
                TemplateIds.Contract,
                TemplateIds.Payslip,
                TemplateIds.PurchaseOrder,
            },
            uris.Select(uri => uri.Split('/').Last()).ToArray());

        Assert.All(uris, uri => Assert.StartsWith(McpServer.TemplateResourcePrefix, uri, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_resource_read_answers_the_manifest_of_the_layout()
    {
        var contents = await ReadAsync(Uri);

        Assert.Equal(Uri, contents.GetProperty("uri").GetString());
        Assert.Equal("application/json", contents.GetProperty("mimeType").GetString());

        using var manifest = JsonDocument.Parse(contents.GetProperty("text").GetString()!);
        var root = manifest.RootElement;

        Assert.Equal(TemplateIds.Invoice, root.GetProperty("id").GetString());
        Assert.Equal("1.0.0", root.GetProperty("version").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("entry").GetString()));
        Assert.Contains("en", root.GetProperty("languages").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains("fr", root.GetProperty("languages").EnumerateArray().Select(value => value.GetString()));

        var labels = Assert.Single(
            root.GetProperty("parameters").EnumerateArray(),
            parameter => parameter.GetProperty("name").GetString() == "labels");

        Assert.Equal("object", labels.GetProperty("type").GetString());
        Assert.True(labels.GetProperty("required").GetBoolean());
    }

    [Fact]
    public async Task Every_uri_the_list_names_can_be_read()
    {
        foreach (var uri in await ListedUrisAsync())
        {
            var contents = await ReadAsync(uri);

            Assert.Equal(uri, contents.GetProperty("uri").GetString());
        }
    }

    [Fact]
    public async Task A_uri_the_pack_does_not_carry_is_refused()
    {
        var error = await ReadErrorAsync(McpServer.TemplateResourcePrefix + "delivery-note");

        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Contains("delivery-note", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_uri_of_another_scheme_is_refused_and_the_served_one_is_named()
    {
        var error = await ReadErrorAsync("file:///etc/passwd");

        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Contains(McpServer.TemplateResourcePrefix, error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_uri_is_invalid_params()
    {
        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames("""{"jsonrpc":"2.0","id":2,"method":"resources/read","params":{}}"""));

        Assert.Equal(-32602, responses[0].GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task A_layout_the_host_did_not_allow_is_neither_listed_nor_read()
    {
        var options = new McpServerOptions { AllowedTemplateIds = [TemplateIds.Quotation] };

        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames(
                List,
                ReadRequest(2, Uri),
                ReadRequest(3, McpServer.TemplateResourcePrefix + TemplateIds.Quotation)),
            options);

        var uris = responses[0].GetProperty("result").GetProperty("resources").EnumerateArray()
            .Select(resource => resource.GetProperty("uri").GetString())
            .ToArray();

        Assert.Equal(new[] { McpServer.TemplateResourcePrefix + TemplateIds.Quotation }, uris);
        Assert.Equal(-32602, responses[1].GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(
            "application/json",
            responses[2].GetProperty("result").GetProperty("contents")[0].GetProperty("mimeType").GetString());
    }

    [Fact]
    public async Task A_manifest_larger_than_the_document_ceiling_is_refused()
    {
        var options = new McpServerOptions { MaxDocumentSize = 128, MaxRequestSize = 256 };

        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames(ReadRequest(4, Uri)),
            options);

        var error = responses[0].GetProperty("error");

        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Contains("128", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Initialize_declares_the_resources_capability_as_read_only()
    {
        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames(TransportSuiteTests.Initialize));

        var capabilities = responses[0].GetProperty("result").GetProperty("capabilities");
        var resources = capabilities.GetProperty("resources");

        Assert.False(resources.GetProperty("subscribe").GetBoolean());
        Assert.False(resources.GetProperty("listChanged").GetBoolean());
    }

    private static string ReadRequest(int id, string uri)
        => $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"method\":\"resources/read\",\"params\":{{\"uri\":\"{uri}\"}}}}";

    private static async Task<string[]> ListedUrisAsync()
    {
        var responses = await McpTestClient.ExchangeAsync(McpTestClient.Frames(List));

        return
        [
            .. responses[0]
                .GetProperty("result")
                .GetProperty("resources")
                .EnumerateArray()
                .Select(resource => resource.GetProperty("uri").GetString()!),
        ];
    }

    private static async Task<JsonElement> ReadAsync(string uri)
    {
        var responses = await McpTestClient.ExchangeAsync(McpTestClient.Frames(ReadRequest(2, uri)));

        Assert.True(responses[0].TryGetProperty("result", out var result), responses[0].GetRawText());

        return result.GetProperty("contents")[0].Clone();
    }

    private static async Task<JsonElement> ReadErrorAsync(string uri)
    {
        var responses = await McpTestClient.ExchangeAsync(McpTestClient.Frames(ReadRequest(3, uri)));

        return responses[0].GetProperty("error");
    }
}
