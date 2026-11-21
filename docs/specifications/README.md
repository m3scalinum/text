# M3scalinum.Text.SharpTypst — Specifications

This directory is the normative description of M3scalinum.Text.SharpTypst: what it is, how it is layered, what
the native contract is, and how it is built, packaged and tested. The documents describe the
intended final state of the system, not its history. When code and specification disagree, one of
them is a bug.

## Reading order

| # | Document | Answers |
| --- | --- | --- |
| 00 | [00-overview.md](00-overview.md) | Scope, terms, use cases, quality attributes |
| 01 | [01-architecture.md](01-architecture.md) | Layers, projects, dependency rules, threading, lifetime |
| 02 | [02-native-abi.md](02-native-abi.md) | The C ABI: handles, buffers, errors, versioning, payloads |
| 03 | [03-compilation.md](03-compilation.md) | Worlds, sources, fonts, packages, export formats, determinism |
| 04 | [04-document-model.md](04-document-model.md) | The fluent builder and its Markup writer |
| 05 | [05-templates.md](05-templates.md) | Template packages, typed parameters, resolution |
| 06 | [06-markdown-and-text-export.md](06-markdown-and-text-export.md) | Semantic extraction and its fidelity contract |
| 07 | [07-diagnostics.md](07-diagnostics.md) | Diagnostic model, mapping, exception taxonomy |
| 08 | [08-build-and-packaging.md](08-build-and-packaging.md) | Solution layout, MSBuild props, NuGet, CI, releases |
| 09 | [09-testing.md](09-testing.md) | Test pyramid, fixtures, golden files, matrices |
| 11 | [11-reference-coverage.md](11-reference-coverage.md) | Every item of the Typst reference and how it is reachable |
| 12 | [12-dependency-injection.md](12-dependency-injection.md) | The `M3scalinum.Text.SharpTypst.DependencyInjection` package: services, lifetimes, options, validation |
| 13 | [13-facturx.md](13-facturx.md) | The `M3scalinum.Text.Pdf.FactureX` package: reading, validating and rendering e-invoices |
| 14 | [14-pdf-signature.md](14-pdf-signature.md) | The `M3scalinum.Text.Pdf` package: PAdES signatures as an incremental update |
| 15 | [15-pdf-conformance.md](15-pdf-conformance.md) | The `PdfConformance` checker: which structural rules it establishes, and what veraPDF still validates |
| 16 | [16-pdf-pages-and-forms.md](16-pdf-pages-and-forms.md) | `PdfPages` and `PdfForm`: merge, extract, split, rotate, remove pages; fill and flatten an AcroForm |
| 17 | [17-mcp.md](17-mcp.md) | The `M3scalinum.Text.Mcp` server: protocol surface, tools, limits and security posture |
| 18 | [18-pdf-text.md](18-pdf-text.md) | `PdfText`: what a received document draws, what is lost, and the per-page fallbacks |
| 19 | [19-pdf-encryption.md](19-pdf-encryption.md) | `PdfEncryption`: reading and writing AES-encrypted documents, and what encryption does to a signature |
| 20 | [20-barcodes.md](20-barcodes.md) | `M3scalinum.Text.Barcodes`: encoding and decoding symbologies in-process, over `rxing` |
| 21 | [21-ocr.md](21-ocr.md) | `M3scalinum.Text.Ocr`: text from page images, with caller-supplied models and no download |
| 22 | [22-docx.md](22-docx.md) | `M3scalinum.Text.Docx`: reading a Word document into the document model |
| 23 | [23-charts.md](23-charts.md) | `M3scalinum.Text.Charts`: a typed chart model that emits SVG, with no dependency |
| 25 | [25-epub.md](25-epub.md) | `M3scalinum.Text.Epub`: a valid EPUB 3 container, built by hand |
| 26 | [26-pdfa-conversion.md](26-pdfa-conversion.md) | `PdfAConverter`: turning an existing PDF into PDF/A, and refusing when it cannot |

## Status

| Area | State |
| --- | --- |
| Specifications | This document set — baseline agreed |
| `native/sharp-typst` (Rust cdylib) | v0.3: world, paged + HTML compilation, all six export formats |
| `src/Text.SharpTypst.Native` (P/Invoke) | v0.3: interop, handles, resolver, error mapping |
| `src/Text.SharpTypst` (managed API) | World builder, compiler, `CompileBoth`, export options, packages, evaluation, templates, document model |
| `src/Text.SharpTypst.DependencyInjection` | `AddSharpTypst`, options, validation |
| `src/Text.SharpTypst.Templates.Generator` | Roslyn generator: `template.toml` → typed options record |
| `src/Text.SharpTypst.Templates` | The layout pack: invoice, quotation, statement and attestation, typed, bilingual labels, catalog and DI |
| `src/Text.Pdf` | PAdES-B signatures as an incremental update, page merge/extract/split/rotate/remove, AcroForm fill and flatten, AES-256 and AES-128 encryption, structural PDF/A and PDF/UA conformance checks, text extraction from a received PDF; no dependency on the compiler |
| `src/Text.Pdf.FactureX` | CII/UBL reader, EN 16931 checks, CII writer, Factur-X renderer and XMP patch |
| `src/Text.Mcp` | JSON-RPC 2.0 over stdio: `render_document`, `render_invoice`, `list_templates`, with limits and no ambient file access |
| `src/Text.Barcodes` | Ten symbologies encoded to SVG or PNG and decoded back, over a Rust `rxing` library of its own |
| `src/Text.Ocr` | Text, lines and boxes from a page image or a PDF, with caller-supplied models and no download |
| `src/Text.Docx` | Word documents read into the document model over `zip` 9 and `roxmltree` |
| `src/Text.Charts` | Seven chart kinds from a typed model, emitted as SVG, with no dependency |
| `src/Text.Epub` | A valid EPUB 3 container built from the compiler's HTML |
| Tests | 459 unit (fake native) + 345 Pdf + 364 Factur-X + 111 templates + 112 mcp + 105 barcodes + 46 docx + 106 epub + 82 charts + 99 ocr + 7 suite + 14 generator + 251 integration (real compiler) |
| Benchmarks | `benchmarks/Text.SharpTypst.Benchmarks`, baseline stored, `--check` gate in CI |
| Build & packaging | Build files in place; native packaging wired in CI |

## Conventions used in these documents

- **MUST / MUST NOT / SHOULD / MAY** carry their RFC 2119 meaning. A *MUST* is a testable
  invariant: a test exists for it, or one is owed.
- Code blocks marked `csharp` are the intended public API at the shape level. Names in these
  documents are normative; whitespace and formatting are not.
- Code blocks marked `c` are the ABI surface. Symbols named there MUST exist with that exact
  spelling.
- Rust snippets describe intent, not final syntax.
- Decisions that could reasonably have gone another way are recorded as **Decision** with the
  rationale and the rejected alternative, so they are not re-litigated later.

## Scope boundary in one paragraph

M3scalinum.Text.SharpTypst compiles Typst source and exports it. It does not implement layout, does not host a
service, and does not parse Typst. Everything a caller can control is either a Typst feature
surfaced as a typed option or an environment input (source bytes, font bytes, package bytes,
clock). Anything else is out of scope and belongs in Typst itself.
