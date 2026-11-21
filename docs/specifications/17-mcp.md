# 17 — Model Context Protocol server

`M3scalinum.Text.Mcp` serves the suite over the Model Context Protocol: a client — an editor, an
agent, anything that speaks MCP — asks for a document and gets the PDF back over stdio, or over the
streamable-HTTP shape a document service exposes to more than one client. It depends on
the compiler wrapper, the layout pack, the e-invoicing package and the PDF post-processing package, so
it follows the suite train (`TextVersion`), like `M3scalinum.Text.Pdf` and
`M3scalinum.Text.Pdf.FactureX`: a package that follows the Typst version cannot depend on one that
does not.

## 1. Scope

| In scope | Detail |
| --- | --- |
| Transport | stdio: one UTF-8 JSON-RPC 2.0 message per line, no headers, no length prefix. HTTP/1.1: the MCP streamable-HTTP shape, authenticated, on loopback by default |
| Lifecycle | `initialize`, notifications, `ping`, end of input as shutdown; HTTP sessions issued, required and deleted |
| Discovery | `tools/list` with a JSON Schema per tool |
| Tools | `render_document`, `render_invoice`, `list_templates`, `sign_document`, `merge_documents`, `extract_text` |
| Limits | document size, message size, allowed template ids, wall clock, file output, concurrent sessions |

| Out of scope | Why |
| --- | --- |
| Streaming responses, `GET` event streams, HTTP/2, chunked request bodies | One request carries one JSON-RPC message and one answer is written: a render finishes or fails inside `RenderTimeout`, so there is nothing to stream. A request has a declared length, so a body of unknown size is refused rather than buffered |
| TLS inside the process | A host that exposes this beyond loopback terminates TLS in a reverse proxy it already operates; the server keeps one job, and one set of crypto, out of the process |
| Resources, prompts, sampling, roots | This server renders; it has nothing to read or ask |
| Progress and cancellation notifications | A call either finishes inside the timeout or fails; the loop is sequential |
| Batch requests | MCP does not define them; the server answers a batch with `-32600` |

**Decision — JSON-RPC by hand, no SDK package.** The official C# SDK brings a hosting model
(`Microsoft.Extensions.Hosting`, dependency injection, logging) the suite does not otherwise use, and
it is not verified here to be trim- and AOT-clean. This repository forbids reflection-based JSON and
requires `PublishAot` to keep working, so the surface a client can reach is small and fixed: four
methods, six tools, two transports. It is written directly against `Utf8JsonWriter` and a
source-generated serialization context, which keeps `IsAotCompatible` meaningful — an analyzer failure
here is a publish failure in a consumer. If the SDK becomes the right answer, the protocol methods
below are the contract to keep.

## 2. The server

```csharp
using M3scalinum.Text.Mcp;

var options = new McpServerOptions
{
    MaxDocumentSize = 512 * 1024,
    AllowedTemplateIds = [TemplateIds.Invoice],
    AllowFileOutput = false,
};

await McpServer.RunStdioAsync(options);
```

`RunStdioAsync` binds the loop to the process's standard input and output; `RunAsync` takes the two
streams, which is how the tests drive it and how a host that owns its pipes embeds it. End of input is
a clean shutdown: the loop returns, it does not throw.

The loop is sequential. Messages are answered in the order they arrive, and one message is handled to
completion before the next is read — a compilation is CPU-bound, and interleaving responses would
serve nobody.

## 3. Transports

The protocol core does not know about a transport. `McpServer.HandleMessageAsync` takes one JSON-RPC
message and returns one answer as UTF-8 bytes — or `null` when the message is a notification — and
`IMcpTransport` is the interface a host serves it over. Two implementations ship:

| Transport | Role | Framing |
| --- | --- | --- |
| `StdioTransport` | what `RunStdioAsync` and `RunAsync(input, output)` use | one JSON-RPC message per line; end of input ends the loop |
| `McpHttpServer` | more than one client, a document service | HTTP/1.1 in the MCP streamable-HTTP shape, with sessions and a bearer token |

`RunAsync(IMcpTransport)` is the whole hosting model:

```csharp
var options = new McpServerOptions { MaxDocumentSize = 512 * 1024 };
var server = new McpServer(options);

// stdio, from a pair of streams a host owns:
await server.RunAsync(input, output);

// or HTTP, on a loopback port the operating system assigns:
await using var http = new McpHttpServer(options, new McpHttpServerOptions { BearerToken = token });
await http.StartAsync(server);
Console.WriteLine(http.Endpoint);   // http://127.0.0.1:54321/mcp, served until the host stops
```

The stdio path is the same loop it was before the abstraction: the existing protocol tests run
unchanged against it, which is what makes the refactor behaviour-neutral rather than merely intended
to be.

### 3.1 The HTTP transport

`McpHttpServer` follows the streamable-HTTP transport of MCP **2025-06-18**, the revision
`McpServer.ProtocolVersion` reports. It was introduced in 2025-03-26 and is unchanged in shape since.

- **Endpoint.** `POST` to one path, `/mcp` unless the host changes it. The body is one JSON-RPC
  message; the answer is `200` with `application/json` and one JSON-RPC message, or `202 Accepted`
  with an empty body when there is nothing to answer — a notification. A request with
  `Accept: text/event-stream` and no `application/json` is answered with the same message framed as a
  single SSE event (`event: message`, one `data:` line), which is the spec's alternative encoding for
  a response. It is one event, not a stream: `GET` is `405`.
- **Sessions.** `initialize` issues `Mcp-Session-Id` in the response. Every later request must send
  it; a request without it — other than `initialize` — or with an id the server does not know is
  `404`. `DELETE` with the header ends the session with `204`, and a `POST` carrying that id
  afterwards is `404`. A session is forgotten after `SessionIdleTimeout` of inactivity
  (one hour by default), and no more than `MaxSessions` exist at once (`503` beyond that).
- **Authentication.** Every `POST` and `DELETE` must carry `Authorization: Bearer <token>`, where the
  token is `McpHttpServerOptions.BearerToken` — required, with no default. The comparison is
  `CryptographicOperations.FixedTimeEquals` over the SHA-256 of both sides, so a caller learns only
  whether it matched, not how far into the token it got. A missing header and a wrong one are both
  `401`.
- **Binding.** `IPAddress.Loopback` unless the host says otherwise, and port `0` — the operating
  system assigns one — unless the host names a port. `Port` and `Endpoint` are readable as soon as
  `StartAsync` returns.
- **Refusals.** Any path other than the endpoint is `404`; any method other than `POST` and `DELETE`
  is `405` with `Allow`; a body larger than `MaxRequestSize` is `413`, decided on the declared
  `Content-Length` before a single body byte is stored; a request with no length is `411`; a
  `Transfer-Encoding` (chunked) body is `411`; a non-JSON `Content-Type` is `415`; a header block
  above `MaxHeaderBytes` is `431`. A refused request closes its connection after the response.

`McpHttpServerOptions` is the network surface, and the protocol limits stay where they were:

| Option | Default | Effect |
| --- | --- | --- |
| `BearerToken` | — (required) | The token every request must carry |
| `Address` | `IPAddress.Loopback` | The address the listener binds |
| `Port` | `0` | The port; `0` asks the operating system |
| `Path` | `/mcp` | The only path served |
| `MaxSessions` | 16 | Sessions alive at once; `initialize` beyond it is `503` |
| `SessionIdleTimeout` | 1 h | How long an untouched session is remembered |
| `RequestTimeout` | 30 s | Head plus body of one request |
| `MaxHeaderBytes` | 16 KiB | Ceiling on one header block |
| `McpServerOptions.MaxRequestSize` | 4 MiB | Ceiling on one body, enforced before it is read |
| `McpServerOptions.MaxDocumentSize` | 1 MiB | Ceiling on the document a call carries, applied after the body is parsed |

**Decision — `TcpListener` and a hand-written HTTP/1.1 parser, not `HttpListener`.** `HttpListener` on
Windows binds through the HTTP Server API, which normally needs a URL reservation the process does not
have: a test host or a service account would have to be configured, or run elevated, before the server
could listen at all. It is also not verified here to be trim- and AOT-clean, which this package's
`IsAotCompatible` requires. `TcpListener` is in the shared framework, needs no reservation on
loopback, adds no dependency, and the protocol this server needs is a request line, headers, and a
`Content-Length` body. Refusing chunked encoding, upgrades and HTTP/2 keeps the parser small enough to
read in one sitting, which is the point of writing it rather than taking one.

## 4. The protocol surface

Every response is `{"jsonrpc":"2.0","id":…}` plus either `result` or `error`.

| Method | Kind | Result |
| --- | --- | --- |
| `initialize` | request | `protocolVersion`, `capabilities.tools`, `serverInfo` |
| `notifications/initialized` | notification | none — a notification is never answered |
| `ping` | request | `{}` |
| `tools/list` | request | the six tools, each with `inputSchema` |
| `tools/call` | request | `content`, and `isError` when the work failed |

`initialize` answers with the revision the client asked for when the server knows it — `2024-11-05`,
`2025-03-26` or `2025-06-18` — and with `2025-06-18`, the newest it speaks, otherwise. The capability
object declares tools only, with `listChanged: false`: the tool list is fixed for the life of the
process.

### Errors

Errors are JSON-RPC error objects, not tool results: a malformed call is a protocol fault, and a
client should be able to tell the two apart.

| Code | Meaning here |
| --- | --- |
| `-32700` | the line is not JSON, or is longer than `MaxRequestSize` |
| `-32600` | not an object, `jsonrpc` is not `"2.0"`, `method` is not a string, or a batch |
| `-32601` | no such method |
| `-32602` | no such tool, or the arguments are missing or of the wrong shape |

A call that was well formed and whose *work* failed — Typst rejected the source, the invoice breaks an
EN 16931 rule, the path could not be written — is a result with `"isError": true` and one text block
carrying the reason. The client can fix the call; the protocol did nothing wrong.

## 5. The tools

### `render_document`

| Argument | Type | Meaning |
| --- | --- | --- |
| `source` | string | Typst source. Mutually exclusive with `template` |
| `template` | string | A layout id from `list_templates` |
| `version` | string | Exact layout version; the newest is used when omitted |
| `parameters` | object | Values of the layout's declared parameters, keyed by name |
| `outputPath` | string | Where to write the PDF; refused unless the host enabled file output |

The JSON Schema declares exactly one of `source` and `template` as required, and the server refuses a
call that carries both or neither. A parameter is checked against the layout's manifest before
anything compiles: a missing required one, an unknown name, a string where a date was declared — each
is `-32602` with the parameter and the expected shape in the message. A `date` parameter is the one
type with no JSON counterpart; it is written `yyyy-MM-dd`.

### `render_invoice`

| Argument | Type | Meaning |
| --- | --- | --- |
| `invoice` | object | The invoice, shaped like `FacturXInvoice` in camelCase |
| `outputPath` | string | As above |

The payload carries the terms the model documents: `number`, `typeCode`, `profile`, `currency`,
`issueDate`, `dueDate`, `seller`, `buyer`, lines, VAT breakdown and totals, plus the optional
references, notes, payment terms and allowances. Money is a JSON number read as `decimal`; a date is
`yyyy-MM-dd`; `profile` is a name — `minimum`, `basic-wl`, `basic`, `en16931`, `extended`,
`extended-ctc-fr`. Omitted terms take the renderer's defaults: `380`, `EUR`, EN 16931.

The invoice is validated against the EN 16931 rules before it is rendered, so a document that does not
add up never becomes an authoritative-looking PDF. The result is a PDF/A-3 with the CII XML attached
and the `fx:` XMP metadata, exactly as `FacturXRenderer.Render` produces it.

### `list_templates`

No arguments. Returns `{"templates":[…]}` as one text block: each layout's `id`, `version`,
`description`, `languages` (`en`, `fr`) and declared `parameters` with their `type` and `required`
flag. Only the ids the host allows are listed.

### `sign_document`

| Argument | Type | Meaning |
| --- | --- | --- |
| `pdf` | string | The PDF to sign, base64. Mutually exclusive with `pdfPath` |
| `pdfPath` | string | Absolute path of the PDF; refused unless the host enabled file output |
| `certificate` | string | The PKCS#12 (`.p12`/`.pfx`) with its private key, base64. Mutually exclusive with `certificatePath` |
| `certificatePath` | string | Absolute path of the certificate; refused unless the host enabled file output |
| `password` | string | Password of the PKCS#12; required, and an empty string is a valid one |
| `reason` | string | Why the document is signed, as the signature declares it |
| `location` | string | Where the signature was made |
| `outputPath` | string | As above |

The result is a PAdES-B signature written as an incremental update: nothing before the appended
block changes, so a document that was PDF/A stays PDF/A and an earlier signature stays valid. The
certificate is loaded into memory only — the private key is never written to a key store — and no
timestamp authority is consulted, so a call needs no network. A certificate that will not open with
the password given is `-32602`: it is an argument, not a failure of the work.

### `merge_documents`

| Argument | Type | Meaning |
| --- | --- | --- |
| `documents` | array of string | The PDFs, base64, in the order they are to appear. Mutually exclusive with `documentPaths` |
| `documentPaths` | array of string | Absolute paths, in order; refused unless the host enabled file output |
| `outputPath` | string | As above |

Two documents is the minimum: merging one is a copy, and refusing it names the count the call
carried. The merge rebuilds the file — objects renumbered, a fresh cross-reference table — so each
page keeps its content, fonts, images and annotations; document-level structures a page tree cannot
carry, such as tagging and outlines, are dropped as `PdfPages.Merge` documents.

### `extract_text`

| Argument | Type | Meaning |
| --- | --- | --- |
| `pdf` | string | The PDF to read, base64. Mutually exclusive with `pdfPath` |
| `pdfPath` | string | Absolute path; refused unless the host enabled file output |

Returns one text block holding `{"pageCount":N,"pages":[{"page":1,"text":"…"}]}`, pages counted from
one as a reader numbers them. The text is read from the content streams and decoded through each
font's `/ToUnicode` map, with a simple font without one read as single-byte WinAnsi; words are
separated by the positioning the run carries, so a sentence reads as a sentence and a row of a table
as a row. An encrypted document, or one using a stream filter this reader does not implement, is a
tool error naming the reason rather than an empty page.

### What a render returns

The PDF is binary, and MCP carries binary as an embedded resource — a URL, a MIME type and base64:

```json
{"content":[
  {"type":"text","text":"{\"byteLength\":24188,\"pageCount\":1}"},
  {"type":"resource","resource":{"uri":"m3scalinum://documents/…","mimeType":"application/pdf","blob":"JVBERi0x…"}}
]}
```

The `uri` is an identifier, not an address: nothing serves it, and the bytes are in `blob`. When the
host allowed file output and the call named `outputPath`, the PDF is written there and the result
carries one text block with `path`, `byteLength` and, for a document, `pageCount`.

## 6. Security posture

A tool call is written by a model, not by a programmer. The server assumes it is hostile, and the
options are the whole of what a host can relax.

- **No ambient file access.** `render_document` compiles the source it was given; it reads no file,
  no font directory, no package cache. `outputPath` is refused — not ignored — unless
  `AllowFileOutput` is set, so a caller that asked for a file never believes one exists. Templates
  come from the assembly's embedded layouts, never from a path. The three PDF tools take their input
  the same way: `pdf`, `documents` and `certificate` are bytes the call carries, and a `pdfPath`,
  `documentPaths` or `certificatePath` is refused with the same `-32602` while file access is
  disabled — a read a model chose is ambient access just as much as a write. A path that is allowed
  is bounded by `MaxDocumentSize` before it is read.
- **No outbound network.** No package is fetched, no TSA or OCSP is consulted, nothing leaves the
  process but the answer. The compiler's package resolution is not reachable from a tool call. The
  HTTP transport listens; it never connects.
- **The network surface is opt-in, authenticated and bounded.** Nothing listens unless the host
  constructs an `McpHttpServer`: the stdio server opens no socket at all. When it does, it binds
  loopback by default, requires a bearer token, compares it in constant time, serves exactly one
  path, refuses every other method, caps the header block, the body (on its declared length, before
  reading it) and the number of live sessions, and forgets a session that goes idle. TLS is not
  terminated here; a host that exposes the port beyond loopback puts it behind a proxy.
- **Bounded work.** `MaxDocumentSize` caps one document in UTF-8 bytes before it is parsed or
  compiled; `MaxRequestSize` caps one JSON-RPC line while it is being read, discarding the rest of an
  oversized line and answering `-32700` rather than growing a buffer; `RenderTimeout` bounds one
  compilation, queue time included.
- **Bounded surface.** `AllowedTemplateIds` restricts which layouts may be rendered and listed — a
  host can ship the invoice layout alone. The tool list never changes.
- **No state.** The protocol core holds no state between messages: no working directory, no cache,
  no loaded document. An HTTP session is a name the transport remembers, not a conversation the
  server keeps — it carries no document and no compiler state, so expiry costs a client its id, not
  its work. Two callers of the same process cannot see each other's work.

## 7. Tests

`tests/Text.Mcp.Tests` drives the server over an in-memory pair of streams with real frames:
`initialize`, `tools/list`, `list_templates`, a successful render asserting the `%PDF` header, an
unknown method, an unknown tool, malformed JSON, a batch, a message over the limit, end of input, and
file output refused and allowed. The PDF tools are exercised on documents the server rendered in the
same session: `sign_document` returns a file whose signature `PdfSignatureVerifier` reads back as
intact, `merge_documents` a file with both pages in the order given, `extract_text` the page count
and a marker each page draws, plus the refusal a path earns while file output is disabled. The
protocol and argument tests need no native library; the ones that compile or render read
`SHARPTYPST_NATIVE_PATH` and skip with the path to set when it is absent, so a change to the JSON-RPC
layer is testable without a Rust build.

The transports are tested twice over. A shared suite — `TransportSuiteTests` — runs the same messages
through `StdioProtocolClient` and through an `McpHttpServer`, so a divergence between the two fails
one of the pair; it also covers the parts of the protocol that are easy to lose when a transport is
added: a notification that is never answered, a batch, malformed JSON, an unknown method, an unknown
tool. `HttpTransportTests` then covers the network surface itself, over a real `HttpClient` on a
loopback port the operating system assigned: `initialize` issuing a session id, `tools/list` and a
`tools/call` that returns a PDF inside the session, `401` with no token and with a wrong one, `404`
for a session id the server never issued or for a closed session after `DELETE`, `404` for another
path, `405` for a `GET`, `202` for a notification, `413` for a body over `MaxRequestSize`, the
session cap, and an `Accept: text/event-stream` client receiving its answer as one event. No real MCP
client was run against the HTTP transport: the shape is exercised with the HTTP client every .NET
process carries, which proves the wire, not another SDK's interpretation of it.
