# 12 — Dependency injection

M3scalinum.Text.SharpTypst ships a second package, `M3scalinum.Text.SharpTypst.DependencyInjection`, that registers the library in a
`Microsoft.Extensions.DependencyInjection` container. This document is the normative description of
that package: what it registers, with which lifetime, how it is configured, and what it deliberately
does not do.

## 1. Why a separate package

`M3scalinum.Text.SharpTypst` has no dependency outside the base class library. That is a property worth keeping: a
console tool that renders one PDF should not pull in the options and hosting abstractions.

The integration therefore lives in its own package, which references `M3scalinum.Text.SharpTypst` and nothing else
beyond what the .NET shared framework already provides. A host that does not use DI never sees it.

**Decision — no `Microsoft.Extensions.*` reference in the core.** Rejected: putting `AddSharpTypst`
in the main package behind a `#if`, which would either force the dependency on everyone or produce
two differently-shaped assemblies from one source.

## 2. Registration surface

```csharp
public static class SharpTypstServiceCollectionExtensions
{
    public static IServiceCollection AddSharpTypst(this IServiceCollection services);
    public static IServiceCollection AddSharpTypst(
        this IServiceCollection services,
        Action<TypstOptions> configure);
}
```

| Service | Lifetime | Why |
| --- | --- | --- |
| `TypstCompiler` | Singleton | Holds only the native binding; no per-call state |
| `TypstWorldBuilder` | Transient | Mutable accumulator; one per world |
| `ITypstWorldFactory` | Singleton | Creates configured builders on demand |
| `IValidateOptions<TypstOptions>` | Singleton (enumerable) | Rejects a configuration that would fail on first use |

Every registration uses `TryAdd`, so `AddSharpTypst` is idempotent and a host can replace any of the
three services before or after the call.

**Decision — the builder is transient, not scoped.** A world is not a unit of work with a lifetime
tied to a request; it is a value that is built, compiled and disposed. Scoping it would invite
holding a world across requests, which pins native memory for no benefit.

**Decision — `ITypstWorldFactory` exists.** A transient service captured by a singleton is a captive
dependency. A host that renders N documents in a loop or in parallel injects the factory and calls
`Create()` per iteration. The factory is a singleton because it holds only the options snapshot.

## 3. `TypstOptions`

```csharp
public sealed class TypstOptions
{
    public TypstLimits Limits { get; }              // hard caps on registered inputs
    public TypstPackages Packages { get; }          // package resolution, offline by default
    public TypstClockOptions Clock { get; }         // fixed instant or system clock
    public TypstCompilationOptions Compilation { get; set; }  // defaults for Compile
    public SvgExportOptions Svg { get; set; }       // default SVG export options
    public PngExportOptions Png { get; set; }       // default PNG export options
    public HtmlExportOptions Html { get; set; }     // default HTML export options
}
```

`Limits`, `Packages` and `Clock` are applied to every `TypstWorldBuilder` the container hands out.
`Compilation` is the default passed to `TypstCompiler.Compile` when the caller passes none.

The export options are **defaults a host reads**, not settings the library applies implicitly: an
export is always an explicit call with explicit options, and silently substituting a container-wide
default would make a call site's behaviour depend on configuration it does not mention.

```csharp
public sealed class TypstClockOptions
{
    public bool UseSystemClock { get; set; }        // default false
    public DateTimeOffset FixedInstant { get; set; } // default 2024-01-01T00:00:00Z
}
```

## 4. Validation

`TypstOptionsValidator` runs when the options are first materialised and rejects:

- a limit that is not strictly positive;
- a PNG `Ppi` that is not strictly positive;
- a package setting that `TypstPackages.Validate` rejects (blank directory, non-http(s) registry).

`ValidateOnStart` is registered as well, so a host that runs `IStartupValidator` fails at startup
rather than on the first render.

## 5. Usage

```csharp
services.AddSharpTypst(options =>
{
    options.Limits.MaxSourceBytes = 8 * 1024 * 1024;
    options.Packages.CacheDirectory = "/var/cache/sharp-typst";
    options.Packages.AllowDownload = true;
    options.Clock.UseSystemClock = true;
    options.Compilation = new TypstCompilationOptions { Root = "report.typ" };
});
```

```csharp
public sealed class ReportService(
    TypstWorldBuilder builder,
    TypstCompiler compiler,
    IOptions<TypstOptions> options)
{
    public byte[] Render(string markup)
    {
        using var world = builder.AddSource("report.typ", markup).Build();
        using var result = compiler.Compile(world, options.Value.Compilation);

        return result.ExportPdf();
    }
}
```

Binding from configuration is the host's job, not the library's: `TypstOptions` is a plain options
class, so `services.Configure<TypstOptions>(configuration.GetSection("M3scalinum.Text.SharpTypst"))` works without
any extra support from this package.

## 6. What is not registered

| Not registered | Why |
| --- | --- |
| `TypstWorld` | Built per compilation, disposed by the caller; it is not a service |
| `CompilationResult` | Same: a value with a native handle and a lifetime |
| `ITypstNative` | Internal to `M3scalinum.Text.SharpTypst.Native`; the compiler owns it |
| Named option sets | One configuration per container is the common case; a host that needs two uses two containers or two `TypstWorldBuilder` instances |

## 7. Testing

The integration is covered by `tests/Text.SharpTypst.Integration.Tests/DependencyInjectionTests.cs`:
lifetimes, option propagation, defaults, validation failures, idempotence, replacement, and one
end-to-end compile through the container. The tests live in the integration project because
resolving a `TypstWorldBuilder` loads the native library.
