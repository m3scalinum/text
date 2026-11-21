M3scalinum.Text.Mcp serves the suite over the Model Context Protocol: an MCP client asks for a
document or an invoice and gets a PDF back, over standard input and output. The compiler, the layout
pack and the e-invoicing package are all inside the process — nothing is spawned and nothing is
downloaded.

## Start the server

```csharp
using M3scalinum.Text.Mcp;

var options = new McpServerOptions
{
    MaxDocumentSize = 512 * 1024,
    AllowedTemplateIds = [TemplateIds.Invoice],
};

await McpServer.RunStdioAsync(options);
```

A host that owns its own pipes calls `RunAsync(input, output, cancellationToken)` instead; the loop is
the same. End of input is a clean shutdown: the method returns.

`McpServerOptions` is the whole of what a host configures:

| Option | Default | Effect |
| --- | --- | --- |
| `ServerName`, `ServerVersion` | `M3scalinum.Text.Mcp`, assembly version | What `initialize` reports |
| `MaxDocumentSize` | 1 MiB | Largest source or invoice payload, in UTF-8 bytes |
| `MaxRequestSize` | 4 MiB | Largest JSON-RPC line; a longer one is discarded and refused |
| `AllowedTemplateIds` | every layout | Which layouts may be rendered and listed |
| `AllowFileOutput` | `false` | Whether a call may name an `outputPath` |
| `RenderTimeout` | 30 s | Wall clock for one compilation |

## The tools

### `render_document`

Renders Typst source, or one layout of `M3scalinum.Text.Templates` with its parameters.

| Argument | Type | Meaning |
| --- | --- | --- |
| `source` | string | Typst source; mutually exclusive with `template` |
| `template` | string | Layout id from `list_templates` |
| `version` | string | Exact version; the newest when omitted |
| `parameters` | object | The layout's declared parameters, by name |
| `outputPath` | string | Where to write the PDF; refused unless the host allows it |

A `date` parameter is written `yyyy-MM-dd` — the one type JSON has no counterpart for. Every parameter
is checked against the layout's manifest before anything compiles, so a missing or misspelled one
comes back as an error naming it.

### `render_invoice`

Renders a Factur-X PDF/A-3 from an invoice payload shaped like `FacturXInvoice` in camelCase:
`number`, `issueDate`, `currency`, `profile`, `seller`, `buyer`, `lines`, `taxBreakdown`, `totals`,
plus the optional references, notes, allowances and payment terms. Money is a JSON number read as
`decimal`; dates are `yyyy-MM-dd`. Omitting `profile`, `typeCode` or `currency` gives `en16931`, `380`
and `EUR`.

The invoice is validated against the EN 16931 rules first: an invoice that does not add up is a tool
error, not a PDF.

### `list_templates`

Returns the ids, versions, languages and declared parameters of the layouts this server serves.

## What comes back

A PDF is binary, so it travels as an MCP embedded resource: a text block with the size, and a resource
block with MIME type `application/pdf` and the bytes in base64.

```json
{"content":[
  {"type":"text","text":"{\"byteLength\":24188,\"pageCount\":1}"},
  {"type":"resource","resource":{"uri":"m3scalinum://documents/…","mimeType":"application/pdf","blob":"JVBERi0x…"}}
]}
```

With file output enabled and an `outputPath`, the bytes are written there and the result is one text
block with the path.

## Errors

Malformed calls are JSON-RPC error objects: `-32700` for a line that is not JSON (or too long),
`-32600` for a message that is not valid JSON-RPC, `-32601` for an unknown method, `-32602` for an
unknown tool or bad arguments. A well-formed call whose work failed — Typst rejected the source, the
invoice breaks a rule — is a result with `"isError": true` and the reason.

## Security posture

A tool call is written by a model. The server reads no file it was not given, writes nothing unless
the host enabled file output, opens no socket, fetches no package, and caps the size of a document,
the length of a message and the time a compilation may take.

## Notes

- The protocol is JSON-RPC 2.0 over stdio, one message per line. The package is `PublishAot`-clean:
  no reflection-based serializer, no hosting framework.
- The tests drive the server over an in-memory stream pair. The five that compile need
  `SHARPTYPST_NATIVE_PATH` and skip with a message when it is absent.
