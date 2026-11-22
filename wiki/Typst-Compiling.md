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
