# M3scalinum.Text

Documents, in .NET, without leaving the process. The suite holds the [Typst](https://typst.app)
compiler in memory behind a managed API, and builds on it where a document is not enough: a signature,
an e-invoice, the attachment the French and German ecosystems exchange.

It ships as **two release trains**, each with its own version and its own release notes, because they
answer different questions:

| Train | Packages | Version | Why its own number |
| --- | --- | --- | --- |
| **SharpTypst** | `M3scalinum.Text.SharpTypst`, `.DependencyInjection`, `.Templates.Generator` | `0.15.1.<revision>`, the embedded Typst version plus a build count | the number *is* the compiler it carries |
| **Suite** | `M3scalinum.Text.Pdf`, `M3scalinum.Text.Pdf.FactureX` | whatever the tag names (`v0.51.2` → `0.51.2`) | code with no compiler in it must not jump when Typst does |

## The packages

**[M3scalinum.Text.SharpTypst](SharpTypst-Getting-Started)** — compile Typst in-process. PDF, SVG, PNG,
HTML, Markdown, plain text, bundles. No process to spawn, no binary on `PATH`, no temporary files.

```csharp
using M3scalinum.Text.SharpTypst.Compilation;

var pdf = TypstCompiler.CompileToPdf("""
    #set page(paper: \"a4\", margin: 2cm)

    = Hello

    A document compiled in-process.
    """);

File.WriteAllBytes("hello.pdf", pdf);
```

**[M3scalinum.Text.Pdf](Pdf-Getting-Started)** — sign what you produced. PAdES-B written as an
incremental update, so a PDF/A document stays PDF/A and an earlier signature stays valid.

```csharp
using M3scalinum.Text.Pdf;

var signed = PdfSigner.Sign(pdf, certificate, new PdfSignatureOptions { Reason = "Invoice" });
```

**[M3scalinum.Text.Pdf.FactureX](FactureX-Getting-Started)** — the e-invoice. Build it in C#, validate
it against the EN 16931 rules, render a PDF/A-3 whose XML is attached and whose XMP declares the
profile; or take one apart, XML and all.

```csharp
using M3scalinum.Text.Pdf.FactureX;

var invoice = FacturXReader.Read(File.ReadAllText("invoice.xml"));
byte[] document = FacturXRenderer.Render(invoice);
```

## Where to go

|[SharpTypst](SharpTypst-Getting-Started)|[Pdf](Pdf-Getting-Started)|[FactureX](FactureX-Getting-Started)|
| --- | --- | --- |
| [Getting Started](SharpTypst-Getting-Started) | [Getting Started](Pdf-Getting-Started) | [Getting Started](FactureX-Getting-Started) |
| [Compiling](SharpTypst-Compiling) | [Signing](Pdf-Signing) | [The invoice model](FactureX-Model) |
| [Document Model](SharpTypst-Document-Model) | [Checking conformance](Pdf-Conformance) | [Rendering](FactureX-Rendering) |
| [Templates](SharpTypst-Templates) | [Pages and forms](Pdf-Pages-and-Forms) | [Reading](FactureX-Reading) |
| [Importing HTML](SharpTypst-Html-Import) | [Reading text](Pdf-Text) | [Validation](FactureX-Validation) |
| [Diagnostics](SharpTypst-Diagnostics) | [Encryption](Pdf-Encryption) | [The French profile](FactureX-French-Profile) |
| [Packages and Fonts](SharpTypst-Packages-and-Fonts) | [The MCP server](Mcp-Server) | |
| [Barcodes](Barcodes) | [OCR](Ocr) | [Reading DOCX](Docx) |
| [Charts](Charts) | [EPUB](Epub) | [Converting to PDF/A](Pdf-Conversion) |
| [Dependency Injection](SharpTypst-Dependency-Injection) | | |
| [Performance](SharpTypst-Performance) | | |
| [Troubleshooting](SharpTypst-Troubleshooting) | | |
| [Building from Source](SharpTypst-Building-from-Source) | | |

Nothing is published on nuget.org yet. The build, the tests and the packaging are documented in
[Building from Source](SharpTypst-Building-from-Source).
