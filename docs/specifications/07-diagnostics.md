# 07 — Diagnostics and errors

## 1. Model

```csharp
namespace M3scalinum.Text.Typst.Diagnostics;

public enum TypstSeverity { Error, Warning, Hint }

public sealed record TypstLocation(
    string File,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    bool IsInSource);

public sealed record TypstDiagnostic(
    TypstSeverity Severity,
    string Message,
    IReadOnlyList<string> Hints,
    IReadOnlyList<string> Trace,
    TypstLocation? Location);
```

- `Line` and `Column` are 1-based. `Column` counts Unicode scalar values, not bytes and not UTF-16
  code units, so it matches what an editor shows for a UTF-8 file.
- `File` is the virtual path (`main.typ`, `partials/header.typ`), never a host path.
- `Location` is `null` when the diagnostic has no span (for example a package resolution failure).
- `IsInSource` is `false` when the span points into a file that is not a registered source — a Typst
  package, or the standard library.
- `Trace` holds the call stack Typst reports for errors raised inside a function, innermost first,
  formatted as `file:line:column in <function>`.

## 2. Where diagnostics come from

| Source | Severity | Delivered as |
| --- | --- | --- |
| `typst::compile` warnings | Warning | `CompilationResult.Diagnostics` |
| `typst::compile` hints | Hint | `CompilationResult.Diagnostics` |
| `typst::compile` errors | Error | `TypstCompilationException.Diagnostics` |
| File loading failures (`FileError`) | Error | `TypstCompilationException` with `TypstErrorCode.File` |
| Export failures | Error | `TypstExportException` |
| ABI misuse, panics | Error | `TypstNativeException` |
| Template validation | Error | `TypstTemplateException` |

A successful compilation always returns a result, even when it produced warnings. Warnings never
throw. This is deliberate: a document with a missing font fallback should still render.

## 3. Exception taxonomy

```
TypstException                     (abstract, carries TypstErrorCode)
├── TypstCompilationException      errors from the compiler; has Diagnostics
├── TypstExportException           export failed after a successful layout
├── TypstTemplateException         manifest/parameter problems; has TemplateName, ParameterName
└── TypstNativeException           ABI-level failure: panic, version mismatch, missing library
```

```csharp
public abstract class TypstException : Exception
{
    public TypstErrorCode Code { get; }
    public IReadOnlyList<TypstDiagnostic> Diagnostics { get; }
}

public enum TypstErrorCode
{
    Compilation, File, Export, Template, Panic, AbiMismatch,
    InvalidArgument, LimitExceeded, Unsupported, Cancelled
}
```

Rules:

1. `Message` is a one-line summary suitable for a log: `"Compilation failed with 2 errors (first:
   unknown variable: 'totl' at main.typ:12:8)"`.
2. `Diagnostics` is never empty for `TypstCompilationException`.
3. `InnerException` is set when a lower-level failure caused the error (for example a
   `DllNotFoundException` inside `TypstNativeException`).
4. Exceptions are serializable in the sense that matters: `Code`, `Diagnostics` and `Message`
   survive a round trip through a structured logger. No custom `ISerializable` implementation.

## 4. Position mapping

Typst reports spans as byte offsets into a `Source`. The Rust layer converts them:

```rust
let line   = source.byte_to_line(span.start());      // 0-based
let column = source.byte_to_column(span.start());    // 0-based, scalar values
```

then adds 1 to both. The end position uses `span.end()`. When the span crosses files (an import
error), the span's file is used, not the root's.

A span that cannot be resolved (detached span, `FileId::detached()`) yields `Location = null`
rather than a fabricated `0:0`.

## 5. Rendering diagnostics for humans

`TypstDiagnostic.ToString()` produces a single line:

```
main.typ:12:8: error: unknown variable: `totl`
  hint: did you mean `total`?
```

A multi-line renderer is provided for console output. It appends the source line and a caret under
the span, so the message is actionable without opening the file:

```csharp
foreach (var d in result.Diagnostics)
    Console.WriteLine(d.ToDisplayString(includeHints: true, includeTrace: false));
```

```
main.typ:12:8: error: unknown variable: `totl`
   |
12 | #let x = totl
   |        ^^^^
  hint: did you mean `total`?
```

The snippet comes from `TypstDiagnostic.SourceLine`, which the native layer fills from the source it
already holds. Pass `includeSnippet: false` for a one-line-per-diagnostic log format.

M3scalinum.Text.Typst does not print anything by itself. There is no logging dependency and no console output
from the library, ever.

## 6. Limits

`WithLimits` produces `TypstErrorCode.LimitExceeded` before compilation starts:

| Limit | Default | Checked at |
| --- | --- | --- |
| `MaxSourceBytes` | 16 MiB | `AddSource` |
| `MaxAssetBytes` | 64 MiB | `AddAsset` |
| `MaxFontBytes` | 64 MiB | `AddFont` |
| `MaxSources` | 1 024 | `AddSource` |
| `MaxFonts` | 256 | `AddFont` |

The limits exist to bound memory in a server that accepts user-supplied templates. They are not a
sandbox: Typst code can still be expensive to lay out. A host that accepts untrusted markup must
also bound wall-clock time (see [03 §9](03-compilation.md#9-cancellation-and-timeouts)).

## 7. Testing

| Test | Asserts |
| --- | --- |
| Error with span | `Location` matches the expected line/column in a fixture |
| Error in an imported file | `File` is the imported path, not the root |
| Error in a package | `IsInSource == false`, `Location` non-null |
| Warning-only document | Compilation succeeds, `Diagnostics` has the warning, no exception |
| Hint | Severity is `Hint`, not `Warning` |
| Panic | `TypstNativeException` with `Code == Panic`, process alive, handle disposed |
| Limit | `LimitExceeded` thrown at registration, not at compile |
| Message format | `Message` matches the documented one-line shape |

## 8. Instrumentation

Compilation diagnostics tell a caller what went wrong in one document. Instrumentation tells an
operator what the library is doing in production. Both are published from
`M3scalinum.Text.Typst.Diagnostics.TypstInstrumentation`, which owns one `ActivitySource` and one `Meter`, both
named `M3scalinum.Text.Typst`:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(TypstInstrumentation.SourceName))
    .WithMetrics(metrics => metrics.AddMeter(TypstInstrumentation.SourceName));
```

| Signal | Kind | Tags / unit | Emitted by |
| --- | --- | --- | --- |
| `Typst.Compile` | span | `typst.root`, `typst.document.kind`, `typst.pages`, `typst.diagnostics` | `TypstCompiler.Compile` |
| `Typst.Export` | span | `typst.format`, `typst.bytes` | `CompilationResult.Export` |
| `typst.compilation.duration` | histogram, ms | — | every compile |
| `typst.document.pages` | histogram, page | — | a successful compile |
| `typst.compilations` | counter | `outcome` = `success` \| `error` | every compile |
| `typst.export.duration` | histogram, ms | `typst.format`, `outcome` | every export |
| `typst.exports` | counter | `typst.format`, `outcome` | every export |

Nothing is collected unless a listener is attached: `StartActivity` returns `null` and the
measurement helpers become no-ops, so the cost in a host that ignores telemetry is one null check
per call. A failed compile or export sets `ActivityStatusCode.Error` with the exception message as
the status description. Instrumentation never throws into the caller.
