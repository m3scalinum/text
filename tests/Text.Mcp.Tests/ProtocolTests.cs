using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using M3scalinum.Text.Mcp;
using M3scalinum.Text.SharpTypst.Templates;
using Xunit;

namespace M3scalinum.Text.Mcp.Tests;

/// <summary>
/// The protocol surface: lifecycle, discovery, dispatch, and the JSON-RPC errors a client must be
/// able to rely on. None of these tests needs the native compiler — they stop before any tool that
/// compiles, or fail on arguments alone.
/// </summary>
public sealed class ProtocolTests
{
    private const string Initialize =
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"tests","version":"1.0.0"}}}""";

    [Fact]
    public async Task Initialize_reports_the_protocol_revision_capabilities_and_server_info()
    {
        var responses = await McpTestClient.ExchangeAsync(McpTestClient.Frames(Initialize));

        var result = Assert.Single(responses).GetProperty("result");

        Assert.Equal("2025-06-18", result.GetProperty("protocolVersion").GetString());
        Assert.False(result.GetProperty("capabilities").GetProperty("tools").GetProperty("listChanged").GetBoolean());
        Assert.Equal("M3scalinum.Text.Mcp", result.GetProperty("serverInfo").GetProperty("name").GetString());
        Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("serverInfo").GetProperty("version").GetString()));
    }

    [Fact]
    public async Task A_revision_the_server_knows_is_echoed_back()
    {
        const string Frame =
            """{"jsonrpc":"2.0","id":"init","method":"initialize","params":{"protocolVersion":"2024-11-05"}}""";

        var responses = await McpTestClient.ExchangeAsync(McpTestClient.Frames(Frame));

        var result = Assert.Single(responses).GetProperty("result");

        Assert.Equal("2024-11-05", result.GetProperty("protocolVersion").GetString());
    }

    [Fact]
    public async Task An_unknown_revision_is_answered_with_the_one_the_server_speaks()
    {
        const string Frame =
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"1999-01-01"}}""";

        var responses = await McpTestClient.ExchangeAsync(McpTestClient.Frames(Frame));

        var result = Assert.Single(responses).GetProperty("result");

        Assert.Equal(McpServer.ProtocolVersion, result.GetProperty("protocolVersion").GetString());
    }

    [Fact]
    public async Task Ping_returns_an_empty_result()
    {
        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames("""{"jsonrpc":"2.0","id":7,"method":"ping"}"""));

        var response = Assert.Single(responses);

        Assert.Equal(7, response.GetProperty("id").GetInt32());
        Assert.Equal(JsonValueKind.Object, response.GetProperty("result").ValueKind);
        Assert.Empty(response.GetProperty("result").EnumerateObject());
    }

    [Fact]
    public async Task Tools_list_exposes_the_six_tools_with_their_schemas()
    {
        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames(Initialize, """{"jsonrpc":"2.0","id":2,"method":"tools/list"}"""));

        var tools = responses[1].GetProperty("result").GetProperty("tools").EnumerateArray().ToArray();

        Assert.Equal(
            new[]
            {
                McpToolNames.RenderDocument,
                McpToolNames.RenderInvoice,
                McpToolNames.ListTemplates,
                McpToolNames.SignDocument,
                McpToolNames.MergeDocuments,
                McpToolNames.ExtractText,
            },
            tools.Select(tool => tool.GetProperty("name").GetString()).ToArray());

        foreach (var tool in tools)
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.GetProperty("description").GetString()));
            Assert.Equal("object", tool.GetProperty("inputSchema").GetProperty("type").GetString());
        }

        var document = tools[0].GetProperty("inputSchema");
        Assert.True(document.GetProperty("properties").TryGetProperty("source", out _));
        Assert.True(document.GetProperty("properties").TryGetProperty("template", out _));
        Assert.True(document.GetProperty("properties").TryGetProperty("outputPath", out _));
    }

    [Fact]
    public async Task List_templates_returns_the_ids_versions_and_parameters_of_the_pack()
    {
        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames(McpTestClient.ToolCall(3, McpToolNames.ListTemplates, "{}")));

        var response = Assert.Single(responses);
        var content = response.GetProperty("result").GetProperty("content");

        Assert.False(response.GetProperty("result").TryGetProperty("isError", out _));

        using var listing = JsonDocument.Parse(content[0].GetProperty("text").GetString()!);
        var templates = listing.RootElement.GetProperty("templates").EnumerateArray().ToArray();

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
            templates.Select(template => template.GetProperty("id").GetString()).ToArray());

        var invoice = templates[0];
        Assert.Equal("1.0.0", invoice.GetProperty("version").GetString());
        Assert.Contains("en", invoice.GetProperty("languages").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains("fr", invoice.GetProperty("languages").EnumerateArray().Select(value => value.GetString()));

        var parameters = invoice.GetProperty("parameters").EnumerateArray().ToArray();
        var labels = Assert.Single(
            parameters,
            parameter => parameter.GetProperty("name").GetString() == "labels");

        Assert.Equal("object", labels.GetProperty("type").GetString());
        Assert.True(labels.GetProperty("required").GetBoolean());
        Assert.Contains(parameters, parameter => parameter.GetProperty("name").GetString() == "items");
    }

    [Fact]
    public async Task An_unknown_method_is_a_method_not_found_error()
    {
        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames("""{"jsonrpc":"2.0","id":"abc","method":"resources/list"}"""));

        var error = Assert.Single(responses).GetProperty("error");

        Assert.Equal(-32601, error.GetProperty("code").GetInt32());
        Assert.Contains("resources/list", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal("abc", responses[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task An_unknown_tool_is_an_invalid_params_error()
    {
        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames(McpTestClient.ToolCall(4, "delete_everything", "{}")));

        var error = Assert.Single(responses).GetProperty("error");

        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Contains("delete_everything", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Malformed_json_is_a_parse_error_with_a_null_id()
    {
        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":", """{"jsonrpc":"2.0","id":2,"method":"ping"}"""));

        Assert.Equal(2, responses.Count);
        Assert.Equal(JsonValueKind.Null, responses[0].GetProperty("id").ValueKind);
        Assert.Equal(-32700, responses[0].GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(JsonValueKind.Object, responses[1].GetProperty("result").ValueKind);
    }

    [Fact]
    public async Task A_tools_call_with_neither_source_nor_template_is_refused()
    {
        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames(McpTestClient.ToolCall(5, McpToolNames.RenderDocument, "{}")));

        var error = Assert.Single(responses).GetProperty("error");

        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Contains("source", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tools_call_with_both_source_and_template_is_refused()
    {
        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames(McpTestClient.ToolCall(
                6, McpToolNames.RenderDocument, """{"source":"= Hi","template":"invoice","parameters":{}}""")));

        var error = Assert.Single(responses).GetProperty("error");

        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Contains("exactly one", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_argument_of_the_wrong_type_is_refused()
    {
        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames(McpTestClient.ToolCall(
                7, McpToolNames.RenderDocument, """{"source":42}""")));

        var error = Assert.Single(responses).GetProperty("error");

        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Contains("'source'", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_template_the_host_did_not_allow_is_refused()
    {
        var options = new McpServerOptions { AllowedTemplateIds = [TemplateIds.Quotation] };

        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames(McpTestClient.ToolCall(
                8, McpToolNames.RenderDocument, """{"template":"invoice","parameters":{}}""")),
            options);

        var error = Assert.Single(responses).GetProperty("error");

        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Contains("not served", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_template_id_the_pack_does_not_carry_is_refused()
    {
        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames(McpTestClient.ToolCall(
                9, McpToolNames.RenderDocument, """{"template":"payslip","parameters":{}}""")));

        var error = Assert.Single(responses).GetProperty("error");

        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Contains("payslip", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_required_template_parameter_is_refused_before_compiling()
    {
        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames(McpTestClient.ToolCall(
                10, McpToolNames.RenderDocument, """{"template":"invoice","parameters":{}}""")));

        var error = Assert.Single(responses).GetProperty("error");

        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Contains("requires parameter", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task File_output_is_refused_when_the_host_disabled_it()
    {
        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames(McpTestClient.ToolCall(
                11,
                McpToolNames.RenderDocument,
                """{"source":"= Hi","outputPath":"out.pdf"}""")));

        var error = Assert.Single(responses).GetProperty("error");

        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Contains("does not write files", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_notification_is_not_answered()
    {
        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames(
                Initialize,
                """{"jsonrpc":"2.0","method":"notifications/initialized"}""",
                """{"jsonrpc":"2.0","id":2,"method":"tools/list"}"""));

        Assert.Equal(2, responses.Count);
        Assert.Equal(1, responses[0].GetProperty("id").GetInt32());
        Assert.Equal(2, responses[1].GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task A_batch_request_is_refused()
    {
        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames("""[{"jsonrpc":"2.0","id":1,"method":"ping"}]"""));

        var error = Assert.Single(responses).GetProperty("error");

        Assert.Equal(-32600, error.GetProperty("code").GetInt32());
        Assert.Contains("batch", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_message_with_another_jsonrpc_version_is_refused()
    {
        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames("""{"jsonrpc":"1.0","id":1,"method":"ping"}"""));

        var error = Assert.Single(responses).GetProperty("error");

        Assert.Equal(-32600, error.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task A_message_over_the_limit_is_refused_and_the_session_continues()
    {
        var options = new McpServerOptions { MaxDocumentSize = 128, MaxRequestSize = 128 };
        var oversized = new string('x', 512);

        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames(oversized, """{"jsonrpc":"2.0","id":2,"method":"ping"}"""),
            options);

        Assert.Equal(2, responses.Count);
        Assert.Equal(-32700, responses[0].GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(2, responses[1].GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task The_server_stops_at_the_end_of_input()
    {
        var responses = await McpTestClient.ExchangeAsync(string.Empty);

        Assert.Empty(responses);
    }

    [Fact]
    public async Task A_last_message_without_a_trailing_newline_is_still_answered()
    {
        var responses = await McpTestClient.ExchangeAsync("""{"jsonrpc":"2.0","id":1,"method":"ping"}""");

        Assert.Single(responses);
    }

    [Fact]
    public void An_allowed_template_id_the_pack_does_not_carry_fails_at_construction()
    {
        Assert.Throws<ArgumentException>(() => new McpServer(
            new McpServerOptions { AllowedTemplateIds = ["delivery-note"] }));
    }

    [Fact]
    public void A_document_ceiling_above_the_message_ceiling_fails_at_construction()
    {
        Assert.Throws<ArgumentException>(() => new McpServer(
            new McpServerOptions { MaxDocumentSize = 4096, MaxRequestSize = 1024 }));
    }
}
