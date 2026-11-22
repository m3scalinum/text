# M3scalinum.Text — Specifications

This directory is the normative description of the suite: what it is, how it is layered, what the
native contracts are, and how it is built, packaged and tested. The documents describe the intended
final state of the system, not its history. When code and specification disagree, one of them is a
bug.

The numbers are stable identifiers, not a reading order. A document keeps its number when a new one
is inserted between two existing ones, which is why 10 and 24 are absent: read this index, not the
file names, to find what you want.

## The compiler

| # | Document | Answers |
| --- | --- | --- |
| 00 | [00-overview.md](00-overview.md) | Scope, terms, use cases, quality attributes |
| 01 | [01-architecture.md](01-architecture.md) | Layers, projects, dependency rules, threading, lifetime |
| 02 | [02-native-abi.md](02-native-abi.md) | The C ABI: handles, buffers, errors, versioning, payloads |
| 03 | [03-compilation.md](03-compilation.md) | Worlds, sources, fonts, packages, export formats, determinism |
| 07 | [07-diagnostics.md](07-diagnostics.md) | Diagnostic model, mapping, exception taxonomy |
| 11 | [11-reference-coverage.md](11-reference-coverage.md) | Every item of the Typst reference and how it is reachable |

## The document model and what extends it

| # | Document | Answers |
| --- | --- | --- |
| 04 | [04-document-model.md](04-document-model.md) | The fluent builder and its Markup writer, in `M3scalinum.Text.Documents` |
| 05 | [05-templates.md](05-templates.md) | Template packages, typed parameters, resolution, the Roslyn generator |
| 06 | [06-markdown-and-text-export.md](06-markdown-and-text-export.md) | Semantic extraction and its fidelity contract |
| 27 | [27-html-export.md](27-html-export.md) | Writing the model as semantic HTML5, and what HTML cannot hold |
| 12 | [12-dependency-injection.md](12-dependency-injection.md) | The `M3scalinum.Text.Typst.DependencyInjection` package: services, lifetimes, options, validation |
| 32 | [32-localized-formatting.md](32-localized-formatting.md) | `TypstFormatting`: dates, numbers and amounts per language, with no culture lookup |

## The PDF family

| # | Document | Answers |
| --- | --- | --- |
| 14 | [14-pdf-signature.md](14-pdf-signature.md) | PAdES signatures as an incremental update, and what they preserve |
| 15 | [15-pdf-conformance.md](15-pdf-conformance.md) | What `PdfConformance` establishes, and what veraPDF still validates |
| 16 | [16-pdf-pages-and-forms.md](16-pdf-pages-and-forms.md) | `PdfPages` and `PdfForm`: merge, extract, split, rotate, remove; fill and flatten |
| 18 | [18-pdf-text.md](18-pdf-text.md) | `PdfText`: what a received document draws, what is lost, the per-page fallbacks |
| 19 | [19-pdf-encryption.md](19-pdf-encryption.md) | `PdfEncryption`: AES-encrypted documents, and what encryption does to a signature |
| 26 | [26-pdfa-conversion.md](26-pdfa-conversion.md) | `PdfAConverter`: turning an existing PDF into PDF/A, and refusing when it cannot |
| 31 | [31-pdf-attachments.md](31-pdf-attachments.md) | `PdfAttachments`: listing, extracting, adding and removing embedded files, and what that implies for PDF/A-4f |
| 33 | [33-archive-integrity.md](33-archive-integrity.md) | `PdfBatchManifest`: sealing a batch of files as SHA-256 fingerprints, and verifying it incrementally |
| 36 | [36-trust-lists.md](36-trust-lists.md) | `PdfLotl`: reading the European LOTL and national trust lists, and what a verifier concludes from them |

## Other formats and engines

| # | Document | Answers |
| --- | --- | --- |
| 20 | [20-barcodes.md](20-barcodes.md) | `M3scalinum.Text.Barcodes`: encoding and decoding symbologies in-process, over `rxing` |
| 21 | [21-ocr.md](21-ocr.md) | `M3scalinum.Text.Ocr`: text from page images, with caller-supplied models and no download |
| 22 | [22-docx.md](22-docx.md) | `M3scalinum.Text.Docx`: reading a Word document into the document model |
| 28 | [28-xlsx.md](28-xlsx.md) | `M3scalinum.Text.Xlsx`: reading an Excel workbook into typed sheets, over `calamine` |
| 29 | [29-threat-model.md](29-threat-model.md) | Every received document is hostile: the parsing surfaces, their limits, the test for each, and what is not bounded |

## The protocol server

| # | Document | Answers |
| --- | --- | --- |
| 17 | [17-mcp.md](17-mcp.md) | The `M3scalinum.Text.Mcp` server: protocol surface, tools, limits and security posture |

## Building, packaging and testing

| # | Document | Answers |
| --- | --- | --- |
| 08 | [08-build-and-packaging.md](08-build-and-packaging.md) | Solution layout, MSBuild props, NuGet, CI, the two release trains |
| 09 | [09-testing.md](09-testing.md) | Every suite and its test count, fixtures, golden files, coverage, matrices |
| — | [dependencies.md](dependencies.md) | The Rust crate policy: licence, traction, last update and decision per crate, and the check that enforces them |

## Status

| Area | State |
| --- | --- |
| Specifications | This document set — baseline agreed |
| `native/sharp-typst` (Rust cdylib) | v0.3: world, paged + HTML compilation, all six export formats |
| `src/Text.Typst.Native` (P/Invoke) | v0.3: interop, handles, resolver, error mapping |
| `src/Text.Documents` (document model) | Content, styles, tables, figures, page setup, HTML and Markdown importers |
| `src/Text.Typst` (managed API) | World builder, compiler, `CompileBoth`, export options, packages, evaluation, templates |
| `src/Text.Typst.DependencyInjection` | `AddTypst`, options, validation |
| `src/Text.Templates.Generator` | Roslyn generator: `template.toml` → typed options record |
| `src/Text.Templates` | The layout pack: invoice, quotation, statement and attestation, typed, bilingual labels, catalog and DI |
| `src/Text.Pdf` | PAdES-B signatures as an incremental update, page merge/extract/split/rotate/remove, AcroForm fill and flatten, AES-256 and AES-128 encryption, structural PDF/A and PDF/UA conformance checks, text extraction from a received PDF; no dependency on the compiler |
| `src/Text.Mcp` | JSON-RPC 2.0 over stdio: `render_document`, `render_invoice`, `list_templates`, with limits and no ambient file access |
| `src/Text.Barcodes` | Ten symbologies encoded to SVG or PNG and decoded back, over a Rust `rxing` library of its own |
| `src/Text.Ocr` | Text, lines and boxes from a page image or a PDF, with caller-supplied models and no download |
| `src/Text.Docx` | Word documents read into the document model over `zip` 9 and `roxmltree` |
| `src/Text.Xlsx` | Excel workbooks read into typed sheets and cells over `calamine` 0.38, then rendered as a table |
| Tests | 1 590 across eleven suites — see [09](09-testing.md#1-the-suites) |
| Benchmarks | `benchmarks/Text.Typst.Benchmarks`, baseline stored, `--check` gate in CI |
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

M3scalinum.Text.Typst compiles Typst source and exports it. It does not implement layout, does not
host a service, and does not parse Typst. Everything a caller can control is either a Typst feature
surfaced as a typed option or an environment input (source bytes, font bytes, package bytes,
clock). Anything else is out of scope and belongs in Typst itself. No package owns the rules of a
jurisdiction, a channel a document is deposited into, or a crawl of the network: those are seams the
caller plugs — see [00 §2.3](00-overview.md#23-the-document-test).
