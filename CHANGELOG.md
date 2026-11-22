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
