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
