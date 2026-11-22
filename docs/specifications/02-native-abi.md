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
uint32_t    tn_abi_version(void);        /* 2 — the revision this library implements */
uint32_t    tn_abi_version_min(void);    /* 1 — the oldest revision it still serves */
const char* tn_abi_version_string(void); /* "2.0.0" */
const char* tn_typst_version(void);      /* "0.15.1" — the compiler the library was built with */
const char* tn_build_info(void);         /* JSON: { "target": "x86_64-pc-windows-msvc", "profile": "release", "commit": "…" } */
```

`M3scalinum.Text.Typst.Native` calls `tn_abi_version` once at load and accepts the library when the
revision falls inside the range it was built for. The range, the deprecation window and the two
refusals are specified in §11. `tn_abi_version_min` exists for introspection and conformance only:
the managed layer must not call it, because a revision 1 library does not export it.

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

### 3.12 Compilation metrics

```c
int32_t tn_document_metrics(const TnDocument *document,
                            TnBuffer *out,
                            TnError **out_error);
```

Returns a JSON object describing what the compilation cost, as the library saw it:

```json
{"compileNanos": 4123456, "pages": 1, "fontsTotal": 12, "fontsEmbedded": 11, "fontBytes": 7340032}
```

`compileNanos` is the time spent inside `typst::compile` (plus the bundle export when the document
is a bundle), `pages` is the laid-out page count and is zero for an HTML or bundle document,
`fontsTotal` is the fonts the world resolved — embedded and host — and `fontBytes` the bytes of the
distinct font buffers behind them, a font collection counted once. The managed side measures the
whole call itself, so a caller sees both the total and the part only the library can see; neither is
kept in a process-wide counter, because two compilations may run at the same time (spec 03 §12).

The function is additive: it was added without changing any existing symbol, so `TN_ABI_VERSION`
stayed at 2. A library built before it does not export it — the managed layer only calls it on a
document handle from a library that does.

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
  `tn_abi_version` matches the revision compiled into the managed layer and `tn_abi_version_min`
  is at most that revision; every status code is reachable; `TnBuffer` of size 0 is handled; a
  panic becomes a `TypstNativeException` and leaves the process alive; 10 000
  create/compile/export/free cycles leave the handle count flat.

## 11. Version negotiation

The handshake is a range, not a point. `tn_abi_version` reports the revision a library
implements and `tn_abi_version_min` the oldest revision it still serves. The managed layer carries
the same pair — `AbiContract.Current` and `AbiContract.Minimum` — and a library loads when the
revision it reports falls inside `[Minimum, Current]`.

The range spans two ABI revisions: the current one and the one before it. A revision is therefore
served for two revisions and dropped on the third — revision 1 is accepted by a managed layer at
revision 2 and refused by a managed layer at revision 3. A release that adds a revision bumps
`Current`, leaves every existing symbol in place, and never renumbers one.

The managed values and the crate's `TN_ABI_VERSION` / `TN_ABI_MIN_VERSION` are the same two
numbers; a test on each side reads the other side's source and fails when they drift, so an
installed pair is never deprecated or refused for a reason nobody chose.

### 11.1 Verdicts

| Native revision | Verdict | What happens |
| --- | --- | --- |
| `Current` | accepted | nothing is reported |
| `Minimum`…`Current - 1` | accepted, deprecated | one warning, then the library is used |
| below `Minimum` | refused | `TN_ERR_ABI_MISMATCH`, library not used |
| above `Current` | refused | `TN_ERR_ABI_MISMATCH`, library not used |

A newer library than the managed package is refused because the direction of a mismatch is what
the message has to explain: a revision the layer does not know may have removed or changed a
symbol it calls, and the failure would land on the first call that crosses it rather than on the
load.

There is no switch for this check. No environment variable, no configuration key, no
`AppContext` flag disables it: a host that could turn the check off would turn it off, and the
mismatch would then surface as an `EntryPointNotFoundException` on whichever symbol changed first,
or as a crash inside Rust. The check runs on every load.

### 11.2 The refusal

Refusing throws before the first symbol is used, with the status `TN_ERR_ABI_MISMATCH`, which the
managed layer surfaces as a `TypstNativeException` carrying `TypstErrorCode.AbiMismatch`. The
message names, in one sentence each:

- the revision the library reported, and the revision the managed layer is at;
- the accepted range, written `Minimum..Current`;
- the file that was loaded, so the reader knows which copy is in play — a stale `sharp_typst` next
  to the application rather than the package's `runtimes/<rid>/native` asset is the usual cause;
- the direction: the library is older than this build, or newer than it;
- the action: replace the stale asset, or update the managed and the native package together.

Nothing in the message refers to a document or a source file the reader has to find first.

### 11.3 The deprecation window

A library inside the range but below `Current` is used, and one warning is written through the
`System.Diagnostics.TraceSource` named `M3scalinum.Text.Typst.Native`, at
`TraceEventType.Warning`. A trace source rather than the console: it is silent until the host
attaches a listener, and it is how a host routes the notice into its own logs.

The warning names the revision found, the revision expected, the accepted range and the loaded
file, and says what is missing from the library and when it will stop working:

- symbols added to the ABI after the library's revision are absent from it, so a feature that
  needs one fails at its call, not at load;
- the library is refused outright starting with the next revision of the managed layer.

### 11.4 What a consumer does with the warning

Read it as a deadline, not as noise:

1. Ship the native asset from the same release as the managed package. When both come from the
   same package version the warning disappears, because the revisions match.
2. If the application directory holds its own copy of the library, delete it. The resolver probes
   `runtimes/<rid>/native` first and the application directory last, so a file there is a
   deliberate override, and an old one keeps being used after the package is updated.
3. Do not silence the trace source to make the warning go away. The next release of the managed
   package refuses that library at load, and the failure then arrives in production instead of in
   a log.
4. If a pinned native package is the constraint, upgrade it: the two packages are released
   together and both carry the same ABI revision.
