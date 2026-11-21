# M3scalinum.Text

[![CI (Typst)](https://github.com/m3scalinum/text/actions/workflows/ci-sharptypst.yml/badge.svg?branch=main)](https://github.com/m3scalinum/text/actions/workflows/ci-sharptypst.yml)
[![CI (Text suite)](https://github.com/m3scalinum/text/actions/workflows/ci-text.yml/badge.svg?branch=main)](https://github.com/m3scalinum/text/actions/workflows/ci-text.yml)
[![NuGet Typst](https://img.shields.io/nuget/v/M3scalinum.Text.Typst.svg)](https://www.nuget.org/packages/M3scalinum.Text.Typst)
[![NuGet Pdf](https://img.shields.io/nuget/v/M3scalinum.Text.Pdf.svg)](https://www.nuget.org/packages/M3scalinum.Text.Pdf)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](https://github.com/m3scalinum/text/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0%20%7C%2011.0-512BD4.svg)](https://dotnet.microsoft.com)

Documents, in .NET, without leaving the process. The suite holds the [Typst](https://typst.app)
compiler in memory and exposes it as a managed API; on top of it, it signs the PDF it produced and
builds the e-invoice European platforms exchange. No browser, no Office, no process spawn, no
binary on `PATH`.

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
- [M3scalinum.Text.Pdf.FactureX](#m3scalinumtextpdffacturex)
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
| `M3scalinum.Text.Documents` | The document model: typed content, inline styles, tables, figures, lists, page setup, and the HTML and Markdown importers | — |
| `M3scalinum.Text.Typst.DependencyInjection` | `AddTypst` for `Microsoft.Extensions.DependencyInjection` | Typst |
| `M3scalinum.Text.Templates.Generator` | Roslyn generator: a `template.toml` becomes a typed options record | — |
| `M3scalinum.Text.Pdf` | PAdES-B signatures, written as incremental updates | — |
| `M3scalinum.Text.Pdf.FactureX` | Factur-X / ZUGFeRD invoices: read, validate, render, read back | Documents, Typst |
| `M3scalinum.Text.Templates` | Seven business-document layouts — invoice, quotation, account statement, attestation, contract, payslip, purchase order — with typed options and English or French labels | Typst |
| `M3scalinum.Text.Mcp` | A Model Context Protocol server over stdio: render a document or a Factur-X invoice, list the layouts, sign, merge and read PDFs | Typst, Documents, Templates, FactureX, Pdf |
| `M3scalinum.Text.Barcodes` | QR, DataMatrix, Aztec, PDF417, Code 128/39, EAN-13/8, ITF and Codabar: encode to SVG or PNG, decode back | — |
| `M3scalinum.Text.Ocr` | Text, lines and boxes out of a page image, with the models supplied by the caller | — |
| `M3scalinum.Text.Docx` | A Word document read into the document model, then renderable like any other | Documents, Typst |
| `M3scalinum.Text.Charts` | Line, area, bar, stacked bar, scatter, pie, donut and sparkline charts, emitted as SVG | — |
| `M3scalinum.Text.Epub` | An EPUB 3 container built from the HTML the compiler produces | — |

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
application and a repository already speak, into that same model. The rest of the builder — metadata,
footnotes, bibliography, math, counters, labels — is in
[the document model](https://github.com/m3scalinum/text/wiki/Documents-Model).

## M3scalinum.Text.Pdf

Signs a PDF you already have. It takes bytes and returns bytes: no dependency on the compiler, no
native library.

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

The signature is PAdES-B, written as an **incremental update**: the original bytes are untouched, so a
PDF/A document stays PDF/A, an embedded Factur-X attachment stays readable, and an earlier signature
stays valid. Verified with pyHanko (intact, trusted, covering the whole file) and, on a signed
Factur-X invoice, with veraPDF in PDF/A-3B.

## M3scalinum.Text.Pdf.FactureX

The e-invoice: read a CII or UBL invoice, check it against the EN 16931 rules, render a PDF/A-3 whose
XML is attached and whose XMP declares the profile.

```bash
dotnet add package M3scalinum.Text.Pdf.FactureX
```

```csharp
using M3scalinum.Text.Pdf.FactureX;

var invoice = FacturXReader.Read(File.ReadAllText("invoice.xml"));   // CII or UBL
FacturXValidator.Validate(invoice).ThrowIfInvalid();

File.WriteAllBytes("factur-x.pdf", FacturXRenderer.Render(invoice));

// The receiving side: the bytes the sender attached, not a re-serialization of them.
var received = FacturXReader.ReadPdf(File.ReadAllBytes("factur-x.pdf"));
```

The result embeds `factur-x.xml`, declares `fx:ConformanceLevel` in its XMP and is PDF/A-3b. A
conforming invoice is never written from an invalid one: validation runs before the compiler does.
CI validates the embedded XML against the official Factur-X schema with the FNFE-MPE reference
library, and the rendered files against PDF/A-3B with veraPDF.

French profiles are covered: `EXTENDED-CTC-FR`, the billing framework of `BT-23`, SIREN/SIRET, the
three mandatory notes, the place of delivery and cascade invoicing.

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
pack and the e-invoice renderer are all inside the process: nothing is spawned and nothing is
downloaded.

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
| `render_invoice` | Renders a Factur-X PDF/A-3 from an invoice payload, validated against the EN 16931 rules |
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

Five packages, one format each, each usable on its own. None embeds a second engine: what needs a
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

### M3scalinum.Text.Epub — an EPUB 3 container

```csharp
using M3scalinum.Text.Epub;

var book = new EpubBook(new EpubMetadata { Title = "A Field Guide", Language = "en" });
book.AddChapter("First Steps", "<h1>First Steps</h1><p>Hello &amp; welcome.</p>");

File.WriteAllBytes("guide.epub", EpubWriter.Write(book));
```

### M3scalinum.Text.Charts — SVG a report can embed

```csharp
using M3scalinum.Text.Charts;

var spec = ChartSpec.Bar()
    .WithTitle("Revenue by quarter")
    .AddSeries("2026", [new ChartPoint(0, 50, "Q1"), new ChartPoint(0, 70, "Q2")]);

string svg = Chart.RenderSvg(spec, new ChartSize(640, 360));
```

### M3scalinum.Text.Barcodes — QR to Codabar, in-process

```csharp
using M3scalinum.Text.Barcodes;

BarcodeImage image = Barcode.Encode(BarcodeSymbology.QrCode, "BT-1=FA-2026-000123");

byte[] png = image.Png;   // ready to embed in a PDF page
string svg = image.Svg;   // ready to hand to a layout engine
```

### M3scalinum.Text.Ocr — text out of a page image

```csharp
using M3scalinum.Text.Ocr;

using OcrEngine engine = OcrEngine.FromModels(detectionModel, recognitionModel);

OcrResult result = engine.Recognize(File.ReadAllBytes("scan.png"));
Console.WriteLine(result.Text);
```

The models are caller-supplied: the package downloads nothing and bundles no weights.

## Versioning

Two trains, two numbers, because they answer different questions.

| Train | Packages | Version |
| --- | --- | --- |
| **Typst** | the four compiler packages | `0.15.1.<revision>` — the version *is* the embedded Typst, plus a build count |
| **Suite** | `M3scalinum.Text.Pdf`, `.Pdf.FactureX`, `.Templates`, `.Mcp`, `.Barcodes`, `.Charts`, `.Docx`, `.Epub`, `.Ocr` | whatever the tag names: `v0.51.2` publishes `0.51.2` |

A signing fix published as `0.15.1.43` would read as a compiler release, and would jump to `0.16.0`
the day Typst does, for code that contains no compiler at all. So the two trains tag and release
separately: `sharptypst-v0.15.1.42` and `v0.51.2`.

## Supported platforms

- .NET 8, 9, 10 or 11 (`net8.0`, `net9.0`, `net10.0`, `net11.0`)
- `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `linux-musl-x64` (Alpine), `osx` (universal,
  Intel and Apple Silicon)

The native compiler ships inside `M3scalinum.Text.Typst` for each of these. The suite packages
ship managed assemblies only, so they run wherever .NET does.

## Documentation

The **[user guide](https://github.com/m3scalinum/text/wiki)** is organised by package: a getting-started
page for each, then one page per topic.

| Group | Pages |
| --- | --- |
| **[Typst](https://github.com/m3scalinum/text/wiki/Typst-Getting-Started)** | [Getting started](https://github.com/m3scalinum/text/wiki/Typst-Getting-Started), [Compiling](https://github.com/m3scalinum/text/wiki/Typst-Compiling), [Templates](https://github.com/m3scalinum/text/wiki/Typst-Templates), [Diagnostics](https://github.com/m3scalinum/text/wiki/Typst-Diagnostics), [Packages and fonts](https://github.com/m3scalinum/text/wiki/Typst-Packages-and-Fonts), [Dependency injection](https://github.com/m3scalinum/text/wiki/Typst-Dependency-Injection), [Performance](https://github.com/m3scalinum/text/wiki/Typst-Performance), [Troubleshooting](https://github.com/m3scalinum/text/wiki/Typst-Troubleshooting) |
| **[Documents](https://github.com/m3scalinum/text/wiki/Documents-Model)** | [The document model](https://github.com/m3scalinum/text/wiki/Documents-Model), [Importing HTML](https://github.com/m3scalinum/text/wiki/Documents-Html-Import), [Importing Markdown](https://github.com/m3scalinum/text/wiki/Documents-Markdown-Import) |
| **[Pdf](https://github.com/m3scalinum/text/wiki/Pdf-Getting-Started)** | [Getting started](https://github.com/m3scalinum/text/wiki/Pdf-Getting-Started), [Signing](https://github.com/m3scalinum/text/wiki/Pdf-Signing), [Conformance](https://github.com/m3scalinum/text/wiki/Pdf-Conformance), [Pages and forms](https://github.com/m3scalinum/text/wiki/Pdf-Pages-and-Forms), [Reading text](https://github.com/m3scalinum/text/wiki/Pdf-Text), [Encryption](https://github.com/m3scalinum/text/wiki/Pdf-Encryption), [Converting to PDF/A](https://github.com/m3scalinum/text/wiki/Pdf-Conversion) |
| **[FactureX](https://github.com/m3scalinum/text/wiki/FactureX-Getting-Started)** | [Getting started](https://github.com/m3scalinum/text/wiki/FactureX-Getting-Started), [The invoice model](https://github.com/m3scalinum/text/wiki/FactureX-Model), [Rendering](https://github.com/m3scalinum/text/wiki/FactureX-Rendering), [Reading](https://github.com/m3scalinum/text/wiki/FactureX-Reading), [Validation](https://github.com/m3scalinum/text/wiki/FactureX-Validation), [Peppol](https://github.com/m3scalinum/text/wiki/FactureX-Peppol), [XRechnung](https://github.com/m3scalinum/text/wiki/FactureX-XRechnung), [The French profile](https://github.com/m3scalinum/text/wiki/FactureX-French-Profile) |
| **Formats** | [Barcodes](https://github.com/m3scalinum/text/wiki/Barcodes), [Charts](https://github.com/m3scalinum/text/wiki/Charts), [Reading DOCX](https://github.com/m3scalinum/text/wiki/Docx), [EPUB](https://github.com/m3scalinum/text/wiki/Epub), [OCR](https://github.com/m3scalinum/text/wiki/Ocr) |
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

## Project status

Not yet on nuget.org. **2 101 tests** run across thirteen suites on the current tree; the per-suite
counts, and which suites need a native artifact, are in
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
