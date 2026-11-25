# The MCP server

M3scalinum.Text.Mcp serves the suite over the Model Context Protocol: an MCP client asks for a
document or an invoice and gets a PDF back, over standard input and output, or over HTTP on a loopback
port when more than one client needs it. The compiler, the layout pack and the PDF package are
all inside the process — nothing is spawned and nothing is downloaded.

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

For a document service — more than one client, a network the host already operates — serve the same
server over HTTP:

```csharp
var options = new McpServerOptions { MaxDocumentSize = 512 * 1024 };
var http = new McpHttpServerOptions { BearerToken = token };   // token is the host's to mint and keep

await using var server = new McpHttpServer(options, http);

await server.StartAsync(new McpServer(options));

Console.WriteLine(server.Endpoint);   // http://127.0.0.1:54321/mcp — the port is the OS's choice
```

## Over HTTP

`McpHttpServer` speaks the MCP streamable-HTTP shape of protocol revision **2025-06-18** — `POST` one
JSON-RPC message to `/mcp`, receive one JSON-RPC message back as `application/json`, or `202` with an
empty body when the message was a notification. A client that sends `Accept: text/event-stream` (and
not `application/json`) gets the same message as a single SSE event. There is no stream behind it:
`GET` is `405`, and the render either finishes inside the timeout or fails.

```text
POST /mcp              Authorization: Bearer <token>
{"jsonrpc":"2.0","id":1,"method":"initialize",...}
→ 200  Mcp-Session-Id: 3f2a…   {"jsonrpc":"2.0","id":1,"result":{...}}

POST /mcp              Authorization: Bearer <token>   Mcp-Session-Id: 3f2a…
{"jsonrpc":"2.0","id":2,"method":"tools/list"}
→ 200  {"jsonrpc":"2.0","id":2,"result":{...}}

DELETE /mcp            Authorization: Bearer <token>   Mcp-Session-Id: 3f2a…
→ 204
```

What the surface enforces:

| Rule | Answer |
| --- | --- |
| No `Authorization: Bearer <token>`, or the wrong token (compared in constant time) | `401` |
| No session id, or one the server did not issue, or one closed by `DELETE` or idle too long | `404` |
| A path other than the endpoint | `404` |
| A method other than `POST` and `DELETE` | `405`, with `Allow` |
| A body over `MaxRequestSize`, decided on its declared length before it is read | `413` |
| No `Content-Length`, or a chunked body | `411` |
| A body that is not `application/json` | `415` |
| More sessions than `MaxSessions` | `503` |

The listener binds `127.0.0.1` and port `0` — the operating system assigns one — unless the host says
otherwise, and TLS is not terminated here: put a proxy in front of it if the port is exposed. The
token has no default, because a network surface with no credential is not a default this package will
pick. `MaxSessions` (16), `SessionIdleTimeout` (1 h) and `RequestTimeout` (30 s) bound the rest.

`McpServerOptions` is the whole of what a host configures for both transports:

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

### `list_templates`

Returns the ids, versions, languages and declared parameters of the layouts this server serves.

### `sign_document`

Signs a PDF with a PKCS#12 certificate, as a PAdES-B signature, and returns the signed file.

| Argument | Type | Meaning |
| --- | --- | --- |
| `pdf` | string | The PDF, base64; mutually exclusive with `pdfPath` |
| `pdfPath` | string | Absolute path of the PDF; refused unless the host allows file output |
| `certificate` | string | The `.p12`/`.pfx` with its private key, base64; mutually exclusive with `certificatePath` |
| `certificatePath` | string | Absolute path of the certificate; refused unless the host allows file output |
| `password` | string | Password of the PKCS#12 — required, and an empty string is a valid one |
| `reason`, `location` | string | What the signature declares about itself |
| `outputPath` | string | Where to write the signed PDF; refused unless the host allows it |

The signature is appended as an incremental update, so a PDF/A document stays PDF/A and an earlier
signature stays valid. The key is loaded into memory only, and no timestamp authority is consulted.

### `merge_documents`

Merges two or more PDFs into one, in the order given.

| Argument | Type | Meaning |
| --- | --- | --- |
| `documents` | array of string | The PDFs, base64, in order; mutually exclusive with `documentPaths` |
| `documentPaths` | array of string | Absolute paths, in order; refused unless the host allows file output |
| `outputPath` | string | Where to write the merged PDF; refused unless the host allows it |

Each page keeps its content, fonts, images and annotations. Tagging, outlines and form dictionaries
are dropped by a merge, exactly as `PdfPages.Merge` documents.

### `extract_text`

Reads the text each page draws.

| Argument | Type | Meaning |
| --- | --- | --- |
| `pdf` | string | The PDF, base64; mutually exclusive with `pdfPath` |
| `pdfPath` | string | Absolute path; refused unless the host allows file output |

The answer is one text block: `{"pageCount":2,"pages":[{"page":1,"text":"…"},{"page":2,"text":"…"}]}`.
Text is decoded through the fonts' `/ToUnicode` maps, with the positioning of each run supplying the
spaces and line breaks. An encrypted document, or one whose streams use a filter the server does not
implement, comes back as a tool error naming the reason.

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
invoice breaks a rule, the PDF is encrypted — is a result with `"isError": true` and the reason.

## Security posture

A tool call is written by a model. The server reads no file it was not given, writes nothing unless the
host enabled file output, fetches no package, and caps the size of a document, the length of a message
and the time a compilation may take. A path in any argument — `outputPath`, `pdfPath`,
`documentPaths`, `certificatePath` — is refused with `-32602` while `AllowFileOutput` is `false`; the
PDF tools take their input as base64 bytes instead. The signing key is loaded into memory, never into
a key store, and the private key never leaves the process.

Nothing listens unless the host builds an `McpHttpServer`, and when it does: loopback only by default,
a bearer token the host supplies and the server compares in constant time, one path served, every
other method refused, the request body capped on its declared length before it is read, the header
block capped, the number of sessions capped, and a session that idles is forgotten. The transport
never makes an outbound connection, and TLS is the proxy's job.

## Notes

- The protocol is JSON-RPC 2.0, framed either as one message per line on stdio or as one HTTP
  request per message. The package is `PublishAot`-clean: no reflection-based serializer, no hosting
  framework, and the HTTP listener is a `TcpListener` from the shared framework rather than
  `HttpListener`, which would need a URL reservation on Windows.
- The protocol core is transport-free: `McpServer.HandleMessageAsync` takes a message and returns an
  answer, `IMcpTransport` is the framing. `StdioTransport` and `McpHttpServer` are the two that ship.
- The tests drive the server over an in-memory stream pair and, for HTTP, over a real `HttpClient` on
  a loopback port the operating system assigns. A shared suite runs the same messages through both
  transports. No third-party MCP client was used. The ones that compile render need
  `SHARPTYPST_NATIVE_PATH` and skip with a message when it is absent; the protocol, transport and
  argument tests run without it.
