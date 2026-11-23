# Compiling

## The three objects

| Type | Lifetime | Role |
| --- | --- | --- |
| `TypstWorldBuilder` | short | Collects sources, assets, fonts and settings. |
| `TypstWorld` | long | The frozen inputs. Immutable, `IDisposable`, safe to compile from several threads. |
| `TypstCompiler` | process-wide | Stateless apart from configuration. **Not** `IDisposable`. |
| `CompilationResult` | per document | The laid-out document. `IDisposable`. |

```csharp
using var world = new TypstWorldBuilder()
    .AddSource("main.typ", "= Hello")
    .Build();

var compiler = new TypstCompiler();
using var result = compiler.Compile(world);

byte[] pdf = result.ExportPdf();
```

## Compilation options

```csharp
using var result = compiler.Compile(world, new TypstCompilationOptions
{
    Root = "main.typ",                        // the entry point
    DocumentKind = TypstDocumentKind.Paged,   // Paged, Html or Bundle
    WarningsAsErrors = false,                 // true fails the build on a warning
});
```

`DocumentKind` selects the layout pass. PDF, SVG and PNG come from `Paged`; HTML, Markdown and text
come from `Html`; the bundle target needs `Bundle`. `CompilationResult.Kind` reports which pass
produced the result.

`WarningsAsErrors` throws `TypstCompilationException` carrying the diagnostics and disposes the
result. It is the switch a CI gate wants.

## Exporting

```csharp
byte[] pdf  = result.ExportPdf();
byte[] pdf2 = result.ExportPdf(new PdfExportOptions
{
    Standards = [PdfStandard.PdfA2b],
    Timestamp = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
});

byte[] svg  = result.ExportSvg(page: 0);
byte[] all  = result.ExportSvgMerged();
byte[] png  = result.ExportPng(page: 0, ppi: 300f);
byte[] pngs = result.ExportPngMerged(ppi: 144f);
string html = result.ExportHtmlString();
string md   = result.ExportMarkdown();
string text = result.ExportText();
```

`Export(ExportFormat, …)` is the general form; the named methods are shortcuts.

### Writing straight to a stream

Those overloads return a `byte[]`. For a large document that is a second copy of the whole file,
which an HTTP handler does not need:

```csharp
result.Export(ExportFormat.Pdf, response.Body);   // any format
result.ExportPdf(stream);                          // PDF
result.ExportPdf(stream, new PdfExportOptions { Standards = [PdfStandard.PdfA3b] });
TypstCompiler.CompileToPdf(stream, markup);        // one-off document
```

The bytes are copied out of the native layer in 64 KiB chunks, so the peak allocation halves: the
encoded file exists once, in native memory, instead of twice. The stream is written synchronously
and never seeked.

### PDF standards

`PdfStandard` covers `1.4` through `2.0`, `a-1b`, `a-1a`, `a-2b`, `a-2u`, `a-2a`, `a-3b`, `a-3u`,
`a-3a`, `a-4`, `a-4f`, `a-4e` and `ua-1`. Three constraints surface as `TypstExportException`:

- `Tagged` cannot be combined with a page range.
- PDF/A needs a document date — set `Timestamp`.
- PDF/UA needs a document title — `#set document(title: …)`.

## Async

```csharp
using var result = await compiler.CompileAsync(world, cancellationToken: token);
```

The work runs on the thread pool. Typst 0.15 has no interrupt hook, so cancellation is observed
**before** the compile starts and **after** it finishes: a cancelled token throws
`OperationCanceledException` and the result is disposed, but a compile already running is not
interrupted.

## Reusing a world

A world is immutable. Build it once with the fonts and the packages, then take a clone per document:

```csharp
public sealed class RenderService : IDisposable
{
    private readonly TypstWorld _template;   // fonts + static assets, built once
    private readonly TypstCompiler _compiler = new();

    public byte[] Render(string markup)
    {
        using var world = _template.WithSource("main.typ", markup);
        using var result = _compiler.Compile(world);
        return result.ExportPdf();
    }
}
```

`Clone()` shares the library, the font book and the fonts through `Arc` and copies only the sources,
so a clone never re-scans fonts and never re-reads packages. `WithSource(path, markup)` is
`Clone()` plus one source. The template is never mutated, so one instance serves every request.

## Editing one document

A preview server, or any caller iterating on a single document, edits the world instead of cloning
it. `TypstWorld.SetSource(path, markup)` replaces one source in place and keeps the world, its font
book and its package handles:

```csharp
using M3scalinum.Text.Typst.Compilation;

using var world = new TypstWorldBuilder().AddSource("main.typ", "#let x = 1").Build();

var state = new TypstIncrementalState();       // one per document, kept between edits
var compiler = new TypstCompiler();

string[] revisions = ["= One", "= One\n\nA paragraph.", "= Two"];

foreach (string revision in revisions)
{
    world.SetSource("main.typ", revision);

    using CompileChangedResult changed = compiler.CompileChanged(world, state);

    Console.WriteLine(changed.IsFirstCompile);   // true on the first pass

    foreach (int page in changed.ChangedPages)   // zero-based, ascending
    {
        Console.WriteLine($"page {page} changed");
    }

    foreach (int page in changed.RemovedPages)
    {
        Console.WriteLine($"page {page} removed");
    }

    _ = changed.Document.ExportPdf();   // the document is complete, not just these pages
}
```

A world is no longer immutable once it has been edited, and that is deliberate: it *is* the document
the caller edits. What replaces immutability is that changing it affects no one else — a clone copies
the sources, so a template world handed to `WithSource` is never touched. Replacing a source while
another thread compiles the same world is memory-safe but ambiguous, because the compile sees either
the old text or the new one; serialise edits per world.

`CompileChanged` is `Compile` plus one comparison. It compiles the whole document, then compares one
64-bit digest per laid-out page with the digests `state` recorded last, and returns a
`CompileChangedResult`:

| Member | Meaning |
| --- | --- |
| `Document` | The complete `CompilationResult` — every page, every export works on all of it. |
| `ChangedPages` | Zero-based indices whose content differs, ascending. All of them when `IsFirstCompile`. |
| `RemovedPages` | Indices the previous compilation had and this one does not, ascending. |
| `IsFirstCompile` | There was no baseline, so every page is listed as changed. |

The digest covers the laid-out page — frame geometry, glyph runs, shapes, images, link destinations,
fill, numbering and bleed — and excludes source spans, which is what stops an edit from marking every
page after it as changed. It does not say *what* changed inside a page, or *why*, and it is an FNV-1a
64 value in the host's byte order: comparable within a process and across runs on one machine, not a
content address to persist. A document without pages — an HTML or bundle compilation — has no
fingerprints, so `ChangedPages` is empty. `TypstIncrementalState` carries only those digests (no
native handle) and is not thread-safe: one state belongs to one document edited by one caller, and
`Reset()` forgets the baseline so the next comparison reports every page.

**Where the saving comes from.** Not from `CompileChanged`: from Typst's own memo table, which is
process-wide and keyed by each stage's inputs. A second compilation of an edited document reuses the
parse and evaluation of everything that did not change, and everything before the edit point; it
re-lays out everything after the edit, because every `Content` hash carries a source span and an
insertion shifts every offset after it. It is a constant factor, not an asymptotic win. Measured in
the suite on one Windows 11 host, release build, an eight-page report with one sentence changed:
3.8 ms cold against 1.8 ms warm, wall clock (2.1×), and 2.9 ms against 1.0 ms inside Typst (2.8×).

What invalidates the recorded state:

| Change | Cost |
| --- | --- |
| A source's text | Its parse and evaluation, and the layout from the edit onward |
| An asset (`AddAsset`) | The layout that reads it, and everything after it |
| A font, the font book, a host font | Everything — the layout depends on glyph metrics |
| The clock (`today()`), a package | The layouts and reads that use them |
| `TypstCompiler.ClearCache()` | Everything, for every document in the process |

`ClearCache()` is the valve on the memo table, which never evicts on its own: a host that compiles
thousands of different documents keeps all of their intermediate results. It is process-wide, not
per compiler — a second `TypstCompiler` has no table of its own — so the next compilation of any
document, from any thread, is cold again. A compilation already running keeps what it has looked up
and recomputes the rest; it pays more, it does not fail.

**The memory is the cost.** Per distinct revision of a document the table holds the parse tree, the
evaluated content and the frames it laid out, with no per-document cap and no per-document eviction.
A preview session that compiles two hundred revisions of a ten-page document holds the frames of all
two hundred. A server that compiles a bounded set of documents in a loop never needs to clear; one
whose revisions are unbounded — a long editing session, a render farm — should clear on a schedule,
on idle, or when its own memory budget says so.

## A verified package mirror

A cache is a directory somebody else can write to, and a compile reads whatever it finds there: a
file edited in place is served without a word. `TypstPackages.MirrorDirectory` makes that edit loud.
It points the world at a directory of packages plus a `mirror.json` listing every file's SHA-256:

```csharp
using var world = new TypstWorldBuilder()
    .WithPackages(packages => packages.MirrorDirectory = "/opt/typst-mirror")
    .AddSource("main.typ", """
        #import "@preview/cetz:0.3.1": canvas
        """)
    .Build();
```

Setting the mirror puts the world in cache-only mode. The mirror becomes both package directories,
so the host's own local packages cannot satisfy an import the mirror does not hold, and downloads are
refused: `AllowDownload = true` with a mirror is an `ArgumentException`. The native layer reads and
verifies the manifest before a world exists, so a compile either runs over the bytes the manifest
describes or does not run. A wrong digest, a file or package the manifest does not list, a listed
file that is absent, and an import the mirror does not hold are each refused with a message naming
the package, the file where there is one, and the mirror. A missing import is caught earlier still,
when `Build()` scans the registered sources for `@namespace/name:version` literals; that preflight is
a scan, not a parser, so a spec built at run time reaches the compiler and from then on gets the
compiler's message.

The manifest is JSON, `format` 1, one entry per package with each file's relative path (forward
slashes), `sha256` and `size`. `tools/typst-mirror.py` fills and checks it, and is standard library
only:

```bash
# Fill from the registry, then write the manifest over everything the mirror holds.
python3 tools/typst-mirror.py --mirror /opt/typst-mirror \
    --package @preview/cetz:0.3.1 --package @preview/tablex:0.0.8

# Fill from a cache a typst CLI already populated, in an air-gapped job.
python3 tools/typst-mirror.py --mirror /opt/typst-mirror --source ~/.cache/typst/packages \
    --package @preview/cetz:0.3.1

# Check a mirror against its manifest, changing nothing.
python3 tools/typst-mirror.py --mirror /opt/typst-mirror --verify
```

Exit status is 0 when the mirror is filled or verified, 1 when verification refuses it, 2 when the
request itself is wrong.

**What the manifest proves is integrity, not authenticity.** It says the files are the ones it
describes; a mirror that can rewrite `lib.typ` can rewrite `mirror.json` in the same motion, and the
result verifies cleanly. The manifest is only as trustworthy as the path it arrived by, so the
deployment has to supply that trust elsewhere — a signed image, a release artifact pinned by digest,
a read-only layer. Two narrower limits: the check happens when the world is created, not when each
file is read, so a file changed between `Build()` and the compile that reads it is served with the
digest that was checked earlier; and the manifest is not a lock file, because it describes what the
mirror holds rather than which versions a document may request. Pinning the import is separate. Build
one world and clone it rather than building one per request — verification costs one walk and one
SHA-256 per file per world creation, and a clone shares the resolution.

With `AddTypst`, the same object is `TypstOptions.Packages`, which the container applies to every
`TypstWorldBuilder` it hands out.

## Determinism

The output is byte-identical for identical inputs when:

1. the clock is fixed — `WithFixedClock`, the default (2024-01-01T00:00:00Z);
2. the fonts are the same bytes;
3. packages are pinned by version, not `latest`;
4. the Typst version is the same.

Two machines running the same M3scalinum.Text.Typst version produce the same bytes. Two different versions may
not: the producer string carries the Typst version and the document IDs are not canonicalised across
builds. Cross-machine determinism is not a goal.

## Introspection

```csharp
var evaluation = result.Evaluate();

foreach (var entry in evaluation.Outline)
{
    Console.WriteLine($"{new string(' ', entry.Level * 2)}{entry.Title} — page {entry.Page}");
}
```

`Evaluate()` reads the **laid-out** document, so page numbers are final and references are resolved.
It requires a paged compilation.

## What the compilation cost

`CompilationResult.Metrics` is a `CompilationMetrics` for that one result, so a pool that compiles in
parallel never overwrites another caller's numbers:

```csharp
CompilationMetrics metrics = result.Metrics;

metrics.TotalDuration;        // the whole call, queue wait excluded
metrics.CompileDuration;      // the part Typst itself spent
metrics.PageCount;            // zero for an HTML or bundle document
metrics.FontCount;            // fonts the world could resolve
metrics.EmbeddedFontCount;    // how many of those are embedded in the native library
metrics.ErrorCount; metrics.WarningCount; metrics.HintCount;
```

`CompileDuration` is measured natively and excludes marshalling and reading the diagnostics back;
the gap between it and `TotalDuration` is what the managed layer costs around the compiler. Nothing
is collected unless a caller reads it — no listener to enable, no counter to reset.

## What the PDF embeds as fonts

`MeasurePdfFonts()` exports the document with the options you give it and reads the font inventory
back, without handing you the file:

```csharp
PdfFontInventory fonts = result.MeasurePdfFonts();

fonts.Bytes;       // size of the PDF the measurement exported
fonts.FontBytes;   // bytes the embedded font files occupy
fonts.FontShare;   // FontBytes / Bytes, between 0 and 1

foreach (PdfEmbeddedFont font in fonts.Fonts)
{
    // font.Name, font.Family, font.SubsetTag, font.IsSubset, font.Format, font.Bytes
}
```

Every face Typst lays out is embedded and subsetted — the writer has no switch for whole fonts — so
this answers which fonts a document carries, what each costs, and therefore how much of it is type.
`PdfFontFormat` is `TrueType`, `Cff`, `OpenType`, `Type1`, or `Unknown` for a writer newer than this
layer.

## Reading the text of a page

Markdown and plain text (`ExportMarkdown()`, `ExportText()`) come from the HTML tree, so they have
the document's structure but no pages. The layout knows the pages, and it can answer what text
actually landed on page *n*:

```csharp
using var result = compiler.Compile(world);        // DocumentKind.Paged

string page0 = result.ExtractText(0);              // zero-based, ends with "\n"
IReadOnlyList<string> pages = result.ExtractTextPages();
```

This is the layout's text, in the order the frame tree yields it — not a semantic reconstruction.
The page's frame is walked in order, groups included, and each text run contributes the characters
its glyphs were shaped from. Runs on one line are joined, with a space where the layout left a
horizontal gap (a table row reads as its cells separated by spaces); runs on different lines are
separated by one newline. A heading is just text on a line and a list item is a line starting with
its bullet. Hyphenation and ligatures come out as the font mapped them. The order is reading order
for a single-column document and the frame tree's order otherwise.

Both members require a paged document: on an HTML or bundle compilation they throw
`TypstNativeException` with `TypstErrorCode.InvalidArgument`. An out-of-range page throws the same
exception with the valid range in the message.
