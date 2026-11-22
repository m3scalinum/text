# Changelog

M3scalinum.Text is a suite of document packages for .NET. It ships as two release trains, each with
its own version number, its own tag namespace and its own changelog:

| Train | Packages | Version | Detail |
| --- | --- | --- | --- |
| Compiler | `M3scalinum.Text.Typst`, `.Documents`, `.DependencyInjection`, `.Templates.Generator` | follows the embedded Typst, `0.15.1.N` | [CHANGELOG-Typst.md](CHANGELOG-Typst.md) |
| Suite | `M3scalinum.Text.Pdf`, `.Templates`, `.Mcp`, `.Barcodes`, `.Ocr`, `.Docx`, `.Xlsx` | follows `TextVersion`, `0.1.N` | [CHANGELOG-Text.md](CHANGELOG-Text.md) |

This file records what the project gains as a whole. The per-package detail lives in the two train
files and is not repeated here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

Not yet published on nuget.org.

### Added

- A Typst compiler that runs in-process behind a managed API: PDF, SVG, PNG, HTML, Markdown, plain
  text and bundles, with no process to spawn, no binary on `PATH` and no temporary files.
- A fluent document model and a template system with typed parameters, generated from a
  `template.toml` manifest at compile time.
- PAdES-B signatures written as incremental updates, so signing a PDF/A document leaves it PDF/A and
  an earlier signature stays valid.
- Signature verification, PAdES B-T and B-LT (RFC 3161 timestamps and a DSS carrying the chain and
  revocation data), a document timestamp over the whole file, and a visible signature appearance.
- A structural conformance check for PDF/UA-1 and PDF/A read straight from the bytes: tagged
  structure, language and title, alternative text on figures, font embedding and `ToUnicode`, the
  cross-reference chain and the XMP identifier. veraPDF remains the reference validator.
- HTML import into the document model, with a published fidelity contract: prose, headings, nested
  lists, tables with spanning cells, inline emphasis, code, links, images from registered assets,
  block quotes and definition lists.
- A template pack of typed business layouts — invoice, quotation, account statement, attestation,
  contract, payslip and purchase order — bilingual, embedded, resolvable by id and version, with
  dependency-injection registration.
- Page operations on a PDF — merge, extract, split, rotate, remove — and AcroForm filling and
  flattening. A field the flattener cannot draw stays interactive and is reported, never silently
  dropped.
- Per-page text extraction from the paged layout, so a produced document can be indexed without
  re-deriving its pages from the Markdown export.
- Text extraction from a PDF the library did not produce — glyphs through each font's `ToUnicode`
  map, one string per page, with the fallbacks a page had to make reported instead of guessed.
- Reading and writing AES-encrypted documents (AES-256 and AES-128), with the permission flags
  returned to the caller and every other operation refusing an encrypted file rather than rewriting
  it half-way.
- A Model Context Protocol server over stdio: `render_document`, `list_templates`, `sign_document`,
  `merge_documents` and `extract_text`, with size limits and no ambient file access.
- Barcodes in both directions (QR Code, DataMatrix, Aztec, PDF417, Code 128/39, EAN-13/8, ITF,
  Codabar) over the `rxing` crate, and OCR over `ocrs` with the models the caller supplies.
- A Word document read into the document model over `zip` and `roxmltree`, then renderable by the
  compiler like any other document.
- An `.xlsx` workbook read into the document model over `calamine`, typed cells and cached formula
  values included, and rendered as a table — a spreadsheet reaches a PDF with no Excel present.
- The document model written back out as Markdown and as semantic HTML, each naming the constructs it
  could not represent, so a document built in C# leaves the library without paying the compiler for
  plain text.
- A per-package accessibility checklist for the European Accessibility Act, with a test per
  automatically checkable line, and the lines that only a screen reader run can settle named as such.
- One XML entry point for the DOCX reader that refuses DTDs, external entities, entity expansion
  bombs, and parts past a size or a depth bound.
- Accessible documents built from the model: alternative text on figures, table header cells with
  their scope, and the document language — the three lines the accessibility checklist could only
  report as violations before.
- Bookmarks and file attachments read from a PDF and written back onto one as an incremental update,
  leaving an existing signature valid.
- A written threat model for every parser the suite exposes, with a surface → limit → test table that
  names the lines no test covers; `cargo deny` over licences and advisories on each crate; a check
  that every declared `rust-version` matches its dependency graph; and one example per package built
  from its `.nupkg` in CI.
- Merged cells, per-language number and date formatting, and a negotiated native ABI revision in the
  document model; a sealed manifest for an archived batch of PDFs; a revocation seam for B-LT
  signatures that never touches the network; and an OCR that refuses an unreadable page instead of
  returning approximate text.
- `linux-musl-x64` for every native package — the default Alpine container loads them — and a
  CycloneDX SBOM plus a build attestation on every release.
- A document style applied once, numbered cross references and a table of contents whose page numbers
  come from the layout; a PDF page rendered to PNG for preview and OCR; the GS1 family of barcodes in
  both directions; OCR preprocessing for rotated, skewed and noisy scans; and the European trust list
  read so a qualified signature can be told from a merely valid one.
- Notes and figures numbered per section, table headers repeated on every page, and a row that can be
  kept whole across a page break — all resolved by the layout, verified against the real compiler.
- Per-compilation metrics on the result: duration, pages, fonts and their bytes, diagnostics by
  severity, and the size of the last export.
- Every barcode on a page decoded under pixel and byte ceilings; OCR models validated, fingerprinted
  and discovered from a directory so a report can name the weights it used.
- Ceilings where a received document could cost unbounded work: the PDF XMP packet, a Flate stream's
  decoded size, the XLSX sheet and text counts.
- Barcodes that know what paper they land on — physical size, quiet zone and a refused undersized
  module — and a symbol placed in a document as a figure; an eIDAS seal and the qualified-timestamp
  verdict that says "not evaluated" rather than "qualified" without a trust list; and PDF/A-4f, which
  carries its files or is refused by name.
- A font inventory per PDF export, so a caller can see what was embedded and what it costs.
- `cargo fuzz` over the four format parsers on a weekly cadence, with a crash failing the job and
  leaving a replayable input behind, and a per-package coverage table published in the run summary.
- Fonts scanned once per process, with the index persisted so the next process reads a file; a document
  laid out in columns across pages, with notes in the margin; and a QR code dressed in brand colours
  with a reserved centre, every level proven decodable by the suite's own decoder.
- Recognition and verification that say what they did: a page whose language has no model is
  recognised with the default one and the result names both, and a lot of signed PDFs is verified in one
  pass with the certificates each chain lacked named.
- An MCP client cannot render without bound: a per-session quota over a sliding minute, a journal of
  what was rendered, one table behind both `tools/list` and `tools/call`, the template pack as read-only
  resources, and an `Origin` check on the HTTP transport.
- Documents that carry their apparatus: a bibliography read from BibTeX with two citation styles and an
  error naming the key it could not resolve, an alphabetical index whose pages come from the real page
  counter, a header, a footer and a watermark per section with an unnumbered cover, and a language tag and
  a direction per block — Japanese justified under its own line-break rules, Arabic laid out in
  bidirectional order.
- Verification that names what it authenticated: the signature of an OCSP response or a CRL, and the
  signature of a trust list, with a qualification verdict that says whether the list behind it was
  verified, stale or unverified.
- A shipping label rendered by the suite, whose SSCC decodes back from the produced PDF, and a symbology
  list held to its specification by a test rather than by discipline.
- An image handed to the OCR is refused on its header, before allocation, past 64 megapixels or 256 MiB.
- `linux-musl-arm64` for the five native packages, with an Alpine arm64 job that loads each library from
  the package, so Graviton and Apple Silicon containers stop meeting `DllNotFoundException`. The package
  weights are measured and published: 168.32 MiB per download, 86.5 % of it unusable on win-x64, with the
  split costed and the reason it is not done yet written down.
