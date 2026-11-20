# Packages and Fonts

## Packages

A Typst package is `@namespace/name:version`, for example `@preview/cetz:0.3.1`. M3scalinum.Text.SharpTypst resolves
them through the same cache layout the `typst` CLI uses, so a machine that already has the packages
does not download them again.

```csharp
using var world = new TypstWorldBuilder()
    .WithPackages(packages =>
    {
        packages.CacheDirectory = Path.Combine(home, ".cache", "typst", "packages");
        packages.DataDirectory = Path.Combine(home, ".local", "share", "typst", "packages");
        packages.AllowDownload = true;
        packages.Registry = "https://packages.typst.org";
    })
    .AddSource("main.typ", """
        #import "@preview/cetz:0.3.1": canvas
        """)
    .Build();
```

| Setting | Meaning |
| --- | --- |
| `CacheDirectory` | Where downloaded archives are unpacked. |
| `DataDirectory` | Where packages are read from. |
| `AllowDownload` | Whether a missing package may be fetched. **Off by default.** |
| `Registry` | The registry base URL. |

**Offline by default.** A compile that silently reaches the network is not reproducible, so
`AllowDownload` is opt-in. With it off, a missing package fails with a clear message instead of a
timeout.

The cache layout is `<cache>/<namespace>/<name>/<version>/`, identical to the CLI, so the two share
their downloads.

## Fonts

Three sources, all explicit:

```csharp
using var world = new TypstWorldBuilder()
    .AddFont(File.ReadAllBytes("Inter-Regular.ttf"))   // one font, from memory
    .AddFontDirectory("assets/fonts")                  // every .ttf/.otf/.ttc in a directory
    .AddSystemFonts()                                  // the OS font directories
    .AddSource("main.typ", "#set text(font: \"Inter\")")
    .Build();
```

`AddSystemFonts()` is **opt-in on purpose**: a silent scan of `/usr/share/fonts` makes the output
depend on the machine, which breaks the determinism guarantee. Register explicit font bytes when you
need reproducible output.

`AddFont` deduplicates by content hash, so registering the same file twice costs nothing.

### Which families are available?

```csharp
foreach (var family in world.FontFamilies)
{
    Console.WriteLine(family);
}
```

`FontFamilies` lists what the world can actually resolve, sorted — the embedded fonts plus whatever
the builder added. It is the answer to an `unknown font family` warning.

### Embedded fonts

The package ships the fonts Typst itself embeds, so a document that uses no explicit font compiles
without registering anything.
