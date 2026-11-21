# Changelog

M3scalinum.Text is a suite of document packages for .NET. It ships as two release trains, each with
its own version number, its own tag namespace and its own changelog:

| Train | Packages | Version | Detail |
| --- | --- | --- | --- |
| Compiler | `M3scalinum.Text.SharpTypst`, `.DependencyInjection`, `.Templates.Generator` | follows the embedded Typst, `0.15.1.N` | [CHANGELOG-SharpTypst.md](CHANGELOG-SharpTypst.md) |
| Suite | `M3scalinum.Text.Pdf`, `M3scalinum.Text.Pdf.FactureX` | follows `TextVersion`, `0.1.N` | [CHANGELOG-Text.md](CHANGELOG-Text.md) |

This file records what the project gains as a whole. The per-package detail — every type, every rule
the validator enforces, every defect the official validators caught — lives in the two train files
and is not repeated here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

Not yet published on nuget.org.

### Added

- A Typst compiler that runs in-process behind a managed API: PDF, SVG, PNG, HTML, Markdown, plain
  text and bundles, with no process to spawn, no binary on `PATH` and no temporary files.
- A fluent document model and a template system with typed parameters, generated from a
  `template.toml` manifest at compile time.
- PAdES-B signatures written as incremental updates, so signing a PDF/A document leaves it PDF/A and
  an earlier signature stays valid.
- Factur-X invoices end to end: build the invoice in C#, write the CII syntax the standard mandates,
  render a PDF/A-3 with the XML attached and the `fx:` XMP extension, then read the invoice back out
  of the PDF for the receiver to verify. The validator covers the EN 16931 arithmetic and business
  rules, and the French profile with its SIREN/SIRET, billing-framework and mandatory-note rules.
- Conformance verified outside the test suite: the embedded XML against the official EN 16931 schema
  with the FNFE-MPE reference library, and the rendered files against PDF/A-3B with veraPDF.
- Signature verification, PAdES B-T and B-LT (RFC 3161 timestamps and a DSS carrying the chain and
  revocation data), a document timestamp over the whole file, and a visible signature appearance.
- A structural conformance check for PDF/UA-1 and PDF/A read straight from the bytes: tagged
  structure, language and title, alternative text on figures, font embedding and `ToUnicode`, the
  cross-reference chain and the XMP identifier. veraPDF remains the reference validator.
- UBL writing alongside the CII writer, and the EN 16931 terms the model was missing — allowances and
  charges, invoice and line periods, contacts, supporting documents, payment reference, SEPA mandate,
  tax currency, gross price and price discount — round-tripped between both syntaxes.
- HTML import into the document model, with a published fidelity contract: prose, headings, nested
  lists, tables with spanning cells, inline emphasis, code, links, images from registered assets,
  block quotes and definition lists.
- A template pack of typed business layouts — invoice, quotation, account statement and attestation —
  bilingual, embedded, resolvable by id and version, with dependency-injection registration.
- The Peppol BIS Billing 3.0 CIUS rule set, with an `IInvoiceValidator` extension point for a caller's
  own validator — the official Schematron — merged into the built-in issues. The UBL writer emits the
  business process and specification identifiers, and both syntaxes carry the preceding invoice
  reference a credit note needs.
- Page operations on a PDF — merge, extract, split, rotate, remove — and AcroForm filling and
  flattening. A field the flattener cannot draw stays interactive and is reported, never silently
  dropped.
- Per-page text extraction from the paged layout, so a produced document can be indexed without
  re-deriving its pages from the Markdown export.
- A Model Context Protocol server over stdio: `render_document`, `render_invoice` and
  `list_templates`, with size limits and no ambient file access.
