# 02 — Native ABI

The contract between the Rust `cdylib` and the managed P/Invoke layer. This document is
normative: a symbol spelled here MUST exist with that spelling, and a struct layout here MUST
match its Rust `#[repr(C)]` counterpart.

## 1. General rules

| Rule | Detail |
| --- | --- |
| Language linkage | Every exported function is `extern "C"` and `#[unsafe(no_mangle)]`. Prefix `tn_`. |
| Types across the boundary | `int32_t`, `uint32_t`, `size_t`, `const char*` (UTF-8, NUL-terminated), `uint8_t*`, and opaque pointers to Rust structs. Nothing else. |
| Strings | UTF-8, NUL-terminated, no interior NUL. Length-passed buffers are used for binary payloads. |
| Handles | Opaque, single-owner, not thread-safe. See §4. |
| Memory ownership | Whoever allocates, frees. Every buffer returned by Rust is freed with `tn_buffer_free`. Every `TnError` is freed with `tn_error_free`. |
| Panics | MUST NOT unwind across the boundary. Every entry point wraps its body in `std::panic::catch_unwind`. The release profile MUST keep `panic = "unwind"` so that this works. |
| Blocking | All calls are synchronous. Compilation may take hundreds of milliseconds; the caller owns the thread. |
| Reentrancy | No callbacks. Rust never calls back into .NET. Progress and diagnostics are pulled, not pushed. |

## 2. Header sketch

```c
#include <stdint.h>
#include <stddef.h>

typedef struct TnWorld    TnWorld;
typedef struct TnDocument TnDocument;
typedef struct TnError    TnError;

typedef struct TnBuffer {
    uint8_t *ptr;   /* owned by Rust, freed by tn_buffer_free */
    size_t   len;
} TnBuffer;

#define TN_OK                  0
#define TN_ERR_INVALID_ARGUMENT 1
#define TN_ERR_INVALID_HANDLE   2
#define TN_ERR_PANIC            3
#define TN_ERR_COMPILATION      4
#define TN_ERR_FILE             5
#define TN_ERR_EXPORT           6
#define TN_ERR_ABI_MISMATCH     7
#define TN_ERR_CANCELLED        8
#define TN_ERR_LIMIT            9
#define TN_ERR_UNSUPPORTED      10
```

## 3. Symbols

### 3.1 Version handshake

```c
uint32_t    tn_abi_version(void);        /* 1 for this specification */
const char* tn_abi_version_string(void); /* "1.0.0" */
const char* tn_typst_version(void);      /* "0.15.1" — the compiler the library was built with */
const char* tn_build_info(void);         /* JSON: { "target": "x86_64-pc-windows-msvc", "profile": "release", "commit": "…" } */
```

`M3scalinum.Text.Typst.Native` MUST call `tn_abi_version` once at load. If it is less than the version the
managed assembly was compiled against, loading fails with `TypstNativeException`
(`TN_ERR_ABI_MISMATCH`). A greater version is accepted if all symbols used are present.

### 3.2 Errors

```c
void        tn_error_free(TnError *error);
int32_t     tn_error_code(const TnError *error);
TnBuffer    tn_error_json(const TnError *error);  /* see §6, freed with tn_buffer_free */
TnBuffer    tn_error_traceback(const TnError *error); /* panic backtrace, empty if none */
```

Every fallible function takes a trailing `TnError **out_error`. Pass `NULL` to ignore the error
object. On failure the function returns a non-zero status and, when `out_error` is not `NULL`,
sets it to a freshly allocated error.

### 3.3 World

```c
TnWorld* tn_world_create(const char *config_json, TnError **out_error);
void     tn_world_free(TnWorld *world);

int32_t  tn_world_add_source(TnWorld *world, const char *path,
                             const uint8_t *data, size_t len, TnError **out_error);
int32_t  tn_world_add_asset (TnWorld *world, const char *path,
                             const uint8_t *data, size_t len, TnError **out_error);
int32_t  tn_world_add_font  (TnWorld *world, const uint8_t *data, size_t len,
                             uint32_t index, TnError **out_error);

int32_t  tn_world_clear_sources(TnWorld *world, TnError **out_error);
uint32_t tn_world_font_count(const TnWorld *world);
uint32_t tn_world_source_count(const TnWorld *world);
uint32_t tn_world_live_count(void);
```

`config_json`:

```json
{
  "clock": { "kind": "fixed", "utc": "2024-01-01T00:00:00Z" },
  "packages": { "mode": "none" },
  "limits": { "maxSourceBytes": 16777216, "maxAssetBytes": 67108864, "maxFontBytes": 67108864 }
}
```

| Field | Values | Default |
| --- | --- | --- |
| `clock.kind` | `fixed` \| `system` | `fixed` |
| `clock.year` … `clock.second` | components of the fixed instant | `2024-01-01T00:00:00Z` |
| `packages.cacheDir` | where downloaded packages are stored and read | system cache |
| `packages.dataDir` | where local packages are read | system data |
| `packages.download` | whether a missing package may be fetched | `false` |
| `packages.registry` | registry base URL, http(s) | `https://packages.typst.org` |
| `fonts.directories` | directories scanned recursively for font files | `[]` |
| `fonts.system` | whether to load the host's installed fonts | `false` |
| `limits.*` | hard caps, `TN_ERR_LIMIT` beyond | see above |

Packages resolve in the order data directory → cache directory → registry. The cache layout is
Typst's own (`<cache>/<namespace>/<name>/<version>/`), so a directory populated by the `typst` CLI
works unchanged. With `download: false`, a missing package fails with the package name in the
diagnostic rather than a network error.

Fonts added with `bn index > 0` address a face inside a collection (`.ttc`/`.otc`). Adding the
same bytes twice MUST be idempotent (deduplicated by content hash).

### 3.4 Compilation

```c
TnDocument* tn_compile(TnWorld *world, const char *options_json, TnError **out_error);
void        tn_document_free(TnDocument *document);

uint32_t    tn_document_page_count(const TnDocument *document);
uint32_t    tn_document_live_count(void);
```

`tn_world_live_count` and `tn_document_live_count` report how many handles of that kind are alive in
the process. They exist so a host can prove that every handle it created was released: the process
working set cannot answer that question, because it is process-wide and every platform retains freed
native blocks differently. They are diagnostics, not limits — nothing is enforced against them.

`options_json`:

```json
{
  "root": "main.typ",
  "document": "paged",
  "features": []
}
```

| Field | Values | Default |
| --- | --- | --- |
| `root` | virtual path registered with `tn_world_add_source` | `main.typ` |
| `document` | `paged` \| `html` \| `bundle` | `paged` |
| `features` | subset of `html`, `a11y-extras`, `bundle` — Typst in-development features | `[]` |

On success the returned `TnDocument*` is non-NULL and owns the laid-out document. On failure it is
`NULL` and `out_error` carries `TN_ERR_COMPILATION` (or `TN_ERR_FILE`, or `TN_ERR_PANIC`) with a
diagnostics array.

Compiled documents keep their world's data alive internally (`Arc`), so `tn_world_free` may be
called before `tn_document_free`.

### 3.5 Diagnostics

```c
int32_t tn_document_diagnostics(const TnDocument *document, TnBuffer *out_json, TnError **out_error);
```

Returns the warnings and hints produced by a *successful* compilation. Errors are reported through
`tn_error_json` instead: there is exactly one place to look, depending on the outcome.

### 3.6 Export

```c
int32_t tn_document_export(const TnDocument *document,
                           const char *format,
                           const char *options_json,
                           TnBuffer *out, TnError **out_error);

void    tn_buffer_free(TnBuffer buffer);
```

A second, chunked path exists for callers that must not hold the whole output twice:

```c
int32_t tn_document_export_reader(const TnDocument *document,
                                  const char *format,
                                  const char *options_json,
                                  TnExportReader **out, TnError **out_error);

int32_t tn_export_reader_read(TnExportReader *reader,
                              uint8_t *buffer, size_t capacity,
                              size_t *written, TnError **out_error);

void    tn_export_reader_free(TnExportReader *reader);
```

`tn_document_export_reader` encodes the export once and returns a cursor over the bytes;
`tn_export_reader_read` copies at most `capacity` bytes and reports how many, with `written == 0`
meaning the reader is exhausted. The reader borrows nothing from the document, but the document
must stay alive until `tn_export_reader_free` — the managed layer holds a `DangerousAddRef` for the
whole copy. The encoded bytes still exist once, contiguously, in Rust: what the reader removes is
the second, managed copy of the same size. It is a pull interface, so §1's "no callbacks" rule
still holds.

| `format` | Requires `document` | `options_json` |
| --- | --- | --- |
| `pdf` | `paged` | `{ "pdf": { … } }` (optional, see below) |
| `svg` | `paged` | `{ "page": 0 }` — one page |
| `svg-merged` | `paged` | `{}` — all pages in a single SVG |
| `png` | `paged` | `{ "page": 0, "ppi": 144.0 }` |
| `png-merged` | `paged` | `{ "ppi": 144.0 }` |
| `html` | `html` \| `bundle` | `{ "pretty": true }` |
| `markdown` | `html` | `{}` |
| `text` | `html` | `{}` |
| `bundle` | `bundle` | `{}` — returns the file manifest |

The `pdf` object mirrors `typst_pdf::PdfOptions`:

```json
{
  "pdf": {
    "standards": ["a-2b"],
    "pageRanges": "1-3,5",
    "tagged": true,
    "ident": "invoice-2026-0042",
    "creator": "M3scalinum.Text.Typst",
    "pretty": false,
    "timestamp": { "year": 2026, "month": 1, "day": 1, "hour": 0, "minute": 0, "second": 0 }
  }
}
```

`standards` accepts `1.4`, `1.5`, `1.6`, `1.7`, `2.0`, `a-1b`, `a-1a`, `a-2b`, `a-2u`, `a-2a`,
`a-3b`, `a-3u`, `a-3a`, `a-4`, `a-4f`, `a-4e` and `ua-1`. An unknown name, an incompatible
combination, a malformed page range or an invalid timestamp returns `TN_ERR_INVALID_ARGUMENT`.

Requesting a format against an incompatible document kind returns `TN_ERR_INVALID_ARGUMENT`; a
page index out of range returns `TN_ERR_INVALID_ARGUMENT` with the valid range in the message.

Export calls are repeatable and side-effect free: the same document can be exported to several
formats, in any order, from the same thread.

### 3.7 Bundle files

```c
int32_t tn_document_bundle_asset(const TnDocument *document,
                                 uint32_t index,
                                 TnBuffer *out,
                                 TnError **out_error);
```

The `bundle` format returns a manifest, not the files:

```json
{ "assets": [ { "path": "index.html", "size": 512 }, { "path": "data.txt", "size": 21 } ] }
```

`tn_document_bundle_asset` then returns the bytes of file `index`, in manifest order. The split
keeps a large file from crossing the ABI twice and avoids base64 in the manifest. An index out of
range returns `TN_ERR_INVALID_ARGUMENT` with the valid range in the message.

### 3.8 Evaluation

```c
int32_t tn_document_evaluate(const TnDocument *document,
                             TnBuffer *out,
                             TnError **out_error);
```

Returns the document's introspection data as JSON:

```json
{
  "pageCount": 2,
  "outline": [
    { "level": 1, "title": "Introduction", "page": 1, "x": 0.0, "y": 72.0, "label": "intro" }
  ],
  "labels": [ { "name": "intro", "page": 1, "x": 0.0, "y": 72.0 } ],
  "metadata": [ { "value": { "id": 42 }, "page": 1 } ]
}
```

Page numbers are one-based and final: the document is already laid out, so cross-references are
resolved. `x` and `y` are in points from the page's top-left corner. `metadata[].value` is the
embedded Typst value serialized as JSON. A document compiled with `document = "html"` or
`"bundle"` returns `TN_ERR_INVALID_ARGUMENT`, because only a paged document has positions.

### 3.9 Font families

```c
int32_t tn_world_font_families(const TnWorld *world,
                               TnBuffer *out,
                               TnError **out_error);
```

Returns a JSON array of the family names the world can resolve, sorted and deduplicated:

```json
["DejaVu Sans Mono", "Libertinus Serif", "New Computer Modern"]
```

The list covers the embedded fonts plus everything registered through `tn_world_add_font` and the
`fonts` section of the world config. It is the diagnostic for an "unknown font family" warning.

### 3.10 Page text

```c
int32_t tn_document_page_text(const TnDocument *document,
                              uint32_t page,
                              TnBuffer *out,
                              TnError **out_error);
```

Returns the text of one page as UTF-8, without a terminator. `page` is zero-based and must be
below `tn_document_page_count`.

The text is the layout's text, not a semantic reconstruction. The frame of `pages()[page]` is
walked in order, recursing into groups, and every `FrameItem::Text` run contributes its glyphs;
each glyph carries the byte range of the characters it was shaped from, so the characters come
back through the font. Consequences a caller must expect:

- Runs on the same line are concatenated, with a space where the layout left a horizontal gap
  (a table cell, an inline island). Runs on different lines are separated by one `\n`.
- A heading is text on a line; a list item is a line that starts with its bullet glyph. Nothing
  marks them as such — use the HTML document and `text` for structure.
- Hyphenation and ligatures come out as the font mapped them: a ligature yields the characters
  it was shaped from, and a hyphen introduced by the line breaker is part of the text.
- The order is the order the frame tree yields, which for a single-column document is reading
  order and for a multi-column or absolutely positioned layout is not.
- Each page's text ends with exactly one `\n`; an empty page returns an empty buffer
  (`ptr = NULL, len = 0`).

A document compiled with `document = "html"` or `"bundle"` returns `TN_ERR_INVALID_ARGUMENT`,
because only a paged document has pages. A page index out of range returns
`TN_ERR_INVALID_ARGUMENT` with the valid range in the message.

### 3.11 Clone

```c
int32_t tn_world_clone(const TnWorld *world,
                       TnWorld **out,
                       TnError **out_error);
```

Returns a new world that shares the library, the font book and the fonts, and owns a copy of the
sources and the assets. The clone is independent: adding a source to it does not affect the
original, and both can be compiled concurrently.

This is the primitive behind "one template world, one clone per request". A clone never re-scans
fonts and never re-reads packages, so its cost is a few map copies.

## 4. Handles and threading

- `TnWorld` and `TnDocument` are owned by exactly one managed `SafeHandle`. Rust never frees them
  spontaneously.
- A `TnWorld` is safe to use from several threads at once. The source and asset registries are
  behind `RwLock`, and a compile only reads them; the managed side imposes no thread affinity.
- The registries recover from lock poisoning. A panic in one thread does not brick the world for the
  next call: the data behind the lock is still valid, the panic happened elsewhere.
- A compiled `TnDocument` is immutable, so exporting it twice concurrently is safe.
- `tn_world_free` and `tn_document_free` are idempotent with respect to `NULL` and do not dereference
  freed memory; double-free is prevented by the managed `SafeHandle`, not by the ABI.
- Every managed call holds a `DangerousAddRef` for its duration, so a `Dispose` on another thread
  cannot free the native object under a running call. Disposing a handle that is in use blocks until
  the call returns, then frees it.

## 5. Buffer ownership

```
Rust allocates ──► TnBuffer{ptr,len} ──► .NET copies into byte[] ──► tn_buffer_free
```

- The returned buffer is valid until `tn_buffer_free`.
- `M3scalinum.Text.Typst.Native` copies with `Marshal.Copy` / `new ReadOnlySpan<byte>(ptr, len).ToArray()`
  immediately, then frees, in a `finally`.
- An empty result is `ptr = NULL, len = 0` and is a success, never an error.
- `tn_buffer_free` tolerates `{ NULL, 0 }`.

## 6. Error payload

```json
{
  "code": "compilation",
  "message": "1 error, 2 warnings",
  "diagnostics": [
    {
      "severity": "error",
      "message": "unknown variable: `totl`",
      "hints": ["did you mean `total`?"],
      "trace": [],
      "file": "/main.typ",
      "line": 12,
      "column": 8,
      "endLine": 12,
      "endColumn": 13,
      "isInSource": true,
      "sourceLine": "#let x = totl"
    }
  ],
  "typstVersion": "0.15.1"
}
```

`code` is the lowercase name of the status constant (`compilation`, `file`, `panic`, …).
`line`/`column` are 1-based; absence of a span yields `null` for all four position fields.
`isInSource` is `false` when the diagnostic points into an imported file that is not a registered
source (for example a Typst package). `sourceLine` is the text of the line the span starts on, so a
host can render a caret without holding the source; it is `null` when there is no span.

Panic payloads (`code: "panic"`) carry `message` = the panic message and are accompanied by
`tn_error_traceback`.

## 7. Versioning policy

| Change | Effect |
| --- | --- |
| New symbol | ABI version unchanged |
| New optional JSON field | ABI version unchanged; old readers ignore it |
| Renamed/removed symbol, changed struct layout, changed JSON semantics | ABI major + managed package major |
| Changed error `code` string | ABI major |

The NuGet package version tracks the managed API. Its metadata records the bundled Typst version
(`TypstVersion`), which the assembly exposes as `TypstEnvironment.TypstVersion`.

## 8. Managed side: marshalling

```csharp
internal static partial class NativeMethods
{
    [LibraryImport(NativeLibraryName, EntryPoint = "tn_compile",
        StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr Compile(SafeWorldHandle world, string optionsJson, out IntPtr error);
}
```

Rules:

1. `LibraryImport` only — never `DllImport`. Source-generated stubs are required for AOT.
2. Structs are `[StructLayout(LayoutKind.Sequential)]` with blittable fields only
   (`BytePtr` + `UIntPtr` for `TnBuffer`).
3. Strings cross as UTF-8 (`StringMarshalling.Utf8`); binary data crosses as `ReadOnlySpan<byte>`
   or a pinned `byte*` with an explicit length.
4. `out IntPtr error` is converted to an `Exception` by a single helper and never escapes
   `M3scalinum.Text.Typst.Native`.
5. The library name passed to `LibraryImport` is the constant `"sharp_typst"`; resolution is
   customised (§9).

## 9. Native library resolution

`M3scalinum.Text.Typst.Native` installs a `NativeLibrary.SetDllImportResolver` in a module initializer:

1. If `SHARPTYPST_NATIVE_PATH` is set, load that exact file.
2. Otherwise probe, in order: `runtimes/<rid>/native/`, the application base directory, and the
   default OS search path.
3. If nothing is found, throw `DllNotFoundException` with a message naming the RID looked up, the
   platform version required and the environment variable that overrides the path.

`<rid>` is computed from the process's OS and architecture (`win-x64`, `linux-x64`, `osx-arm64`),
not from the full portable RID. On Linux the libc is detected — the musl dynamic loader
(`/lib/ld-musl-<arch>.so.1`) or `/etc/alpine-release` — because a framework-dependent app published
for `linux-x64` reports that portable RID even on Alpine, where the glibc build cannot load. The
probe order is therefore:

1. `runtimes/<rid>/native/` — `linux-musl-x64` on a musl host.
2. `runtimes/<portable-rid>/native/` — the glibc build, kept as a fallback.
3. `runtimes/<os>/native/` — where the universal macOS dylib lives.
4. The application base directory.

## 10. Conformance tests

The ABI is tested from both sides:

- Rust integration tests (`cargo test`) call every symbol through the C ABI, including
  double-free avoidance, `NULL` arguments, a deliberately panicking input (a corrupted font) and
  a 0-byte source.
- Managed tests (`tests/Text.Typst.Integration.Tests`) assert:
  `tn_abi_version` matches the constant compiled into the managed layer; every status code is
  reachable; `TnBuffer` of size 0 is handled; a panic becomes a `TypstNativeException` and leaves
  the process alive; 10 000 create/compile/export/free cycles leave the handle count flat.
