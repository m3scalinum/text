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
refuses a workbook past `Options::max_sheets` before it reads a sheet, a used region past
`Options::max_cells`, a string past `Options::max_cell_text_bytes`, and a total text past
`Options::max_text_bytes`. `calamine` builds the range first; the suite's caps are a ceiling on what
is shaped into the AST, not on what the dependency allocates (§6).

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
| Flate-decoded stream size | 32 MiB per stream (`PdfFile.MaxStreamBytes`, `Internal/PdfFile.cs`), measured while the filter produces bytes rather than after | the stream reads as undecodable: `Decode` returns `null` and records a reading problem, `PdfConformance` reports `PDF.OBJECT_UNREADABLE` with the observed ceiling, and a page counts the stream in `SkippedContentStreams` | `tests/Text.Pdf.Tests/ParserLimitTests.cs` — `A_flate_stream_that_would_decode_past_the_limit_is_not_read`, `A_stream_past_the_limit_is_a_reading_problem_that_names_the_limit`; the boundary is pinned by `A_stream_just_under_the_limit_is_decoded` |
| Whole PDF | **no size cap** — `PdfFile.TryOpen(byte[])` reads whatever it is given | none; what a small file can *become* is now bounded per stream, so the remaining cost is the bytes the caller already holds | **no test** — there is no bound to test; the caller owns the bytes already |
| XMP packet | 4 MiB, 64 levels, 100 000 nodes, no DTD, no external entity (`PdfAXmp.MaxXmpBytes`, `MaxXmpDepth`, `MaxXmpNodes`, `Internal/PdfAXmp.cs`); the byte ceiling is applied before the packet becomes a string, and a scan bounds depth and node count before the tree is built | `PdfOperationException` naming the rule, the limit and the value observed, and the conversion writes no bytes. A packet that merely does not parse is still replaced, not refused | `ParserLimitTests.cs` — `An_XMP_packet_over_the_byte_limit_is_refused`, `An_XMP_packet_nested_past_the_depth_limit_is_refused`, `An_XMP_packet_past_the_node_limit_is_refused`, `An_XMP_packet_that_declares_a_document_type_is_refused`, `An_XMP_packet_that_declares_an_external_entity_is_refused`, `An_XMP_packet_within_the_policy_is_kept_and_declares_the_standard`, `Converting_a_document_whose_XMP_declares_a_document_type_is_refused_by_name` |
| Encryption dictionary: handler revision, key length | R2–R4 and R5–R6 with the filters the package implements; anything else refused by name | `PdfEncryptionException` naming the handler or the crypt filter | `tests/Text.Pdf.Tests/PdfEncryptionTests.cs` — `A_security_handler_revision_the_library_does_not_implement_is_refused_by_name`, `A_revision_4_document_with_an_RC4_crypt_filter_is_refused` |
| PDF/A conversion walk over fonts and form resources | 8 levels of resources, each dictionary visited once (`PdfAConverter.Fonts`) | no refusal; the walk stops and the report says what it found | **no test** — no fixture nests nine resource levels, and the guard's effect is a shorter font list, not a refusal |

### 5.2 DOCX

| Surface | Limit, and where | Refusal the caller sees | Test that proves it |
| --- | --- | --- | --- |
| Whole document | 64 MiB (`parse::DEFAULT_MAX_BYTES`), checked before the zip is opened | `DocxErrorCode.Limit` | `native/text-docx/src/parse.rs` — `a_document_over_the_limit_is_refused_before_it_is_opened` |
| One part, declared and decoded | 32 MiB (`package::MAX_PART_BYTES`, `Package::read_capped`); the declared size is refused first, the decoded length again | `DocxErrorCode.Limit` | `parse.rs` — `a_part_that_decodes_larger_than_its_cap_is_refused` |
| One XML part | 32 MiB (`xml::MAX_XML_BYTES`) | `DocxErrorCode.Limit` | `xml.rs` — `a_part_over_the_xml_ceiling_is_refused` |
| Element nesting in a part | 256 levels (`xml::MAX_XML_DEPTH`) | `DocxErrorCode.Limit` | `xml.rs` — `a_part_nested_past_the_depth_cap_is_refused` |
| Nodes built from one part | 2 000 000 (`xml::MAX_XML_NODES`, passed as `nodes_limit`) | `DocxErrorCode.Limit` | `xml.rs` — `a_part_that_would_build_too_many_nodes_is_refused` (eight megabytes of empty siblings; the parser refuses at the ceiling) |
| Document type declaration | none permitted (`xml::declared_type`) | `DocxErrorCode.XmlPolicy` | `xml.rs` — `a_doctype_is_refused`; through a real package, `parse.rs` — `a_document_with_a_doctype_is_refused` |
| External entity or external subset | none permitted (`SYSTEM`/`PUBLIC`) | `DocxErrorCode.XmlPolicy` | `xml.rs` — `an_external_entity_that_names_a_file_is_refused`; through a package, `parse.rs` — `a_document_with_an_external_entity_is_refused` |
| Internal entity expansion | 1 MiB (`xml::MAX_ENTITY_EXPANSION`), measured arithmetically before parsing | `DocxErrorCode.XmlPolicy` | `xml.rs` — `a_billion_laughs_document_is_refused` (nine levels of references) and `an_entity_loop_is_an_expansion_bomb` (a reference loop); through a real package, `parse.rs` — `a_billion_laughs_document_is_refused` |
| Entity declarations in one subset | 64 (`xml::MAX_ENTITY_DECLARATIONS`), checked before the expansion is measured | `DocxErrorCode.XmlPolicy` | `xml.rs` — `a_subset_with_more_declarations_than_the_cap_is_refused` (sixty-five short entities, refused as too many declarations rather than as an expansion) |
| Block nesting in the document tree | 64 levels (`parse::MAX_DEPTH`) | `DocxErrorCode.Limit` | `parse.rs` — `a_document_nested_past_the_depth_cap_is_refused` |
| Runs / table cells / media parts | 200 000 / 50 000 / 2 048 (`parse::MAX_RUNS`, `MAX_CELLS`, `MAX_MEDIA`), counted as the tree is walked | `DocxErrorCode.Limit` | `parse.rs` — `a_document_with_more_runs_than_the_cap_is_refused`, `a_document_with_more_cells_than_the_cap_is_refused`, `a_document_with_more_media_parts_than_the_cap_is_refused` |
| Relationships in one part | 4 096 (`package::MAX_RELATIONSHIPS`) | `DocxErrorCode.Limit` | `parse.rs` — `a_relationships_part_over_the_cap_is_refused` |
| Relationship target: path traversal, absolute path, scheme, wrong directory | none permitted (`package::resolve`, `parse::read_media`) | `DocxErrorCode.MalformedPackage` | `package.rs` — `a_traversal_is_refused`, `a_part_outside_the_expected_directory_is_refused`; `parse.rs` — `an_image_relationship_that_leaves_the_media_directory_is_refused` |

### 5.3 XLSX, OCR, barcodes

| Surface | Limit, and where | Refusal the caller sees | Test that proves it |
| --- | --- | --- | --- |
| XLSX whole workbook | 64 MiB (`parse::DEFAULT_MAX_BYTES`), checked before `calamine` opens it | `XlsxErrorCode.Limit` (`TX_ERR_LIMIT`) | `native/text-xlsx/src/parse.rs` — `a_workbook_over_the_byte_limit_is_refused_before_it_is_opened` |
| XLSX used region of one sheet | 1 000 000 cells (`parse::DEFAULT_MAX_CELLS`, rows × columns, not populated cells) | `XlsxErrorCode.Limit` | `parse.rs` — `a_region_over_the_cell_limit_is_refused` |
| XLSX container that is not a package | — | `XlsxErrorCode.MalformedPackage` (`TX_ERR_MALFORMED_PACKAGE`) | `parse.rs` — `bytes_that_are_not_a_package_are_refused` |
| XLSX sheet count | 256 sheets (`parse::DEFAULT_MAX_SHEETS`), checked before the first sheet is read | `XlsxErrorCode.Limit` (`TX_ERR_LIMIT`), naming the count and the cap | `native/text-xlsx/src/parse.rs` — `a_workbook_over_the_sheet_limit_is_refused` |
| XLSX text of one cell | 64 KiB (`parse::DEFAULT_MAX_CELL_TEXT_BYTES`), checked on each string the AST would carry, including a formula's own text | `XlsxErrorCode.Limit`, naming the cell, the sheet, the observed size and the cap | `parse.rs` — `a_text_cell_over_the_per_cell_limit_is_refused`; the boundary is pinned by `a_text_cell_at_the_per_cell_limit_is_read` |
| XLSX text of one workbook | 64 MiB across all cells (`parse::DEFAULT_MAX_TEXT_BYTES`), spent as the cells are shaped | `XlsxErrorCode.Limit` | `parse.rs` — `a_workbook_over_the_text_budget_is_refused` |
| XLSX XML depth, XML node count, part count | **no suite limit** — `calamine` parses the parts with `quick-xml`, a pull parser that keeps no recursion on the stack, and the suite never sees the XML | none | **no test** — nothing is bounded here; what the suite can bound is what it shapes (the three rows above, and the used-region cap), and §6 states the rest |
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
  rendered pixmap, so the pixmap exists before the refusal. A Flate stream in a PDF now stops at
  `PdfFile.MaxStreamBytes` while it is being decoded, so the bomb's bytes are never allocated; what
  remains is the 32 MiB a stream may legitimately decode to, per stream, while a page's streams are
  joined in memory. Budget for the peak, not for the limit.
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
   stream consumer knows that `Decode` bounds the decoded size at `MaxStreamBytes` and treats `null`
   as "this stream is not readable", never as "this stream is empty". A new XML payload read from a
   PDF — an XMP packet today — goes through a policy of the same shape as the DOCX reader's.
3. A new format is a Rust crate with the conventions of [02](02-native-abi.md): bytes and options in,
   an owned tree out, typed status codes, `catch_unwind`, and a limits table in its specification. It
   adds rows to §5 before it ships, including the rows that say "no limit".
4. Every new limit gets a test that fails when the bound is removed. A test that asserts
   `MAX_XML_DEPTH == 256` is not that test; a document with 257 levels of element is.
5. A limit that cannot be tested at its real size (two million nodes, 120 million pixels) is stated
   as untested in §5 with the reason, and the refusal it produces shares a path that is tested.

## 8. The checks behind this table

Run on the change that added the PDF, XLSX and DOCX bounds this document was missing:

| Command | Result |
| --- | --- |
| `cargo test --lib` in `native/text-docx` | 59 passed, 0 failed (53 before the six new limit tests) |
| `cargo test --lib` in `native/text-xlsx` | 40 passed, 0 failed (36 before the four new ones) |
| `cargo test --lib` in `native/text-barcodes` | 32 passed, 0 failed (unchanged by this change) |
| `dotnet test tests/Text.Pdf.Tests -c Release` | 537 total, 523 passed, 0 failed, 14 skipped by their own guards (527 before the ten tests in `ParserLimitTests.cs`) |
| `dotnet test tests/Text.Suite.Tests -c Release` | 7 passed, 0 failed |

Every bound added here was checked the other way too: with the guard removed, the test that pins it
fails. The Flate ceiling removed inflates the bomb into the page and both stream tests fail; the XMP
byte, depth and node ceilings removed turn their three tests from a refusal into an accepted packet;
the DOCX run, cell, media, relationship, node and declaration ceilings removed fail their six tests.
The rows that still say "no test" are the ones with no bound to remove: the whole-file size caps,
`calamine`'s own XML handling, the OCR and barcode decode limits, and the ones whose fixture does not
exist yet.
