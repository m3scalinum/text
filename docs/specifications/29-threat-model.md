# 29 — Threat model: every received document is hostile

## 1. The claim

This suite parses documents it did not produce. A PDF, a DOCX, an XLSX, a zip, an image, a barcode
scan and an XML part are all bytes a stranger chose, and every one of them is an input to a parser
that runs inside the caller's process, with the caller's memory and the caller's privileges. The
suite is not a renderer on a server that can be restarted: `M3scalinum.Text.Pdf`, `.Docx`, `.Xlsx`,
`.Ocr` and `.Barcodes` are libraries, and a library that trusts its input hands the attacker the
host.

The position this document records is therefore not "the bytes are probably fine". It is:

> **Every document handed to this suite is hostile until a bound has been applied to it.** A parser
> is acceptable when each of its limits is a constant beside the code, is enforced before or while
> the memory is taken, is reported to the caller as a typed refusal that names the surface and the
> observed value, and is pinned by a test that fails when the bound is removed.

The compiler side of the suite — `Typst` source, font bytes, package bytes — is a different trust
boundary, described in [01](01-architecture.md) and [03](03-compilation.md); this document covers
the readers of received files.

## 2. What an attacker controls

| Input | Sent by whom | What it can lie about |
| --- | --- | --- |
| PDF bytes | a sender, a scanner, a portal | the cross-reference, the object graph, a stream's declared `/Length`, the number of pages, the depth of forms, a Flate stream's decoded size, XMP |
| DOCX bytes | the same | the zip central directory, a part's declared and decoded size, the XML prologue, entity declarations, nesting, the number of runs, cells and media parts, relationship targets |
| XLSX bytes | the same | the zip container, a sheet's used region (rows × columns), the sheet count, cell text |
| Images | a scan, a camera upload | the encoded format, the declared dimensions, the decoded allocation |
| PDF pages rendered for OCR | the same as PDF | the page's `/MediaBox` and therefore the pixmap it renders to |
| PNG for barcode decoding | a scan | the image dimensions and the pixel data |

The attacker does **not** get the file system, the network, or a process: no reader extracts to disk,
resolves a URI, or downloads anything. What remains is memory, CPU time and the correctness of what
comes back — and those are bounded by the limits below, not by the absence of a sandbox.

## 3. The invariants

Each row of the table in §5 is an instance of one of these, and a new parser is held to all of them:

- **P1 — one door per format.** Bytes of a format enter through that format's documented entry point
  and nowhere else: PDF through `PdfFile.TryOpen`, DOCX through `Package::open` and `xml::parse`,
  XLSX through `XlsxReader.Read`, OCR through `OcrEngine`, barcodes through `Barcode`. A parser that
  reads the same bytes by a second route is a defect, even if it is correct today.
- **P2 — a bound, not a hope.** A declared size is measured again after decoding (a zip header that
  understates its payload is the attack the cap exists for). Recursion is a depth counter, never the
  call stack. A walk over a graph keeps a visited set, never trust in the producer's tree.
- **P3 — the refusal is typed.** The caller receives a code, not a panic and not a truncated result:
  `DocxException` with `DocxErrorCode.Limit` or `XmlPolicy`, `XlsxException` with
  `XlsxErrorCode.Limit`, `PdfTextException` with its `PdfTextError`, a `PdfConformanceReport` finding
  for a structural problem, `OcrException` with the `pdf` or `image` code, `BarcodeException` with
  `Limit`. The message names the surface, the limit and the value observed, so a host can log it
  without guessing.
- **P4 — no panic crosses the ABI.** Every native entry point wraps its work in `catch_unwind` and
  returns a status code (see [02](02-native-abi.md)); a malformed document is a returned error, not a
  process abort.
- **P5 — the limit is testable.** A limit is pinned by a test that removes the bound and fails, not
  by a test that asserts the constant equals itself. A test that would pass with the limit deleted
  is not a limit test.
- **P6 — what is not bounded is written down.** A surface without a limit appears in §5 as "no
  limit" with the consequence stated, and in §6 as a residual risk. Silence would read as a
  guarantee.

## 4. The surfaces

**PDF.** `src/Text.Pdf/Internal/PdfFile.cs` reads the header, the cross-reference (tables and
streams), indirect objects, object streams and the filters the checks need. `PdfLexer` parses one
object, `PdfTextReader` walks a page's content streams and form XObjects, `PdfStandardSecurity`
parses `/Encrypt`, and `PdfAXmp` edits the XMP packet. No page is rendered, no font program is
interpreted: the reader is a structural and textual parser, which is why its limits are counts,
depths and byte lengths rather than pixels.

**DOCX.** `native/text-docx` opens the zip (`package.rs`), reads each part under a per-part cap, and
parses every XML part through `xml.rs::parse`, the crate's only parser call. The XML policy — no
DTD, no external entity, a bounded internal expansion, a byte cap, a node cap, a depth cap — is
stated in [22 §10](22-docx.md#10-the-xml-policy) and not repeated here.

**XLSX.** `native/text-xlsx` hands the bytes to `calamine` 0.38 after capping the workbook size, then
refuses a used region past `Options::max_cells`. `calamine` builds the range first; the suite's cap
is a ceiling on what is shaped into the AST, not on what the dependency allocates (§6).

**OCR.** `native/text-ocr` decodes an image through the `image` crate, or renders PDF pages through
`hayro` and refuses each rendered page past `MAX_PIXELS`. The PDF render and the image decode are
separate paths with separate bounds; neither is bounded by the caller's document size.

**Barcodes.** `native/text-barcodes` bounds the encoded payload and the rasterised pixels on the
encode side, and decodes one PNG through `image` on the decode side.

## 5. Surface → limit → test

Every limit below is a constant in the file named, and every test named was executed in this change;
the counts in §7 are the runs. "No limit" and "no test" are stated as such, with the reason.

### 5.1 PDF

| Surface | Limit, and where | Refusal the caller sees | Test that proves it |
| --- | --- | --- | --- |
| Cross-reference table: entries a subsection may declare | 1 000 000 (`PdfFile.MaxObjects`, `Internal/PdfFile.cs`) | the section is abandoned; `PdfConformance` reports `XrefInvalid` ("not plausible"), never throws | `tests/Text.Pdf.Tests/PdfReaderTests.cs` — `An_implausible_xref_count_is_reported` |
| Syntax nesting of one object | 64 levels (`PdfLexer.MaxDepth`) | object unreadable; a conformance report says "nested more than 64 levels deep" | `PdfReaderTests.cs` — `An_object_nested_beyond_the_limit_is_reported_as_unreadable` |
| Page tree: a page reached twice or a `/Kids` cycle | each object number visited once (`PdfFile.Pages`) | no refusal; the page is counted once and the walk terminates | `tests/Text.Pdf.Tests/ThreatModelLimitsTests.cs` — `A_page_the_tree_reaches_twice_is_counted_once` |
| Form XObjects: nesting | 8 levels (`PdfTextReader.MaxFormDepth`) plus an open-set per page | no refusal; text past the cap is silently dropped (the page does not claim it) | `ThreatModelLimitsTests.cs` — `A_form_nested_past_the_depth_cap_draws_no_text` |
| Flate-decoded stream size | **no limit** — `PdfFile.Inflate` copies the whole decoded stream into a `MemoryStream` | none; a decompression bomb is bounded only by the process's address space | **no test** — there is no bound to test; the mitigation is a caller-side cap on input bytes (§6) |
| Whole PDF | **no size cap** — `PdfFile.TryOpen(byte[])` reads whatever it is given | none | **no test** — the caller owns the bytes already; the OCR PDF path does cap pixels, not bytes |
| XMP packet | **no byte, node or depth cap** — `PdfAXmp.Load` parses the decoded metadata with `XDocument.Parse` | DTD processing is prohibited by the framework's default reader, so no entity expansion; a packet that does not parse is replaced, not refused (a converter cannot fail a document for its metadata) | **no test** — the packet is read on the PDF/A writing path only, and no bound exists to assert |
| Encryption dictionary: handler revision, key length | R2–R4 and R5–R6 with the filters the package implements; anything else refused by name | `PdfEncryptionException` naming the handler or the crypt filter | `tests/Text.Pdf.Tests/PdfEncryptionTests.cs` — `A_security_handler_revision_the_library_does_not_implement_is_refused_by_name`, `A_revision_4_document_with_an_RC4_crypt_filter_is_refused` |
| PDF/A conversion walk over fonts and form resources | 8 levels of resources, each dictionary visited once (`PdfAConverter.Fonts`) | no refusal; the walk stops and the report says what it found | **no test** — no fixture nests nine resource levels, and the guard's effect is a shorter font list, not a refusal |

### 5.2 DOCX

| Surface | Limit, and where | Refusal the caller sees | Test that proves it |
| --- | --- | --- | --- |
| Whole document | 64 MiB (`parse::DEFAULT_MAX_BYTES`), checked before the zip is opened | `DocxErrorCode.Limit` | `native/text-docx/src/parse.rs` — `a_document_over_the_limit_is_refused_before_it_is_opened` |
| One part, declared and decoded | 32 MiB (`package::MAX_PART_BYTES`, `Package::read_capped`); the declared size is refused first, the decoded length again | `DocxErrorCode.Limit` | `parse.rs` — `a_part_that_decodes_larger_than_its_cap_is_refused` |
| One XML part | 32 MiB (`xml::MAX_XML_BYTES`) | `DocxErrorCode.Limit` | `xml.rs` — `a_part_over_the_xml_ceiling_is_refused` |
| Element nesting in a part | 256 levels (`xml::MAX_XML_DEPTH`) | `DocxErrorCode.Limit` | `xml.rs` — `a_part_nested_past_the_depth_cap_is_refused` |
| Nodes built from one part | 2 000 000 (`xml::MAX_XML_NODES`, passed as `nodes_limit`) | `DocxErrorCode.Limit` | **no test** — the fixture would be a 32 MiB part with two million nodes; the refusal shares the tested `Limit` path |
| Document type declaration | none permitted (`xml::declared_type`) | `DocxErrorCode.XmlPolicy` | `xml.rs` — `a_doctype_is_refused`; through a real package, `parse.rs` — `a_document_with_a_doctype_is_refused` |
| External entity or external subset | none permitted (`SYSTEM`/`PUBLIC`) | `DocxErrorCode.XmlPolicy` | `xml.rs` — `an_external_entity_that_names_a_file_is_refused`; through a package, `parse.rs` — `a_document_with_an_external_entity_is_refused` |
| Internal entity expansion | 1 MiB (`xml::MAX_ENTITY_EXPANSION`), measured arithmetically before parsing | `DocxErrorCode.XmlPolicy` | `xml.rs` — `a_billion_laughs_document_is_refused` (nine levels of references) and `an_entity_loop_is_an_expansion_bomb` (a reference loop); through a real package, `parse.rs` — `a_billion_laughs_document_is_refused` |
| Entity declarations in one subset | 64 (`xml::MAX_ENTITY_DECLARATIONS`) | `DocxErrorCode.XmlPolicy` | **no test** — every DTD is refused as a DTD, so the declaration cap cannot be reached through the public entry point; it bounds the prologue scan's own recursion |
| Block nesting in the document tree | 64 levels (`parse::MAX_DEPTH`) | `DocxErrorCode.Limit` | `parse.rs` — `a_document_nested_past_the_depth_cap_is_refused` |
| Runs / table cells / media parts | 200 000 / 50 000 / 2 048 (`parse::MAX_RUNS`, `MAX_CELLS`, `MAX_MEDIA`) | `DocxErrorCode.Limit` | **no test** — no fixture builds those counts; the depth guard is the tested shape of "large rather than deep" |
| Relationships in one part | 4 096 (`package::MAX_RELATIONSHIPS`) | `DocxErrorCode.Limit` | **no test** — as above |
| Relationship target: path traversal, absolute path, scheme, wrong directory | none permitted (`package::resolve`, `parse::read_media`) | `DocxErrorCode.MalformedPackage` | `package.rs` — `a_traversal_is_refused`, `a_part_outside_the_expected_directory_is_refused`; `parse.rs` — `an_image_relationship_that_leaves_the_media_directory_is_refused` |

### 5.3 XLSX, OCR, barcodes

| Surface | Limit, and where | Refusal the caller sees | Test that proves it |
| --- | --- | --- | --- |
| XLSX whole workbook | 64 MiB (`parse::DEFAULT_MAX_BYTES`), checked before `calamine` opens it | `XlsxErrorCode.Limit` (`TX_ERR_LIMIT`) | `native/text-xlsx/src/parse.rs` — `a_workbook_over_the_byte_limit_is_refused_before_it_is_opened` |
| XLSX used region of one sheet | 1 000 000 cells (`parse::DEFAULT_MAX_CELLS`, rows × columns, not populated cells) | `XlsxErrorCode.Limit` | `parse.rs` — `a_region_over_the_cell_limit_is_refused` |
| XLSX container that is not a package | — | `XlsxErrorCode.MalformedPackage` (`TX_ERR_MALFORMED_PACKAGE`) | `parse.rs` — `bytes_that_are_not_a_package_are_refused` |
| XLSX sheet count, cell text length, part count | **no suite limit** — whatever `calamine` accepts | none | **no test** — nothing is bounded here; §6 |
| OCR: one rendered PDF page | 120 000 000 pixels (`pdf::MAX_PIXELS`), checked on the rendered pixmap | `OcrException` with the `pdf` code, naming the page, the size and the cap | **no test** — reaching the cap allocates a ~480 MB page first; the check itself is a single comparison |
| OCR: encoded image (PNG, JPEG, BMP, GIF, TIFF, WebP) | **no suite dimension cap** — `image::load_from_memory`, whose default `Limits` allows 512 MiB of allocation per decode and sets no width or height | `OcrException` with the `image` code when the decoder refuses; otherwise the image is decoded | **no test** — the bound belongs to the dependency, and the suite sets none |
| OCR: pages read from a PDF | caller's `page_count` (0 means to the end), no suite cap | none | **no test** — a page range is a caller decision, not a parser bound |
| Barcode: encoded payload | 4 096 bytes (`encode::MAX_PAYLOAD_BYTES`) | `BarcodeException` with `Limit` (`BT_ERR_LIMIT`) | `native/text-barcodes/src/encode.rs` — `an_oversized_payload_is_a_limit_error` |
| Barcode: rasterised symbol | 100 000 000 pixels (`encode::MAX_RASTER_PIXELS`), scale 1–64 | `BarcodeException` with `Limit` | `encode.rs` — `a_scale_that_would_exceed_the_raster_limit_is_rejected` |
| Barcode: decoded PNG | **no dimension or byte cap** — `decode::decode_png` through `image`, only "empty is refused" | `BarcodeException` with `Decode` for bytes that are not a PNG; a document without a symbol is a failed decode, not an error | **no test** — `decode.rs::an_empty_image_is_rejected` pins the empty case only |

## 6. What this library does not protect

The bounds above are structural. They are not a sandbox, and four costs remain the caller's:

- **Time of parse.** Nothing bounds wall-clock time. A document at every limit — a million xref
  entries, two million nodes, a full used region — is a valid document and will take as long as it
  takes. A host that accepts uploads needs its own timeout and process isolation.
- **Transient memory.** Several limits are ceilings on the result, not on the peak. `calamine`
  builds a sheet's `Range` before `max_cells` is consulted, so a workbook that declares a huge used
  region allocates that region first and is refused afterwards. The OCR pixel cap is checked on the
  rendered pixmap, so the pixmap exists before the refusal. A Flate stream in a PDF inflates to its
  full size with no ceiling at all (`PdfFile.Inflate`). Budget for the peak, not for the limit.
- **Third-party allocations.** `calamine` (XLSX), `roxmltree` (DOCX after the policy's own checks),
  `image` (OCR and barcode decoding, default `Limits`: 512 MiB of allocation, no dimension cap),
  `hayro` (OCR rendering) and `rxing` (barcode detection) each allocate before the suite sees the
  result. The suite's caps are set below those defaults where it has one; where it has none, §5 says
  so. A dependency upgrade can move these numbers, which is why
  [dependencies.md](dependencies.md) is a gate.
- **No document-level total.** PDF has no input-size cap and no page cap; OCR has no page cap; DOCX
  caps bytes but not, for instance, the total text length it returns. A caller that stores or
  forwards the result must apply its own quota.

Nothing here reads the network, resolves a URI, runs a macro, or writes a file; that part is
guaranteed by construction (`Package` never extracts, `calamine` reads a `Cursor`, OCR takes model
bytes from the caller, barcodes only encode and decode). The guarantee this document makes is about
malformed and oversized input, not about a hostile *author* of a well-formed document: text that is
merely untrue is in scope for the caller's own judgement, not for a parser.

## 7. The architecture rule that keeps this true

**A parser new to the suite passes through the existing entry point of its format, or it inherits
this document's obligations in full.** Concretely:

1. A new DOCX part is read with `Package::read_capped` and parsed with `xml::parse`. It does not call
   `roxmltree` directly and does not open the zip by another route; the policy is one function so a
   replacement parser cannot silently drop the guarantees.
2. A new PDF reader walks objects through `PdfFile` (`Resolve`, `GetObject`, `Decode`) instead of
   scanning the byte array itself, so the cross-reference bounds, the object limit and the filter
   handling apply to it. A new recursive walk carries its own depth counter and a visited set; a new
   stream consumer states what it does about the decoded size, because `Decode` does not bound it.
3. A new format is a Rust crate with the conventions of [02](02-native-abi.md): bytes and options in,
   an owned tree out, typed status codes, `catch_unwind`, and a limits table in its specification. It
   adds rows to §5 before it ships, including the rows that say "no limit".
4. Every new limit gets a test that fails when the bound is removed. A test that asserts
   `MAX_XML_DEPTH == 256` is not that test; a document with 257 levels of element is.
5. A limit that cannot be tested at its real size (two million nodes, 120 million pixels) is stated
   as untested in §5 with the reason, and the refusal it produces shares a path that is tested.

## 8. The checks behind this table

Run on the change that introduced this document:

| Command | Result |
| --- | --- |
| `cargo test --lib` in `native/text-docx` | 53 passed, 0 failed |
| `cargo test --lib` in `native/text-xlsx` | 36 passed, 0 failed |
| `cargo test --lib` in `native/text-barcodes` | 32 passed, 0 failed |
| `dotnet test tests/Text.Pdf.Tests -c Release` | 371 passed, 0 failed (369 before this document's two tests) |
| `dotnet test tests/Text.Suite.Tests -c Release` | 7 passed, 0 failed |

The two new PDF tests are the rows that had none: a page reached twice by the page tree, and a form
chain one level past `MaxFormDepth`. Both fail if their guard is removed — the page is counted
twice, and the dropped form's text reappears — which is the property P5 asks for. The OCR, barcode
decode, XMP, decompression and size rows remain untested for the reasons stated in their rows; this
document does not claim otherwise.
