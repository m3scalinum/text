# 03 — Compilation

How a world is described, what the compiler is given, and what comes out.

## 1. Entry points

```csharp
using M3scalinum.Text.Typst.Compilation;

// Shortest path: one source, defaults, PDF.
byte[] pdf = TypstCompiler.CompileToPdf("#set page(paper: \"a4\")\nHello *world*.");

// Explicit world: assets, fonts, packages.
using var world = new TypstWorldBuilder()
    .AddSource("main.typ", File.ReadAllText("report.typ"))
    .AddSource("partials/summary.typ", summaryMarkup)
    .AddAsset("logo.png", logoBytes)
    .AddFont(interBytes)
    .WithFixedClock(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero))
    .Build();

var compiler = new TypstCompiler();
using var result = compiler.Compile(world);

byte[] pdf  = result.Export(ExportFormat.Pdf);
string html = result.ExportHtmlString();   // requires a compilation with DocumentKind.Html
```

`TypstCompiler` is cheap and stateless apart from configuration; one instance per process (or per
DI singleton) is the intended usage. It is not `IDisposable`. A world is safe to compile from
several threads at once.

## 2. `TypstWorld`

`TypstWorldBuilder` mutates a draft; `Build()` freezes it and creates the native world handle.
`TypstWorld.Clone()` returns a new world sharing the library, the font book and the fonts, and
`WithSource(path, markup)` returns a specialised clone. `SetSource(path, markup)` replaces a source
in the world itself, which is what a caller iterating on one document wants — a clone per keystroke
copies the source map each time and throws away nothing else (§16).

| Member | Meaning |
| --- | --- |
| `AddSource(string path, string markup)` | Registers a UTF-8 source. `path` is virtual, forward slashes only, no leading `/` or drive letter. A second call with the same path replaces it. |
| `AddSource(string path, ReadOnlySpan<byte> utf8)` | Same, pre-encoded. |
| `AddAsset(string path, ReadOnlySpan<byte> bytes)` | Any file the document can read: images, `json`, `csv`, `yaml`, `bib`, `.typ` data. |
| `AddFont(ReadOnlySpan<byte> bytes, uint faceIndex = 0)` | Registers a font. Deduplicated by content hash. |
| `AddFontDirectory(string path, FontScanMode mode = FontScanMode.Shared)` | Scans a directory for `.ttf/.otf/.ttc`. Explicit opt-in, no system scan. |
| `AddSystemFonts(FontScanMode mode = FontScanMode.Shared)` | Opt-in use of the OS font directories. Off by default. |
| `WithFontIndexCache(string directory)` | Puts the persistent font index in `directory` instead of the user's cache directory. |
| `WithoutFontIndexCache()` | Disables the persistent font index: every process scans the fonts itself. |
| `WithPackages(Action<TypstPackages>)` | Package resolution: `CacheDirectory`, `DataDirectory`, `AllowDownload`, `Registry`. Offline by default. |
| `WithLimits(Action<TypstLimits>)` | Caps on source, asset and font bytes, and on the number of sources and fonts. |
| `WithFixedClock(DateTimeOffset utc)` | Makes `datetime.today()` deterministic. **Default.** |
| `WithSystemClock()` | Uses the real clock. Documented as non-reproducible. |

### 2.1 Path rules

- Paths are case-sensitive regardless of the host file system, so documents port between
  Windows and Linux unchanged.
- `main.typ` is not special in the world; it is special only as the default *root*.
- A path MUST NOT contain `..`, a backslash, a leading `/`, or a colon. Violations throw
  `ArgumentException` at `AddSource` time, not at compile time.
- Imports resolve relative to the importing file; the root is the anchor for absolute-looking
  paths, which are still relative to the virtual root.

### 2.2 The World implementation

The Rust side implements `typst::World`:

```rust
struct NetWorld {
    library: LazyHash<Library>,   // built once per process, cloned by Arc
    book:    LazyHash<FontBook>,  // immutable for the handle's lifetime
    host:    Option<Arc<FontIndex>>, // the host fonts, shared per source set (see §14)
    sources: RwLock<HashMap<FileId, Source>>,
    files:   RwLock<HashMap<FileId, Bytes>>,
    main:    FileId,
    today:   Option<Datetime>,
    packages: Option<Box<dyn PackageStorage>>,
}
```

- `library()` and `book()` return precomputed values. Building the library per world is a
  measurable cost and is avoided.
- `source()` and `file()` look up the virtual table; a miss returns `FileError::NotFound` with the
  virtual path (never a host path, never a disk read).
- `today()` returns the fixed instant unless `WithSystemClock` was used.
- Fonts are parsed once, at `AddFont`, so an invalid font fails at registration rather than during
  layout.

**Decision — no ambient file system access.** The world never reads the disk unless the caller
explicitly opts in. Sources, assets and fonts are registered as bytes; packages are the one
exception, and they are opt-in and offline by default (§2.3). Rejected: mapping the working
directory into the world, which makes builds depend on `cd` and breaks caching.

### 2.3 Packages

`@namespace/name:version` resolves through `typst-kit`'s package storage: the data directory, then
the cache directory, then — only if allowed — the registry.

```csharp
using var world = new TypstWorldBuilder()
    .WithPackages(packages =>
    {
        packages.CacheDirectory = "/var/cache/sharp-typst";  // null = system cache
        packages.AllowDownload = true;                       // default false
    })
    .AddSource("main.typ", """
        #import "@preview/cetz:0.3.1": canvas, draw

        #canvas({ draw.circle((0, 0), radius: 1) })
        """)
    .Build();
```

| Setting | Default | Effect |
| --- | --- | --- |
| `CacheDirectory` | system cache | Where downloaded packages are stored and read from |
| `DataDirectory` | system data | Where local packages are read from |
| `AllowDownload` | `false` | Whether a missing package may be fetched |
| `Registry` | `https://packages.typst.org` | Registry base URL; must be http(s) |
| `MirrorDirectory` | `null` | A verified, cache-only mirror; the only package source (§15) |

**Decision — offline by default.** A compile is a pure function of its inputs; a network call
inside it would make latency, failure modes and reproducibility depend on the host's connectivity.
A host that wants packages either pre-seeds the cache (CI, containers, air-gapped deployments) or
sets `AllowDownload`. With downloads disabled, a missing package fails with the package name in the
diagnostic rather than a network error.

The cache layout is Typst's own — `<cache>/<namespace>/<name>/<version>/` — so a directory populated
by the `typst` CLI works unchanged, and vice versa.

A cache is silent about what changed in it. A deployment that has to answer that question ships a
mirror instead: the same layout plus a `mirror.json` of SHA-256 digests, verified before a world
exists. §15 covers the script that fills it, the cache-only mode, and what a digest does and does not
prove.

### 2.4 Fonts

Three sources, in increasing order of surprise:

| Source | Call | Reproducible |
| --- | --- | --- |
| Embedded | always on | yes |
| A directory | `AddFontDirectory(path)` | yes, if the directory is pinned |
| The host's fonts | `AddSystemFonts()` | **no** — two machines can lay out differently |

```csharp
using var world = new TypstWorldBuilder()
    .AddFontDirectory("/opt/fonts")   // scanned recursively
    .AddSystemFonts()                 // opt-in, and the scan is shared by the process
    .AddSource("main.typ", "#set text(font: \"Inter\")\n= Title")
    .Build();
```

**Decision — host fonts are opt-in.** A compile that silently reads `/usr/share/fonts` makes the
output depend on the machine, which breaks the determinism guarantee in §6. `AddSystemFonts` is
there for the cases where that trade is right, and the documentation says so.

`TypstWorld.FontFamilies` lists what the world can actually resolve, sorted. It is the answer to an
"unknown font family" warning: the list is the embedded families plus whatever the builder added.

**The scan is per process, not per world.** §14 describes the index: where it lives, how it is
invalidated, what it costs and how a world refuses it.

## 3. Compilation options

```csharp
public sealed class TypstCompilationOptions
{
    public string Root { get; init; } = "main.typ";
    public TypstDocumentKind DocumentKind { get; init; } = TypstDocumentKind.Paged;
    public bool WarningsAsErrors { get; init; }
}
```

`TypstDocumentKind` is `Paged`, `Html` or `Bundle`. It is a property of the compilation, not of the
export: PDF and HTML are different layout passes, and a document compiled as `Paged` cannot be
exported to HTML. `CompilationResult.Kind` reports which pass produced the result.

`WarningsAsErrors` turns a warning into a `TypstCompilationException` carrying the diagnostics, and
disposes the result. It is the switch a build or a CI gate wants; the default keeps warnings
non-fatal, because a document that lays out correctly is still a document.

Consequence, stated plainly: **PDF + HTML from the same source means two compilations.** That is a
Typst constraint, not a limitation of this library. The `CompileAll` helper exists to make it one
call:

```csharp
using var both = compiler.CompileBoth(world);   // Paged + Html, sharing the same world
byte[] pdf  = both.Paged.ExportPdf();
string html = both.Html.ExportHtmlString();
```

`CompileBoth` compiles the paged document first; if it fails, the HTML pass is not attempted and
the exception carries only the paged diagnostics, which are the authoritative ones.

## 4. Export

```csharp
public enum ExportFormat { Pdf, Svg, Png, Html, Markdown, Text }

public sealed record PdfExportOptions { public IReadOnlyList<string>? Standards { get; init; } }

public sealed record SvgExportOptions
{
    public int? Page { get; init; }        // null = merged document
    public bool Pretty { get; init; }
}

public sealed record PngExportOptions
{
    public int? Page { get; init; }        // null = merged document
    public float Ppi { get; init; } = 144f;
}

public sealed record HtmlExportOptions
{
    public bool Pretty { get; init; } = true;   // line breaks and indentation
    public bool EmbedFrames { get; init; }      // inline SVG for layouted islands
}
```

| Product | Underlying crate call | Notes |
| --- | --- | --- |
| PDF | `typst_pdf::pdf(&document, &PdfOptions)` | Byte-deterministic for fixed inputs. Standards are passed through as `PdfStandard` values (`1.7`, `2.0`, `a-2b`, `ua-1`). |
| SVG | `typst_svg::svg(page)` / `svg_merged(&document)` | Text remains text where the font allows; used glyphs are embedded as paths otherwise. |
| PNG | `typst_render::render(page, &RenderOptions::new(pixel_per_pt, fill))` | `ppi / 72` is the pixel-per-point factor. `png-merged` stacks pages vertically. |
| HTML | `typst_html::html(&document)` | Requires `DocumentKind.Html`. Emits MathML for equations (Typst 0.15). |
| Markdown | derived from the HTML document | See [06](06-markdown-and-text-export.md). |
| Text | derived from the HTML document | See [06](06-markdown-and-text-export.md). |
| Bundle | `typst_bundle::export(&bundle, &BundleOptions)` | Requires `DocumentKind.Bundle`. Returns every file the document produces. |

### 4.1 PDF options

`ExportPdf(PdfExportOptions)` exposes what `typst_pdf::PdfOptions` carries:

| Setting | Effect |
| --- | --- |
| `Standards` | PDF/A-1b … A-4e, PDF/UA-1, or a PDF version. Incompatible combinations are rejected |
| `PageRanges` | One-indexed subset, e.g. `"1-3,5"` or `"2-"` |
| `Tagged` | Tagged output for accessibility; default on |
| `Timestamp` | Creation date. PDF/A requires one, either here or via `#set document(date: …)` |
| `Ident` | Stable document identifier |
| `Creator` | `/Creator` metadata |
| `Pretty` | Human-readable PDF syntax |

Two constraints come from Typst and are surfaced as `TypstExportException` rather than silently
worked around: a page range cannot be combined with tagged output, and PDF/A needs a document date.

Fonts are deliberately absent from this table: the writer embeds a subset of every face it uses and
exposes no switch for it. §13 covers what that means and how to measure it.

### 4.2 The bundle target

`#asset` and `#document` are only valid in the bundle target, so a document that uses them cannot
be compiled as `Paged` or `Html`. `TypstCompiler.CompileBundle` runs the bundle pass and returns a
`BundleResult`:

Content must live inside a `#document("index.html")[…]` block. The bundle target has no implicit
document, so a heading or bare text at the top level is rejected with
`heading is not allowed at the top-level in bundle export`.

```csharp
var bundle = new TypstCompiler().CompileBundle(world);

string html = bundle.Html;                       // the main HTML document
foreach (var file in bundle.Files)               // index.html, data.txt, nested/meta.json, …
{
    Console.WriteLine($"{file.Path} ({file.Data.Length} bytes)");
}

bundle.WriteTo("out/");                          // one file per path, directories created
```

The bundle is exported once, at compile time: `typst_bundle::export` renders every `#document` in
the bundle (HTML, PDF, PNG or SVG, per the document's declared format) and collects every `#asset`.
The ABI then exposes the result as a manifest plus per-file bytes, so a large file never crosses
the boundary twice.

### 4.3 Page addressing

`Page` is 0-based. Out-of-range values throw `ArgumentOutOfRangeException` naming the valid range.
`PageCount` is available on the result. Merged exports ignore `Page`.

### 4.4 Memory and streaming

`Export(...)` returns `byte[]`. For a 200-page PDF at 1 MB per page that is a second 200 MB
allocation on top of the encoded file the native layer already holds, so the result can be written
straight into a stream instead:

```csharp
result.Export(ExportFormat.Pdf, response.Body);   // any format
result.ExportPdf(stream);                          // PDF
result.ExportPdf(stream, new PdfExportOptions { Standards = [PdfStandard.PdfA3b] });
TypstCompiler.CompileToPdf(stream, markup);        // one-off document
```

The native side still encodes the whole file first — Typst's writers return a `Vec<u8>` — and the
bytes are then pulled out in 64 KiB chunks (`tn_document_export_reader`, Spec 02 §3.6). What the
overloads remove is the managed copy, so the peak allocation of a large export is halved. The
stream is written synchronously and never seeked; a failing stream leaves the output truncated.

### 4.5 Per-page text

Markdown and plain text are derived from the HTML tree, so they have the document's structure but
no pages, and they refuse a paged document. The layout knows the pages, so it can answer a
different question: what text actually landed on page *n*.

```csharp
using var result = compiler.Compile(world);       // DocumentKind.Paged

string page0 = result.ExtractText(0);             // zero-based
IReadOnlyList<string> pages = result.ExtractTextPages();
```

| Member | Result |
| --- | --- |
| `ExtractText(int page)` | The text of one page, ending with `\n` |
| `ExtractTextPages()` | One entry per page, in page order |

The text is the layout's text, in the order the frame tree yields it, not a semantic
reconstruction. The page's frame is walked in order, recursing into groups, and every text run
contributes the characters its glyphs were shaped from. Runs on the same line are joined, with a
space where the layout left a horizontal gap — a table row therefore reads as its cells separated
by spaces; runs on different lines are separated by one newline. A heading is text on a line, a
list item is a line that starts with its bullet, and nothing marks them as such. Hyphenation and
ligatures come out as the font mapped them, so a ligature yields the characters it was shaped
from and a hyphen inserted by the line breaker is part of the text.

The order is the frame tree's order, which is reading order for a single-column document and is
not for a multi-column or absolutely positioned layout.

Both members require a paged document. On an HTML or bundle document the call throws
`TypstNativeException` with `TypstErrorCode.InvalidArgument` and the message
`page text requires a paged document`. An out-of-range page throws the same exception with the
valid range in the message. `ExportText()` is unchanged: it still requires an HTML document and
still returns the HTML tree's text.

**Decision — a second entry point rather than a mode on `ExportText()`.** The two answers are
produced by different parts of the compiler and carry different guarantees: one has structure and
no pages, the other has pages and no structure. Folding them behind one flag would hide which one
the caller got, and the failure modes are already disjoint.

## 5. Evaluation

`CompilationResult.Evaluate()` returns what the laid-out document says about itself:

```csharp
using var result = compiler.Compile(world);

var evaluation = result.Evaluate();

foreach (var entry in evaluation.Outline)
{
    Console.WriteLine($"{new string(' ', (entry.Level - 1) * 2)}{entry.Title} .... {entry.Page}");
}
```

| Property | Content |
| --- | --- |
| `PageCount` | Number of pages |
| `Outline` | Every heading: level, title, page, position, label |
| `Labels` | Every labelled element: name, page, position |
| `Metadata` | Every `#metadata(...)` entry: the value as JSON, and its page |

Page numbers are one-based and final, because the document is already laid out. The title is the
heading's body alone — not the supplement ("Section") and not the numbering.

**Decision — evaluation is a separate call, not part of `Compile`.** Most hosts never read the
outline, and building it costs a query over the element introspector. Making it explicit keeps the
common path free and makes the cost visible at the call site.

Evaluation requires a paged document: an HTML or bundle document has no positions, and the call
fails with `TypstNativeException` rather than returning an empty result.

## 6. Determinism

The output is byte-identical for identical inputs when:

1. the clock is fixed (`WithFixedClock`, the default), and
2. fonts are the same bytes, and
3. packages are pinned by version (`@preview/pkg:1.2.3`), not by `latest`, and
4. the compilation runs on the same Typst version (exposed as `TypstEnvironment.TypstVersion`).

PDF metadata (`CreationDate`, `ModDate`, `Producer`) is derived from the fixed clock. IDs are
content-derived. No random, no timestamps from the host, no absolute paths in the output.

Two things remain host-dependent and are called out because callers trip on them:

- **Font fallback across machines**: two hosts with different *system* fonts (`UseSystemFonts`)
  can pick different glyphs for a font name. Reproducible builds MUST register explicit font bytes.
- **PNG encoding**: the PNG encoder is deterministic for the same pixels, but pixels depend on the
  rasterizer backend which Typst pins, so this holds across platforms today, not by contract.

**Decision — cross-machine PDF determinism is not a goal.** The producer string carries the Typst
version, and the document IDs are content-derived but not canonicalised across builds of the native
library. Two machines running the same M3scalinum.Text.Typst version produce the same bytes; two different
versions may not. Pinning the producer string and the ID derivation would buy nothing a caller can
use, since a reproducible build pins the package version anyway.

## 7. Diagnostics

Every compilation returns diagnostics: `CompilationResult.Diagnostics`. Warnings and hints do not
fail a compilation; errors do, via `TypstCompilationException`. Position mapping, severities and
the exception taxonomy are in [07](07-diagnostics.md).

## 8. Performance model

| Cost | Paid when | Mitigation |
| --- | --- | --- |
| Font parsing | `AddFont` | Cache the parsed `FontBook` in a long-lived world; `Clone()` shares it |
| Host font scan | the first world of the process that asks for host fonts | One index per source set, shared through `Arc` and persisted to disk (§14) |
| Standard library construction | first compile in the process | Built once, shared via `Arc` |
| Package fetch | `UseUniversePackages` + first use of a version | Cached on disk by the caller-supplied cache directory |
| Layout | `Compile` | Unavoidable; scales with content, not with page count alone |
| Layout of the next compilation | `Compile` of an edited world | Reuses Typst's process-wide memo table: everything before the edit and every unchanged source (§16) |
| Rasterization | PNG export only | Choose PPI deliberately; 144 is the default, 300 for print |

Guidance for servers: build one template world with the fonts and the static assets, then take a
clone per request. A clone shares the library, the font book and the fonts, and copies only the
sources, so it never re-scans fonts and never re-reads packages.

## 9. Reusing a world across requests

```csharp
public sealed class RenderService : IDisposable
{
    private readonly TypstWorld _template;   // fonts + static assets, built once
    private readonly TypstCompiler _compiler = new();

    public byte[] Render(string markup)
    {
        using var world = _template.WithSource("main.typ", markup);
        using var result = _compiler.Compile(world);
        return result.Export(ExportFormat.Pdf);
    }
}
```

`WithSource` returns a new world; the template is never mutated, so the same instance serves every
request. A world is safe to compile from several threads at once — the sources are copied per clone,
the fonts are shared through `Arc`.

A caller that edits *one* document repeatedly — an author typing, a preview server — keeps one world
and replaces its source instead: `world.SetSource("main.typ", markup)` then
`compiler.CompileChanged(world, state)`. §16 has what survives the edit, what it costs in memory,
and what the returned list of changed pages does and does not say.

## 10. Cancellation and timeouts

`typst::compile` has no interrupt hook in 0.15. Therefore:

- `CompileAsync` runs the compilation on the thread pool and returns a task.
- If the token is cancelled, the task throws `OperationCanceledException` *when the compilation
  finishes*; the work is not aborted. The world handle is marked poisoned and disposed by the
  worker as soon as the compile returns.
- `TypstCompilationOptions.Timeout` bounds **the caller's wait**, not the work. The compilation runs
  on its own thread over a clone of the world; when the time is up the call throws
  `TypstTimeoutException` and the thread keeps going, releasing its clone and its result when it
  ends. The clone is what makes this safe: the caller may dispose its world the moment the call
  returns, and the abandoned compile still reads a live one.
- A hard cap on document size (`WithLimits`) is the supported way to bound worst-case *work*. The
  two are complementary: the timeout protects the request, the limits protect the machine.
- `TypstCompiler(maxConcurrency)` caps how many compilations run at once, one per processor by
  default. A compilation is CPU-bound: a hundred requests starting a hundred of them does not make
  them faster, it makes every one slower and the process unresponsive. `CompileAsync` waits for a
  slot **asynchronously**, so a queued request parks on the semaphore instead of occupying a thread
  pool thread to stand in line. `0` removes the cap for a caller that already bounds the work.

**Decision — the queue is the async story, not the compile.** `CompileAsync` returns a task, but the
compilation has nothing to await: once it starts it holds a thread for its whole duration. What can
be made asynchronous is the *wait*, and that is what the semaphore does. A host that needs more
concurrency than its processors is asking for slower compiles, not for more of them.

**Decision — the timeout does not pretend to kill the compiler.** Abandoning the thread is the only
thing that can be done without an interrupt hook, and it is honest about its cost: the abandoned
compilation still burns CPU and native memory until it finishes. A host that accepts markup from an
untrusted source and cannot afford that needs process-level isolation, not a bigger timeout. Stating
this is better than a `Timeout` that silently leaks a thread per hostile request.

## 11. Version pinning

The bundled Typst version is fixed by the Rust crate version and exposed as
`TypstEnvironment.TypstVersion` and `TypstEnvironment.LibraryVersion`. The package version
carries it: `0.15.1.<revision>` (see [08 §7](08-build-and-packaging.md#7-versioning-and-releases)).
Supporting a second Typst version means publishing a second package line (`M3scalinum.Text.Typst` 0.16.x
against Typst 0.16), not loading two compilers in one process.

## 12. Compilation metrics

Every `CompilationResult` carries `Metrics`, a `CompilationMetrics` describing what the compilation
cost and what it had to load. The numbers are read once, when the result is built, and are attached
to the document they describe.

```csharp
using var result = compiler.Compile(world);

var m = result.Metrics;
Report("compiled {Pages} page(s) in {Total} ms ({Compile} ms in Typst), {Fonts} font(s), {Bytes} B",
    m.PageCount, m.TotalDuration.TotalMilliseconds, m.CompileDuration.TotalMilliseconds,
    m.FontCount, m.FontBytes);

byte[] pdf = result.ExportPdf();
Report("pdf: {Bytes} B in {Duration} ms",
    result.LastExport!.Bytes, result.LastExport.Duration.TotalMilliseconds);
```

### 12.1 What is measured

| Member | Source | Meaning |
| --- | --- | --- |
| `TotalDuration` | managed, `Stopwatch` | the whole `Compile` call, after the queue wait |
| `CompileDuration` | native, `Instant` | `typst::compile`, plus the bundle export for a bundle |
| `PageCount` | native, layout | pages produced; `0` for HTML and bundle documents |
| `FontCount` | native, world | fonts the world could resolve |
| `EmbeddedFontCount` | native, world | how many of them are in the binary |
| `HostFontCount` | managed, derived | `FontCount - EmbeddedFontCount` |
| `FontBytes` | native, world | bytes of font data, each file counted once |
| `ErrorCount`, `WarningCount`, `HintCount` | managed | diagnostics of the document, by severity |
| `DiagnosticCount` | managed, derived | the three above, summed |
| `result.LastExport` | managed, `Stopwatch` | format, bytes and duration of the last successful export |

`TotalDuration` and `CompileDuration` differ by design: the first is what the caller waited, the
second is what Typst spent. A gap that grows between two releases is the managed layer or the
marshalling regressing; a `CompileDuration` that grows is the template or the compiler.

`LastExport` is separate from `Metrics` for one reason: a document's size does not exist before it is
encoded. Attaching a size to a compilation would mean encoding a PDF nobody asked for, so the export
reports its own numbers, and the last successful one wins. A failed export writes nothing and leaves
the previous reading in place rather than reporting a zero-byte document.

`FontBytes` counts each underlying buffer once. A font collection exposes one face per face over the
same file, so summing per face would over-report a `ttc` by its face count.

### 12.2 What is not measured

- **Memory.** The layout's peak allocation is not observable from inside the compiler, and the
  process working set is process-wide and platform-dependent — it cannot be attributed to one
  compilation. Budget `FontBytes` plus an allowance per page instead, and measure the process with
  your own tools if that matters.
- **Time per stage.** The library measures the compile as one span. Sub-stage timings (parse,
  evaluate, layout, encode) are Typst internals with no stable hooks; a number named after a stage
  would be a lie the next time the compiler changes. Use the two totals and the export durations.
- **Cache hits.** Whether Typst reused a memoised result is not exposed.
- **Failed compilations.** A compile that fails throws `TypstCompilationException` carrying the
  diagnostics, so no `CompilationMetrics` exists; `ErrorCount` on a result is therefore always zero,
  and the count is there so a caller can assert that rather than assume it.

### 12.3 Why a return value, not a collector

The metrics travel with the result rather than through a static registry or an `ActivityListener`.
Compilations run in parallel — a pool compiles one document per request — and a process-wide "last
metrics" slot would have every concurrent compilation overwrite another one's page count and
duration. A static collector would also need a flag to turn it off, a reset to keep it from growing,
and it would still be wrong under concurrency. A value returned with the document has none of those
properties: it is true for exactly the compilation that produced it, and it costs nothing when it is
ignored.

`TypstInstrumentation` (Spec 07) remains the process-wide view: spans and histograms for a dashboard.
`CompilationMetrics` is the per-document view, for the caller that has to make a decision about
*this* document.

### 12.4 Using it to size a pool and to alert

Sizing. A caller that caps concurrency with `new TypstCompiler(maxConcurrency)` can pick the cap from
what a compilation actually costs. `CompileDuration` gives the time side:

```csharp
// A cap is a statement about the queue: with a measured p95 compile time T and a request
// budget B, the slots you can afford are B / T, and never fewer than one.
var slots = (int)(budget.TotalMilliseconds / p95Compile.TotalMilliseconds);
new TypstCompiler(slots < 1 ? 1 : slots);
```

`FontBytes` gives the memory side: the embedded fonts and the faces registered with `AddFont` are
held by the world, and the host's own faces are held once per process because the index is shared
(§14), so a server no longer pays for them once per world. Neither number was reachable from the
public API before, which is why a cap used to be a guess.

Alerting. A regression is a change in a ratio, not in an absolute duration:

```csharp
if (m.CompileDuration > baseline.CompileDuration * 1.5)
{
    alert($"compile of {root} took {m.CompileDuration.TotalMilliseconds:F0} ms, "
        + $"baseline {baseline.CompileDuration.TotalMilliseconds:F0} ms");
}

if (m.PageCount != baseline.PageCount)
{
    // A template that starts paginating differently is a defect before it is a performance issue.
    alert($"{root} produced {m.PageCount} pages, baseline {baseline.PageCount}");
}
```

Keep a baseline per document kind and per root, recorded from a run you trust, and compare
`CompileDuration` (not `TotalDuration`, which includes the machine's own load) against it. The
diagnostic counts make the same series catch a template that started warning: a jump in
`WarningCount` is usually the first symptom of an edited include.

### 12.5 ABI

`CompilationMetrics` is filled from `tn_document_metrics`, an additive symbol of the native library
that returns one JSON object per document handle:

```json
{"compileNanos":1500000,"pages":3,"fontsTotal":12,"fontsEmbedded":10,"fontBytes":2048576}
```

The symbol is added to ABI revision 2 without changing `TN_ABI_VERSION`: every revision 2 symbol keeps
its name and signature, and a host that never asks for metrics never calls it. The managed decoder
treats an empty payload as zeros, so a library that does not answer still produces a usable result.

## 13. Font subsetting and what it costs

### 13.1 What Typst offers, and what it does not

Typst's PDF writer has no switch for font subsetting. `typst_pdf::PdfOptions` in 0.15.1 carries
`ident`, `creator`, `timestamp`, `page_ranges`, `standards`, `tagged` and `pretty` — nothing about
fonts — and the writer it delegates embedding to (`krilla` 0.8.2) calls the subsetter for every face
it embeds, with no parameter to turn it off: `krilla::configure::Configuration` holds a PDF version
and a validator set, nothing else. Three consequences follow, and they are properties of the writer
rather than settings this layer chose:

- **Every face is embedded.** Nothing is left as a reference to a system font, which is what makes
  the output portable and what makes a system font's licence matter at export time.
- **Every embedded face is a subset.** Its PDF name is `TAG+PostScriptName`, where `TAG` is a
  six-letter hash of the glyph set; two documents that use the same glyphs of the same face produce
  the same tag.
- **There is no whole-font mode and no per-range control.** Asking for "embed the font in full", or
  for a subset bounded by a Unicode range, would need a change in `krilla`; an option here that
  flipped a flag nothing reads would be a lie with a documented default.

`Tagged` remains the one PDF setting that changes the file's size without changing the document —
turning it off drops the structure tree (~800 bytes on the sample below) and keeps the fonts
untouched, so it is not a substitute for a subsetting control.

### 13.2 What the API gives instead

The measurement, which is the half a caller can act on:

```csharp
using var result = compiler.Compile(world);

PdfFontInventory inventory = result.MeasurePdfFonts();

foreach (var font in inventory.Fonts)
{
    Report("{Name}: {Bytes} B, {Format}, subset={Subset}",
        font.Name, font.Bytes, font.Format, font.IsSubset);
}
```

| Member | Meaning |
| --- | --- |
| `Bytes` | Total size of the PDF the inventory was read from |
| `FontBytes` | Bytes the embedded font files occupy |
| `FontShare` | `FontBytes / Bytes`, between 0 and 1 |
| `Fonts` | One `PdfEmbeddedFont` per embedded font file, in the file's order |

`PdfEmbeddedFont` carries `Name` (the PDF name, tag included), `Family` (the name without the tag),
`SubsetTag` (`null` for a whole font), `IsSubset`, `Format` (`PdfFontFormat`: `TrueType`, `Cff`,
`OpenType`, `Type1`, `Unknown`) and `Bytes`.

`MeasurePdfFonts` takes the same `PdfExportOptions` as the export it describes, and it **encodes the
document** — the numbers come from the bytes, so it is a second export when the caller already holds
the PDF. It never populates `LastExport`: nothing was handed to the caller. Call it on a measurement
or a template-review path, not per request.

### 13.3 What it measures

Four documents, exported on one machine with the embedded fonts only. Sizes are the encoded PDF and
the font streams inside it, which is what the file pays — the same face costs several times more once
inflated.

| Document | PDF | Font bytes | Share | Faces |
| --- | --- | --- | --- | --- |
| Title + one paragraph | 14 519 B | 7 219 B | 49.7 % | `LibertinusSerif-Regular` 5 229 B, `LibertinusSerif-Bold` 1 990 B |
| The same, `Tagged = false` | 13 740 B | 7 219 B | 52.5 % | unchanged |
| Title + one paragraph, wide repertoire | 18 379 B | 10 818 B | 58.9 % | `LibertinusSerif-Regular` 9 015 B, `Bold` 1 803 B |
| Ten paragraphs of the same text | 16 070 B | 7 219 B | 44.9 % | unchanged |

Three readings worth stating:

- **A text document is about half type.** On a one-page document the two faces are 49.7 % of the
  bytes; a caller budgeting a PDF has little left to shave elsewhere.
- **The subset tracks the glyph set, not the text length.** Ten paragraphs of the same paragraph
  embed exactly the same 7 219 B: the layout reuses the same glyphs, and the subset is a property of
  the repertoire. A wider repertoire — Greek, Cyrillic, maths, symbols — grows the same face from
  5 229 B to 9 015 B.
- **The whole font is nowhere near the file.** The world holds 9 683 068 B of font data across 17
  faces; the document embeds 7 219 B of it. Subsetting is doing most of the work already, which is
  why "embed fewer bytes" is not the lever it looks like — the lever is which faces the template
  uses at all. A body face and its bold cost 7 kB; asking for a third family for a footer costs the
  same order again.

The measurement is deterministic: the same document exported twice produces the same 14 519 bytes and
the same subset tags.

### 13.4 ABI

The inventory is a second export format, not a second symbol: `tn_document_export` accepts
`"pdf-fonts"` alongside `"pdf"`, encodes the document with the same options and returns one JSON
object instead of the file.

```json
{"bytes":14519,"fontBytes":7219,"fonts":[
  {"name":"PLPXUO+LibertinusSerif-Regular","family":"LibertinusSerif-Regular","subsetTag":"PLPXUO",
   "subset":true,"format":"cff","bytes":5229},
  {"name":"RMDPCX+LibertinusSerif-Bold","family":"LibertinusSerif-Bold","subsetTag":"RMDPCX",
   "subset":true,"format":"cff","bytes":1990}]}
```

`format` is `truetype` (`/FontFile2`), `cff` (`/FontFile3` with `/CIDFontType0C`), `opentype`
(`/FontFile3` with `/OpenType`) or `type1` (`/FontFile`); a value this layer does not know decodes as
`PdfFontFormat.Unknown` rather than throwing. The reader walks the objects, skips stream bodies by
their `/Length`, and pairs each font descriptor's `/FontName` with the stream its `/FontFile*` entry
points at, so binary font data cannot pose as an object.

### 13.5 Out of reach

- **Embedding a whole font.** No flag exists in `typst-pdf` or `krilla`; exposing one means changing
  the writer, not this layer.
- **Per-Unicode-range subsetting.** The subset is the glyph set the layout used. There is no way to
  say "include U+0020–U+007F" and no way to omit a glyph the document set.
- **Variable fonts.** krilla subsets with variations, keeping the variation data of the instance the
  document used. A caller cannot ask for a static instance, nor for the whole variation space.
- **Which glyphs were kept.** The inventory reports a face and its bytes, not a glyph coverage map.
- **Predicting the size before encoding.** The subset exists only after the encode; there is no
  estimate to read from the layout.

## 14. The host font index

A world that asks for host fonts — `AddSystemFonts` or `AddFontDirectory` — used to enumerate the
directories and parse every face on every `Build()`. A service that compiles per request paid that
scan per request. The metadata scan is now one per process, and the result is reused across
processes through a file on disk.

### 14.1 Where it lives

- **In the process**, a registry keyed by the source set: whether the system fonts were asked for,
  and the directories in the order the builder registered them. Every world with the same key holds
  the same `Arc<FontIndex>`, so the second world and the two-thousandth cost a lookup.
- **On disk**, one JSON file per source set — `index-<hash>.json`, the hash covering the index
  format, the compiler version and the source set — under the user's cache directory
  (%LOCALAPPDATA%\m3scalinum\text\fonts on Windows, `$XDG_CACHE_HOME/m3scalinum/text/fonts` or
  `~/.cache/m3scalinum/text/fonts` elsewhere), or under `WithFontIndexCache(directory)`.
- **What it holds**: per face, the path, the face index, the `FontInfo` Typst lays out with — family,
  variant, flags, axes, Unicode coverage — and the file's fingerprint.

The index holds metadata, never font bytes. A face is parsed the first time a compilation uses it,
so a warm process builds the font book without reading a single font file, and a document pays only
for the faces its layout picks.

### 14.2 Invalidation: the fingerprint

The fingerprint is the file's length and its modification time, not a hash of its contents. A
content hash means reading every font file — the cost the cache exists to remove — while a stat is
one syscall per file, and replacing a font changes its mtime on every file system this library runs
on. The two are compared per file, so a directory with one upgraded font re-parses that font and
nothing else.

| Change | Consequence |
| --- | --- |
| A font file is replaced | Its entry is dropped and the file parsed again |
| A font file is added to a walked directory | The directory walk finds it and parses it; the known faces are reused |
| A font file is deleted | Its entry leaves the index |
| A walked root no longer exists | The file is discarded and the sources go through `typst-kit`/`fontdb` again |
| Another Typst version or index format | The file is ignored and rewritten; there is no migration |
| Bytes changed but length and mtime preserved | The stale entry is served — a refresh (§14.3) is the remedy |

A process keeps the index it built: the file is read once, at the first world of the process. A font
installed or replaced while the process runs is picked up on disk by a refreshing world, never by an
ordinary one.

### 14.3 Refusing the index

`AddSystemFonts(FontScanMode.Refresh)` — and `AddFontDirectory(path, FontScanMode.Refresh)` — scans
the sources again and replaces the process-wide index, so every world built after it sees the new
faces:

```csharp
// After an operator installed fonts, or on a schedule: rescans once, for the whole process.
using var refreshed = new TypstWorldBuilder()
    .AddSystemFonts(FontScanMode.Refresh)
    .AddSource("main.typ", "= x")
    .Build();
```

The default is `FontScanMode.Shared`: a world joins the index if the process has one. The refresh
is cooperative rather than local — a per-world copy would put the two branches of a service on
different font sets, which is worse than the staleness it fixes.

### 14.4 What it costs

Measured on one Windows 11 host with 185 installed faces, from a debug build of the native library
(a release build is faster on both sides; what matters here is the ratio):

| Measurement | Value |
| --- | --- |
| First scan of the system fonts in a process | 0.91 s |
| The same scan plus parsing every face, per world, before this change | 2.56 s |
| 2 000 worlds, one page compiled each, one scan | 0.72 s in total — 0.36 ms per world, zero further scans |
| Second process, persistent index reused | no font file parsed for the faces that did not change |

The first world of a process that asks for host fonts still pays the scan; nothing later does. A
host that wants that paid before the first request pre-warms (§14.5).

### 14.5 Pre-warming

- Build one world with `AddSystemFonts()` at startup and dispose it: the first real request finds a
  warm index. A world that only registers fonts needs no source.
- Seed a container image or a CI cache by building once with `WithFontIndexCache("/var/cache/...")`
  and shipping that directory: the next process reads it instead of the fonts.
- `WithoutFontIndexCache()` on a read-only file system: the process still scans once and keeps the
  index in memory, it just does not persist it.
- `TypstWorld.Clone()` remains the cheapest path of all: a clone shares the world's book and fonts
  and never touches the index.

### 14.6 ABI

No symbol was added. `system`, `directories`, `refresh`, `cache` and `cacheDir` travel in the world
config JSON that `tn_world_create` already takes, so a host that predates this change sends the same
payload it always did and gets the shared index; a host that wants a refresh sets one boolean.

## 15. Package mirrors

A mirror is a directory of `@namespace/name:version` packages plus a `mirror.json` that lists every
file of every package with its SHA-256 and its size. A world configured with one resolves packages
only from it, and verifies the manifest before it exists.

**Decision — the failures of a cache are silent, so the mirror is loud.** §2.3 already makes a
compile offline: with `AllowDownload` false a missing package fails instead of reaching the network.
That leaves the other half of the problem. A cache is a directory somebody else can write to, and
the compiler reads whatever it finds there: a file edited in place is served without a word, a
package dropped into the tree is resolved as if it had been fetched. In a deployment that ships a
cache — a container image, a build artifact, an air-gapped host — nobody can tell afterwards which
bytes were compiled. A manifest makes an edit loud and an unlisted file louder.

### 15.1 What a mirror is

```text
/opt/typst-mirror/
├── mirror.json
└── preview/
    └── cetz/
        └── 0.3.1/
            ├── typst.toml
            ├── lib.typ
            └── ...
```

The package layout is Typst's own (`<namespace>/<name>/<version>/`), so a directory the `typst` CLI
populated is already a mirror — it only needs the manifest. The manifest names every package it
covers, and every file inside them:

```json
{
  "format": 1,
  "generatedBy": "typst-mirror/1 (+https://github.com/m3scalinum/text)",
  "packages": {
    "@preview/cetz:0.3.1": [
      { "path": "lib.typ", "sha256": "9b5b…", "size": 4123 },
      { "path": "typst.toml", "sha256": "7a10…", "size": 402 }
    ]
  }
}
```

Paths are relative to the package directory and use forward slashes. `format` is checked: a manifest
of a format this build does not read is refused rather than guessed at.

### 15.2 Filling and verifying a mirror

`tools/typst-mirror.py` writes and checks the manifest. It is standard library only, so it runs in a
build image with nothing installed but Python:

```bash
# From the registry. Repeatable; the manifest covers everything the mirror then holds.
python3 tools/typst-mirror.py --mirror /opt/typst-mirror \
    --package @preview/cetz:0.3.1 \
    --package @preview/tablex:0.0.8

# From a directory a `typst` CLI already populated, for an air-gapped job.
python3 tools/typst-mirror.py --mirror /opt/typst-mirror --source ~/.cache/typst/packages \
    --package @preview/cetz:0.3.1

# Check a mirror against its manifest, changing nothing.
python3 tools/typst-mirror.py --mirror /opt/typst-mirror --verify
```

`--verify` exits `0` when every file matches, `1` when it refuses one, and `2` when the request
itself is wrong (no manifest, a package the registry does not serve). A refusal names the package,
the file and the mirror:

```text
refused: package `@preview/cetz:0.3.1` in the mirror at `/opt/typst-mirror` has a `lib.typ` whose
SHA-256 is 3f1c…, and the manifest records 9b5b…
```

`fill` unpacks into a staging directory inside the mirror and moves the result into place, so an
interrupted download never leaves a half-populated package that the manifest would then describe.

### 15.3 The cache-only mode

```csharp
using var world = new TypstWorldBuilder()
    .WithPackages(packages => packages.MirrorDirectory = "/opt/typst-mirror")
    .AddSource("main.typ", """
        #import "@preview/cetz:0.3.1": canvas, draw

        #canvas({ draw.circle((0, 0), radius: 1) })
        """)
    .Build();
```

| Setting | JSON | Effect |
| --- | --- | --- |
| `MirrorDirectory` | `packages.mirror` | The mirror is the only package source |
| — | `packages.cacheDir`, `packages.dataDir` | Set to the mirror by the native layer; a different value is refused |
| `AllowDownload` | `packages.download` | Must stay `false`; `true` with a mirror is an `ArgumentException` |

Setting a mirror switches the world to cache-only, and it does so in the strict sense: the mirror
becomes **both** package directories, so the system data directory — where the host's own local
packages live — cannot satisfy an import that the mirror does not hold. The manifest is then read
and verified in `WorldConfig::parse`, before any world exists. A mirror that does not match it never
becomes a world.

Three refusals, each naming what an operator has to fix:

| Situation | Message |
| --- | --- |
| A file's bytes are not the ones recorded | ``… has a `lib.typ` whose SHA-256 is 3f1c…, and the manifest records 9b5b…`` |
| A file is present but unlisted | ``… holds `extra.typ`, which mirror.json does not list`` |
| A package is present but unlisted | ``the mirror at `…` holds package `@preview/other:1.0.0`, which mirror.json does not list`` |
| A listed package or file is absent | ``package `@preview/cetz:0.3.1` is missing `lib.typ` …`` |
| A package the document imports is not in the mirror | ``package `@preview/absent:9.9.9` is not in the package mirror at …; add it with tools/typst-mirror.py`` |

Every one of them names the package, the file where there is one, and the mirror — which is what
turns "the build is red" into "re-fetch this".

The last one is the one a compile hits in practice, and it is the reason the managed layer keeps a
preflight of its own. Verification can only speak about what the manifest lists; a package the
document imports and the mirror never held is invisible to it, and the compiler's own diagnostic
(`package not found (searched for @preview/absent:9.9.9)`) names the package but not the mirror —
which reads like a typo in the import. `TypstWorldBuilder.Build` therefore checks the registered
sources for `@namespace/name:version` literals and fails with the mirror and the command that fills
it. That preflight is a scan, not a parser: a spec built at run time still reaches the compiler, and
from then on the message is the compiler's.

Unlisted files and unlisted packages are refused for the same reason as a wrong digest: resolution
walks the mirror's directories, so a file or a package that verification never saw is a file or a
package a compilation can still read.

### 15.4 What it proves, and what it does not

A verified mirror proves **integrity**: the bytes a world will read are the bytes this manifest
describes. It does not prove **authenticity**: a mirror that can rewrite `lib.typ` can rewrite
`mirror.json` in the same motion, and the result verifies cleanly. The manifest is only as trustworthy
as the path it arrived by, so the deployment has to supply that trust elsewhere — a signed image, a
release artifact pinned by digest, a read-only layer, or a signature over `mirror.json` of its own.

Two narrower limits are worth stating rather than discovering:

- **The check happens when the world is created, not when each file is read.** A file changed between
  `Build()` and the compilation that reads it is served with the digest that was checked earlier. Mount
  the mirror read-only, or verify it again before the build, if the process outlives the check.
- **The manifest is not a lock file.** It does not enumerate the versions a document may request; it
  describes what the mirror holds. Pinning is §11's job, and the two compose: a pinned import plus a
  verified mirror is a compile that cannot silently change either branch.

Verification costs one walk and one SHA-256 per file per world creation. Build one world and clone it
(§9) rather than building one per request; a clone shares the resolution and pays nothing.

## 16. Incremental compilation

An author iterating on a document compiles it again after every edit, and each of those
compilations used to pay the full price. It no longer does, and the reason is not a cache this
library built: it is the one Typst already has.

### 16.1 What is reused

Typst 0.15 memoizes each stage of a compilation — parsing, evaluation, realization, layout — in
`comemo`, a **process-wide** table keyed by the inputs of that stage. Nothing in this library owns
it, nothing in it is per world, and `typst::compile` never evicts it. Two consequences matter:

- **The key is the inputs, not the world.** A second compilation of the same text hits the entries
  the first one wrote, whether it runs on the same `TypstWorld` or on a clone of it (§16.3
  measures the clone: 9.9 µs). `WithSource` — which clones the world — therefore does not throw the
  reuse away; `SetSource` is not required for the saving.
- **A tracked world is not part of the key either.** The compiler records *what it asked the world*
  (`source(id)`, `file(id)`, `font(n)`, the library, the book, the clock) and validates those
  hashes on the next lookup. A new world with the same answers hits the same entries.

What `SetSource` buys is honesty and a little work: the world *is* the document a caller edits, the
source map is not copied per edit, and the handle a preview server holds never changes. It is the
API that says "same document, new text".

### 16.2 What invalidates it

| Input | What it costs when it changes |
| --- | --- |
| The text of a source | Its parse and evaluation are redone, and so is the layout of everything from the edit onward (§16.3) |
| An asset (`AddAsset`) | The layout that reads it, and everything after it |
| A font (`AddFont`, a host font, the font book) | Everything: the layout depends on glyph metrics |
| The clock (`today()`) | Every `datetime.today()` call, and the layout that uses it |
| A package (`@preview/...`) | The reading compilation that imports it |
| `ClearCache()` | Everything, for every document in the process |

### 16.3 What survives an edit, and what does not

Every `Content` hash in Typst includes a `Span`, and a span is a **byte midpoint** in the source
(`typst_syntax::Span` packs the file id and the offset). Inserting or deleting text anywhere shifts
the offsets of everything after it, so the evaluated content after an edit is not the content the
previous compilation saw — its layout is recomputed.

That is the shape of the win: **everything before the edit is reused, everything after it is laid
out again**, plus the parse and evaluation of every source that did not change. A one-line edit on
page one of a hundred-page document still re-lays out the other ninety-nine; the saving is the
front matter, the imported partials, the arrangement of the text before the cursor and every
artefact that does not depend on what follows it. It is a constant factor, not an asymptotic win,
and it is the reason this section says "cheaper" rather than "free".

Measured on one Windows 11 host, release build of the native library, an eight-page A4 report of
`lorem` paragraphs where a single sentence on page one is changed between the two compilations:

| Layer | Cold | Warm (one sentence changed) | Ratio |
| --- | --- | --- | --- |
| `typst::compile` in `cargo test --release`, first compilation of a fresh process | 11.6 ms | 1.3 ms | 9.1× |
| The same sources compiled from a *new* world (`clone_world`) | 11.6 ms | 0.01 ms | 1176× |
| `TypstCompiler.CompileChanged` from .NET, process warm, memo table cleared between rounds | 3.8 ms wall / 2.9 ms inside Typst | 1.8 ms wall / 1.0 ms inside Typst | 2.1× / 2.8× |

All three rows are the same document and the same edit. The first is the ratio a cold process sees:
it also pays for the first use of everything. The second is the claim of §16.1 in numbers — a
world that asked the same questions is answered from the table, so `WithSource` per request is not
a regression, it is a hit. The third is what a host lives with: **the wall clock of an edit is
about half of a cold compile**, and the part inside Typst about a third. What is left is the fixed
cost of the call — marshalling, the metrics and diagnostics round trip, and the fingerprint walk of
§16.4 — which does not shrink because nothing changed, and which is why the managed ratio is
smaller than the compiler's own.

`tests/Text.Typst.Integration.Tests/IncrementalCompilationTests.cs` measures this per run and
asserts it, on both the wall clock and the time inside Typst;
`benchmarks/Text.Typst.Benchmarks` replays the loop as `compile-incremental`.

### 16.4 `CompileChanged` and page fingerprints

```csharp
using var world = new TypstWorldBuilder().AddSource("main.typ", markup).Build();
var state = new TypstIncrementalState();     // one per document, kept between edits
var compiler = new TypstCompiler();

while (stillEditing)
{
    world.SetSource("main.typ", currentMarkup);

    using var changed = compiler.CompileChanged(world, state);

    foreach (var page in changed.ChangedPages)   // indices, ascending
        rerender(page, changed.Document);        // the document is complete, not just these pages
}
```

`CompileChanged` compiles the whole document, then compares one 64-bit digest per page with the
digests `state` recorded last. The document it returns is complete — every page, every export — and
`ChangedPages` is the answer to "which of them do I have to re-render". `IsFirstCompile` means
"all of them"; `RemovedPages` lists the indices the previous compilation had and this one does not.

The digest is computed from the **laid-out page**: its frame's geometry (positions, sizes,
baselines), its glyph runs (font, size, paint, language, text, glyph ids), its shapes, its images
and its link destinations, plus the page's fill, numbering and bleed. Source spans and element tags
are excluded — that is the whole point (§16.3): a digest that included them would mark every page
after an edit as changed and answer nothing.

What it does not say:

- **What changed inside a page.** A page is the same digest or not. Diffing positions or words is a
  different job and a different cost.
- **Why it changed.** A reflowed paragraph and an edited one look identical here.
- **Anything about a document without pages.** An HTML or bundle compilation has no page
  fingerprints: `ChangedPages` is empty and `IsFirstCompile` is still true.
- **That the fingerprints are stable across machines.** They are FNV-1a 64 over the hashed
  structures in the host's byte order: comparable within a process and across runs on one machine,
  not a content address to persist.
- **That an unchanged page is unchanged.** An edit before a page moves its spans, so the page is
  laid out again and the digest is recomputed. When it comes out equal, that is Typst having
  produced the same frame, which is exactly what a renderer needs to skip.
- **That links are exempt.** A link whose target moved changes the digest, so the page is reported
  as changed even if nothing visible did. Over-reporting is the safe direction; the alternative
  misses a page.

The fingerprints are computed on demand, in `tn_document_page_fingerprints`, not during
`tn_compile`: a host that never asks pays nothing. The walk is part of the difference between the
1.0 ms Typst reports and the 1.8 ms wall clock in §16.3 — 0.8 ms for eight pages, which also covers
marshalling, metrics and the diagnostics round trip, so it is an upper bound rather than a
measurement of the walk alone.

### 16.5 What a persistent world costs

- **The world itself is small.** One `Source` per source (its text and line index), the font book
  and fonts it shares with every other world through `Arc`, and its package handles. Keeping one
  world per open document is cheaper than re-cloning one per keystroke.
- **The memo table is the cost, and it is unbounded.** `comemo` keeps, per distinct version of a
  document, the parse tree, the evaluated content and the frames it laid out. Nothing evicts them —
  not `typst::compile`, not this library — so a preview session that compiles two hundred revisions
  of a ten-page document holds the frames of all two hundred. There is no per-document cap and no
  per-document eviction: `TypstCompiler.ClearCache()` drops the whole table for the whole process.
- **Call it deliberately.** A server that compiles a bounded set of documents in a loop never needs
  it. A server whose per-document revisions are unbounded — a long editing session, a render farm —
  should clear on a schedule, on idle, or when its own memory budget says so, and accept that the
  next compilation of everything is cold.
- **Clearing is safe while others compile.** A running compilation keeps the entries it already
  looked up and recomputes what it had not reached; it pays more, it does not fail.

### 16.6 ABI

Two symbols were added, and no existing one changed, so `TN_ABI_VERSION` stays at 2:

```c
int32_t tn_document_page_fingerprints(const TnDocument *document,
                                      TnBuffer *out,
                                      TnError **out_error);
void    tn_cache_clear(void);
```

`tn_document_page_fingerprints` returns `{"pages":[...]}`, one `u64` per page in page order, empty
for a document without pages. `tn_cache_clear` evicts every memoized result in the process. Spec 02
§3 does not describe either yet: §3.12 (metrics) is where the pages section belongs, and the cache
call belongs in a new §3.13 or next to the world functions — this document specifies the behaviour,
the ABI document must list the symbols.

## 17. Variable fonts and intermediate weights

A variable font carries a range per axis — typically `wght` from 100 to 900, or 300 to 700 — instead
of one cut per weight. Asking for a weight inside that range makes the compiler interpolate an
instance; asking for one outside it renders at the nearest end. This section states what the compiler
actually does with the axes, what the library exposes about them, and what is left to the caller.

### 17.1 What Typst does with an axis

`text.weight` takes one of nine names (`thin` … `black`) or an integer, clamped to 100–900. The value
is not compared against a list of installed cuts: it becomes a variation coordinate. On instantiation
Typst builds the axes the face advertises — `wght` from the weight, `wdth` from `stretch`, `opsz` from
the point size, `ital`/`slnt` from the style — clamps each to the range the font declares, and hands
the result to the shaper. A face without an `fvar` table gets no coordinate and cannot be
interpolated, which is the whole difference.

Selection is separate and does not defeat this: `FontBook::select` scores a family by family, style
and stretch before weight, and for weight it measures the distance to the face's axis *range* rather
than to a single cut. A variable family is therefore chosen for any weight inside its range, and the
interpolation happens afterwards.

The weight of a *static* face is fixed, so the same selection picks the nearest cut: 350 in a family
that ships 400 and 700 renders the 400 face. Nothing warns — the document simply looks like 400.

### 17.2 The measurement

Measured on Windows 11 with Bahnschrift (a variable face: `wght` 300–700, default 400; `wdth` 75–100,
default 100), the same one-line paragraph at 10 pt, read back from the glyph placements and the glyph
outlines of the SVG export:

| Weight | Line width | Outlines |
| --- | --- | --- |
| 300 | 174.185 pt | distinct |
| 350 | 174.185 pt | distinct |
| 400 | 174.185 pt | distinct |
| 500 | 174.185 pt | distinct |
| 550 | 174.185 pt | distinct |
| 700 | 174.185 pt | distinct |
| 900 | 174.185 pt | identical to 700 |

Two things follow, and both matter:

- **The axis is applied.** Every weight from 300 to 700 draws a different outline, 350 and 550
  included. They are interpolated instances, not one of the shipped cuts: the 350 outline matches
  neither 300 nor 400.
- **It does not have to move the layout.** Bahnschrift keeps its advances constant across the weight
  axis, so the line occupies exactly the same width at every weight while the stems thicken. A test
  that only compares line widths would conclude the axis was ignored. The geometry that answers the
  question is the outline, not the advance.
- **Out of range is clamped, silently.** 900 renders the 700 instance, byte for byte.

The same paragraph in an embedded family, which is static, behaves as it did before this section
existed: 350 is *identical* to 400 in width (160.8 pt) and in outlines, and 700 is the bold cut
(176.97 pt).

`weight: "350"` — the weight as a string — is not a weight. Typst refuses the document with a cast
error rather than falling back to 400, which is the behaviour worth relying on: a wrong spelling fails
loudly instead of rendering something plausible.

### 17.3 What the library exposes

`TypstWorld.Fonts` reports every face the world resolves — family, default weight, width as a
percentage of normal, style, whether the face is variable, and each axis with its minimum, maximum and
default:

```csharp
using var world = new TypstWorldBuilder()
    .AddFontDirectory("/opt/fonts")
    .AddSource("main.typ", "#set text(weight: 350)\n\nInterpolated.")
    .Build();

foreach (var face in world.Fonts)
{
    foreach (var axis in face.Axes)
    {
        if (axis.Tag == "wght")
        {
            // 350 renders as 350 only when this is true.
            var reachable = axis.Contains(350);
        }
    }
}
```

`TypstFontFace.Axis("wght")` answers the same question for one axis:

```csharp
var weight = face.Axis("wght");

if (weight is not null && weight.Contains(350))
{
    // The face interpolates: 350 draws an instance of its own.
}
```

The list is metadata from the scan the world already did — no font is parsed to answer it — and it
covers the embedded faces too, which is how a caller sees that none of them carries an axis.

Nothing else in the compilation path filters weights: Typst source reaches the compiler as written, so
`#set text(weight: 350)` and `#text(weight: 350)[…]` work as soon as the world resolves a face with
the axis. The document model's styled-run helper quotes its argument, which is why the numeric form has
to be written as markup or through a raw node.

### 17.4 What is left to the caller

- **A static family cannot interpolate.** 350 in a family that ships 400 and 700 is 400, in silence.
  `TypstWorld.Fonts` is how a host finds that out before it promises the typography.
- **A static instance for a caller that needs the file, not the layout.** Freezing an axis into a
  static font (`varLib.instancer` and similar) is a font-production step; the suite ships no such tool
  and does not need one to render an intermediate weight.
- **Advances may not change.** A grid, a table column or a measured width cannot be used as evidence
  that a weight was applied; only the drawn outline can.

### 17.5 ABI

One symbol was added, and no existing one changed, so `TN_ABI_VERSION` stays at 2:

```c
int32_t tn_world_font_faces(const TnWorld *world,
                            TnBuffer *out,
                            TnError **out_error);
```

It returns a JSON array, one object per face:
`{"family":"Bahnschrift","weight":400,"stretch":100,"style":"normal","variable":true,"axes":[{"tag":"wght","min":300,"max":700,"default":400}]}`.
Spec 02 §3 does not list it yet; it belongs next to `tn_world_font_families`.
