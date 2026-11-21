# Changelog — the suite train

All notable changes to the packages that do not embed the Typst compiler are documented in this
file: `M3scalinum.Text.Pdf`, `M3scalinum.Text.Pdf.FactureX`, `M3scalinum.Text.Templates` and
`M3scalinum.Text.Mcp`. They are released from `v*` tags
by `.github/workflows/release-text.yml`, and that workflow takes its release page from here.

The compiler packages have their own file, [CHANGELOG-SharpTypst.md](CHANGELOG-SharpTypst.md); what
the project gains as a whole is in [CHANGELOG.md](CHANGELOG.md).

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and these packages
adhere to [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Their version follows the suite,
`TextVersion` in `Directory.Build.props`, not the compiler: a fix to a signature must not read as a
Typst release.

## [Unreleased]

Not yet published.

### Added

- `M3scalinum.Text.Templates`: three more layouts, `contract` (contrat: two parties, numbered clauses
  with an optional bullet list each, and a signature block), `payslip` (bulletin de paie: employer
  and employee blocks, the period, the gross earnings lines, the contributions and the net, computed
  from the amounts the caller supplies) and `purchase-order` (bon de commande: buyer, supplier,
  delivery address, ordered lines with tax and total, terms and two signature lines). Each ships as
  an embedded `layouts/<id>/template.toml` and `template.typ`, a typed options record generated from
  the manifest (`ContractOptions`, `PayslipOptions`, `PurchaseOrderOptions`), an English and French
  label set in `TemplateLabels`, and an entry in `TemplateCatalog` — seven layouts in all. The
  layouts print no word of their own: every heading, clause phrase, column title and date pattern is
  a label, so the same file renders in English or in French. Tested with a minimal and a realistic
  dataset per layout (page count, a known string in the text the PDF draws, both languages) and with
  the typed exception a missing required parameter raises.
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
  `PdfTextError.Encrypted`, naming the security handler; decryption is not implemented. What is
  recovered is glyph order as the content stream yields it — no reading order, no layout, no OCR —
  and a scanned page comes back empty. Verified on compiler output (two pages with a marker each, a
  table and a footnote) and on a browser-printed document, plus hand-written files for a Type0 CMap
  in every shape, a font with no CMap, a form XObject and an encrypted trailer.
- `M3scalinum.Text.Pdf.FactureX` and `XRechnungValidator`: the XRechnung CIUS rules that can be
  decided from the document — the specification identifier (`BT-24`) in its 3.0, extension, vehicle
  and 4.0 pre-release spellings, the business process (`BT-23`), the buyer reference (`BT-10`) as the
  Leitweg-ID, the seller contact (`BG-6`) with its name, telephone and e-mail, the seller and buyer
  postal terms, the deliver-to group, the seller and buyer electronic addresses, the document type
  code list, the payment instructions and the account or mandate each payment means requires, the
  IBAN check digits, the `SKONTO` spelling of the payment terms, the VAT rate of each breakdown and
  the seller identification the VAT categories require, the price base quantity, the gross-to-net
  price arithmetic, the line period against the invoice period, the allowance and charge base,
  percentage and amount, and the German terms of a corrected invoice, the VAT accounting currency and
  the sub invoice lines the CIUS does not support. `FacturXValidator.Validate(invoice)` now selects
  one of the three overlays by declaration — XRechnung first, then Peppol, then the French rules —
  because a German document carries the Peppol billing process in `BT-23` while declaring the German
  CIUS in `BT-24`, and a gate on `BT-23` alone would hold every Peppol invoice to the German rules.
  `CiiWriter` writes the CIUS identifier in `BT-24` when that is what `SpecificationId` holds, so an
  XRechnung CII document is no longer written as a plain EN 16931 one. The official KoSIT Schematron
  remains the reference: this is the subset `FacturXInvoice` states exactly, with one document built
  from the model per rule as the negative case and one conformant invoice as the positive one.
- `M3scalinum.Text.Mcp`: a Model Context Protocol server over stdio, exposing `render_document`
  (Typst source, or a layout of `M3scalinum.Text.Templates` with its parameters), `render_invoice`
  (a `FacturXInvoice`-shaped JSON payload, validated and rendered as Factur-X PDF/A-3) and
  `list_templates`. JSON-RPC 2.0 is implemented by hand on a source-generated serializer context —
  no SDK dependency, no reflection-based JSON, `PublishAot`-clean — with `initialize`, `tools/list`,
  `tools/call`, JSON-RPC error objects for an unknown method, an unknown tool and invalid arguments,
  and end of input as a clean shutdown. `McpServerOptions` carries the limits and permissions: max
  document size, max request size, allowed template ids, render timeout, and `AllowFileOutput`,
  which defaults to `false` so no tool writes a file unless the host says so. A PDF is returned as
  an MCP embedded `application/pdf` resource, base64-encoded. Tests drive the server over an
  in-memory stream pair with real frames; the render tests read `SHARPTYPST_NATIVE_PATH` and skip
  when it is absent.
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
- `M3scalinum.Text.Pdf.FactureX` and `PeppolBisValidator`: the Peppol BIS Billing 3.0 CIUS rules that
  can be decided from the document — the business process (`BT-23`) and the specification identifier
  (`BT-24`), the seller and buyer electronic addresses (`BT-34` / `BT-49`) and their EAS scheme codes,
  the invoice and credit note type codes, the credit-note reference (`BG-3`), the direct-debit mandate
  and the credit-transfer account, the reverse-charge and intra-community buyer identifiers, the VAT
  breakdown coverage, the seller identifier, and the allowance and charge percentage, base and amount.
  `UblWriter` now writes `cbc:ProfileID` and honours `FacturXInvoice.SpecificationId` for
  `cbc:CustomizationID`, and both readers and both writers carry the preceding invoice reference, so a
  document that was schema-valid and rejected by every access point is now diagnosable. The new
  `IInvoiceValidator` extension point and `FacturXValidator.Validate(invoice, IInvoiceValidator)` run a
  caller's rule set — the official Schematron — alongside the built-in ones and merge the issues. The
  official Schematron remains the reference: this is the subset the model states exactly, and the
  `PEPPOL-COMMON` identifier grammars are left to it. Verified against the OpenPEPPOL publication's own
  sample invoice, credit note and direct-debit snippet, with one document built from the model per
  rule as the negative case.
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
- `M3scalinum.Text.Pdf` and `PdfSigner.Sign`: a PAdES-B signature written as an incremental update,
  so a PDF/A document stays PDF/A, an embedded Factur-X attachment stays readable, and an earlier
  signature stays valid. Verified with pyHanko (intact, trusted, entire file) and, on a signed
  Factur-X invoice, with veraPDF in PDF/A-3B.
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
- `M3scalinum.Text.Pdf.FactureX`: read a CII or UBL invoice into one model, check it against the
  EN 16931 rules, and render a PDF/A-3 whose XML is attached and whose XMP carries the `fx:`
  extension the standard requires. `CiiWriter` produces the syntax Factur-X mandates from an
  invoice built in C#, and `UblWriter` produces an EN 16931 / Peppol BIS Billing 3.0 compatible
  UBL 2.1 `Invoice` — or a `CreditNote` for `TypeCode` 381. The element order of both writers
  follows the official schemas, and both round trips are tested in every direction.
- The model gains the EN 16931 terms both syntaxes bind to: document-level (`BG-20` / `BG-21`) and
  line-level (`BG-27` / `BG-28`) allowances and charges, the invoice period (`BG-14`) and line
  period (`BG-26`), seller and buyer contacts (`BG-6` / `BG-9`), supporting documents (`BG-24`),
  contract (`BT-12`) and project (`BT-11`) references, the payment reference (`BT-83`), the SEPA
  mandate (`BG-19`), the tax currency (`BT-6`), the line base quantity, gross price and price
  discount, and item classification codes. `FacturXTotals` gains `AllowanceTotal` (`BT-107`) and
  `ChargeTotal` (`BT-108`).
- `FacturXAllowanceCharge`, `FacturXPeriod`, `FacturXContact`, `FacturXSupportingDocument`,
  `FacturXClassification` and `FacturXMandate` carry those terms, and both readers and both
  writers keep them in step.
- The validator covers the new arithmetic and identifiers: `BR-CO-11`, `BR-CO-12`, `BR-CO-13`,
  `BR-CO-19`, `BR-CO-20`, `BR-CO-21`, `BR-CO-22`, `BR-27`, `BR-31`, `BR-32`, `BR-36` and `BR-49`.
- `FacturXReader.ReadPdf(ReadOnlySpan<byte>)` and `FacturXReader.TryExtractXml(…)`: the invoice is
  read back out of a Factur-X PDF, byte for byte, so a receiver can verify what it was given. The
  attachment is located through the catalog's name tree and inflated when compressed.
- `FacturXCodeLists`: the nine VAT categories and the payment means codes of EN 16931, enforced;
  currencies and units checked for shape.
- `FacturXInvoice.PaymentMeansCode` (BT-81) and `FacturXInvoice.IsCreditNote`; a credit note
  (`TypeCode` 381) is rendered as *Avoir* and accepts negative amounts.
- The validator covers `BR-04`, `BR-08`, `BR-09`, `BR-18`, `BR-CL-16`, `BR-CL-17`, `BR-CL-23`,
  `BR-CO-9`, `BR-S-08` and `BR-Z-08` in addition to the arithmetic rules.
- The French profile (`ExtendedCtcFr`) and its rules: legal identifiers (SIREN/SIRET), the billing
  framework of `BT-23`, the three mandatory notes (`PMT`, `PMD`, `AAB`), the place of delivery and
  cascade invoicing.
- CI validates the embedded XML against the official schema with the FNFE-MPE reference library, and
  runs **veraPDF** against PDF/A-3B on the rendered samples.
