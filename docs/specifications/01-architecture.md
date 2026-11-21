# 01 — Architecture

## 1. Layers

```
┌──────────────────────────────────────────────────────────────────────┐
│ Consumer: ASP.NET Core, worker service, desktop app, tests           │
└──────────────────────────────────────────────────────────────────────┘
                                 │  public managed API
┌──────────────────────────────────────────────────────────────────────┐
│ M3scalinum.Text.Documents.dll                   (net8.0+, managed, public)      │
│   M3scalinum.Text.Documents  M3scalinum.Text.Documents.Html  .Documents.Markdown│
│   the document model: content, styles, tables, figures, page setup,  │
│   importers — no compiler, no native code                            │
└──────────────────────────────────────────────────────────────────────┘
                                 │  public managed API
┌──────────────────────────────────────────────────────────────────────┐
│ M3scalinum.Text.Typst.dll                       (net8.0+, managed, public)      │
│   M3scalinum.Text.Typst.Compilation  M3scalinum.Text.Typst.Templates           │
│   M3scalinum.Text.Typst.Export       M3scalinum.Text.Typst.Diagnostics         │
└──────────────────────────────────────────────────────────────────────┘
                                 │  internal interface ITypstNative
┌──────────────────────────────────────────────────────────────────────┐
│ M3scalinum.Text.Typst.Native.dll                (net8.0+, internal, unsafe)     │
│   LibraryImport declarations, marshalling, handle lifetime,          │
│   buffer ownership, JSON payload (de)serialization                   │
└──────────────────────────────────────────────────────────────────────┘
                                 │  C ABI (extern "C", no unwinding)
┌──────────────────────────────────────────────────────────────────────┐
│ libsharp_typst  (Rust cdylib)                                        │
│   ffi/  world.rs  compile.rs  export/{pdf,svg,png,html,md,text}.rs   │
└──────────────────────────────────────────────────────────────────────┘
                                 │  Rust crates
┌──────────────────────────────────────────────────────────────────────┐
│ typst 0.15.x · typst-pdf · typst-svg · typst-render · typst-html     │
│ typst-kit (fonts, packages, files) · typst-syntax · typst-eval       │
└──────────────────────────────────────────────────────────────────────┘
```

Each layer may only call the layer below it. The managed public API never sees a pointer, a
`SafeHandle` or a `byte*`; the native layer never sees a .NET type.

## 2. Projects

| Project | Kind | Responsibility | May reference |
| --- | --- | --- | --- |
| `native/sharp-typst` | Rust `cdylib` + `staticlib` | Own the Typst world, run compilation, export bytes, translate panics into error codes | Typst crates only |
| `src/Text.Typst.Native` | C# library, `internal` | P/Invoke surface, marshalling, handle ownership, payload DTOs | BCL only |
| `src/Text.Documents` | C# library, public | The document model: typed content, inline styles, tables, figures, lists, page setup, and the HTML and Markdown importers | BCL only |
| `src/Text.Typst` | C# library, public | Public API: compiler, world builder, templates, diagnostics | `M3scalinum.Text.Typst.Native`, `M3scalinum.Text.Documents` |
| `tests/Text.Typst.Tests` | xUnit v3 | Unit tests that do not need the native library (writer, escaping, template binding, diagnostic mapping) | `M3scalinum.Text.Typst` |
| `tests/Text.Typst.Integration.Tests` | xUnit v3 | Tests that compile real documents through the native library | `M3scalinum.Text.Typst` |
| `samples/Text.Typst.Samples` | console app | Executable documentation: invoice, report, template, HTML+Markdown | `M3scalinum.Text.Typst` |

**Decision — two managed assemblies, not one.** The P/Invoke layer is `internal` and lives in its
own assembly so that `M3scalinum.Text.Typst` can be reasoned about (and unit-tested) without loading native
code, and so that the marshalling surface can be replaced (for example by a WASM host) without
touching the public API. Rejected: a single assembly with `internal` P/Invoke (untestable without
natives) and a public native package (leaks `IntPtr` into user code).

**Decision — the document model does not depend on the compiler.** It is a package of its own,
`M3scalinum.Text.Documents`, and it produces a `string`. It can be restored, unit-tested, diffed and
snapshotted with no native library and no compiler present — which is what lets a format package such
as `M3scalinum.Text.Docx` build a document without pulling in the compiler.

## 3. Dependency rules

1. `M3scalinum.Text.Typst` MUST NOT reference `System.Drawing`, `SkiaSharp`, `PdfSharp`, `iText` or any other
   rendering/PDF library. Rendering is Typst's job.
2. `M3scalinum.Text.Typst.Native` MUST NOT expose public types. Everything is `internal`; `InternalsVisibleTo`
   grants access to `M3scalinum.Text.Typst` and to the test assemblies.
3. No project may reference `M3scalinum.Text.Typst` from `M3scalinum.Text.Typst.Native` (no cycles).
   `M3scalinum.Text.Documents` MUST NOT reference `M3scalinum.Text.Typst` nor
   `M3scalinum.Text.Typst.Native`: the model is what the compiler consumes, never the reverse.
4. The Rust crate MUST NOT depend on anything outside the Typst crate family plus `serde`,
   `serde_json`, `thiserror` and `libc`/`windows-sys` where unavoidable.
5. Public API types MUST be in the `Typst` namespace tree and MUST NOT be generic over native
   handles.

## 4. Public API surface

```
Typst
├── Compilation
│   ├── TypstCompiler            (IDisposable)  Compile / CompileAsync
│   ├── TypstWorld               (IDisposable)  sources, fonts, assets, packages, clock
│   ├── TypstWorldBuilder
│   ├── CompilationResult        Document, Diagnostics, Duration
│   └── TypstCompilationOptions  Root, Features, PackageMode
├── Export
│   ├── ExportFormat             Pdf | Svg | Png | Html | Markdown | Text
│   ├── PdfExportOptions
│   ├── SvgExportOptions         Page selection, merged
│   ├── PngExportOptions         Ppi, page selection, merged
│   └── HtmlExportOptions
├── Documents
│   ├── TypstDocument            fluent root
│   ├── TypstNode                abstract node
│   └── nodes: Heading, Paragraph, BulletList, NumberedList, Table, Figure,
│              Image, CodeBlock, Math, Quote, Raw, Link, Reference, PageBreak,
│              Columns, Box, Align, Text
├── Templates
│   ├── TypstTemplate            non-generic, dictionary parameters
│   ├── TypstTemplate<TOptions>  typed parameters
│   └── TypstTemplateManifest
└── Diagnostics
    ├── TypstDiagnostic          Severity, Message, Hints, Trace, Location
    ├── TypstSeverity            Error | Warning | Hint
    └── TypstCompilationException
```

The exact member shapes are specified in [03](03-compilation.md), [04](04-document-model.md),
[05](05-templates.md) and [07](07-diagnostics.md).

## 5. Compilation flow

```
TypstWorldBuilder
   .AddSource("main.typ", text)
   .AddFont(bytes)
   .AddAsset("logo.png", bytes)
   .WithClock(fixedDate)
        │
        ▼
   TypstWorld  ──(native world handle)──►  Rust World impl
        │                                     sources: HashMap<FileId, Source>
        │                                     files:   HashMap<FileId, Bytes>
        │                                     fonts:   FontBook (immutable)
        │                                     library: LazyHash<Library>
        ▼
TypstCompiler.Compile(world, options)
        │  1. tn_world_compile(handle, "paged" | "html")
        │  2. on error → tn_world_diagnostics → TypstCompilationException
        │  3. on success → CompilationResult(document handle)
        ▼
result.Document.Export(ExportFormat.Pdf, options)  →  byte[]
```

A `CompilationResult` owns a native document handle. It is `IDisposable`; disposing it releases
the laid-out document. Exporting twice from the same result is supported and cheap for PDF/SVG
(the document is already laid out); PNG re-rasterizes per page.

## 6. Lifetime and ownership

| Object | Owns | Released by |
| --- | --- | --- |
| `TypstWorld` | one native world handle (sources, fonts, assets) | `Dispose()` / finalizer as a safety net |
| `TypstCompiler` | no native state; holds configuration and an engine pool | `Dispose()` (drains the pool) |
| `CompilationResult` | one native document handle | `Dispose()` |
| `byte[]` returned by export | nothing native | GC |

Rules:

- Every native handle is wrapped in a `SafeHandle` subclass (`TypstWorldHandle`,
  `TypstDocumentHandle`) so that a leaked object cannot leak native memory past finalization.
- A world handle MUST NOT be used after `Dispose()`. Doing so throws `ObjectDisposedException`,
  never crashes.
- A document handle keeps its world alive: the native side holds an `Arc` on the world's data, so
  disposing the world before the document is safe.

## 7. Threading

- **Handles are not thread-safe.** A `TypstWorld` and a `CompilationResult` are single-threaded
  objects. Concurrent use is a programming error and is detected (an atomic "in use" flag) and
  reported as `InvalidOperationException`, not as undefined behaviour.
- **Compilation is CPU-bound and synchronous.** `Compile` blocks the calling thread. `CompileAsync`
  offloads to the thread pool; it is a convenience, not a different code path.
- **Concurrency comes from multiple worlds.** Two compilations never share a world. The font book
  and the standard library are immutable and shared at the Rust level (`Arc`), so N concurrent
  compilations cost N worlds, not N font scans.
- **No global lock.** The process-wide state is the loaded native library, the shared standard
  library and font set, and two atomic counters of live handles; all of it is read-only or atomic
  after initialization.
- **Cancellation.** Typst 0.15 has no cooperative interrupt inside `typst::compile`. A cancelled
  compilation therefore abandons the worker thread and marks the world handle poisoned; the
  handle is freed when the thread returns. `CompileAsync` honours `CancellationToken` at that
  granularity and documents it. See [03 §9](03-compilation.md#9-cancellation-and-timeouts).

## 8. Error propagation

```
Rust panic / typst error
   → catch_unwind at the FFI boundary
   → tn_status code + JSON error payload on the handle
   → M3scalinum.Text.Typst.Native maps to NativeResult
   → M3scalinum.Text.Typst throws TypstCompilationException / TypstNativeException
```

No `abort`, no `longjmp`, no exception crossing the ABI. Details in
[02 §6](02-native-abi.md#6-error-payload) and [07](07-diagnostics.md).

## 9. Configuration

Configuration is constructor/builder state, not ambient:

- `TypstWorldBuilder` — sources, fonts, assets, packages, clock.
- `TypstCompilationOptions` — root path, enabled Typst features, package resolution mode.
- Per-export options objects — page selection, PPI, merged output.

There is no `appsettings.json` binding, no static `Typst.Configure(...)`, no environment variable
that changes behaviour. A host that wants configuration reads it itself and passes it in.

## 10. What is deliberately absent

| Absent | Why |
| --- | --- |
| Async streaming of pages | Typst lays out the whole document before any page is final |
| A `TypstDocument.Parse` | One-way generation; parsing Typst is a different product |
| A plugin/provider model for exporters | Six formats, all in the native layer; an abstraction would have one implementation |
| A DI extension package | `services.AddSingleton<TypstCompiler>()` is one line the consumer writes |
| A logging abstraction | Diagnostics are returned as data; the consumer logs them |
