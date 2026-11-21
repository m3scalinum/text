using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using M3scalinum.Text.Mcp;
using M3scalinum.Text.Mcp.Internal;
using M3scalinum.Text.Templates;
using Xunit;

namespace M3scalinum.Text.Mcp.Tests;

/// <summary>
/// The argument surface below the happy path: every refusal a client can provoke without a
/// compiler, and the limits a host sets.
/// </summary>
/// <remarks>
/// These matter as much as the successful calls: a model that sends the wrong shape must be told
/// what is wrong, and must not be able to make the server read a file, render a layout the host
/// withheld, or spend unbounded memory.
/// </remarks>
public sealed class ArgumentTests
{
    private static Task<IReadOnlyList<JsonElement>> CallAsync(
        string arguments,
        McpServerOptions? options = null)
        => McpTestClient.ExchangeAsync(
            McpTestClient.Frames(McpTestClient.ToolCall(1, McpToolNames.RenderDocument, arguments)),
            options);

    private static JsonElement Error(IReadOnlyList<JsonElement> responses)
        => Assert.Single(responses).GetProperty("error");

    [Fact]
    public async Task A_tools_call_without_params_is_an_invalid_params_error()
    {
        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames("""{"jsonrpc":"2.0","id":1,"method":"tools/call"}"""));

        var error = Error(responses);

        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Contains("'tools/call'", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tools_call_without_a_tool_name_is_an_invalid_params_error()
    {
        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"arguments":{}}}"""));

        var error = Error(responses);

        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Contains("tool name", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Parameters_that_are_not_an_object_are_refused()
    {
        var error = Error(await CallAsync("""{"template":"invoice","parameters":[1,2]}"""));

        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Contains("'parameters'", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_parameter_the_layout_does_not_declare_is_refused()
    {
        var error = Error(await CallAsync(
            """{"template":"invoice","parameters":{"discount":0.2}}"""));

        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Contains("no parameter 'discount'", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"issued":"not-a-date"}""", "yyyy-MM-dd")]
    [InlineData("""{"items":"not-an-array"}""", "an array")]
    [InlineData("""{"tax_rate":"twenty"}""", "a number")]
    [InlineData("""{"title":42}""", "a string")]
    public async Task A_parameter_of_the_wrong_shape_is_refused_with_the_declared_type(
        string parameters,
        string expected)
    {
        var error = Error(await CallAsync($$"""{"template":"invoice","parameters":{{parameters}}}"""));

        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Contains(expected, error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_item_of_the_wrong_shape_in_an_array_parameter_is_refused()
    {
        var error = Error(await CallAsync(
            """{"template":"invoice","parameters":{"items":[7]}}"""));

        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Contains("'items'", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_version_the_pack_does_not_carry_is_refused()
    {
        var error = Error(await CallAsync(
            """{"template":"invoice","version":"9.9.9","parameters":{}}"""));

        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Contains("9.9.9", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_source_larger_than_the_document_ceiling_is_refused()
    {
        var source = new string('a', 200);

        var error = Error(await CallAsync(
            $$"""{"source":"{{source}}"}""",
            new McpServerOptions { MaxDocumentSize = 100 }));

        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Contains("accepts at most 100", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_empty_output_path_is_refused()
    {
        var error = Error(await CallAsync("""{"source":"= Hi","outputPath":""}"""));

        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Contains("must not be empty", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_templates_only_lists_the_ids_the_host_allowed()
    {
        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames(McpTestClient.ToolCall(1, McpToolNames.ListTemplates, "{}")),
            new McpServerOptions { AllowedTemplateIds = [TemplateIds.Quotation] });

        var text = Assert.Single(responses)
            .GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();

        using var listing = JsonDocument.Parse(text!);
        var ids = listing.RootElement.GetProperty("templates").EnumerateArray()
            .Select(template => template.GetProperty("id").GetString())
            .ToArray();

        Assert.Equal(new[] { TemplateIds.Quotation }, ids);
    }

    [Fact]
    public async Task A_jsonrpc_message_that_is_not_an_object_is_refused()
    {
        var responses = await McpTestClient.ExchangeAsync(McpTestClient.Frames("42"));

        var error = Error(responses);

        Assert.Equal(-32600, error.GetProperty("code").GetInt32());
        Assert.Equal(JsonValueKind.Null, responses[0].GetProperty("id").ValueKind);
    }
}
