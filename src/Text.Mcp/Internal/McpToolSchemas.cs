using System.Text.Json;

namespace M3scalinum.Text.Mcp.Internal;

/// <summary>
/// The JSON Schema of each tool's arguments, as the MCP <c>tools/list</c> result carries it.
/// </summary>
/// <remarks>
/// Written as JSON text and parsed once rather than built with <c>Utf8JsonWriter</c>: the schemas are
/// the part of this server a model reads before calling anything, so they should read as schemas.
/// </remarks>
internal static class McpToolSchemas
{
    /// <summary>Arguments of <c>render_document</c>: a source, or a layout and its parameters.</summary>
    internal static readonly JsonElement RenderDocument = Parse(
        """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "properties": {
            "source": {
              "type": "string",
              "description": "Typst source to compile. Mutually exclusive with template."
            },
            "template": {
              "type": "string",
              "description": "Id of a layout returned by list_templates."
            },
            "version": {
              "type": "string",
              "description": "Exact version of the layout. The newest version is used when omitted."
            },
            "parameters": {
              "type": "object",
              "description": "Values of the layout's declared parameters, keyed by name. Ignored with source.",
              "additionalProperties": true
            },
            "outputPath": {
              "type": "string",
              "description": "Absolute path to write the PDF to instead of returning it. Refused unless the host enabled file output."
            }
          },
          "oneOf": [
            { "required": ["source"] },
            { "required": ["template"] }
          ],
          "additionalProperties": false
        }
        """);

    /// <summary>Arguments of <c>render_invoice</c>: one Factur-X invoice, as JSON.</summary>
    internal static readonly JsonElement RenderInvoice = Parse(
        """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "properties": {
            "invoice": {
              "type": "object",
              "description": "The invoice: number, issueDate, currency, profile, seller, buyer, lines, taxBreakdown and totals, camelCase, dates as yyyy-MM-dd, money as JSON numbers.",
              "additionalProperties": true
            },
            "outputPath": {
              "type": "string",
              "description": "Absolute path to write the PDF to instead of returning it. Refused unless the host enabled file output."
            }
          },
          "required": ["invoice"],
          "additionalProperties": false
        }
        """);

    /// <summary>Arguments of <c>list_templates</c>: none.</summary>
    internal static readonly JsonElement ListTemplates = Parse(
        """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "properties": {},
          "additionalProperties": false
        }
        """);

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
