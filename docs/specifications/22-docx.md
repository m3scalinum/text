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

## 2. Why the OOXML is read here, and not by a crate

The suite's rule is a Rust dependency where one is proven. `docx-rs` 0.4.22 satisfied it until it
could not be kept: that release pins `zip ^8.6.0`, and `zip` 8.6.0 is the subject of a **critical**
advisory — a parser differential, so that a package one reader renders is not the package another
reader sees — three high ones (decoder denial of service in the Shrink/Reduce/Implode paths,
unbounded symlink handling, a write outside the extraction directory) and a medium one (an AES HMAC
bypass on premature EOF). The fix is the `zip` 9.x line, which `docx-rs` cannot take: 0.4.22 is the
latest release and its constraint is `^8.6.0`. A reader that sits on a crate that cannot move is a
reader that ships the advisory.

**Decision — the reader is ours.** `native/text-docx` opens the package with `zip` 9.8 and reads the
XML with `roxmltree`. Nothing between this package and the container can pin a vulnerable decoder
again. The C ABI and the JSON AST are unchanged, so `M3scalinum.Text.Docx` and its tests are
unmodified by the change (§4, §9): the rewrite is entirely behind the `td_` symbols.

| | before | after |
| --- | --- | --- |
| Container | `zip` 8.6.0, reached through `docx-rs` | `zip` 9.8.1, a direct dependency |
| XML | `quick-xml` through `docx-rs`'s tree, read via its serde form | `roxmltree` 0.21.2, read as a tree here |
| Parts this crate names | the three a package must carry | the same three, plus `styles.xml`, `numbering.xml`, the document relationships and the media, each through its relationship (§3) |
| `Cargo.lock` entries | 32, including `quick-xml`, `encoding_rs`, `smallvec`, `thiserror`, `base64` | 44, including `roxmltree`, and `zip` 9.8's own `getrandom`, `zeroize`, `constant_time_eq` |
| Licence | MIT (`docx-rs`) | MIT (`zip`), MIT OR Apache-2.0 (`roxmltree`) |

`roxmltree` was chosen for what it refuses as much as for what it does: it builds a read-only tree,
it does not expand external entities, and it has no DTD handling by default. A part is parsed into a
tree the walker can bound (§3), not streamed into a visitor that has to hold its own state.

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
| 9 | `limit` | Over the byte ceiling, over a part's cap, deeper than the reader goes, or past a count cap |
| 10 | `unsupported` | Reserved: a part outside what the reader implements |
| **11** | **`malformed_package`** | Not a zip package, a part that does not parse, or a part name the rules below refuse |
| **12** | **`missing_part`** | A package that lacks `[Content_Types].xml`, `_rels/.rels` or `word/document.xml` |
| 13 | `internal` | A failure this library does not classify |

### 3.1 Which parts are read, and how a name is found

| Part | Why |
| --- | --- |
| `word/document.xml` | The body |
| `word/_rels/document.xml.rels` | The relationships that name the media, and the styles and numbering parts |
| `word/styles.xml` | Only for a heading style whose `w:name` or `w:outlineLvl` supplies a level |
| `word/numbering.xml` | Only for whether a `numId`'s level counts |
| each media part | The picture bytes, stored as they arrived |

**Decision — every part name comes from the package's own relationships, and is then checked.** A
relationship target is a relative URI, and it is resolved against the directory of the part that owns
the relationship — `word/` for the document — by joining segments. The result is refused when a
segment is `..`, empty or `.`, when the target is absolute, when it carries a drive letter or a URI
scheme, or when it does not land under the directory the relationship kind is allowed to name
(`word/media/` for an image). This is the rule the EPUB writer already enforces on asset names
(Spec 25), and it is the rule the old dependency did not enforce: an image relationship is attacker
input like any other part of the file.

`word/styles.xml` and `word/numbering.xml` are optional by specification, so their absence is not an
error: a document with no styles still reads, and a heading whose style id already names its level
(`Heading2`) needs neither part.

### 3.2 The bounds, and why they exist

This reader opens documents a sender supplies. Four bounds sit in front of the parse, and every one of
them fails with `limit` rather than allocating:

| Bound | Value | What it stops |
| --- | --- | --- |
| Whole document | `Options::max_bytes`, 64 MiB by default | A range request gone wrong, a 3 GB upload |
| One part, declared | 32 MiB | A header that promises an allocation |
| One part, decoded | 32 MiB | A header that understates its payload — the "Zip Cloaking" differential |
| Nesting | 64 levels of block | A nesting bomb looking for a stack |
| Runs / cells / media | 200 000 / 50 000 / 2 048 | A body that is large rather than deep |

**Decision — a size is measured, not believed.** The zip central directory states an uncompressed
size, and a reader that trusts it reads a different part from the one the next reader would. Every
part is therefore refused when the header declares more than the cap *and* refused again when the
decoded bytes pass it, and the local file header is compared with its central directory entry before
anything is decoded (`ZipReadOptions::enforce_header_consistency`). That is the class of bug the old
`zip` 8.6.0 carried, and it is closed by construction here rather than by a patch release.

**No file system is touched.** Nothing is extracted to disk: a part is a `Vec<u8>` and a name, never
a path. The arbitrary-write advisory that made the old dependency dangerous cannot apply to a reader
that never writes.

**No `unwrap` on the file.** Everything derived from the package goes through `Option`/`Result`;
the only `unwrap`s in the crate are in its own tests.

### 3.3 Options

`td_document_parse`'s third argument is `{"images": bool, "maxBytes": number}`, or null for the
defaults — images extracted, a 64 MiB ceiling. The ceiling is checked before the archive is opened,
so an accidental 3 GB upload fails in microseconds instead of allocating.

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

**Page setup is reported only when the file states it.** A section is read as stated values with
Word's defaults filled in for whatever the section omits — A4, 2.54 cm top and bottom, 3 cm left and
right — so a reader cannot tell an unstated default from a stated one that equals it. A section equal
to that default is therefore reported as "no page setup", and the Typst document gets no `#set page`
rule; anything else is reported and applied. A host that wants Word's page for every document can set
it itself. This is the behaviour `docx-rs` supplied by filling the defaults into its own section
property, kept rather than changed, because a managed test asserts it.

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
| Images that are not pictures | Shapes, charts, text boxes and SmartArt are drawings this reader skips; a DrawingML `a:blip` or a VML `v:imagedata` that names a relationship becomes an image run. EMF and WMF are kept as bytes with their MIME type, but Typst cannot rasterise them, so a compile of such a document fails at the image, not at the read. |
| Content controls | `w:sdt` is descended into at body level, so its blocks are read; the control's own properties are not. A content control inside a paragraph contributes no runs. |

The rule behind the table: **nothing that is not read throws.** A document with footnotes reads, and
the footnote is lost. Only a package that cannot be opened — not a zip, a missing part, over the size
limit — is an exception, because there is then nothing to hand back.

## 8. Licence

The two crates this reader now depends on:

| Crate | Version | Licence | Copyright |
| --- | --- | --- | --- |
| `zip` | 9.8.1 | MIT | © 2014 Mathijs van de Nes (`https://github.com/zip-rs/zip2`) |
| `roxmltree` | 0.21.2 | MIT OR Apache-2.0 | © 2018 Yevhenii Reizner (`https://github.com/RazrFalcon/roxmltree`) |

Both were checked against their published `Cargo.toml` and their shipped licence files: `zip` 9.8.1
declares `license = "MIT"` and ships `LICENSE`; `roxmltree` 0.21.2 declares
`license = "MIT OR Apache-2.0"` and ships `LICENSE-MIT` and `LICENSE-APACHE`. This crate takes
`roxmltree` under the MIT half, which keeps the whole graph MIT or MIT-or-Apache — the same position
`docx-rs` occupied.

`THIRD-PARTY-NOTICES.md` at the repository root is the file a release ships, and it is maintained by
the release, not by this specification. The blocks to add to it, once this package ships, are:

```
zip 9.8.1 — https://github.com/zip-rs/zip2

The MIT License (MIT)

Copyright (c) 2014 Mathijs van de Nes

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

```
roxmltree 0.21.2 — https://github.com/RazrFalcon/roxmltree

The MIT License (MIT)

Copyright (c) 2018 Yevhenii Reizner

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

The rest of the graph — `serde`, `serde_json`, `memchr`, `crc32fast`, `flate2`/`zlib-rs`, `indexmap`,
`typed-path`, `getrandom`, `zeroize`, `constant_time_eq`, `zopfli` and their dependencies — is MIT,
Apache-2.0 or MIT-or-Apache throughout. `zip` is taken with `default-features = false`: the AES,
bzip2, lzma, zstd and deflate64 decoders, and their advisories, are not compiled, because a `.docx`
is stored or deflated and those are the only two methods a package may use here.

## 9. Tests and fixtures

`tests/Text.Docx.Tests` holds the fixture and the assertions. None of it changed with the rewrite:
the AST it reads is the same AST, byte for byte, and that is what the change was held to.

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
  compiles it with the real compiler, and asserts the text through `ExportText`. **26 tests, all
  passing, unmodified.**

The native crate carries its own unit tests, and they are run with `cargo test --lib`, which builds
the library as a test binary and does not need the cdylib the host cannot link in a debug profile:

- `parse` — a package written in memory with `zip`'s writer, read back: a heading run and every run
  property, a style supplying an outline level, a bulleted level, a table with `gridSpan` and
  `vMerge`, an image through its relationship, images turned off, the page setup, a section equal to
  Word's defaults, the two typed failures, the whole-document ceiling, a part that decodes past its
  cap, and a nesting bomb one level past the depth cap.
- `package` — the part-name rules: a traversal, an absolute name, a drive letter, a scheme, an empty
  or `.` segment, a part outside `word/media/`, and a `.rels` part read with its modes.
- `ffi` — the buffer and error shapes.

**Verified here.** `cargo fmt --check`, `cargo clippy --all-targets -- -D warnings`,
`cargo build --release`, `cargo test --lib` (34 tests) and `dotnet test tests/Text.Docx.Tests
-c Release` (26 tests) all ran on the build host for this change. The same fixture was also read by
the pre-change build: the AST JSON and every image byte were identical, which is the claim §2 makes.

**Not verified here.** No CI job builds this crate yet: `.github/workflows` is outside this change.
A release therefore packs the native library only from `native/artifacts`, which no job currently
fills for this crate.
