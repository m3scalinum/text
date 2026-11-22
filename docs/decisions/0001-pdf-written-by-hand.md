# 0001 — PDF is written by hand, not through a PDF library

**Status:** accepted. **Area:** `M3scalinum.Text.Pdf`, file syntax.

## Decision

`M3scalinum.Text.Pdf` serialises PDF itself. It takes no dependency on `lopdf`, `pdf-writer`, `printpdf`
or any other crate for writing, and it will not, unless one of them can do what this package's
signature path needs.

## Context

The package does more than produce a file once. It edits a file that already exists:

- `PdfSigner` appends a signature as an incremental update, so an earlier signature and the page it
  covers stay byte-identical.
- `PdfDocumentTimestamp.Add` appends a second update over the whole file, including the revocation
  data the first one added.
- `PdfOutline.Write` and `PdfAttachments.Add` and `.Remove` append an update that restates the catalog
  and chains a new cross-reference section to the previous one by `/Prev`.
- `PdfPages.Merge` and the PDF/A conversion rebuild a document instead, renumbering objects and
  writing a fresh cross-reference table.

Every one of those is a decision about bytes: which object number, which offset, which trailer, which
`/ID`, and what is left untouched. A writer library owns that layout. Using one would mean either
copying the file and re-serialising it — which invalidates a signature that covers the original bytes
— or driving the library into a mode it does not offer.

The dependency policy adds a second reason: every crate in the tree is checked for licence, traction
and maintenance in [dependencies.md](../specifications/dependencies.md), and `lopdf` (MIT) is recorded
there as considered. It is a fine crate for producing a document once. It is not a byte-level editor.

## Consequences

- File-syntax code — objects, streams, xref, trailer — is ours to get right, and it is covered by the
  signature, conformance and round-trip tests rather than by a library's test suite.
- Anything not needed to write, sign, stamp and restructure a document is out of scope: no form
  flattening by a third party, no font subsetting by a third party, no external renderer. Rendering
  reads through `hayro` in `native/text-pdf`; writing stays here.
- A feature that would force a rewrite of the document is refused with a typed error rather than
  performed silently — an xref-stream file cannot be edited incrementally, and `PdfAttachments.Add`
  says so.
