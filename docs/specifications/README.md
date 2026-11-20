# M3scalinum.Text.SharpTypst — Specifications

This directory is the normative description of M3scalinum.Text.SharpTypst: what it is, how it is layered, what
the native contract is, and how it is built, packaged and tested. The documents describe the
intended final state of the system, not its history. When code and specification disagree, one of
them is a bug.

## Reading order

| # | Document | Answers |
| --- | --- | --- |
| 00 | [00-overview.md](00-overview.md) | Scope, terms, use cases, quality attributes |
| 01 | [01-architecture.md](01-architecture.md) | Layers, projects, dependency rules, threading, lifetime |
| 02 | [02-native-abi.md](02-native-abi.md) | The C ABI: handles, buffers, errors, versioning, payloads |
| 03 | [03-compilation.md](03-compilation.md) | Worlds, sources, fonts, packages, export formats, determinism |
| 04 | [04-document-model.md](04-document-model.md) | The fluent builder and its Markup writer |
| 05 | [05-templates.md](05-templates.md) | Template packages, typed parameters, resolution |
| 06 | [06-markdown-and-text-export.md](06-markdown-and-text-export.md) | Semantic extraction and its fidelity contract |
| 07 | [07-diagnostics.md](07-diagnostics.md) | Diagnostic model, mapping, exception taxonomy |
| 08 | [08-build-and-packaging.md](08-build-and-packaging.md) | Solution layout, MSBuild props, NuGet, CI, releases |
| 09 | [09-testing.md](09-testing.md) | Test pyramid, fixtures, golden files, matrices |
| 11 | [11-reference-coverage.md](11-reference-coverage.md) | Every item of the Typst reference and how it is reachable |
| 12 | [12-dependency-injection.md](12-dependency-injection.md) | The `M3scalinum.Text.SharpTypst.DependencyInjection` package: services, lifetimes, options, validation |
| 13 | [13-facturx.md](13-facturx.md) | The `M3scalinum.Text.Pdf.FactureX` package: reading, validating and rendering e-invoices |
| 14 | [14-pdf-signature.md](14-pdf-signature.md) | The `M3scalinum.Text.Pdf` package: PAdES signatures as an incremental update |

## Status

| Area | State |
| --- | --- |
| Specifications | This document set — baseline agreed |
| `native/sharp-typst` (Rust cdylib) | v0.3: world, paged + HTML compilation, all six export formats |
| `src/Text.SharpTypst.Native` (P/Invoke) | v0.3: interop, handles, resolver, error mapping |
| `src/Text.SharpTypst` (managed API) | World builder, compiler, `CompileBoth`, export options, packages, evaluation, templates, document model |
| `src/Text.SharpTypst.DependencyInjection` | `AddSharpTypst`, options, validation |
| `src/Text.SharpTypst.Templates.Generator` | Roslyn generator: `template.toml` → typed options record |
| `src/Text.Pdf` | PAdES-B signatures as an incremental update; no dependency on the compiler |
| `src/Text.Pdf.FactureX` | CII/UBL reader, EN 16931 checks, CII writer, Factur-X renderer and XMP patch |
| Tests | 281 unit (fake native) + 8 Pdf + 125 Factur-X + 14 generator + 7 suite + 229 integration (real compiler) |
| Benchmarks | `benchmarks/Text.SharpTypst.Benchmarks`, baseline stored, `--check` gate in CI |
| Build & packaging | Build files in place; native packaging wired in CI |

## Conventions used in these documents

- **MUST / MUST NOT / SHOULD / MAY** carry their RFC 2119 meaning. A *MUST* is a testable
  invariant: a test exists for it, or one is owed.
- Code blocks marked `csharp` are the intended public API at the shape level. Names in these
  documents are normative; whitespace and formatting are not.
- Code blocks marked `c` are the ABI surface. Symbols named there MUST exist with that exact
  spelling.
- Rust snippets describe intent, not final syntax.
- Decisions that could reasonably have gone another way are recorded as **Decision** with the
  rationale and the rejected alternative, so they are not re-litigated later.

## Scope boundary in one paragraph

M3scalinum.Text.SharpTypst compiles Typst source and exports it. It does not implement layout, does not host a
service, and does not parse Typst. Everything a caller can control is either a Typst feature
surfaced as a typed option or an environment input (source bytes, font bytes, package bytes,
clock). Anything else is out of scope and belongs in Typst itself.
