# 22 — Reading Word documents

## 1. Scope

`M3scalinum.Text.Docx` reads a Word `.docx` package into a typed tree, and turns that tree into a
`TypstDocument` of the suite's own model, so a Word file compiles through the compiler an application
already hosts:

```text
DocxDocument document = DocxReader.Read(File.ReadAllBytes("received.docx"));

TypstDocument typst = document.ToTypstDocument();

var world = new TypstWorldBuilder().AddSource("main.typ", typst.ToMarkup());

foreach (var (name, bytes) in document.Images)
{
    world.AddAsset(name, bytes);
}

using CompilationResult pdf = new TypstCompiler().Compile(world.Build());
```

Bytes in, a document out: no Word installation, no Open XML SDK, no file system once the bytes are in
hand. Reading and conversion are separate calls over separate objects, so a host can read a package,
inspect it, and decide what to render.

**In scope.** The body of `word/document.xml`: paragraphs with their runs and run properties, heading
styles resolved to a level, tables with their grid spans and vertical merges, numbered and bulleted
lists with their level, embedded images, and the section's page setup.

**Out of scope, deliberately.** Table 1 in §7 lists what is dropped and why.

## 2. Why `docx-rs`, and not the Open XML SDK

The suite's rule is a Rust dependency where one is proven; here it also happens to be the smaller
package.

| | `docx-rs` 0.4.22 | DocumentFormat.OpenXml |
| --- | --- | --- |
| Language | Rust, in the same cdylib as everything else this suite ships native | .NET, a second managed dependency tree |
| Size | one crate, 62 transitive crates, no `image` (see below) | `DocumentFormat.OpenXml` plus `DocumentFormat.OpenXml.Framework` |
| Reading | `read_docx(&[u8])` returns a document tree, and its serde form is that crate's documented JSON API | `WordprocessingDocument.Open` over a stream, then a walk of typed elements |
| Writing | also a writer, which this package does not use | also a writer |
| Licence | MIT | MIT |

**Decision — the reader goes through `docx-rs`'s serde form.** The run properties a caller cares about
(`w:b`, `w:i`, `w:u`, `w:color`, `w:sz`, `w:vertAlign`, `w:strike`) sit behind private fields in that
crate, but every one of them is part of its `Serialize` implementation — the JSON contract its
WebAssembly build publishes. Reading that JSON is therefore a supported surface, and it keeps this
crate from depending on a struct layout that a patch release may rearrange.

**Decision — `default-features = false`.** `docx-rs`'s default features pull the `image` crate to
decode a picture into a PNG preview. This reader never decodes an image: it hands the original part
back byte for byte. Turning the feature off drops `image`, `png`, `jpeg-decoder`, `tiff`, `fax`,
`zopfli` and their licences from the dependency graph, and the preview field is simply empty.

## 3. The native crate

`native/text-docx` builds `sharp_text_docx`, a `cdylib` with the same conventions as `sharp_typst`
(Spec 02): `extern "C"`, `catch_unwind` at every entry point, owned buffers freed by the caller, one
owned error object per failure. Its symbols are prefixed `td_` so a process that loads both libraries
never has to wonder which crate answered — separate libraries, but a single stack trace.

| Symbol | Purpose |
| --- | --- |
| `td_abi_version` | 1. The host refuses a library it does not know. |
| `td_document_parse` | Bytes in, an owned handle out. Options are optional JSON. |
| `td_document_ast_json` | The AST as JSON, in a caller-owned buffer. |
| `td_document_image_count` | How many media parts the handle holds. |
| `td_document_image` | One part's bytes, in a caller-owned buffer. This is the second call the image handles point at. |
| `td_document_free` | Releases the handle and the tree it owns. |
| `td_document_live_count` | Handles alive right now. |
| `td_buffer_free`, `td_error_free`, `td_error_code`, `td_error_json` | The shapes of Spec 02, verbatim. |

Status codes are the ones `02-native-abi.md` defines where the meaning matches, and two of this
crate's own:

| Code | Name | Meaning |
| --- | --- | --- |
| 0 | `ok` | |
| 1 | `invalid_argument` | A null pointer, an unreadable options payload, an index past the media table |
| 2 | `invalid_handle` | A handle this library does not own |
| 3 | `panic` | A panic caught at the boundary; the bug is here |
| 9 | `limit` | The document is over the byte ceiling |
| 10 | `unsupported` | Reserved: a part outside what the reader implements |
| **11** | **`malformed_package`** | Not a zip package, or a part of it does not parse |
| **12** | **`missing_part`** | A zip package that lacks `[Content_Types].xml`, `_rels/.rels` or `word/document.xml` |
| 13 | `internal` | A failure this library does not classify |

**Decision — the package is checked before `docx-rs` sees it.** That crate reports both "not a zip"
and "no `word/document.xml`" as one `ZipError`, and a host needs to tell them apart: the first is a
file that was never a document, the second is a truncated or rewritten one. The crate therefore opens
the archive itself (the `zip` 8.6 build is already in the graph, so this is not a second copy) and
names the missing part before handing the bytes on.

**Options.** `td_document_parse`'s third argument is `{"images": bool, "maxBytes": number}`, or null
for the defaults — images extracted, a 64 MiB ceiling. The ceiling is checked before the archive is
opened, so an accidental 3 GB upload fails in microseconds instead of allocating.

## 4. The AST

`td_document_ast_json` returns the whole document in one buffer. It is compact by construction: every
`false` flag and every absent property is left out.

```json
{
  "blocks": [
    {"type": "paragraph", "heading": 1, "runs": [{"type": "text", "text": "Quarterly report"}]},
    {"type": "paragraph", "runs": [
      {"type": "text", "text": "Revenue grew "},
      {"type": "text", "text": "twelve percent", "bold": true}
    ]},
    {"type": "paragraph", "list": {"numberingId": 1, "level": 0, "ordered": false},
     "runs": [{"type": "text", "text": "First finding"}]},
    {"type": "paragraph", "runs": [
      {"type": "image", "name": "word/media/image1.png", "mimeType": "image/png", "bytesHandle": 0}]},
    {"type": "table", "columns": 3, "rows": [
      {"cells": [{"colspan": 2, "rowspan": 1, "blocks": []}]}]}
  ],
  "page": {"widthPt": 595.3, "heightPt": 841.9, "landscape": false,
           "marginTopPt": 72, "marginRightPt": 85.05, "marginBottomPt": 72, "marginLeftPt": 85.05},
  "images": [{"name": "word/media/image1.png", "mimeType": "image/png", "bytesHandle": 0, "length": 74}]
}
```

| Field | Meaning |
| --- | --- |
| `heading` | 1 to 6. Word's level is clamped: Typst has no seventh heading. |
| `list` | The `numId`, the zero-based level, and whether that level counts (a bullet does not). |
| `underline` | Word's style name, kept verbatim — `single`, `double`, `dotted`, … |
| `strike` | True for `w:strike` and for `w:dstrike`: Typst has one strike, not two. |
| `script` | `superscript` or `subscript`. |
| `color` | `RRGGBB` as Word writes it, or absent for `auto` and theme colours. |
| `sizePt` | Points, converted from Word's half-points. |
| `colspan` / `rowspan` | How many grid columns and rows the cell covers. |
| `bytesHandle` | The index `td_document_image` takes. |
| `page` | Absent when the document states no page setup of its own (§6). |

**Decision — `bytesHandle` is an index, not a global registry.** The handle owns its media table and
nothing lives outside it, so there is no process-wide map to leak, no handle to collide with another
document's, and `td_document_free` releases everything at once. A run's `bytesHandle` and the media
table entry with the same number name the same part; two relationships to one part share one entry,
because a picture inserted twice must not become two assets.

## 5. The managed model

`DocxReader.Read(byte[])` (and `Read(Stream)`) returns a `DocxDocument`:

| Member | Type | Meaning |
| --- | --- | --- |
| `Blocks` | `IReadOnlyList<DocxBlock>` | The body, in document order |
| `Images` | `IReadOnlyDictionary<string, byte[]>` | Part name to bytes, the originals untouched |
| `Page` | `DocxPageSetup?` | The page setup, or null |

`DocxBlock` is `DocxParagraph` or `DocxTableBlock`. A paragraph carries `HeadingLevel`, `List`,
`Inlines` and `Text`; a table carries `Columns` and `Rows` of `DocxTableRow`, each of `DocxTableCell`
with `Colspan`, `Rowspan` and `Blocks`. `DocxInline` is `DocxTextRun`, `DocxImageRun` or
`DocxLineBreak`. Both hierarchies are closed — the constructors are internal — so a `switch` over
them is exhaustive and a caller cannot add a case that the conversion will not handle.

`DocxOptions` has two members: `ExtractImages` (default true) and `MaxDocumentBytes` (default 64 MiB).
With images off, no media is copied out of the package at all and the image runs disappear from the
tree, so the AST and the tree agree about what the document contains.

**Decision — the tree is built in managed code from the JSON, not exposed as JSON.** The AST is the
wire format between two libraries; the tree is the API. Reading the JSON into DTOs and then into the
public types costs one allocation per node and buys a model with no `null` strings where a value is
guaranteed, no `type` discriminator for a caller to switch on, and a place to compute `Text` once.

## 6. Conversion to Typst

`DocxDocument.ToTypstDocument()` builds the same `TypstDocument` every other producer builds. Nothing
is invented for Word: a heading is `HeadingNode`, emphasis is `EmphasisNode`, a table is `TableNode`
with `TypstTableCell` spans.

| Word | Typst |
| --- | --- |
| `w:pStyle` `Heading1`…`Heading6`, or `w:outlineLvl` | `= ` to `====== ` |
| `w:b` | `*strong*` |
| `w:i` | `_emphasis_` |
| `w:u` | `#underline[…]` |
| `w:strike`, `w:dstrike` | `#strike[…]` |
| `w:vertAlign` | `#super[…]` / `#sub[…]` |
| `w:color`, `w:sz` | `#text(fill: …, size: …)[…]` |
| Consecutive paragraphs with one `numId`, `numFmt` bullet | `- item` |
| Consecutive paragraphs with one `numId`, any counting format | `+ item` |
| `w:tbl` | `#table(columns: (1fr, …))` |
| `w:gridSpan` | `table.cell(colspan: n)` |
| `w:vMerge` across rows | `table.cell(rowspan: n)` |
| `w:drawing` with an embedded picture | `#figure(image("word/media/image1.png"))` |
| `w:sectPr` (`w:pgSz`, `w:pgMar`) | `#set page(width: …, height: …, margin: …)` |

**Decision — an inline image is emitted as a block-level figure before its paragraph.** Word keeps a
picture in the run flow; Typst has no inline image. Emitting it as a figure keeps the picture in the
document, in the right place relative to the paragraph, and keeps the model's one image node — at the
cost of the text that followed it on the same line now starting a new paragraph.

**Decision — a vertical merge is counted, not copied.** Word writes a merged cell as a cell that
restarts the merge and one empty continuation cell per row below. Typst writes one cell with
`rowspan`, and a row below simply provides fewer cells. The reader counts the continuations, sets the
row span, and drops the cells, which is exactly the shape the model's table expects.

**Page setup is reported only when the file states it.** `docx-rs` fills Word's defaults — A4,
2.54 cm top and bottom, 3 cm left and right — whether the document states them or not, so a reader
cannot tell an unstated default from a stated one that equals it. A section equal to that default is
therefore reported as "no page setup", and the Typst document gets no `#set page` rule; anything else
is reported and applied. A host that wants Word's page for every document can set it itself.

## 7. What is lost

| Not read | Why, and what the reader does instead |
| --- | --- |
| Styles beyond the run properties | `w:pStyle` decides a heading, and nothing else. A custom style that sets spacing, borders or a font is not resolved: the run properties a run states are read, and the style's are not inherited. |
| Character styles and theme colours | `w:color` with `auto` or a theme reference is dropped; a hex value is kept. |
| Footnotes and endnotes | `word/footnotes.xml` is not opened. A footnote reference is a run this reader does not see, so the text of the note is lost and no marker appears. |
| Tracked changes | Insertions are read as their runs; deletions, moves and the change metadata are dropped. A document read while a change is pending therefore reads as "accepted". |
| Fields | `w:fldSimple` and the `fldChar`/`instrText` sequence are not evaluated: a table of contents, a page reference or a `DATE` field is lost, not rendered. |
| Hyperlinks | A hyperlink's runs are read as text; the relationship target is dropped, because the model's link node takes a URL and Word's target lives in the package's rels — including it is a small addition, and it is not one this change made. |
| Numbering formats | Only "counting or not" is read from `word/numbering.xml`. A list restarting at 5, or a custom `lvlText`, starts at 1 in Typst. |
| List nesting | A paragraph's level is in the model, but the model's list builder takes flat items, so levels past the first are emitted in the same list. |
| Headers, footers, comments | Not read at all. |
| Tables inside cells | Dropped: Typst's cell holds content, and a nested table would need a block the model does not have. |
| Tabs, `w:tab` | Dropped: honouring them means reproducing Word's tab stops. |
| Images that are not pictures | Shapes, charts, text boxes and SmartArt are drawings this reader skips; only `pic` drawings become image runs. EMF and WMF are kept as bytes with their MIME type, but Typst cannot rasterise them, so a compile of such a document fails at the image, not at the read. |

The rule behind the table: **nothing that is not read throws.** A document with footnotes reads, and
the footnote is lost. Only a package that cannot be opened — not a zip, a missing part, over the size
limit — is an exception, because there is then nothing to hand back.

## 8. Licence

`docx-rs` 0.4.22 is MIT, © 2020 bokuweb (`https://github.com/bokuweb/docx-rs`). Its `Cargo.toml`
declares `license = "MIT"` and the crate ships no separate licence file, so the text below is the
licence of the repository it is published from.

`THIRD-PARTY-NOTICES.md` at the repository root is the file a release ships, and it is maintained by
the release, not by this specification. The block to add to it, once this package ships:

```
docx-rs 0.4.22 — https://github.com/bokuweb/docx-rs

MIT License

Copyright (c) 2020 bokuweb

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

The crate graph `docx-rs` brings in — `zip`, `quick-xml`, `serde`, `serde_json`, `smallvec`,
`thiserror`, `base64`, `crc32fast` and their own dependencies — is MIT or Apache-2.0 throughout;
`default-features = false` keeps the `image` family, and its wider licence surface, out.

## 9. Tests and fixtures

`tests/Text.Docx.Tests` holds the fixture and the assertions:

- `fixtures/report.docx` — a committed package built from raw OOXML by `DocxFixture` in the test
  project. `fixtures/README.md` says how it was made and how to regenerate it. The test that compares
  the committed file's parts to the builder's output is what keeps the two from drifting.
- The reader tests assert a heading level, bold and italic runs, underline/colour/size/superscript, a
  struck run, the bulleted list, a table's grid span, a table's row span and the dropped continuation
  cell, the image bytes, the page setup, images turned off, a stream, and that every native handle is
  released.
- Three failure tests assert the typed errors: bytes that are not a package (`MalformedPackage`), a
  package without `word/document.xml` (`MissingPart`), and a document over the limit (`Limit`).
- The end-to-end test reads the committed fixture, converts it, registers its images as world assets,
  compiles it with the real compiler, and asserts the text through `ExportText`.

**Not verified here.** `cargo test` for the native crate is not run on the build host the fixture was
made on (the crate's own unit tests compile under `cargo clippy --all-targets` but were not executed),
and no CI job builds this crate yet: `.github/workflows` is outside this change. A release therefore
packs the native library only from `native/artifacts`, which no job currently fills for this crate.
