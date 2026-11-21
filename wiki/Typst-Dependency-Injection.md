# Dependency Injection

The `M3scalinum.Text.Typst.DependencyInjection` package registers a compiler, a world factory and validated
options.

```bash
dotnet add package M3scalinum.Text.Typst.DependencyInjection
```

```csharp
using M3scalinum.Text.Typst.DependencyInjection;

builder.Services.AddTypst(options =>
{
    options.Clock.UseSystemClock = false;
    options.Clock.FixedInstant = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    options.Limits.MaxSourceBytes = 4 * 1024 * 1024;
    options.Packages.AllowDownload = false;
});
```

## What is registered

| Service | Lifetime | Why |
| --- | --- | --- |
| `TypstCompiler` | singleton | Stateless apart from configuration. |
| `TypstWorldBuilder` | transient | A draft, mutated then frozen by `Build()`. |
| `ITypstWorldFactory` | singleton | Builds worlds from the options. |

`ITypstWorldFactory` exists so a singleton can create a world without capturing a transient
`TypstWorldBuilder` — the classic captive dependency.

```csharp
public sealed class ReportService(ITypstWorldFactory factory, TypstCompiler compiler)
{
    public byte[] Render(string markup)
    {
        using var world = factory.Create(builder => builder.AddSource("main.typ", markup));
        using var result = compiler.Compile(world);
        return result.ExportPdf();
    }
}
```

## Options

```csharp
public sealed class TypstOptions
{
    public TypstLimits Limits { get; }
    public TypstPackages Packages { get; }
    public TypstClockOptions Clock { get; }
    public TypstCompilationOptions Compilation { get; set; }
    public SvgExportOptions Svg { get; set; }
    public PngExportOptions Png { get; set; }
    public HtmlExportOptions Html { get; set; }
}
```

`TypstClockOptions`:

| Member | Meaning |
| --- | --- |
| `UseSystemClock` | `false` by default. `true` makes the output non-reproducible. |
| `FixedInstant` | The instant used when the system clock is off. Defaults to 2024-01-01T00:00:00Z. |

## Validation

`TypstOptionsValidator` implements `IValidateOptions<TypstOptions>` and rejects a configuration that
would fail on first use — a negative limit, a cache directory that cannot be created, a clock with
neither a fixed instant nor the system clock. The failure surfaces at startup, not on the first
document.
