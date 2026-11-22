# M3scalinum.Text

Documents, in .NET, without leaving the process. The suite holds the [Typst](https://typst.app)
compiler in memory behind a managed API, and builds on it where a document is not enough: a signature,
an e-invoice, the attachment the French and German ecosystems exchange.

It ships as **two release trains**, each with its own version and its own release notes, because they
answer different questions:

| Train | Packages | Version | Why its own number |
| --- | --- | --- | --- |
| **Typst** | `M3scalinum.Text.Typst`, `.Documents`, `.DependencyInjection`, `.Templates.Generator` | `0.15.1.<revision>`, the embedded Typst version plus a build count | the number *is* the compiler it carries |
| **Suite** | `M3scalinum.Text.Pdf`, `.Templates`, `.Mcp`, `.Barcodes`, `.Docx`, `.Ocr` | whatever the tag names (`v0.51.2` → `0.51.2`) | code with no compiler in it must not jump when Typst does |

## Start here

These three carry most of the value.

**[M3scalinum.Text.Typst](Typst-Getting-Started)** — compile Typst in-process. PDF, SVG, PNG,
HTML, Markdown, plain text, bundles. No process to spawn, no binary on `PATH`, no temporary files.

```csharp
using M3scalinum.Text.Typst.Compilation;

var pdf = TypstCompiler.CompileToPdf("""
    #set page(paper: "a4", margin: 2cm)

    = Hello

    A document compiled in-process.
    """);

File.WriteAllBytes("hello.pdf", pdf);
```

**[M3scalinum.Text.Pdf](Pdf-Getting-Started)** — sign what you produced, and change it. PAdES-B
written as an incremental update, so a PDF/A document stays PDF/A and an earlier signature stays
valid; plus pages, forms, text, encryption, conformance, outlines, attachments, batch manifests and
a page rendered as a PNG.

```csharp
using M3scalinum.Text.Pdf;

var signed = PdfSigner.Sign(pdf, certificate, new PdfSignatureOptions { Reason = "Invoice" });
```

## The rest of the suite

- **`M3scalinum.Text.Documents`** — the document model every other package builds: typed content,
  styles, tables, figures, lists, page setup, and the HTML and Markdown importers. No compiler, no
  native code. → [The document model](Documents-Model)
- **`M3scalinum.Text.Templates`** — seven business layouts (invoice, quotation, account statement,
  attestation, contract, payslip, purchase order) with typed options and English or French labels.
  → [Templates](Typst-Templates)
- **`M3scalinum.Text.Docx`** — read a Word document into the document model, then render it like any
  other. → [Reading Word documents](Docx)
- **`M3scalinum.Text.Xlsx`** — read an Excel workbook into typed sheets and cells, then render it as a
  table. → [Reading Excel workbooks](Xlsx)
- **`M3scalinum.Text.Barcodes`** — 19 symbologies, 15 of them written and read: QR, Micro QR,
  DataMatrix, Aztec, PDF417, Code 128/39/93, Telepen, EAN-13/8, UPC-A/E, ITF, Codabar, GS1-128 and
  the DataBar family, encoded to SVG or PNG, decoded back one symbol or a whole page. Plus the GS1
  element string and Digital Link. → [Barcodes](Barcodes)
- **`M3scalinum.Text.Ocr`** — text, lines and boxes out of a page image or a scanned PDF, with the
  models the caller supplies. → [OCR](Ocr)
- **`M3scalinum.Text.Mcp`** — a Model Context Protocol server over stdio or HTTP: render, list the
  layouts, sign, merge and read PDFs, writing nothing to disk unless the host says so.
  → [The MCP server](Mcp-Server)
- **`M3scalinum.Text.Typst.DependencyInjection`** — `AddTypst` for
  `Microsoft.Extensions.DependencyInjection`, with validated options.
  → [Dependency injection](Typst-Dependency-Injection)

## Where to go

**[Typst](Typst-Getting-Started)** — the compiler
[Getting Started](Typst-Getting-Started) · [Compiling](Typst-Compiling) · [Templates](Typst-Templates) · 
[Diagnostics](Typst-Diagnostics) · [Packages and Fonts](Typst-Packages-and-Fonts) · 
[Dependency Injection](Typst-Dependency-Injection) · [Performance](Typst-Performance) · 
[Troubleshooting](Typst-Troubleshooting) · [Building from Source](Typst-Building-from-Source)

**[Documents](Documents-Model)** — the document model
[The document model](Documents-Model) · [Importing HTML](Documents-Html-Import) · 
[Importing Markdown](Documents-Markdown-Import)

**[Pdf](Pdf-Getting-Started)** — sign, change and read a PDF
[Getting Started](Pdf-Getting-Started) · [Signing](Pdf-Signing) · [Checking conformance](Pdf-Conformance) · 
[Pages and forms](Pdf-Pages-and-Forms) · [Reading text](Pdf-Text) · [Encryption](Pdf-Encryption) · 
[Outlines and attachments](Pdf-Outlines-and-Attachments) · [Batches and trust](Pdf-Batches-and-Trust) · 
[Converting to PDF/A](Pdf-Conversion)

[Barcodes](Barcodes) · [Reading DOCX](Docx) · [OCR](Ocr)

**[MCP](Mcp-Server)** — serve it over the Model Context Protocol
[The MCP server](Mcp-Server)

Nothing is published on nuget.org yet. The build, the tests and the packaging are documented in
[Building from Source](Typst-Building-from-Source).
