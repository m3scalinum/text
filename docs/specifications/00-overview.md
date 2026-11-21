# 00 — Overview

## 1. What M3scalinum.Text.Typst is

A .NET library (single public package, `M3scalinum.Text.Typst`) that hosts the Typst compiler in-process and
offers three ways to produce a document:

1. **Source** — hand-written or generated `.typ` text, compiled as-is.
2. **Document model** — a fluent C# builder that emits Typst source.
3. **Template** — a packaged `.typ` file with declared, typed parameters, filled from C#.

All three converge on the same compiler entry point and the same export pipeline.

## 2. Scope

### 2.1 In scope (v1)

| Capability | Detail |
| --- | --- |
| Compilation | Typst 0.15.x language and standard library, paged documents and HTML documents |
| Export | PDF, SVG (per page and merged), PNG (per page and merged, configurable PPI), HTML, Markdown, plain text |
| Inputs | In-memory sources, in-memory fonts, in-memory binary assets, Typst packages from a local directory or from Typst Universe |
| Diagnostics | Errors, warnings and hints with file, line, column and trace |
| Document model | Headings, paragraphs, lists, tables, figures, images, code, math, quotes, references, page setup, metadata |
| Templates | Directory or embedded-resource template with a manifest and typed parameters |
| Determinism | Injected clock, stable ordering, reproducible output |
| Reference coverage | All 151 items of the Typst 0.15.1 reference are reachable: 60 typed, 9 as options, 82 through `Raw`/templates — see [11](11-reference-coverage.md) |
| Platforms | `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx` (universal); .NET 8, 9, 10 and 11 |

### 2.2 Out of scope (v1)

- Parsing Typst into the object model (one-way generation only).
- Incremental/watch compilation and a live-preview server.
- Multi-file bundle export (`typst-bundle`).
- PDF post-processing (merging, signing, form filling, encryption).
- Font subsetting control, PDF/A and PDF/UA conformance switches beyond what Typst exposes.
- `osx-x64`, `linux-arm64`, `win-arm64` — accepted as future RIDs, not shipped in v1.
- Any network access at compile time unless the caller opts into the Universe package resolver.

### 2.3 The document test

The suite produces and manipulates **documents**: bytes that a reader opens and a person reads. It
does not run services, and it does not own a jurisdiction's rules. Every proposed capability answers
one question:

> **Does it produce or transform bytes that are a document?**

A capability that fails the test is `Hors scope`. The exclusion is recorded with its reason and with
its counterpart: an extension point the caller plugs, so that the capability leaves the library
without leaving the product.

**Decision — the package owns the standard, not the national rules.** For e-invoicing the suite owns
EN 16931, the syntaxes that express it (CII, UBL, FatturaPA, Facturae, Factur-X/ZUGFeRD) and the
profile mechanism; it owns neither a jurisdiction's rule set, nor the channel a document is deposited
into, nor the identifier a platform assigns. The operational test that follows: *when a country
publishes a new version of its rules, does the suite have to ship a release?* If it does, the line has
been crossed. The national syntaxes already implemented are frozen: no extension, no new syntax.

| Excluded | Why it is not this suite's job | Counterpart the caller plugs |
| --- | --- | --- |
| Peppol addressing and delivery (SMP/SML, AS4) | The access point resolves the address and delivers: a lookup and a transport | `IInvoiceTransport` |
| Platform clearance and identifiers (ZATCA Fatoora, KSeF, MyInvois, CFDI timbrado, GST IRN, NF-e) | The document only exists once the platform has stamped it | `IInvoiceTransport` |
| Public-sector deposit (Chorus Pro, TED/eForms) | A deposit protocol with its own lifecycle and status | `IInvoiceTransport` |
| Country reporting APIs (SII, VeriFactu, myDATA, NAV, e-Factura) | A report per administration, published by that administration | `IInvoiceTransport` |
| National rule sets and CIUS (XRechnung, Peppol BIS, UBL-TR, Peppol PINT) | N rule sets, versioned outside our calendar | `IEInvoiceProfile` |
| Trust lists and revocation (LOTL, OCSP, CRL) | A fetch; the library signs and verifies with what it is given — see [14](14-pdf-signature.md) | `IRevocationSource`, and the trust list supplied as bytes |
| Qualified signatures and seals (eIDAS, EUDI wallet) | A qualified signature is delivered by an accredited provider | the caller's certificate and provider |
| Health documents (FHIR server, CDA ecosystem) | A server stands behind them; the suite renders the document it is given | the caller's own API |
| Vertical declarations (DSN, SAF-T, payroll) | National obligations with a legal calendar, not a document format | the caller's reader, fed with the bytes |
| Print workflow (imposition, PDF/X, ZPL/EPL) | A production chain for a press, not a document | none — out of the product |
| PDF editing (conversion to DOCX, XFA forms) | A different product built on a renderer | none — out of the product |
| A preview server | A socket and a session state | none — out of the product |

The list is the backlog's; the project board carries the ticket-level exclusions, and a ticket that
fails the test is closed by pointing at this section rather than argued case by case.

## 3. Use cases

| ID | Use case | Entry point |
| --- | --- | --- |
| UC-1 | Render a one-off document from a string | `TypstCompiler.Compile(source)` |
| UC-2 | Render a document with images and a custom font, all from memory | `TypstWorld` + `Compile` |
| UC-3 | Render the same document to PDF and HTML in one pass | `Compile` once, export twice |
| UC-4 | Fill a shipped template with a typed options record | `TypstTemplate<TOptions>.Render` |
| UC-5 | Build a report from a domain model without writing Typst | `TypstDocument` builder |
| UC-6 | Produce Markdown for an e-mail body and PDF for the attachment | `ExportFormat.Markdown` |
| UC-7 | Surface compiler warnings in a build log without failing | `CompilationResult.Diagnostics` |
| UC-8 | Fail a request with a precise error when the template is broken | `TypstCompilationException` |
| UC-9 | Compile concurrently from many requests | engine pool, one world per compile |
| UC-10 | Reproduce a customer's PDF byte-for-byte | fixed clock + pinned fonts + pinned packages |

## 4. Quality attributes

| Attribute | Requirement | How it is met |
| --- | --- | --- |
| Latency | Warm 1-page compile < 15 ms; 50-page report < 400 ms | In-process compiler, reused font book and library, memoization |
| Throughput | Linear with cores; no global lock | One world per compilation, no shared mutable state |
| Memory | < 60 MB steady state for a typical app; flat over 10 000 compiles | Explicit handle disposal, no per-compile font re-scan |
| Determinism | Byte-identical output for identical inputs | Injected clock, sorted iteration, no ambient file system |
| Diagnosability | Every compiler diagnostic reachable with position | Structured diagnostic payload over the ABI |
| Deployability | `dotnet add package` and nothing else | Natives inside the package, no PATH dependency |
| AOT/trim | `PublishAot` and `PublishTrimmed` succeed | `LibraryImport`, source-generated JSON, no reflection |
| Safety | No crash on malformed input; no UB across the ABI | `catch_unwind` at the boundary, owned buffers, no borrowed pointers |

## 5. Vocabulary

| Term | Meaning |
| --- | --- |
| **World** | Typst's environment trait: the set of sources, fonts, packages and the clock a compilation sees. In M3scalinum.Text.Typst, a `TypstWorld` is the managed description of that environment. |
| **Source** | A named byte buffer of Typst markup, identified by a virtual path (`main.typ`, `partials/header.typ`). |
| **Root** | The virtual path of the source that is compiled. Defaults to `main.typ`. |
| **Paged document** | The result of laying out content onto pages. Required for PDF, SVG and PNG. |
| **HTML document** | The result of the HTML export path. Required for HTML, Markdown and plain text. |
| **Bundle** | A multi-file output produced by `typst-bundle`. Out of scope for v1. |
| **Diagnostic** | A compiler message: severity, message, hints, trace, span. |
| **Span** | A byte range in a source file. Mapped to line/column for consumers. |
| **Handle** | An opaque native pointer owned by the managed layer. Never exposed publicly. |
| **Engine** | A managed object holding a native world handle plus its configuration. |
| **Template** | A directory (or embedded resource set) containing `template.typ` and `template.toml`. |
| **Markup writer** | The component that turns document-model nodes into Typst source with correct escaping. |

## 6. Package identity

| Item | Value |
| --- | --- |
| Package id | `M3scalinum.Text.Typst` |
| Root namespace | `Typst` |
| Assembly | `M3scalinum.Text.Typst.dll` |
| Target framework | `net8.0`, `net9.0`, `net10.0`, `net11.0` |
| Native library | `sharp_typst` (`sharp_typst.dll`, `libsharp_typst.so`, `libsharp_typst.dylib`) |
| Licence | Apache-2.0 — the same licence as Typst, so the combined work is trivially compliant |

**Decision — the libraries target `net8.0`, `net9.0`, `net10.0` and `net11.0`.** The managed layer
uses nothing newer than .NET 8, so the four targets share one source with no conditional compilation,
and the native library is RID-based and identical for all of them. Tests, samples and benchmarks
target `net11.0` only, which keeps the test matrix at one runtime. Rejected: `netstandard2.0` (no
`Span`-based marshalling, no AOT story).

**Decision — root namespace `Typst`.** The package id carries the `.NET` suffix; repeating it in
every `using` is noise. Rejected: `M3scalinum.Text.Typst.*` namespaces.
