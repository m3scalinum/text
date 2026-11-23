# 00 — Overview

## 1. What M3scalinum.Text.Typst is

A .NET library that hosts the Typst compiler in-process and offers three ways to produce a document.
It is the compiler package of the suite, `M3scalinum.Text.Typst`:

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
| Platforms | `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `linux-musl-x64`, `linux-musl-arm64`, `osx-arm64`, `osx-x64`; .NET 8, 9, 10 and 11 |

### 2.2 Out of scope (v1)

- Parsing Typst into the object model: generation is one-way.
- Incremental compilation and a live-preview server.
- Font subsetting control, and PDF/A or PDF/UA switches beyond what Typst exposes.
- Any network access at compile time unless the caller opts into the Universe package resolver.

### 2.3 The document test

The suite produces and transforms **documents**: bytes a reader opens and a person reads. Three
questions decide whether a capability belongs in it.

1. **Does it produce or transform bytes that are a document?** A page, a file, an image — something
   that exists to be read.
2. **Does it run one way?** The suite reads a document format and writes the PDF family: PDF, and
   its siblings SVG, PNG, HTML, Markdown and plain text. A received PDF is not an editing surface;
   signing, attaching and structure are the only things written into one.
3. **Is the calendar ours?** The suite keeps the standards whose schedule it controls — the Typst
   language and the PDF format — and never depends on a rule set, an identifier or a fetch that
   somebody else publishes. The operational test: *when someone else publishes a new version of
   their rules, does the suite have to ship a release?* Where the answer would be yes, the caller
   supplies the bytes, the certificate or the integration, and the library stays a library.

The same three questions bound what OCR is here: recognition is in, and document analysis — layout,
tables, field capture — is a different engine. A capability that fails one of the three stays
outside, and the caller plugs its own implementation.

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
