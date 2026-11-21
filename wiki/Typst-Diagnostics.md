# Diagnostics

Every compilation returns diagnostics. Warnings and hints do not fail it; errors throw.

```csharp
using var result = compiler.Compile(world);

foreach (var diagnostic in result.Diagnostics)
{
    Console.WriteLine(diagnostic.ToDisplayString());
}
```

## The shape

```csharp
public sealed record TypstDiagnostic(
    TypstSeverity Severity,          // Error, Warning, Hint
    string Message,                  // the compiler's message, verbatim
    IReadOnlyList<string> Hints,     // suggestions
    IReadOnlyList<string> Trace,     // call stack, innermost first
    TypstLocation? Location,         // file, line, column, endLine, endColumn
    string? SourceLine);             // the text of the line the span starts on
```

`Location` is `null` when the diagnostic has no span — an export failure, for instance. It is never a
fabricated `0:0`.

## Rendering

`ToString()` gives one line:

```
main.typ:12:8: error: unknown variable: `totl`
```

`ToDisplayString()` adds the source line and a caret under the span:

```
main.typ:12:8: error: unknown variable: `totl`
   |
12 | #let x = totl
   |        ^^^^
  hint: did you mean `total`?
```

The snippet comes from `SourceLine`, which the native layer fills from the source it already holds —
you do not need to keep the source around to render it. Pass `includeSnippet: false` for a
one-line-per-diagnostic log format.

M3scalinum.Text.Typst never prints anything itself: no logging dependency, no console output.

## Failing a build on a warning

```csharp
using var result = compiler.Compile(world, new TypstCompilationOptions
{
    WarningsAsErrors = true,
});
```

A document that compiles with warnings throws `TypstCompilationException` carrying those warnings, and
the result is disposed. Hints are not warnings and do not trigger it.

## Exceptions

| Exception | When |
| --- | --- |
| `TypstCompilationException` | The document has errors, or `WarningsAsErrors` saw a warning. Carries `Diagnostics`. |
| `TypstExportException` | The export failed: an unsupported standard, a page range with `Tagged`, PDF/A without a date, PDF/UA without a title. |
| `TypstTemplateException` | A template manifest, a parameter or the `render` function is wrong. |
| `TypstNativeException` | The native layer failed in a way the managed side cannot classify. |

All of them derive from `TypstException`, which carries a `TypstErrorCode`:

`Compilation`, `File`, `Export`, `Template`, `Panic`, `AbiMismatch`, `InvalidArgument`,
`LimitExceeded`, `Unsupported`, `Cancelled`.

## Limits

`WithLimits` rejects an input before compilation starts, with `TypstErrorCode.LimitExceeded`:

| Limit | Default |
| --- | --- |
| `MaxSourceBytes` | 16 MiB |
| `MaxAssetBytes` | 64 MiB |
| `MaxFontBytes` | 64 MiB |
| `MaxSources` | 1024 |
| `MaxFonts` | 256 |

```csharp
using var world = new TypstWorldBuilder()
    .WithLimits(limits => limits.MaxSourceBytes = 1024 * 1024)
    .AddSource("main.typ", markup)
    .Build();
```
