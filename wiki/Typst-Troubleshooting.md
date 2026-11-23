# Troubleshooting

Most surprises come from Typst itself, not from M3scalinum.Text.Typst. These are the ones that bite.

## `heading is not allowed at the top-level in bundle export`

The bundle target has no implicit document. Wrap the content:

```typst
#document("index.html")[
  = Bundled

  A paragraph.
]
```

`#asset` and `#document` are only valid in the bundle target, so a document that uses them cannot be
compiled as `Paged` or `Html`.

## `can only be used when context is known`

`#counter(page).display()` needs `context` inside a header or footer:

```typst
#set page(footer: context [Page #counter(page).display()])
```

`TypstContent.PageNumber()` and `PageCount()` already emit the `context` for you.

## `cannot reference heading without numbering`

A `@label` on a heading requires the heading to be numbered:

```csharp
document.Set("heading(numbering: \"1.\")");
```

## `HTML export requires an HTML document`

PDF, SVG and PNG come from a paged compilation; HTML, Markdown and text come from an HTML one. Use
`CompileBoth` when you need both, or compile twice with different `DocumentKind`.

## `missing argument: body`

`#text(...)` needs a body. The model wraps it for you; a hand-written `Raw` call does not.

## `unknown font family`

The family is not registered. Check `world.FontFamilies` — it lists exactly what the world can
resolve. Remember that `AddSystemFonts()` is opt-in.

## `unknown variable: steelblue`

`TypstColor.Named` accepts only the 18 Typst colour names: `black`, `gray`, `silver`, `white`,
`navy`, `blue`, `aqua`, `teal`, `eastern`, `purple`, `fuchsia`, `maroon`, `red`, `orange`, `yellow`,
`olive`, `green`, `lime`. Use `TypstColor.Rgb` or `TypstColor.Hex` for anything else.

## `DllNotFoundException`

The native library was not found. In a normal package install it ships inside `runtimes/`. When
running from a source build, point the loader at it:

```bash
export SHARPTYPST_NATIVE_PATH=$HOME/.cache/sharp-typst-target/release/libsharp_typst.so
```

On Alpine or another musl image the package carries the musl build under
`runtimes/linux-musl-x64/`: `NativeLibraryResolver` detects the libc and probes that directory
first, then falls back to the portable RID. A Docker build that copies only `runtimes/linux-x64/`
produces a glibc binary that fails to load on musl.

macOS ships two thin dylibs rather than one universal file — `runtimes/osx-arm64/native/` and
`runtimes/osx-x64/native/`, each built on its own runner. The resolver probes the RID of the running
process, so an Apple-silicon host resolves `osx-arm64` first and an Intel host `osx-x64`; there is no
`lipo` step and no fat header to inspect. A copy of the build output that carries only one of the two
directories fails on the other architecture.

## The output is not byte-identical between runs

Check, in order: the clock (`WithFixedClock` is the default — did something call `WithSystemClock`?),
the fonts (system fonts differ between machines), the package versions (`@preview/x:1.2.3`, not
`latest`), and the Typst version.

## A compile hangs

Typst 0.15 has no interrupt hook, so a pathological document cannot be cancelled mid-layout. Bound
the input with `WithLimits`, and run untrusted documents in a process you can kill.
