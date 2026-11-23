# M3scalinum.Text

[![CI (Typst)](https://github.com/m3scalinum/text/actions/workflows/ci-sharptypst.yml/badge.svg?branch=main)](https://github.com/m3scalinum/text/actions/workflows/ci-sharptypst.yml)
[![CI (Text suite)](https://github.com/m3scalinum/text/actions/workflows/ci-text.yml/badge.svg?branch=main)](https://github.com/m3scalinum/text/actions/workflows/ci-text.yml)
[![NuGet Typst](https://img.shields.io/nuget/v/M3scalinum.Text.Typst.svg)](https://www.nuget.org/packages/M3scalinum.Text.Typst)
[![NuGet Pdf](https://img.shields.io/nuget/v/M3scalinum.Text.Pdf.svg)](https://www.nuget.org/packages/M3scalinum.Text.Pdf)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](https://github.com/m3scalinum/text/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0%20%7C%2011.0-512BD4.svg)](https://dotnet.microsoft.com)

Documents, in .NET, without leaving the process. The suite holds the [Typst](https://typst.app)
compiler in memory, exposes it as a managed API, and hands you the document model and the templates
that make it usable; on top of it, it signs, merges, encrypts and reads back the PDF it produced. No
browser, no Office, no process spawn, no binary on `PATH`.

It produces and transforms documents, and nothing else: the
[document test](docs/specifications/00-overview.md#23-the-document-test) states the three questions
that decide what belongs here, in three lines.

```csharp
using M3scalinum.Text.Typst.Compilation;

byte[] pdf = TypstCompiler.CompileToPdf("""
    #set page(paper: "a4")
    = Hello
    This is *Typst*, compiled in-process.
    """);
```

## Contents

- [The packages](#the-packages)
- [M3scalinum.Text.Typst](#m3scalinumtexttypst)
- [M3scalinum.Text.Documents](#m3scalinumtextdocuments)
- [M3scalinum.Text.Pdf](#m3scalinumtextpdf)
- [M3scalinum.Text.Templates](#m3scalinumtexttemplates)
- [M3scalinum.Text.Mcp](#m3scalinumtextmcp)
- [The other formats](#the-other-formats)
- [Versioning](#versioning)
- [Supported platforms](#supported-platforms)
- [Documentation](#documentation)
- [Building from source](#building-from-source)
- [Project status](#project-status)
- [Contributing](#contributing)
- [License](#license)

## The packages

| Package | What it does | Depends on |
| --- | --- | --- |
| `M3scalinum.Text.Typst` | Compiles Typst in-process. PDF, SVG, PNG, HTML, Markdown, plain text, bundles | — |
| `M3scalinum.Text.Documents` | The document model: typed content, inline styles, tables with merged cells, figures, references, a reusable document style, page setup, HTML and Markdown in and out | — |
| `M3scalinum.Text.Typst.DependencyInjection` | `AddTypst` for `Microsoft.Extensions.DependencyInjection` | Typst |
| `M3scalinum.Text.Templates.Generator` | Roslyn generator: a `template.toml` becomes a typed options record | — |
| `M3scalinum.Text.Pdf` | PAdES signatures written as incremental updates — B-B to B-LTA, eIDAS seals, trust lists — plus text, outlines, attachments, batch manifests, a page rendered as a PNG | — |
| `M3scalinum.Text.Templates` | Seven business-document layouts — invoice, quotation, account statement, attestation, contract, payslip, purchase order — with typed options and English or French labels | Typst |
| `M3scalinum.Text.Mcp` | A Model Context Protocol server over stdio: render a document, list the layouts, sign, merge and read PDFs | Typst, Documents, Templates, Pdf |
| `M3scalinum.Text.Barcodes` | 19 symbologies — QR, DataMatrix, Aztec, PDF417, Code 128/39/93, Telepen, EAN-13/8, UPC-A/E, ITF, Codabar, GS1-128, DataBar — encode to SVG or PNG, decode one symbol or a whole page; GS1 element strings and Digital Link | — |
| `M3scalinum.Text.Ocr` | Text, lines and boxes out of a page image, with the models supplied by the caller; confidence thresholds, rotation/deskew/denoise, model fingerprints | — |
| `M3scalinum.Text.Docx` | A Word document read into the document model, then renderable like any other | Documents, Typst |
| `M3scalinum.Text.Xlsx` | An Excel workbook read into typed sheets and cells — text, numbers, booleans, dates, errors, formulas with their cached results — then renderable as a table | Documents, Typst |

Two trains carry them: a package that embeds the compiler takes its version from Typst itself, and the
rest release on their own schedule — a signing fix must not read as a compiler release. See
[Versioning](#versioning).

## M3scalinum.Text.Typst

Hosts the Typst compiler **in-process** behind a managed .NET API.

```bash
dotnet add package M3scalinum.Text.Typst
```

### Why

- **In-process.** The compiler is a Rust `cdylib` behind a C ABI, loaded into your process. Nothing is
  spawned, nothing is written to a temporary directory, nothing has to be installed on the host.
- **Deterministic.** The clock is fixed by default, so identical inputs produce byte-identical output —
  safe for caching, content addressing and reproducible builds.
- **Self-contained.** The native library for every supported platform ships inside the NuGet package.
  A consumer needs no Rust toolchain and no `PATH` entry.

### Quick start

```csharp
using M3scalinum.Text.Typst.Compilation;

var pdf = TypstCompiler.CompileToPdf("""
    #set page(paper: "a4", margin: 2cm)

    = Hello

    A document compiled in-process.
    """);

File.WriteAllBytes("hello.pdf", pdf);
```

`CompileToPdf` is a convenience for a one-off document: it builds a world, compiles, exports and
disposes everything. For anything you do more than once, keep a world:

```csharp
using var world = new TypstWorldBuilder()
    .AddSource("main.typ", """
        #set text(font: "Inter")
        #image("logo.png", width: 4cm)
        """)
    .AddAsset("logo.png", File.ReadAllBytes("logo.png"))
    .AddFont(File.ReadAllBytes("Inter-Regular.ttf"))
    .Build();

using var result = new TypstCompiler().Compile(world);
File.WriteAllBytes("out.pdf", result.ExportPdf());
```

Paths are virtual: `logo.png` is a name inside the world, not a path on disk.

To write a PDF straight to an HTTP response or a file, without a second copy of it in memory:

```csharp
using var result = new TypstCompiler().Compile(world);
result.ExportPdf(response.Body);
```

### What it does

| Capability | Detail |
| --- | --- |
| Compile | Typst 0.15 language and standard library, paged and HTML documents |
| Export | PDF, SVG, PNG, HTML, Markdown, plain text, and an HTML bundle with its assets |
| Streaming | `ExportPdf(Stream)` copies the output out in 64 KiB chunks: no second, managed copy of a large PDF |
| Telemetry | `ActivitySource` and `Meter` named `M3scalinum.Text.Typst`: spans, durations, page counts, outcomes |
| PDF standards | PDF/A-1b to A-4e, PDF/UA-1, page ranges, tagged output, metadata |
| Fonts | Embedded, scanned from a directory, or the host's installed fonts |
| Async | `CompileAsync` on the thread pool, with the cancellation caveat documented |
| Concurrency | `TypstCompiler(maxConcurrency)` bounds the work in flight; the queue wait is asynchronous |
| Timeout | `TypstCompilationOptions.Timeout` bounds how long a caller waits, including the queue |
| Inputs from memory | Sources, fonts, images, data files and Typst packages as bytes — nothing has to be on disk |
| Packages | `@preview/...` resolves from a cache or the registry; offline by default |
| Diagnostics | Errors, warnings and hints with file, line and column, typed and structured |
| Evaluation | Outline, labels and metadata of a compiled document, with final page numbers |
| Document model | A fluent C# builder that emits Typst source |
| Templates | A `.typ` layout with declared, typed parameters, filled from C# |
| Determinism | Fixed clock by default; identical inputs produce identical output |

### Wiring it up

`M3scalinum.Text.Typst.DependencyInjection` registers the compiler as a singleton, a pre-configured
world builder as a transient and `ITypstWorldFactory` as the seam a host injects, with
`ValidateOnStart` so a bad limit fails at boot rather than at the first compile:

```bash
dotnet add package M3scalinum.Text.Typst.DependencyInjection
```

```csharp
builder.Services.AddTypst(options => options.Limits.MaxSourceBytes = 4 * 1024 * 1024);
```

`M3scalinum.Text.Templates.Generator` turns a `template.toml` into a typed options record at build
time, so a parameter renamed in a layout is a compile error in the record that fills it:

```bash
dotnet add package M3scalinum.Text.Templates.Generator
```

## M3scalinum.Text.Documents

The document model, and the package the rest of the suite builds on: typed content, inline styles,
tables with merged cells, figures, lists, page setup, and the HTML and Markdown importers. It emits
Typst markup and depends on nothing — no compiler, no native library — so a parser can build a
document without paying for the compiler.

```bash
dotnet add package M3scalinum.Text.Documents
```

```csharp
using M3scalinum.Text.Documents;

var document = TypstDocument.Create()
    .Page(p => p.Paper("a4").Margin(TypstLength.Cm(2)))
    .Heading(1, "Q3 report")
    .Paragraph(TypstContent.Empty.Text("Revenue grew by ").Bold("12%").Text(" this quarter."))
    .Table(t => t
        .Columns(TypstLength.Fr(1), TypstLength.Fr(1))
        .HeaderRow("Region", "Revenue")
        .Row("North", "1.2M"));
```

`TypstDocument.FromHtml(html)` and `TypstDocument.FromMarkdown(markdown)` import the two formats a web
application and a repository already speak, into that same model, and `ToHtml()` and `ToMarkdown()`
write it back out without compiling — each reporting the constructs it could not carry. A
`TypstDocumentStyle` holds the page, body text, heading levels 1 to 3, language, running header and
footer once and applies to any document, and `Label`/`Reference`/`Outline` give cross-references and a
table of contents whose numbers Typst resolves at layout time. The rest of the builder — metadata,
footnotes, formatting, counters, figures — is in
[the document model](https://github.com/m3scalinum/text/wiki/Documents-Model).

## M3scalinum.Text.Pdf

Signs a PDF you already have. It takes bytes and returns bytes, and depends on no part of the
compiler: signatures, conformance, text, forms, pages, attachments, outlines, encryption, batch
manifests, European trust lists and PDF/A conversion are managed code. Rendering a page as an image is
the one feature that needs a native library — its own Rust crate, `native/text-pdf`, packed for the
six runtimes — and the package carries it.

```bash
dotnet add package M3scalinum.Text.Pdf
```

```csharp
using M3scalinum.Text.Pdf;

var certificate = X509CertificateLoader.LoadPkcs12FromFile("signature.pfx", password);

byte[] signed = PdfSigner.Sign(pdf, certificate, new PdfSignatureOptions
{
    Reason = "Facture FA-2026-0042",
    Location = "Lille",
});
```

```csharp
// The first page as a PNG preview: 150 DPI unless asked otherwise, under a pixel and memory ceiling.
PdfPageImage preview = PdfRaster.Render(pdf, 1);
File.WriteAllBytes("preview.png", preview.Bytes);
```

The signature is PAdES-B, written as an **incremental update**: the original bytes are untouched, so a
PDF/A document stays PDF/A, an embedded file attachment stays readable, and an earlier signature
stays valid. Verified with pyHanko (intact, trusted, covering the whole file) and, on a signed
PDF/A-3 document with an attachment, with veraPDF in PDF/A-3B.

## M3scalinum.Text.Templates

Seven business-document layouts, each a Typst source and a `template.toml` manifest embedded in the
package, so a document renders without shipping a template directory:

| Id | Document |
| --- | --- |
| `invoice` | Invoice (facture): seller and customer blocks, line items, tax, amount paid and balance due |
| `quotation` | Quotation (devis): what is offered, until when, with the acceptance block |
| `statement` | Account statement (relevé de compte): dated movements with the balance after each |
| `attestation` | Attestation: an employer certifying a fact about an employee |
| `contract` | Contract (contrat): two parties, numbered clauses and a signature block |
| `payslip` | Payslip (bulletin de paie): employer, employee, period, gross lines, contributions and net |
| `purchase-order` | Purchase order (bon de commande): buyer, supplier, lines, delivery address and terms |

```bash
dotnet add package M3scalinum.Text.Templates
```

Every word a layout prints is a label the caller passes, so one file renders in English or in
French — column headings, legal phrases and the date pattern included:

```csharp
using M3scalinum.Text.Typst.Compilation;
using M3scalinum.Text.Templates;

var catalog = TemplateCatalog.Default;
var invoice = catalog.Get("invoice");                // or catalog.Get("invoice", "1.0.0")

var template = invoice.LoadTyped<InvoiceOptions>();  // the record generated from that manifest
byte[] pdf = template.Render(new TypstCompiler(), new InvoiceOptions
{
    Title    = "Facture 2026-0042",
    Number   = "FA-2026-0042",
    Issued   = new DateOnly(2026, 3, 1),
    Due      = new DateOnly(2026, 3, 31),
    Seller   = new Dictionary<string, object?> { ["name"] = "Atelier Dupont SAS" },
    Customer = new Dictionary<string, object?> { ["name"] = "Clinique des Tilleuls" },
    Items    = [ /* … */ ],
    Labels   = TemplateLabels.For("invoice", TemplateLanguage.French),
});
```

The same layout also loads from a directory — `TypstTemplate.Load("templates/invoice")` — when it is
edited locally rather than taken from the package, and `AddTextTemplates()` registers
`TemplateCatalog.Default` in a `Microsoft.Extensions.DependencyInjection` container. A label the
layout reads but the set does not declare fails the render naming the missing key, never a silent
English word in a French document.

## M3scalinum.Text.Mcp

A [Model Context Protocol](https://modelcontextprotocol.io) server, so a model — in an editor, an
agent, anywhere that speaks MCP — can ask for a document and get a PDF back. The compiler, the layout
pack and the PDF tools are all inside the process: nothing is spawned and nothing is downloaded.

```bash
dotnet add package M3scalinum.Text.Mcp
```

```csharp
using M3scalinum.Text.Mcp;

var options = new McpServerOptions
{
    MaxDocumentSize = 512 * 1024,
    AllowedTemplateIds = [TemplateIds.Invoice],
};

await McpServer.RunStdioAsync(options);
```

| Tool | Does |
| --- | --- |
| `render_document` | Renders Typst source, or a layout from `M3scalinum.Text.Templates` with its parameters |
| `list_templates` | Lists the layouts this server serves: ids, versions, languages, parameters |
| `sign_document` | Signs a PDF with a PKCS#12 certificate, as a PAdES signature, with an optional reason and location |
| `merge_documents` | Merges two or more PDFs into one, in the order given |
| `extract_text` | Reads the text each page of a PDF draws, one entry per page |

The PDF comes back as an MCP embedded resource — `application/pdf`, base64 — unless the host allowed
file output and the call named a path. The protocol is JSON-RPC 2.0 over stdio, hand-written on a
source-generated serializer context so the package stays `PublishAot`-clean; there is no SDK
dependency.

A tool call is written by a model, so the defaults are the restrictive ones: no file is read, no file
is written unless `AllowFileOutput` is set, no socket is opened, and a document, a message and a
compilation are each capped. The PDF tools take their documents as base64 and refuse a `pdfPath`,
`documentPaths` or `certificatePath` while file output is disabled. See [the specification](docs/specifications/17-mcp.md).

## The other formats

Four packages, one format each, each usable on its own. None embeds a second engine: what needs a
page goes through `M3scalinum.Text.Typst`, and the rest emits bytes.

### M3scalinum.Text.Docx — read a Word document

`DocxReader` turns a `.docx` into a `DocxDocument` — blocks in document order, merged table cells,
numbering, styled runs — with the embedded images as bytes, not re-encoded. `ToTypstDocument()` then
hands it to the suite's own model, so it renders like anything else.

```csharp
using M3scalinum.Text.Docx;

DocxDocument document = DocxReader.Read(File.ReadAllBytes("received.docx"));
byte[] pdf = TypstCompiler.CompileToPdf(document.ToTypstDocument());
```

### M3scalinum.Text.Xlsx — read an Excel workbook

`XlsxReader` turns a `.xlsx` into an `XlsxWorkbook` — every sheet in workbook order, its used region,
and typed cells: text, number, boolean, date, duration and error. A formula keeps the result the
workbook cached for it, and the formula itself. `ToTypstDocument()` renders the workbook as one table
per sheet, so an invoice typed into a spreadsheet reaches a PDF without Excel.

```csharp
using M3scalinum.Text.Xlsx;

XlsxWorkbook workbook = XlsxReader.Read(File.ReadAllBytes("facture.xlsx"));
byte[] pdf = TypstCompiler.CompileToPdf(workbook.ToTypstDocument());
```

### M3scalinum.Text.Barcodes — QR to Codabar, in-process

```csharp
using M3scalinum.Text.Barcodes;

BarcodeImage image = Barcode.Encode(BarcodeSymbology.QrCode, "BT-1=FA-2026-000123");

byte[] png = image.Png;   // ready to embed in a PDF page
string svg = image.Svg;   // ready to hand to a layout engine
```

Nineteen symbologies in all, fifteen of them written and read. GS1 data goes through `Gs1Data`, which
parses an element string, serialises the Digital Link URL and encodes a GS1-128; `Barcode.Print` sizes
a symbol for paper at a given DPI and refuses anything under the module and quiet-zone floors; and
`Barcode.DecodeAll` reads every symbol on a page — including a page `M3scalinum.Text.Pdf` rendered.

### M3scalinum.Text.Ocr — text out of a page image

```csharp
using M3scalinum.Text.Ocr;

using OcrEngine engine = OcrEngine.FromModels(detectionModel, recognitionModel);

// A page the engine cannot read is refused, not returned as approximate text.
if (engine.Recognize(File.ReadAllBytes("scan.png")) is OcrResult result)
{
    Console.WriteLine(result.Text);
}
```

The models are caller-supplied: the package downloads nothing and bundles no weights, and
`OcrEngine.FromDirectory` takes a folder holding `detection.rten` and `recognition.rten` when you would
rather not read them yourself. `Recognize` returns an `OcrRecognition`, which is either an `OcrResult`
or an `OcrUnreadable` carrying the refusal — page blank, mean confidence under the threshold, or too
many words under it — with the observed value, the threshold and the word count. The thresholds are
`OcrOptions`, which also turns on the three preprocessing stages: quarter-turn detection, deskew and
denoise. Every result reports what preprocessing did and which model fingerprints produced it.

## Versioning

Two trains, two numbers, because they answer different questions.

| Train | Packages | Version |
| --- | --- | --- |
| **Typst** | the four compiler packages | `0.15.1.<revision>` — the version *is* the embedded Typst, plus a build count |
| **Suite** | `M3scalinum.Text.Pdf`, `.Templates`, `.Mcp`, `.Barcodes`, `.Docx`, `.Xlsx`, `.Ocr` | whatever the tag names: `v0.1.0` publishes `0.1.0` |

A signing fix published as `0.15.1.43` would read as a compiler release, and would jump to `0.16.0`
the day Typst does, for code that contains no compiler at all. So the two trains tag and release
separately: `sharptypst-v0.15.1.42` and `v0.1.0`.

## Supported platforms

- .NET 8, 9, 10 or 11 (`net8.0`, `net9.0`, `net10.0`, `net11.0`)
- `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `linux-musl-x64` and `linux-musl-arm64` (Alpine),
  `osx-arm64` and `osx-x64` (one thin library each, not a universal one)

The native compiler ships inside `M3scalinum.Text.Typst` for each of these. The five format packages that
carry a Rust library of their own — `M3scalinum.Text.Barcodes`, `.Ocr`, `.Docx`, `.Xlsx` and `.Pdf` — ship
one native asset per identifier too: `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`,
`linux-musl-x64`, `linux-musl-arm64` and `osx`. The remaining suite packages (`Templates`, `Mcp`,
`Documents`, `Typst.DependencyInjection`, `Templates.Generator`) are managed assemblies and run wherever
.NET does.

## Documentation

The **[user guide](https://github.com/m3scalinum/text/wiki)** is organised by package: a getting-started
page for each, then one page per topic.

| Group | Pages |
| --- | --- |
| **[Typst](https://github.com/m3scalinum/text/wiki/Typst-Getting-Started)** | [Getting started](https://github.com/m3scalinum/text/wiki/Typst-Getting-Started), [Compiling](https://github.com/m3scalinum/text/wiki/Typst-Compiling), [Templates](https://github.com/m3scalinum/text/wiki/Typst-Templates), [Diagnostics](https://github.com/m3scalinum/text/wiki/Typst-Diagnostics), [Packages and fonts](https://github.com/m3scalinum/text/wiki/Typst-Packages-and-Fonts), [Dependency injection](https://github.com/m3scalinum/text/wiki/Typst-Dependency-Injection), [Performance](https://github.com/m3scalinum/text/wiki/Typst-Performance), [Troubleshooting](https://github.com/m3scalinum/text/wiki/Typst-Troubleshooting) |
| **[Documents](https://github.com/m3scalinum/text/wiki/Documents-Model)** | [The document model](https://github.com/m3scalinum/text/wiki/Documents-Model), [Importing HTML](https://github.com/m3scalinum/text/wiki/Documents-Html-Import), [Importing Markdown](https://github.com/m3scalinum/text/wiki/Documents-Markdown-Import) |
| **[Pdf](https://github.com/m3scalinum/text/wiki/Pdf-Getting-Started)** | [Getting started](https://github.com/m3scalinum/text/wiki/Pdf-Getting-Started), [Signing](https://github.com/m3scalinum/text/wiki/Pdf-Signing), [Conformance](https://github.com/m3scalinum/text/wiki/Pdf-Conformance), [Pages and forms](https://github.com/m3scalinum/text/wiki/Pdf-Pages-and-Forms), [Reading text](https://github.com/m3scalinum/text/wiki/Pdf-Text), [Encryption](https://github.com/m3scalinum/text/wiki/Pdf-Encryption), [Outlines and attachments](https://github.com/m3scalinum/text/wiki/Pdf-Outlines-and-Attachments), [Batches and trust](https://github.com/m3scalinum/text/wiki/Pdf-Batches-and-Trust), [Converting to PDF/A](https://github.com/m3scalinum/text/wiki/Pdf-Conversion) |
| **Formats** | [Barcodes](https://github.com/m3scalinum/text/wiki/Barcodes), [Reading DOCX](https://github.com/m3scalinum/text/wiki/Docx), [Reading XLSX](https://github.com/m3scalinum/text/wiki/Xlsx), [OCR](https://github.com/m3scalinum/text/wiki/Ocr) |
| **[MCP](https://github.com/m3scalinum/text/wiki/Mcp-Server)** | [The MCP server](https://github.com/m3scalinum/text/wiki/Mcp-Server), [Building from source](https://github.com/m3scalinum/text/wiki/Typst-Building-from-Source) |

The [specifications](docs/specifications/README.md) are the contract the implementation is held to:
one document per area, numbered, with an index grouped by subject.

## Building from source

Requires the .NET 11 SDK and Rust 1.92 or newer.

```bash
cargo build --release --manifest-path native/sharp-typst/Cargo.toml
dotnet build -c Release
dotnet test tests/Text.Typst.Tests -c Release
```

Set `SHARPTYPST_NATIVE_PATH` to the built `libsharp_typst` to run the integration tests against it.
See [Building from Source](https://github.com/m3scalinum/text/wiki/Typst-Building-from-Source)
for the full sequence, the coverage commands and the packaging checks.

## Verifying a release

Every release attaches a CycloneDX SBOM per `.nupkg`, listing the packages it references, the Rust
crates its native library was built from with their versions, and the RIDs it ships; each package
and each SBOM also carries a build provenance attestation signed by the release workflow. Both are
checked by `gh attestation verify`, which is why the repository is public:

```bash
gh attestation verify M3scalinum.Text.Docx.0.1.0.nupkg --repo m3scalinum/text
```

What the SBOM contains, what the attestation proves and what it does not, and the check that the
two agree, are in
[08 §12](docs/specifications/08-build-and-packaging.md#12-sbom-and-build-provenance).

## Project status

Not yet on nuget.org. Every suite runs green on the current tree; the per-suite counts, the eleven
projects and which suites need a native artifact are in
[09-testing.md](docs/specifications/09-testing.md#1-the-suites).

The `coverage` job fails below 90 % of production lines. Native AOT publish, a 10 000-cycle leak test
and a benchmark regression gate are in place. The public API of `M3scalinum.Text.Typst` is the
last thing to settle before 1.0.

## Contributing

Issues and pull requests are welcome. Read [CONTRIBUTING.md](CONTRIBUTING.md) for the build, the test
suites and the gates CI enforces. Changes to behaviour belong in `CHANGELOG.md` under `[Unreleased]`,
and in the matching train file — `CHANGELOG-Typst.md` or `CHANGELOG-Text.md` — when the package
itself changes.

This project follows the [Contributor Covenant](CODE_OF_CONDUCT.md). To report a vulnerability, use
[SECURITY.md](SECURITY.md) rather than a public issue.

## License

[Apache-2.0](LICENSE), the same license as Typst. That is deliberate: the compiler packages embed the
Typst compiler, so sharing its license keeps the combined work trivially compliant and carries
Typst's patent grant (section 3) and trademark clause (section 6) through to consumers.

`NOTICE` carries the attribution, and `THIRD-PARTY-NOTICES.md` reproduces the notices Typst itself
carries for the components it embeds — required by section 4(d). Both ship inside the NuGet packages.

M3scalinum.Text is an independent project. It is not affiliated with, endorsed by, or sponsored by
Typst GmbH. "Typst" is a trademark of Typst GmbH.
