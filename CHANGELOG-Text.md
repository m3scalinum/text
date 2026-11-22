# Changelog — the suite train

All notable changes to the packages that do not embed the Typst compiler are documented in this
file: `M3scalinum.Text.Pdf`, `M3scalinum.Text.Templates`, `M3scalinum.Text.Mcp`, and the four that
carry a Rust library of their own — `M3scalinum.Text.Barcodes`, `M3scalinum.Text.Ocr`,
`M3scalinum.Text.Docx` and `M3scalinum.Text.Xlsx`. They are released from `v*` tags by
`.github/workflows/release-text.yml`, and that workflow takes its release page from here.

The compiler packages have their own file, [CHANGELOG-Typst.md](CHANGELOG-Typst.md); what
the project gains as a whole is in [CHANGELOG.md](CHANGELOG.md).

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and these packages
adhere to [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Their version follows the suite,
`TextVersion` in `Directory.Build.props`, not the compiler: a fix to a signature must not read as a
Typst release.

## [Unreleased]

Not yet published.

### Added

- `M3scalinum.Text.Barcodes`: ten symbologies in both directions — QR Code, DataMatrix, Aztec, PDF417,
  Code 128, Code 39, EAN-13, EAN-8, ITF and Codabar — encoded to SVG at one unit per module or to a
  PNG at a chosen scale, and decoded back from a PNG. The engine is `rxing` (Apache-2.0), a Rust port
  of ZXing, in a native library of its own.
- `M3scalinum.Text.Ocr`: text, lines and word boxes out of a page image, and pages out of a PDF, with
  the detection and recognition models supplied by the caller as bytes. Nothing is downloaded and no
  default path is read; the engine is `ocrs`/`rten` over `hayro`.
- `M3scalinum.Text.Docx`: a Word document read into the document model — runs with their emphasis,
  headings, tables with spanning and merged cells, lists, images and page setup — and renderable by
  the compiler like any other document. The OOXML is parsed directly over `zip` 9 and `roxmltree`.
- `PdfArchiveTimestamp` and PAdES B-LTA: a document timestamp over the whole file including the DSS,
  refreshable as the algorithms age, with `Inspect` reporting what each entry covers and whether the
  chain still holds.
- `PdfAConverter.Convert`: an existing PDF turned into PDF/A, or refused with the blocking issue
  named — an untagged document cannot be given a structure tree, and it does not pretend otherwise.
  The verdict comes from `PdfConformance.Check` on the produced bytes.
- `M3scalinum.Text.Mcp`: three more tools. `sign_document` signs a PDF with a PKCS#12 certificate and
  its password, with an optional reason and location, and returns the PAdES-signed file; the key is
  loaded into memory only. `merge_documents` merges two or more PDFs, in the order given, and
  `extract_text` reads the text each page draws, through the fonts' `/ToUnicode` maps, as
  `{"pageCount":N,"pages":[{"page":…,"text":…}]}`. A document arrives base64-encoded, or as a path
  only when the host set `AllowFileOutput` — a path a model chose is ambient access, so it is refused
  with `-32602` exactly as `outputPath` is — and every input is bounded by `MaxDocumentSize`. The
  tools share the existing dispatch, limits and error mapping: a bad argument is a JSON-RPC error, a
  failed signature or an unreadable document is a tool error carrying the reason. Tested with real
  frames: a `%PDF` header and a signature `PdfSignatureVerifier` reads back as intact, a page count
  and the markers of the pages merged, the text of a page, plus the invalid-argument and
  `AllowFileOutput=false` refusals.
- `M3scalinum.Text.Pdf` and `PdfText`: `Extract` and `ExtractPage` read the text a received PDF
  draws, as bytes in and bytes out. The walk decodes the page content streams, runs the text state
  (`BT`/`ET`, `Tf`, `Td`/`TD`/`Tm`/`T*`, `Tj`/`TJ`/`'`/`"`, spacing, `q`/`Q`, `cm`) and recurses into
  form XObjects with their own resources; glyph codes are mapped through each font's `/ToUnicode`
  CMap, including `bfchar`, both `bfrange` forms and the code-space ranges that decide the code
  width. A font with no CMap is read through its `/Differences` or as raw code values, and the page
  names it in `FallbackFonts` instead of emitting mojibake silently; `SkippedContentStreams` counts
  streams whose filter is not implemented. Words are separated by a space where the layout leaves a
  horizontal gap, lines by a newline, and a page with text ends with one newline, the shape the
  compiler's per-page text has. An encrypted document is refused with `PdfTextException` and
  `PdfTextError.Encrypted`, naming the security handler; a password reaches `PdfEncryption` first,
  so an AES-128 or AES-256 document renders its text, and a password that does not open it is
  refused with `PdfTextError.WrongPassword` instead of a raw decryption exception. What is
  recovered is glyph order as the content stream yields it — no reading order, no layout, no OCR —
  and a scanned page comes back empty. Verified on compiler output (two pages with a marker each, a
  table and a footnote) and on a browser-printed document, plus hand-written files for a Type0 CMap
  in every shape, a font with no CMap, a form XObject and an encrypted trailer.
- `M3scalinum.Text.Pdf` and `PdfPages`: `Merge`, `Extract`, `Split`, `Rotate` and `Remove` rebuild a
  document — objects renumbered, page tree rewritten, a fresh cross-reference table — so a page
  keeps its content, resources, embedded fonts, images and annotations, and a font two pages share
  is copied once. A page selection uses the compiler's `PageRanges` semantics (`1-3,5`, `2-`, `-3`).
  `Rotate` adds to `/Rotate` and keeps the catalog, so tagging, outlines and form fields survive;
  the other operations drop the document-level structures a page tree cannot carry. Verified on
  real Typst output: page order, rotation and page count read back from the rewritten dictionaries,
  the text read back through the fonts' `/ToUnicode` maps, and the merged file checked for
  cross-reference, page-tree, object and font issues.
- `M3scalinum.Text.Pdf` and `PdfForm`: `Fill` sets `/V` on the fields a caller names and redraws the
  appearance a widget already carries with the field's `/DA` font; checkboxes and radio buttons take
  an appearance state. `Flatten` draws each widget's appearance into the page as a Form XObject at
  its `/Rect`, removes the widget and its field, and removes `/AcroForm` when every field was
  flattened. A field it cannot draw — no appearance stream, an empty rectangle, an unusable `/BBox`,
  a signature field — is left interactive and reported in `PdfFormFlattenResult.Skipped`, because a
  flattened form that lost its values is worse than one that is still editable. Encrypted documents
  are refused, and page operations, filling and flattening invalidate existing signatures.
- `M3scalinum.Text.Pdf` and `PdfEncryption`: `Open(pdf, password)` returns a `PdfDecryptionResult` —
  the decrypted document as whole bytes, the `PdfPermissions` the password grants, the handler
  revision and whether the owner password matched — and `Encrypt(pdf, userPassword, permissions,
  ownerPassword)` writes AES-256. Both are the standard security handler in the two revisions real
  documents carry: AES-256 with `V=5/R=6`, whose file key comes from the hardened SHA-256 algorithm
  of ISO 32000-2, and AES-128 with `V=4/R=4` and an `/AESV2` crypt filter, whose key comes from the
  legacy MD5 derivation over `/O`, `/P` and `/ID`. Passwords are validated against `/U` and `/O`,
  strings and streams are decrypted per object (with the per-object key revision 4 requires), the
  metadata exemption is honoured, and the `/Encrypt` dictionary and `/ID` are left in the clear. A
  wrong password is a `PdfEncryptionException`, not a damaged document; RC4, revision 5 and
  public-key encryption are refused with the `V`/`R` or crypt filter they declare. `Encrypt` writes
  a fresh key, `/ID` and salts per call, sets `/P` from the permission bits and `/Perms` from them,
  and defaults the owner password to the user one. `PdfPages`, `PdfForm`, `PdfSigner`,
  `PdfDocumentTimestamp` and `PdfSignatureVerifier` now refuse an encrypted document with a message
  naming `PdfEncryption.Open` instead of failing on ciphertext, and `PdfConformance` keeps reporting
  `PDF.ENCRYPTED`. Verified by a round trip on real Typst output (page count and text read back
  through `/ToUnicode`), a wrong and an empty password, the permission bits read back through both
  the user and the owner password, an AES-128 document built in the test project from the
  specification's own algorithms, and a 256-bit AES document qpdf wrote, so neither direction is
  only checked against itself.
- `M3scalinum.Text.Pdf` and `PdfConformance`: check PDF/UA-1 and PDF/A conformance from the bytes,
  with `Check` returning a `PdfConformanceReport` and `Assert` throwing a
  `PdfConformanceException`. The checker establishes catalog and cross-reference integrity, tagged
  structure (`/StructTreeRoot`, `/ParentTree`, `/MarkInfo`), `/Lang`, a document title, `/Alt` or
  `/ActualText` on every figure, embedded fonts with `/ToUnicode`, absence of `/Encrypt`, the PDF
  version the claimed standard allows, and the XMP `pdfaid`/`pdfuaid` identifier. It is pure managed
  parsing with no new dependency, has no reference on the compiler, and is a structural pre-flight
  rather than a validator: veraPDF remains the reference for ISO 14289 and ISO 19005. Verified on
  real Typst output (PDF/UA-1, PDF/A-2b, PDF/A-4) with one negative fixture per rule, produced by
  mutating the real file. No PDF/UA-2 flavour is added: Typst 0.15.1 supports only `ua-1`.
- `M3scalinum.Text.Templates`: four business-document layouts — invoice (facture), quotation
  (devis), account statement (relevé de compte) and attestation — embedded as Typst sources with
  their `template.toml` manifests and the typed records generated from them, each usable from a
  directory on disk or from the embedded copy. `TemplateCatalog` resolves a layout by id and
  version, `TemplateLabels` supplies the English or French labels a layout prints, and
  `AddTextTemplates` registers the catalog for `Microsoft.Extensions.DependencyInjection`.
- `PdfSignatureVerifier.Verify(ReadOnlySpan<byte>, PdfTrustStore?)`: reads every signature back and
  reports what it could prove — intactness, whether the byte range reaches the end of the file, the
  signer's subject, issuer and validity window, the claimed and the timestamped signing time, whether
  a document timestamp covers it, the PAdES baseline level, and the chain's standing against the
  caller's trust store. The machine's trust store is never consulted, and no revocation data is
  fetched.
- PAdES-B-T: `PdfSignatureOptions.TimestampAuthority` and `TimestampClient` timestamp the signature
  value over RFC 3161, with the TSA and the `HttpClient` supplied by the caller. The token is checked
  before it is written: a token whose imprint is not this signature produces an exception, not a
  document.
- PAdES-B-LT: `PdfSignatureOptions.Dss` writes a document security store carrying the chain and the
  caller's OCSP responses and CRLs. Nothing is fetched; a store without revocation data is written
  as it is, and the verifier reports the level it actually proves.
- `PdfSignatureOptions.Appearance` and `PdfSignatureAppearance`: a visible signature at a page and a
  rectangle in PDF points, drawn as text or as a PNG/JPEG image, still written as an incremental
  update with the page's earlier annotations kept.
- `PdfDocumentTimestamp.Add`: an RFC 3161 document timestamp over the whole file, to be added after
  the signatures it covers.
- `M3scalinum.Text.Xlsx`: an `.xlsx` workbook read into the document model — sheets, typed cells
  (text, number, boolean, date, duration, error), a formula with its cached value, and the cell
  range the sheet actually occupies. `ToTypstDocument()` renders a sheet as a table, so an invoice
  that arrives as a spreadsheet reaches a PDF with no Excel and no Open XML SDK. The reader is
  `calamine` (MIT) behind a native library of its own; merged cells, cell formats, defined names,
  charts and the legacy `.xls`/`.xlsb`/`.ods` containers are not read, and a workbook is bounded in
  bytes and in cells.
- `PdfConformance.CheckAccessibility`: the usability half of PDF/UA-1, separate from the structural
  check — table headers and their scope, heading levels that skip a rank, a document that does not
  ask to display its title, a structure tree that does not drive the tab order, and a role with no
  entry in `/RoleMap`. `docs/specifications/15-pdf-conformance.md` §6 carries the per-package
  checklist this implements, names the rules that belong to the document author rather than to the
  file (contrast, type size), and lists what a screen reader run must still confirm by hand — none
  has been run, and the specification says so.
- `M3scalinum.Text.Pdf` and `PdfOutline`: the bookmarks of a document read back as a hierarchy —
  title, target page, children, open or closed, bold or italic — with `/Dest` resolved through a
  direct array, a named destination tree, the older `/Dests` dictionary and a `/GoTo` action, and a
  plan written onto an existing file as an incremental update, so a signature already in it stays
  valid. A document without bookmarks reads as an empty collection rather than throwing.
- `M3scalinum.Text.Pdf` and `PdfAttachments`: the files a PDF carries, read through
  `/Names /EmbeddedFiles` and `/AF` — name, MIME type, `/AFRelationship`, size, SHA-256 and the
  bytes, with a Flate-compressed stream inflated — extracted byte for byte, and added or removed as
  an incremental update. `Add` and `Remove` need the classic `trailer` that `PdfDocument.Parse`
  reads, so a file with a cross-reference stream is refused with the reason rather than rewritten.
- `PdfBatchManifest`: a batch of files sealed as a manifest — a SHA-256, size, last-write time and
  path identity per entry, a seal over the canonical bytes of the manifest itself, and a
  verification that reports each entry as current, modified, missing or added. `Full` hashes every
  entry; `Fast` trusts size and last-write time and hashes only what moved. The manifest holds no
  copy of the files, and it does not sign itself: an opposable record is the manifest signed.
- `IRevocationSource` and `PdfSignatureResult`: the caller supplies OCSP responses and CRLs and
  keeps its own cache; the library embeds them in the DSS and refuses a B-LT signature when a
  certificate in the chain has no revocation answer, naming the subject, the issuer and the serial
  number. Asking for B-T explicitly turns the refusal into a result that lists what is missing. No
  network call is made by the library, ever. `PdfSignatureVerifier.ReadRevocation` reads the
  embedded answers back per certificate, with their production times.
- OCR refuses rather than approximates: `OcrOptions` carries a minimum page confidence, a minimum
  word confidence and a maximum ratio of rejected words, and `Recognize` returns an `OcrRecognition`
  that is either an `OcrResult` or an `OcrUnreadable` whose `OcrRefusal` names the reason — blank
  page, mean confidence, rejected words — with the observed value, the threshold and the count. A
  word under its threshold is still reported, flagged, so the caller can see what was doubtful.
- `linux-musl-x64` for the four format packages: `Barcodes`, `Ocr`, `Docx` and `Xlsx` carry the same
  six runtimes as the rest of the suite, so an Alpine image — the default .NET container — loads
  their native library instead of failing with `DllNotFoundException`.
- A CycloneDX SBOM per package, generated from the `.nupkg` and its lock files — managed assemblies,
  NuGet references, the crates the native library was built from, and the RIDs the package carries —
  attached to every release, verified in CI, and attested with `actions/attest-build-provenance` so
  `gh attestation verify` can check a downloaded package.
- `PdfRaster` and the `native/text-pdf` crate: a page of a received PDF rendered to PNG at a chosen
  DPI through `hayro`, so a preview or the OCR of a scan needs no external PDF engine. `PageCount`
  answers without rendering; the default is 150 dpi, the ceilings are checked before anything is
  allocated, and an encrypted document is refused with the package's own error rather than silently
  decrypted. PNG only: hayro decodes, it does not encode.
- The GS1 family in the barcodes: GS1-128 encoded with FNC1 and its field separators, read back as
  the raw string and as a parsed list of application identifiers (76 of them, with their fixed or
  variable length), a GS1 Digital Link URL built from those identifiers and parsed back, and DataBar,
  MaxiCode and Micro QR decoding. `rxing` also brings UPC-A, UPC-E, Code 93 and Telepen to the
  encoder side; what it still cannot do — DataBar encoding — is written as unsupported rather than
  left to fail at runtime.
- OCR preprocessing: page orientation detected among 0/90/180/270, skew estimated to a quarter of a
  degree and corrected, and a median filter for noise — three options that default to off, so a
  straight clean page renders exactly as before. The result reports the rotation and skew applied,
  and the confidence refusal still decides what comes out.
- `PdfLotl`: the European trust list (ETSI TS 119 612) read into territories, services and their
  status periods, with the repository's XML policy applied from the first day — no DTD, no external
  entity, byte, depth and node ceilings. `PdfSignatureVerifier` then reports, per signature, whether a
  qualified service covered the signer's certificate at the judged instant. The list's own XAdES
  signature is not verified here, and the specification says so.

### Security

- `M3scalinum.Text.Docx` reads every XML part through one entry point, `xml::parse`, which refuses a
  DTD or an external entity, an internal entity that would expand past 1 MiB, more than 64 entity
  declarations, a part over 32 MiB, a depth over 256 elements and more than 2 000 000 nodes per
  part. The refusal is decided on the bytes, not on the parser's error, so it survives a change of
  XML parser, and it reaches .NET as `DocxErrorCode.XmlPolicy` with the part and the rule named.
  Verified with an external entity over a file and over HTTP, a billion-laughs document, an entity
  loop, a depth and a size past their bound, and a valid document that still reads.
