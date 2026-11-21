# Getting Started

## Install

```bash
dotnet add package M3scalinum.Text.Typst
```

The package carries the native library for every supported platform, so there is nothing else to
install — no Typst binary, no Rust toolchain, no `PATH` entry.

> The package is not on nuget.org yet. Until the release workflow has produced the native assets,
> build it from source: see [Building from Source](Typst-Building-from-Source).

## Your first document

```csharp
using M3scalinum.Text.Typst.Compilation;

var pdf = TypstCompiler.CompileToPdf("""
    #set page(paper: "a4", margin: 2cm)

    = Hello

    A document compiled in-process.
    """);

File.WriteAllBytes("hello.pdf", pdf);
```

`CompileToPdf` is a convenience for a one-off document: it builds a world, compiles, exports and
disposes everything. For anything you do more than once, keep a world — see [Compiling](Typst-Compiling).

## The other formats

```csharp
byte[]   svg  = TypstCompiler.CompileToSvg(markup);            // page 0
byte[]   png  = TypstCompiler.CompileToPng(markup, ppi: 300f); // 300 dpi
string   html = TypstCompiler.CompileToHtml(markup);
string   md   = TypstCompiler.CompileToMarkdown(markup);
string   text = TypstCompiler.CompileToText(markup);
```

| Format | Returns | Notes |
| --- | --- | --- |
| PDF | `byte[]` | The default. Standards, page ranges and metadata via `PdfExportOptions`. |
| SVG | `byte[]` | One page, or every page merged vertically. |
| PNG | `byte[]` | One page, or every page merged. `ppi` defaults to 144. |
| HTML | `string` | A **different layout pass**: compile with `DocumentKind.Html`. |
| Markdown | `string` | Derived from the HTML tree, not from the paged layout. |
| Text | `string` | Same. |
| Bundle | `BundleResult` | HTML plus every asset the document emits. |

Markdown and text need an HTML compilation. PDF, SVG and PNG need a paged one. `CompileBoth` runs
both passes in one call when you need both.

## Assets and fonts

Everything the document reads comes from memory:

```csharp
using var world = new TypstWorldBuilder()
    .AddSource("main.typ", """
        #set text(font: "Inter")
        #image("logo.png", width: 4cm)
        """)
    .AddAsset("logo.png", File.ReadAllBytes("logo.png"))
    .AddFont(File.ReadAllBytes("Inter-Regular.ttf"))
    .Build();

using var result = new TypstCompiler().Compile(world);
File.WriteAllBytes("out.pdf", result.ExportPdf());
```

Paths are virtual: `logo.png` is a name inside the world, not a path on disk. See
[Packages and Fonts](Typst-Packages-and-Fonts) for the rules and for `@preview` packages.

## Next

- [Compiling](Typst-Compiling) — worlds, options, async, determinism.
- [Document Model](Documents-Model) — build the document in C# instead of Typst.
- [Templates](Typst-Templates) — a reusable layout with typed parameters.
