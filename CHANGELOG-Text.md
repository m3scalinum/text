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
- `M3scalinum.Text.Pdf` and `PdfSigner.Sign`: a PAdES-B signature written as an incremental update,
  so a PDF/A document stays PDF/A, an embedded Factur-X attachment stays readable, and an earlier
  signature stays valid. Verified with pyHanko (intact, trusted, entire file) and, on a signed
  Factur-X invoice, with veraPDF in PDF/A-3B.
- `M3scalinum.Text.Pdf.FactureX`: read a CII or UBL invoice into one model, check it against the
  EN 16931 rules, and render a PDF/A-3 whose XML is attached and whose XMP carries the `fx:`
  extension the standard requires. `CiiWriter` produces the syntax Factur-X mandates from an
  invoice built in C#.
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
