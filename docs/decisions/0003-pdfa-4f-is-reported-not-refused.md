# 0003 — A PDF/A-4f export with nothing attached is reported, not refused

**Status:** accepted. **Area:** `M3scalinum.Text.Typst`, PDF export.

## Decision

`ExportPdf` does not refuse a `Standards = [PdfStandard.PdfA4f]` request whose document attaches no
file. The bytes are produced, they declare part 4, level F, and they carry an empty `/EmbeddedFiles`
name tree. `PdfConformance.Check(pdf, PdfConformanceStandard.PdfA4f)` reports
`PDFA4F.EMBEDDED_FILES_MISSING` on them, and the documentation says so.

`PdfAConverter` keeps the opposite behaviour for the same situation: it refuses a 4f conversion of a
document with nothing embedded, and writes no bytes. The difference is who produces the file.

## Context

PDF/A-4f is the level of ISO 19005-4 that permits embedded files of any type and, in exchange,
requires at least one. A 4f claim over a document with nothing embedded is therefore false.

Two paths can make that claim:

- **The converter** rewrites an existing file object by object. It knows, before writing anything,
  whether an `/EmbeddedFiles` tree with an entry exists, and it already refuses the other
  unattainable targets (an untagged `a` level, an unembedded font) on the same principle: a refusal
  says the file cannot reach the target and why, which the caller can act on.
- **The compiler** delegates the whole export to `typst-pdf`. krilla knows the rule — `Archival::A4F`
  returns true from `requires_embedded_files_when_empty` — and uses it to write the empty name tree
  rather than to fail: `chunk_container.rs` asks the validator whether the tree must exist, then
  writes it, whatever it holds. Typst therefore produces a file that claims 4f without meeting it.

## Why the compiler path does not add the refusal

**Refusing would mean parsing PDF inside the compiler package.** `M3scalinum.Text.Typst` has no PDF
reader and must not grow one: `M3scalinum.Text.Pdf` depends on nothing in the compiler, and the
reverse dependency would tie every consumer of the compiler to the PDF package for one check. The
alternatives are worse than the gap they close:

- scanning the produced bytes for `/EmbeddedFiles` is a substring search standing in for a parser,
  and it is wrong on an object stream, a compressed stream and any document that names the key for
  another reason;
- refusing on the markup text (`#pdf.attach` appearing in a source) refuses the documents that attach
  a file through an import, and accepts the ones that mention the call in a code block;
- inspecting the compiled document's attachments would need an evaluation surface the compiler does
  not have, and a fourth ABI call to serve one error message.

The suite already ships the tool that answers the question exactly: `PdfConformance` reads the
produced file, and its 4f rules are the ones the profile states. A caller that requires conformance
runs it, as it must anyway before depositing a file — the checker establishes declarations, and
veraPDF remains the reference.

## Consequences

- The behaviour is stated where a caller reads the options: `03-compilation.md` §4.1, the wiki's
  `Typst-Compiling`, and the README's capability table.
- `tests/Text.Typst.Integration.Tests/PdfOptionsTests` proves both halves: a 4f export with
  `#pdf.attach(path, relationship: …)` passes the checker and keeps the attachment's name, role and
  bytes; a 4f export with nothing attached claims the profile and is rejected with
  `PDFA4F.EMBEDDED_FILES_MISSING`.
- A document built through `TypstDocument` reaches 4f only with the raw `pdf.attach` call, because
  `TypstPdf.Attach` writes the path and the description and no relationship — and the profile reads
  `/AFRelationship`. Extending the model is a separate change.
