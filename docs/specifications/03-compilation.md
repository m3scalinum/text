# 03 — Compilation

How a world is described, what the compiler is given, and what comes out.

## 1. Entry points

```csharp
using M3scalinum.Text.SharpTypst.Compilation;

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

A world is immutable once built. `TypstWorldBuilder` mutates a draft; `Build()` freezes it and
creates the native world handle. `TypstWorld.Clone()` returns a new world sharing the library, the
font book and the fonts, and `WithSource(path, markup)` returns a specialised clone.

| Member | Meaning |
| --- | --- |
| `AddSource(string path, string markup)` | Registers a UTF-8 source. `path` is virtual, forward slashes only, no leading `/` or drive letter. A second call with the same path replaces it. |
| `AddSource(string path, ReadOnlySpan<byte> utf8)` | Same, pre-encoded. |
| `AddAsset(string path, ReadOnlySpan<byte> bytes)` | Any file the document can read: images, `json`, `csv`, `yaml`, `bib`, `.typ` data. |
| `AddFont(ReadOnlySpan<byte> bytes, uint faceIndex = 0)` | Registers a font. Deduplicated by content hash. |
| `AddFontDirectory(string path)` | Scans a directory for `.ttf/.otf/.ttc`. Explicit opt-in, no system scan. |
| `AddSystemFonts()` | Opt-in scan of OS font directories. Off by default. |
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

**Decision — offline by default.** A compile is a pure function of its inputs; a network call
inside it would make latency, failure modes and reproducibility depend on the host's connectivity.
A host that wants packages either pre-seeds the cache (CI, containers, air-gapped deployments) or
sets `AllowDownload`. With downloads disabled, a missing package fails with the package name in the
diagnostic rather than a network error.

The cache layout is Typst's own — `<cache>/<namespace>/<name>/<version>/` — so a directory populated
by the `typst` CLI works unchanged, and vice versa.

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
    .AddSystemFonts()                 // opt-in, and it costs a scan
    .AddSource("main.typ", "#set text(font: \"Inter\")\n= Title")
    .Build();
```

**Decision — host fonts are opt-in.** A compile that silently reads `/usr/share/fonts` makes the
output depend on the machine, which breaks the determinism guarantee in §6. `AddSystemFonts` is
there for the cases where that trade is right, and the documentation says so.

`TypstWorld.FontFamilies` lists what the world can actually resolve, sorted. It is the answer to an
"unknown font family" warning: the list is the embedded families plus whatever the builder added.

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
library. Two machines running the same M3scalinum.Text.SharpTypst version produce the same bytes; two different
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
| Standard library construction | first compile in the process | Built once, shared via `Arc` |
| Package fetch | `UseUniversePackages` + first use of a version | Cached on disk by the caller-supplied cache directory |
| Layout | `Compile` | Unavoidable; scales with content, not with page count alone |
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
`TypstEnvironment.TypstVersion` and `TypstEnvironment.SharpTypstVersion`. The package version
carries it: `0.15.1.<revision>` (see [08 §7](08-build-and-packaging.md#7-versioning-and-releases)).
Supporting a second Typst version means publishing a second package line (`M3scalinum.Text.SharpTypst` 0.16.x
against Typst 0.16), not loading two compilers in one process.
