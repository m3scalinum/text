using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using M3scalinum.Text.Mcp;
using M3scalinum.Text.Mcp.Internal;
using M3scalinum.Text.Pdf.FactureX;
using M3scalinum.Text.SharpTypst.Templates;
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
    public async Task Render_invoice_without_a_payload_is_refused()
    {
        var responses = await McpTestClient.ExchangeAsync(McpTestClient.Frames(
            McpTestClient.ToolCall(1, McpToolNames.RenderInvoice, "{}")));

        var error = Error(responses);

        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Contains("'invoice'", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Render_invoice_with_a_payload_that_is_not_an_object_is_refused()
    {
        var responses = await McpTestClient.ExchangeAsync(McpTestClient.Frames(
            McpTestClient.ToolCall(1, McpToolNames.RenderInvoice, """{"invoice":"FA-1"}""")));

        var error = Error(responses);

        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Render_invoice_with_an_unreadable_date_is_refused()
    {
        var responses = await McpTestClient.ExchangeAsync(McpTestClient.Frames(
            McpTestClient.ToolCall(
                1,
                McpToolNames.RenderInvoice,
                """{"invoice":{"number":"FA-1","issueDate":"2026-13-45"}}""")));

        var error = Error(responses);

        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Contains("invoice.issueDate", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Render_invoice_with_an_unknown_profile_is_refused()
    {
        var responses = await McpTestClient.ExchangeAsync(McpTestClient.Frames(
            McpTestClient.ToolCall(
                1,
                McpToolNames.RenderInvoice,
                """{"invoice":{"number":"FA-1","profile":"ultra","issueDate":"2026-02-01"}}""")));

        var error = Error(responses);

        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Contains("ultra", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_jsonrpc_message_that_is_not_an_object_is_refused()
    {
        var responses = await McpTestClient.ExchangeAsync(McpTestClient.Frames("42"));

        var error = Error(responses);

        Assert.Equal(-32600, error.GetProperty("code").GetInt32());
        Assert.Equal(JsonValueKind.Null, responses[0].GetProperty("id").ValueKind);
    }

    [Theory]
    [InlineData("minimum", FacturXProfile.Minimum)]
    [InlineData("MINIMUM", FacturXProfile.Minimum)]
    [InlineData("basicwl", FacturXProfile.BasicWithoutLines)]
    [InlineData("basic-without-lines", FacturXProfile.BasicWithoutLines)]
    [InlineData("basic", FacturXProfile.Basic)]
    [InlineData("en16931", FacturXProfile.En16931)]
    [InlineData("comfort", FacturXProfile.En16931)]
    [InlineData("extended", FacturXProfile.Extended)]
    [InlineData("extended-ctc-fr", FacturXProfile.ExtendedCtcFr)]
    public void Every_profile_spelling_maps_to_its_conformance_level(
        string spelling,
        FacturXProfile expected)
    {
        var invoice = Map($$"""{"number":"FA-1","issueDate":"2026-02-01","profile":"{{spelling}}"}""");

        Assert.Equal(expected, invoice.Profile);
    }

    [Fact]
    public void An_absent_profile_type_and_currency_take_the_documented_defaults()
    {
        var invoice = Map("""{"number":"FA-1","issueDate":"2026-02-01"}""");

        Assert.Equal(FacturXProfile.En16931, invoice.Profile);
        Assert.Equal("EUR", invoice.Currency);
        Assert.Equal("380", invoice.TypeCode);
    }

    [Fact]
    public void Every_optional_term_of_the_payload_reaches_the_model()
    {
        var invoice = Map(
            """
            {
              "number": "FA-2026-0042",
              "typeCode": "384",
              "profile": "extended",
              "currency": "CHF",
              "taxCurrency": "EUR",
              "issueDate": "2026-03-01",
              "dueDate": "2026-03-31",
              "period": { "start": "2026-02-01", "end": "2026-02-28" },
              "buyerReference": "SE-42",
              "businessProcess": "A1",
              "orderReference": "PO-1",
              "contractReference": "CT-1",
              "projectReference": "PR-1",
              "note": "Livraison en une fois.",
              "notes": [{ "content": "Pénalités de retard.", "subjectCode": "PMD" }],
              "paymentTerms": "Net 30",
              "paymentMeansCode": "30",
              "paymentReference": "RF18",
              "iban": "FR7630006000011234567890189",
              "bic": "AGRIFRPP",
              "seller": { "name": "ACME SAS", "vatId": "FR12345678901", "country": "FR", "email": "a@b.example" },
              "buyer": { "name": "Client SARL", "country": "FR" },
              "deliveryAddress": { "name": "Dépôt", "line2": "Quai 4", "country": "FR" },
              "lines": [
                {
                  "id": "1",
                  "name": "Audit",
                  "sellerAssignedId": "AUD-1",
                  "quantity": 2,
                  "baseQuantity": 1,
                  "unitCode": "C62",
                  "unitPrice": 50.5,
                  "lineNetAmount": 101,
                  "taxCategory": "S",
                  "taxRate": 20
                }
              ],
              "allowancesCharges": [
                { "isCharge": true, "amount": 10, "reason": "Port", "vatCategory": "S", "vatRate": 20 }
              ],
              "taxBreakdown": [
                { "category": "S", "rate": 20, "basis": 101, "amount": 20.2 }
              ],
              "totals": {
                "lineTotal": 101,
                "chargeTotal": 10,
                "taxBasisTotal": 111,
                "taxTotal": 20.2,
                "grandTotal": 131.2,
                "prepaid": 0,
                "due": 131.2
              }
            }
            """);

        Assert.Equal("FA-2026-0042", invoice.Number);
        Assert.Equal("384", invoice.TypeCode);
        Assert.Equal("CHF", invoice.Currency);
        Assert.Equal("EUR", invoice.TaxCurrency);
        Assert.Equal(new DateOnly(2026, 2, 1), invoice.Period!.Start);
        Assert.Equal("SE-42", invoice.BuyerReference);
        Assert.Equal("PO-1", invoice.OrderReference);
        Assert.Equal("CT-1", invoice.ContractReference);
        Assert.Equal("PR-1", invoice.ProjectReference);
        Assert.Single(invoice.Notes);
        Assert.Equal("PMD", invoice.Notes[0].SubjectCode);
        Assert.Equal("RF18", invoice.PaymentReference);
        Assert.Equal("AGRIFRPP", invoice.Bic);
        Assert.Equal("a@b.example", invoice.Seller.Email);
        Assert.Equal("Dépôt", invoice.DeliveryAddress!.Name);
        Assert.Equal(1m, invoice.Lines[0].BaseQuantity);
        Assert.Single(invoice.AllowancesCharges);
        Assert.True(invoice.AllowancesCharges[0].IsCharge);
        Assert.Equal(20.2m, invoice.Totals.TaxTotal);
        Assert.Equal(131.2m, invoice.Totals.Due);
    }

    /// <summary>Maps a payload exactly as the tool does, without rendering.</summary>
    private static FacturXInvoice Map(string json)
    {
        using var document = JsonDocument.Parse(json);

        return InvoicePayload.Read(document.RootElement);
    }
}
