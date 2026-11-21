# Changelog — the suite train

All notable changes to the packages that do not embed the Typst compiler are documented in this
file: `M3scalinum.Text.Pdf` and `M3scalinum.Text.Pdf.FactureX`. They are released from `v*` tags
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
