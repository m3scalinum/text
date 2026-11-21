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

    /// <summary>Arguments of <c>sign_document</c>: one PDF, one PKCS#12 certificate and its password.</summary>
    internal static readonly JsonElement SignDocument = Parse(
        """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "properties": {
            "pdf": {
              "type": "string",
              "description": "The PDF to sign, base64-encoded. Mutually exclusive with pdfPath."
            },
            "pdfPath": {
              "type": "string",
              "description": "Absolute path of the PDF to sign. Refused unless the host enabled file output."
            },
            "certificate": {
              "type": "string",
              "description": "The PKCS#12 (.p12/.pfx) certificate with its private key, base64-encoded. Mutually exclusive with certificatePath."
            },
            "certificatePath": {
              "type": "string",
              "description": "Absolute path of the PKCS#12 certificate. Refused unless the host enabled file output."
            },
            "password": {
              "type": "string",
              "description": "Password of the PKCS#12 file; an empty string when it carries none."
            },
            "reason": {
              "type": "string",
              "description": "Why the document is signed, as the signature declares it."
            },
            "location": {
              "type": "string",
              "description": "Where the signature was made."
            },
            "outputPath": {
              "type": "string",
              "description": "Absolute path to write the signed PDF to instead of returning it. Refused unless the host enabled file output."
            }
          },
          "required": ["password"],
          "oneOf": [
            { "required": ["pdf"] },
            { "required": ["pdfPath"] }
          ],
          "additionalProperties": false
        }
        """);

    /// <summary>Arguments of <c>merge_documents</c>: the PDFs to merge, in order.</summary>
    internal static readonly JsonElement MergeDocuments = Parse(
        """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "properties": {
            "documents": {
              "type": "array",
              "items": { "type": "string" },
              "minItems": 2,
              "description": "The PDFs to merge, base64-encoded, in the order they are to appear. Mutually exclusive with documentPaths."
            },
            "documentPaths": {
              "type": "array",
              "items": { "type": "string" },
              "minItems": 2,
              "description": "Absolute paths of the PDFs to merge, in order. Refused unless the host enabled file output."
            },
            "outputPath": {
              "type": "string",
              "description": "Absolute path to write the merged PDF to instead of returning it. Refused unless the host enabled file output."
            }
          },
          "oneOf": [
            { "required": ["documents"] },
            { "required": ["documentPaths"] }
          ],
          "additionalProperties": false
        }
        """);

    /// <summary>Arguments of <c>extract_text</c>: the PDF to read.</summary>
    internal static readonly JsonElement ExtractText = Parse(
        """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "properties": {
            "pdf": {
              "type": "string",
              "description": "The PDF to read, base64-encoded. Mutually exclusive with pdfPath."
            },
            "pdfPath": {
              "type": "string",
              "description": "Absolute path of the PDF to read. Refused unless the host enabled file output."
            }
          },
          "oneOf": [
            { "required": ["pdf"] },
            { "required": ["pdfPath"] }
          ],
          "additionalProperties": false
        }
        """);

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
