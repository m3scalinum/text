# Changelog — the compiler train

All notable changes to the packages that embed the Typst compiler are documented in this file:
`M3scalinum.Text.Typst`, `M3scalinum.Text.Documents`, `M3scalinum.Text.Typst.DependencyInjection` and
`M3scalinum.Text.Templates.Generator`. They are released from `sharptypst-v*` tags by
`.github/workflows/release-sharptypst.yml`, and that workflow takes its release page from here.

The rest of the suite has its own file, [CHANGELOG-Text.md](CHANGELOG-Text.md); what the project
gains as a whole is in [CHANGELOG.md](CHANGELOG.md).

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and these packages
adhere to [Semantic Versioning](https://semver.org/spec/v2.0.0.html). The major and minor version
track the Typst compiler the package embeds; the build revision distinguishes two builds of the same
compiler. A Typst upgrade is a minor version unless it changes emitted output, in which case it is a
major one.

## [Unreleased]

Not yet published. Embeds Typst 0.15.1.

### Added

- The document model ships as a package of its own, `M3scalinum.Text.Documents`: it produces Typst
  markup with no native library and no compiler present, so a format package can build a document
  without depending on the compiler.
- `TypstDocument.FromMarkdown`: Markdown into the document model, with the same fidelity contract the
  HTML importer publishes — headings, nested lists, GFM tables, emphasis, code, links, footnotes and
  images resolved against registered assets only. Written by hand, so the package gains no dependency.
- `TypstDocument.ToMarkdown`: the model written back out as Markdown, over the same fidelity contract
  the import publishes — headings, nested lists, a GFM pipe table, emphasis, inline and fenced code,
  links, images, quotes and rules — and the text of everything Markdown cannot carry (raw markup,
  calls, plugins, footnotes, citations, colours and weights) conserved rather than dropped. The
  result names each construct it could not represent and how many times it saw it, so a lossy export
  is inspectable instead of silent.
- `TypstDocument.ToHtml`: the model as a semantic HTML5 fragment — headings, paragraphs, nested
  lists, tables with their spans and alignment, code with its language, links, images resolved by the
  asset resolver or inlined as a data URL, figures with their captions — with every node that has no
  HTML equivalent counted in the result. Entities are escaped in text and in attributes, and the
  output is deterministic, so a document diff is a document diff.
- In-process compilation of Typst source through a C ABI over the Rust `typst` crate. No process
  spawn, no binary on `PATH`, no temporary files.
- Export to PDF, SVG (single page or merged), PNG (single page or merged), HTML, Markdown, plain
  text, and the bundle target.
- `TypstWorldBuilder`: sources, assets, fonts and package settings registered from memory.
- One standard library and one parsed set of embedded fonts per process, shared by every world
  through `Arc`: building a world costs microseconds, not a font scan.
- `TypstCompiler`: paged and HTML compilation, `CompileBoth`, structured diagnostics with file,
  line and column.
- Package resolution for `@namespace/name:version` through the data directory, the cache directory
  and the registry. Offline by default; the cache layout is Typst's own.
- `TypstEvaluation`: the outline, the labels and the metadata entries of a compiled document, with
  final page numbers.
- Fluent document model (`TypstDocument`) covering text, headings, lists, tables, figures, code,
  math, shapes, paints, decorations and layout, with a markup writer that escapes every value.
- Template system: a `.typ` layout with declared, typed parameters, bound from C# and rendered
  through `TypstTemplate` / `TypstTemplate<TOptions>`.
- `TypstTemplate.RenderDocument(compiler, parameters, TypstDocumentKind)` and the same overload on
  `TypstTemplate<TOptions>`: one layout, either layout pass, so a template renders to PDF and to
  HTML, Markdown or plain text from the same call site.
- `M3scalinum.Text.Typst.DependencyInjection`: `AddTypst`, `TypstOptions`,
  `ITypstWorldFactory` and options validation.
- `M3scalinum.Text.Templates.Generator`: a Roslyn incremental generator that turns a
  `template.toml` manifest into a typed options record, with five compile-time diagnostics.
- Hard caps on source, asset and font sizes and counts, reported as `TypstErrorCode.LimitExceeded`.
- `tn_world_live_count` and `tn_document_live_count` in the ABI: the native layer reports the handles
  it is holding, so a host can assert that every handle it created was released instead of guessing
  from the process working set.
- Native AOT compatibility, verified by publishing the sample with `PublishAot`.
- Benchmarks with a stored baseline and a 1.5× regression gate on the minimum.
- `TypstCompilationOptions.WarningsAsErrors`: a warning fails the compilation with a
  `TypstCompilationException` carrying the diagnostics.
- `TypstCompilationOptions.Timeout`: a bound on how long the caller waits for a compilation, which
  is what a request has to bound when the markup comes from a customer. The work runs on its own
  thread over a clone of the world, so the caller may dispose its world the moment the call gives
  up; `TypstTimeoutException` reports it with the code `Timeout`. It bounds the wait, not the work —
  Typst has no interrupt hook, and saying otherwise would hide a thread per hostile request.
- `TypstCompiler(int maxConcurrency)`: a cap on how many compilations run at once, one per processor
  by default, `0` for none. `CompileAsync` waits for a slot asynchronously, so a queued request
  parks on the semaphore instead of holding a thread to stand in line.
- `Export(ExportFormat, Stream)`, `ExportPdf(Stream)`, `ExportPdf(Stream, PdfExportOptions)` and
  `CompileToPdf(Stream, …)`: the export is copied out of the native layer in 64 KiB chunks instead
  of being materialised in a second, managed array. The peak allocation of a large export halves,
  which is what an HTTP response body needs. The ABI grows a pull reader
  (`tn_document_export_reader`, `tn_export_reader_read`, `tn_export_reader_free`).
- `TypstInstrumentation`: one `ActivitySource` and one `Meter`, both named
  `M3scalinum.Text.Typst`. Spans `Typst.Compile` and `Typst.Export`, histograms for duration
  and page count, counters by outcome. Silent until a listener is attached.
- `linux-musl-x64` native library, shipped in the main package, so the library works in Alpine and
  other musl containers. NuGet resolves `linux-musl-x64` to the glibc asset otherwise, and the load
  fails; the resolver now detects the libc and probes the musl directory first. Rust cannot emit a
  `cdylib` for musl, so the shared object is linked from the staticlib.
- `CompilationResult.Kind`: which layout pass produced the result.
- `TypstWorld.FontFamilies`: the families the world can resolve, sorted — the answer to an
  `unknown font family` warning.
- `TypstWorld.Clone()` and `TypstWorld.WithSource(path, markup)`. A world is immutable; a clone
  shares the library, the font book and the fonts through `Arc` and copies only the sources, so it
  never re-scans fonts or re-reads packages.
- `TypstDiagnostic.SourceLine`, and a source snippet with a caret in `ToDisplayString()`, so a
  compile error is actionable without opening the file.
- Page setup: per-side margins (`Margin(top, right, bottom, left)`, `MarginX`, `MarginY`),
  `Landscape()`, `Fill(paint)` and `Columns(count)`.
- `TypstContent.PageNumber()` and `TypstContent.PageCount()` for running headers and footers.
- A `wiki/` directory in GitHub-wiki format: home, getting started, compiling, document model,
  templates, diagnostics, packages and fonts, dependency injection, performance, troubleshooting,
  building from source.
- `DocumentationDriftTests`: every `Typst*` type and every `.Method(` in a C# block of the
  specifications, the README and the wiki must exist on the public surface, and every `tn_*` named
  in the ABI spec must be exported by the crate. A guard test fails if the docs are not found, so
  the check cannot pass vacuously.
- `DocumentModelCoverageTests`: every node of the document model, the three export option types, the
  bundle, the embedded template loader and the fixed clock are compiled by the real compiler.
- `PublicApiTests`: no two public types share a name across namespaces, every public type lives in a
  `M3scalinum.Text.Typst` namespace, every public async method ends with `Async`.
- Leak coverage for the clone path: 10 000 `Clone()` and 1 000 `WithSource()` calls keep managed
  memory flat.
- Coverage tooling: `Microsoft.Testing.Extensions.CodeCoverage` and `tests/coverage.config.xml`.
- `tests/verify-package.py`, run by the `pack` job of the release workflows: every supported RID
  must carry its native library, the package must not depend on the unpublished
  `M3scalinum.Text.Typst.Native`, and the four licence files must be inside.
- The world's source and asset registries recover from lock poisoning instead of panicking, so one
  panic no longer bricks the world for every subsequent call. `TnError` derives `Debug`.
- Every P/Invoke call holds a `DangerousAddRef` for its duration, so a concurrent `Dispose` can no
  longer free the native object under a running call.
- `BundleResult.WriteTo` and `WriteToZip` reject a path that would escape the target directory or
  the archive. Typst already refuses such an `#asset`, so this is defence in depth.
- `TypstTemplate.Load(directory)` rejects a manifest `entry` that would read outside the template
  directory: `..`, an absolute path or a drive letter.
- The markup writer escapes a leading `1.` — Typst's enumerated list marker — so host data can no
  longer turn a paragraph into a list. A decimal such as `1.5` is left alone.
- A template `number` parameter rejects `NaN` and `Infinity` with a message naming the parameter,
  instead of emitting a literal the compiler answers with `unknown variable: NaN`.
- The TOML parser bounds the nesting of arrays and inline tables at 64. Without the bound, a
  malformed manifest raised an uncatchable `StackOverflowException` and took the process with it.
- `ConcurrentDisposeTests`: eight threads compile from one world while a ninth disposes it. Every
  caller sees a result or `ObjectDisposedException`; the process never crashes.
- `NativeLibraryResolverTests`: the native search order is pinned, including the `runtimes/osx/`
  fallback for the universal macOS dylib.
- `TypstDocument.FromHtml(html, options)` and `TypstHtmlImporter`: a hand-written HTML tokenizer and
  tree repair that imports `p`, `h1`–`h6`, nested `ul`/`ol`/`li`, tables with `colspan`/`rowspan`,
  `strong`/`b`, `em`/`i`, `code`, `pre`, `a href`, `img`, `blockquote`, `br`, `hr`, `dl`/`dt`/`dd`,
  `sup` and `sub` into the existing node graph, and counts — or throws on — everything else.
  `TypstHtmlImportOptions` carries the asset resolver for `img`, the unknown-element policy and the
  opt-in for the three inline declarations honoured. No new dependency, no network fetch.
- `TypstTableCell` and `TypstTable.Row(TypstTableCell…)` / `HeaderRow(TypstTableCell…)`: cells that
  carry a column or row span, emitted as `table.cell(colspan: …, rowspan: …)`.
- `TypstContent.Underline`, `Strikethrough`, `Subscript` and `Superscript` accept content, not only
  a string, so a nested run can be decorated without flattening it.
- Per-page text from a paged document: `CompilationResult.ExtractText(page)` and
  `CompilationResult.ExtractTextPages()`. The page's frame is walked in order, groups included, and
  each text run contributes the characters its glyphs were shaped from, so the result carries the
  page boundary the layout decided. It is the layout's text in frame order, not a semantic
  reconstruction: no headings, no list structure, and hyphenation and ligatures come out as the
  font mapped them. `ExportText()` is unchanged and still reads the HTML tree. The ABI grows
  `tn_document_page_text`.

### Changed

- The libraries target `net8.0`, `net9.0`, `net10.0` and `net11.0`, with one assembly per runtime in
  the package. `M3scalinum.Text.Typst.DependencyInjection` references the 8.0 line of the
  `Microsoft.Extensions.*` packages below `net11.0`, where the shared framework provides them.
- `EnablePackageValidation` is on for the three packages: a future breaking change to the public API
  fails `dotnet pack` instead of reaching a consumer.
- `TypstLocation` (the static location helpers) is renamed `TypstLocations`, so it no longer
  collides with the `TypstLocation` record in `M3scalinum.Text.Typst.Diagnostics`.
- `TypstWorld.ReplaceSource` is removed: `WithSource` returns the new world instead of mutating one.

### Security

- Every value that reaches the markup writer is escaped, so host data can never become Typst code.
  Covered by an adversarial test suite.
