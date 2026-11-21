using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using M3scalinum.Text.Mcp;
using M3scalinum.Text.Templates;
using Xunit;

namespace M3scalinum.Text.Mcp.Tests;

/// <summary>
/// The tools that compile: a document from source, a document from a layout, and an invoice.
/// </summary>
/// <remarks>
/// These need the real Typst compiler, which the test host supplies through
/// <c>SHARPTYPST_NATIVE_PATH</c>. Without it the test is skipped with the path to set rather than
/// failing: the protocol tests above run on a machine with no native library at all, and a developer
/// who only touches the JSON-RPC layer should not need a Rust build to see them pass.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class RenderTests
{
    private const string SkipReason =
        "set SHARPTYPST_NATIVE_PATH to native/sharp-typst/target/release/sharp_typst.dll "
        + "(or libsharp_typst.so) to run the render tests";

    private const string Seller =
        """{"name":"ACME SAS","address":["12 rue des Glyphes"],"postcode":"75011","city":"Paris","country":"FR","legal":"SIREN 123 456 789","tax_id":"FR12345678901"}""";

    private const string Customer =
        """{"name":"Client SARL","address":["5 avenue du Test"],"postcode":"69003","city":"Lyon","email":"compta@client.example"}""";

    private static bool NativeAvailable
        => Environment.GetEnvironmentVariable("SHARPTYPST_NATIVE_PATH") is { Length: > 0 } path
            && File.Exists(path);

    [Fact]
    public async Task Render_document_returns_a_pdf_as_an_embedded_resource()
    {
        Assert.SkipUnless(NativeAvailable, SkipReason);

        var responses = await McpTestClient.ExchangeAsync(McpTestClient.Frames(McpTestClient.ToolCall(
            1,
            McpToolNames.RenderDocument,
            """{"source":"#set page(paper: \"a4\")\n= Hello\nThis is *Typst*, rendered by the MCP server."}""")));

        var response = Assert.Single(responses);
        var pdf = McpTestClient.RenderedPdf(response, out var summary);

        Assert.Equal("%PDF"u8.ToArray(), pdf[..4]);
        Assert.True(pdf.Length > 1000, $"the PDF is only {pdf.Length} bytes");

        var block = response.GetProperty("result").GetProperty("content")[1].GetProperty("resource");
        Assert.Equal("application/pdf", block.GetProperty("mimeType").GetString());

        using var metadata = JsonDocument.Parse(summary.GetProperty("text").GetString()!);
        Assert.Equal(pdf.Length, metadata.RootElement.GetProperty("byteLength").GetInt32());
        Assert.Equal(1, metadata.RootElement.GetProperty("pageCount").GetInt32());
    }

    [Fact]
    public async Task Render_document_with_a_template_renders_the_layout()
    {
        Assert.SkipUnless(NativeAvailable, SkipReason);

        var responses = await McpTestClient.ExchangeAsync(
            McpTestClient.Frames(McpTestClient.ToolCall(2, McpToolNames.RenderDocument, TemplateArguments())));

        var pdf = McpTestClient.RenderedPdf(Assert.Single(responses), out _);

        Assert.Equal("%PDF"u8.ToArray(), pdf[..4]);
    }

    [Fact]
    public async Task Render_document_writes_the_pdf_when_file_output_is_enabled()
    {
        Assert.SkipUnless(NativeAvailable, SkipReason);

        var path = Path.Combine(Path.GetTempPath(), $"m3scalinum-mcp-{Guid.NewGuid():N}.pdf");

        try
        {
            var responses = await McpTestClient.ExchangeAsync(
                McpTestClient.Frames(McpTestClient.ToolCall(
                    5,
                    McpToolNames.RenderDocument,
                    $$"""{"source":"= Written","outputPath":"{{Escape(path)}}"}""")),
                new McpServerOptions { AllowFileOutput = true });

            var result = Assert.Single(responses).GetProperty("result");

            Assert.False(result.TryGetProperty("isError", out _));
            Assert.True(File.Exists(path));

            using var metadata = JsonDocument.Parse(result.GetProperty("content")[0].GetProperty("text").GetString()!);
            Assert.Equal(path, metadata.RootElement.GetProperty("path").GetString());
            Assert.Equal("%PDF"u8.ToArray(), File.ReadAllBytes(path)[..4]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The <c>render_document</c> arguments for the invoice layout: required values, plus the French
    /// label set the layout prints.
    /// </summary>
    private static string TemplateArguments()
    {
        var labels = string.Join(
            ",",
            TemplateLabels.For(TemplateIds.Invoice, TemplateLanguage.French)
                .Select(pair => $"\"{pair.Key}\":\"{Escape(pair.Value?.ToString() ?? string.Empty)}\""));

        var items =
            """[{"description":"Audit technique","quantity":1,"unit_price":950.00}]""";

        return string.Concat(
            """{"template":"invoice","parameters":{"title":"Facture 2026-0042","number":"FA-2026-0042","issued":"2026-03-01","due":"2026-03-31","seller":""",
            Seller,
            ""","customer":""",
            Customer,
            ""","items":""",
            items,
            ""","labels":{""",
            labels,
            "}}}");
    }

    /// <summary>One line, because the transport frames one message per line.</summary>
    private static string Compact(string json)
        => json.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    private static string Escape(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
