# Performance

## The cost model

| Cost | Paid when | Mitigation |
| --- | --- | --- |
| Font parsing | `AddFont` | Build the world once; `Clone()` shares the font book. |
| Standard library construction | first compile in the process | Built once, shared through `Arc`. |
| Package fetch | first use of a version | Cached on disk, shared with the `typst` CLI. |
| Layout | `Compile` | Unavoidable; scales with content. |
| Rasterization | PNG export only | Choose the PPI deliberately: 144 for screen, 300 for print. |

## Clone instead of rebuild

A world is immutable, so the server pattern is one template world and one clone per request:

```csharp
using var world = _template.WithSource("main.typ", markup);
using var result = _compiler.Compile(world);
```

`Clone()` shares the library, the font book and the fonts through `Arc` and copies only the sources
and the assets. It never re-scans fonts and never re-reads packages, so it costs a few map copies
rather than a font scan.

Measured on the reference machine: 50 clones are cheaper than 50 builds, and 10 000 clones keep
managed memory flat.

## Benchmarks

The repository ships a dependency-free harness with seven scenarios, 50 iterations each, reporting
the **minimum** — the most stable estimator for CPU-bound work — and a 1.5× regression gate.

```bash
dotnet run -c Release --project benchmarks/M3scalinum.Text.SharpTypst.Benchmarks -- --check benchmarks/baseline.json
```

| Scenario | Baseline (linux-x64, WSL) |
| --- | --- |
| `compile-small` | 2.97 ms |
| `compile-report` | 3.28 ms |
| `export-pdf` | 3.90 ms |
| `export-png` | 9.27 ms |
| `export-html` | 2.51 ms |
| `export-markdown` | 2.68 ms |
| `document-model` (100 builds) | 1.91 ms |

A shared runner varies by tens of percent between runs, so a tighter gate would fail on noise; 1.5×
still catches a real regression such as a lost cache or an extra copy.

## Memory

Every native allocation is owned by a handle the managed side disposes. The test suite runs 10 000
compile-and-export cycles, 10 000 worlds, 1 000 failed compilations, 10 000 clones and 1 000
specialised clones, and asserts that managed memory stays flat.

An export holds its encoded file in native memory; `Export(ExportFormat, Stream)` copies it out in
64 KiB chunks instead of materialising a second `byte[]`, which halves the peak. See
[Compiling](Compiling#writing-straight-to-a-stream).

## Telemetry

`TypstInstrumentation` publishes one `ActivitySource` and one `Meter`, both named `M3scalinum.Text.SharpTypst`:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(TypstInstrumentation.SourceName))
    .WithMetrics(metrics => metrics.AddMeter(TypstInstrumentation.SourceName));
```

| Signal | Kind | Tags |
| --- | --- | --- |
| `Typst.Compile` | span | `typst.root`, `typst.document.kind`, `typst.pages`, `typst.diagnostics` |
| `Typst.Export` | span | `typst.format`, `typst.bytes` |
| `typst.compilation.duration` | histogram (ms) | — |
| `typst.document.pages` | histogram (page) | — |
| `typst.compilations` | counter | `outcome` |
| `typst.export.duration` | histogram (ms) | `typst.format`, `outcome` |
| `typst.exports` | counter | `typst.format`, `outcome` |

Nothing is recorded unless a listener is attached, so a host that ignores telemetry pays a null
check per call and nothing else.

## Concurrency

A world is safe to compile from several threads at once: the sources are copied per clone, the fonts
are shared through `Arc`. `TypstCompiler` is stateless, so one instance serves the whole process.

`CompileAsync` runs the work on the thread pool. Typst 0.15 has no interrupt hook, so a cancellation
is observed before the compile starts and after it finishes — a compile already running is not
interrupted.

## AOT

The library is trimming- and AOT-friendly: no reflection over the document model, no dynamic code
generation, source-generated JSON. A `PublishAot` sample produces a ~3.5 MB self-contained binary.
