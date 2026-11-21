# 20 — Barcodes

## 1. Scope

`M3scalinum.Text.Barcodes` writes and reads barcodes in-process, with no external tool, no service
and no temporary file:

```csharp
BarcodeImage image = Barcode.Encode(BarcodeSymbology.QrCode, "BT-1=FA-2026-000123");
byte[] png = image.Png;                       // 1-bit-exact greyscale, Scale pixels per module
string svg = image.Svg;                       // one unit per module, for a layout engine
DecodeResult read = Barcode.Decode(png);      // text, symbology, points, success
```

Bytes and strings in, bytes and strings out. The package carries its engine — a Rust `cdylib` over
`rxing` — per runtime identifier, so nothing else has to be installed.

**In scope.** Ten symbologies in both directions: QR Code, DataMatrix, Aztec, PDF417, Code 128, Code
39, EAN-13, EAN-8, ITF and Codabar. SVG and PNG rendering from one module matrix. PNG decoding of the
same ten, plus every other format the engine reads (a UPC-A is reported by name even though this
package does not write one).

**Out of scope, deliberately.**

| Not done | Why |
| --- | --- |
| `DecodeSvg` | Reading a barcode out of arbitrary SVG means rasterising SVG — a renderer, a font stack and a CSS subset. The vector half of this package is for output; a caller who needs to read a symbol rasterises it where it already has a renderer, or keeps the PNG. |
| UPC-A, UPC-E, Code 93, Telepen, MaxiCode, RSS/GS1 DataBar | Written by the engine, but each is a distinct specification with its own conformance burden. The set stops where the tests can prove both directions. |
| Composite, structured-append and GS1 Application Identifier parsing | Faithful GS1 handling is a data model (AI dictionaries, date and measure semantics), not a renderer feature. The payload is encoded as given. |
| Drawing text under a one-dimensional symbol | A human-readable line is typography, and this repository already has Typst for that. The bar pattern is all this package emits. |
| Multi-symbol detection | `Decode` returns the first symbol it finds, which is what an invoice or a label has. Scanning a page of many labels is a different API with a different result shape. |
| Colour, alpha, quiet-zone artwork | A barcode is black modules on white. Anything else is a claim the reader cannot verify. |

## 2. The two halves

| Layer | Project | What it does |
| --- | --- | --- |
| `native/text-barcodes` (Rust, edition 2024) | — | Encodes with `rxing`, renders the module matrix to SVG and PNG, decodes a PNG to text. Exposes the C ABI below. |
| `src/Text.Barcodes` (managed) | `M3scalinum.Text.Barcodes` | P/Invoke, handle lifetime, the exception taxonomy, and the public API. |

The managed package is the only caller of the native half. No type outside `Internal` mentions a
handle, a buffer or a status code.

## 3. Public surface

| Member | Contract |
| --- | --- |
| `Barcode.Encode(BarcodeSymbology, string, BarcodeOptions?)` | Returns `BarcodeImage`. Throws `BarcodeFormatException` when the payload cannot be represented, `BarcodeException` (`InvalidArgument`) for an empty or NUL-carrying payload and an out-of-range option, `BarcodeException` (`Limit`) past 4 096 bytes. |
| `Barcode.Decode(byte[] png)` | Returns `DecodeResult`. A readable image without a barcode is `Success == false`; only bytes that are not a PNG are `BarcodeImageException`. |
| `Barcode.SupportedSymbologies` | The symbologies the loaded native library reports, read from it rather than declared. |
| `BarcodeImage` | `Svg`, `Png`, `ModuleWidth`, `ModuleHeight`, `Symbology`, `Payload`, `AspectRatio`. |
| `DecodeResult` | `Success`, `Text`, `Symbology` (null when the format is outside the enum), `Format` (the engine's spelling), `Points`, `Error`. |
| `BarcodeOptions` | `Scale` (pixels per module, 1–64, default 4), `QuietZone` (modules per side, default per symbology), `BarHeight` (modules, one-dimensional only, default 40), `ErrorCorrection` (QR Code only). |
| `BarcodeEnvironment` | `EngineVersion` (`rxing 0.9.4`), `BarcodesVersion`. |

### 3.1 Defaults, per symbology

| Symbology | Quiet zone (modules/side) | Bar height | Notes |
| --- | --- | --- | --- |
| QR Code | 4 | — | `ErrorCorrection` L/M/Q/H; the only symbology that takes one. |
| DataMatrix | 1 | — | ECC 200, square or rectangular as the encoder chooses. |
| Aztec | 2 | — | Needs **two** pixels per module to be read back; `Scale = 1` is accepted and does not round-trip. |
| PDF417 | 2 | — | Each bar row is emitted four units tall, which is the symbology's own aspect. |
| Code 128 | 5 | 40 | Full ASCII. |
| Code 39 | 5 | 40 | Extended ASCII; a character outside ASCII is a `BarcodeFormatException`. |
| EAN-13 | 5 | 40 | Exactly 13 digits, check digit included. |
| EAN-8 | 5 | 40 | Exactly 8 digits. |
| ITF | 5 | 40 | Digits, an even number of them. |
| Codabar | 5 | 40 | Payload includes the start and stop characters (`A`…`B`); a reader returns the digits between them. |

### 3.2 One module per unit

`BarcodeImage.Svg` is emitted at exactly one user-space unit per module of the matrix, with the
`viewBox` set to that matrix, so a layout engine scales it to whatever physical size the page needs
and no resampling happens at any point. The matrix width and height are on the image, so a caller can
compute the physical size a quiet zone and a module size imply.

`BarcodeImage.Png` is the same matrix at an integer `Scale`, written as an 8-bit greyscale PNG whose
pixels are only 0 or 255. Greyscale rather than 1-bit because every decoder and every image library
reads it, and because the property that matters is that a second pass decodes it — which the test
suite asserts for all ten symbologies.

### 3.3 Errors

| Exception | `BarcodeErrorCode` | When |
| --- | --- | --- |
| `BarcodeFormatException` | `Encode` | The symbology cannot hold this payload: wrong length for EAN-13/EAN-8, a character outside the symbology's set, a payload past the symbol's capacity. |
| `BarcodeUnsupportedException` | `Unsupported` | The symbology is not one this build writes. |
| `BarcodeImageException` | `Decode` | The bytes are not a PNG this build can read. |
| `BarcodeNativeException` | `Native` | A panic in the native library, or an ABI version mismatch. |
| `BarcodeException` | `InvalidArgument`, `Limit` | A caller argument, a size limit. |

A page without a barcode is not in that table on purpose: it is `DecodeResult.Success == false` with
`Error` set, because scanning a stack of documents should not throw once per blank page.

## 4. The native ABI

Prefix `bt_`. Strings are UTF-8 and NUL-terminated; buffers are owned by Rust and freed with
`bt_buffer_free`; errors are owned and freed with `bt_error_free`; every entry point wraps its body in
`catch_unwind`, so a panic crosses the boundary as a status code rather than as an unwind. The rules
of Spec 02 §1 apply unchanged; this section lists only what is specific here.

### 4.1 Handshake

```c
uint32_t    bt_abi_version(void);          /* 1 */
const char* bt_abi_version_string(void);   /* "1.0.0" */
const char* bt_engine_version(void);       /* "rxing 0.9.4" */
const char* bt_build_info(void);           /* JSON: target, profile, engine, engineVersion */
```

The managed side calls `bt_abi_version` once at load and refuses anything but 1.

### 4.2 Errors, buffers, symbologies

```c
void     bt_error_free(BtError *error);
int32_t  bt_error_code(const BtError *error);
BtBuffer bt_error_json(const BtError *error);        /* {"code":"encode","message":"…"} */
BtBuffer bt_error_traceback(const BtError *error);
void     bt_buffer_free(BtBuffer buffer);
BtBuffer bt_symbologies(void);                        /* ["QRCode", …] */
```

### 4.3 Encoding

```c
BtBarcode* bt_barcode_encode(const char *symbology, const char *payload,
                             int32_t quiet_zone, int32_t bar_height,
                             const char *error_correction, BtError **out_error);
int32_t    bt_barcode_svg(const BtBarcode *barcode, BtBuffer *out_svg, BtError **out_error);
int32_t    bt_barcode_png(const BtBarcode *barcode, uint32_t scale,
                          BtBuffer *out_png, BtError **out_error);
uint32_t   bt_barcode_width(const BtBarcode *barcode);
uint32_t   bt_barcode_height(const BtBarcode *barcode);
void       bt_barcode_free(BtBarcode *barcode);
uint32_t   bt_barcode_live_count(void);
```

`quiet_zone` and `bar_height` take the symbology default when negative. The requested pixel size is
always zero internally: rxing then returns the symbol at one unit per module instead of scaling it,
which is what keeps the SVG exact.

### 4.4 Decoding

```c
BtDecode* bt_decode_png(const uint8_t *data, size_t len, BtError **out_error);
int32_t   bt_decode_success(const BtDecode *decoded);
int32_t   bt_decode_text(const BtDecode *decoded, BtBuffer *out_text, BtError **out_error);
int32_t   bt_decode_symbology(const BtDecode *decoded, BtBuffer *out_name, BtError **out_error);
int32_t   bt_decode_format(const BtDecode *decoded, BtBuffer *out_format, BtError **out_error);
int32_t   bt_decode_points(const BtDecode *decoded, BtBuffer *out_points, BtError **out_error);
int32_t   bt_decode_error(const BtDecode *decoded, BtBuffer *out_message, BtError **out_error);
void      bt_decode_free(BtDecode *decoded);
uint32_t  bt_decode_live_count(void);
```

A decode that finds nothing is a successful call with `bt_decode_success` returning `0`: the caller
reads the reason from `bt_decode_error`. Only bytes that are not a readable PNG set an error status
(`BT_ERR_DECODE`).

### 4.5 Status codes

| Value | Name | Meaning |
| --- | --- | --- |
| 0 | `BT_OK` | |
| 1 | `BT_ERR_INVALID_ARGUMENT` | Null pointer, empty payload, out-of-range option. |
| 2 | `BT_ERR_INVALID_HANDLE` | A null handle where a live one was required. |
| 3 | `BT_ERR_PANIC` | A panic was caught at the boundary. |
| 4 | `BT_ERR_ENCODE` | The payload cannot be represented in the symbology. |
| 5 | `BT_ERR_UNSUPPORTED` | Unknown symbology name. |
| 6 | `BT_ERR_DECODE` | The bytes are not a PNG this build can read. |
| 7 | `BT_ERR_ABI_MISMATCH` | Reserved for the load-time handshake. |
| 8 | `BT_ERR_LIMIT` | Payload or raster size limit. |

The numbering is a contract: a value never changes meaning.

### 4.6 Handles

Both handle types are opaque, single-owner and counted. `bt_barcode_live_count` and
`bt_decode_live_count` report how many are alive right now, which is the property the library owns:
the process working set is process-wide, and every platform retains freed native blocks differently,
so a soak that reads it measures the allocator rather than a leak. The managed test suite asserts
both counts return to their baseline over thousands of cycles.

## 5. Why rxing, and what it costs

| Candidate | Verdict |
| --- | --- |
| `rxing` 0.9.4 — Apache-2.0 | **Chosen.** A Rust port of ZXing: ten writers and readers behind one API, no C dependency, no runtime, no fonts, no network. Apache-2.0 is the licence of this repository, so a consumer needs no extra notice to comply with. |
| ZXing.Net — Apache-2.0 | The .NET port, and the obvious alternative. It would have removed the Rust crate, but it drags `System.Drawing.Common` on several targets — a GDI+ dependency that is Windows-only in modern .NET — and its writer/reader API is a decade behind the ZXing it forked. |
| `barcoders` — MIT | Writers only, and a small format set. Reading would still have needed a second library. |
| `qrcode` (Rust) — MIT/Apache-2.0 | QR Code only. |
| A hand-written encoder per symbology | Ten specifications, ten conformance surfaces, and a Reed-Solomon implementation per family. Not a feature, a project. |

The cost is one native library per runtime identifier, shipped inside the package under
`runtimes/<rid>/native/`. That is the same shape `M3scalinum.Text.Typst` already ships, and the
resolver is the same code: `BARCODES_NATIVE_PATH` overrides it for a local build, then
`runtimes/<rid>/native/`, then `runtimes/<portable-rid>/native/`, then `runtimes/<os>/native/`, then
the application directory.

The engine is built with only the features this package uses — encoders, decoders and the barcode
formats — so no image codec other than PNG, no SVG reader and no client support is linked in. `image`
0.25.8 (MIT OR Apache-2.0) is a direct dependency for the PNG codec only, pinned to the version rxing
itself depends on so the tree holds one copy.

## 6. Tests

The suite is `tests/Text.Barcodes.Tests`, xUnit v3 on Microsoft.Testing.Platform, and it runs the
real engine: every assertion below is a real encode and a real decode, no mock.

| Test | Property |
| --- | --- |
| Round trip, all ten symbologies, `Scale` 4 | The payload comes back, and so does the symbology. |
| Round trip, all ten symbologies, `Scale` 2 | The raster is still exact at a smaller factor. |
| QR Code of a Factur-X header (nine `BT-n=value` terms) | The payload this package exists for round-trips, line by line. |
| EAN-13 with twelve digits, EAN-8 with seven | `BarcodeFormatException`, with the expected length in the message. |
| A blank PNG | `Success == false`, no exception. |
| SVG shape | Starts with `<svg`, one `<path>`, the `viewBox` is the module matrix, every run inside it, runs fewer than dark modules where a wide bar merges them. |
| Options | `Scale` sets the raster and never the vector; `QuietZone` widens the matrix by two per side; `BarHeight` sets a one-dimensional symbol's height. |
| Aztec at `Scale` 1 | Fails to decode — the boundary is pinned rather than hidden. |
| Handle soaks | Five thousand encode/decode cycles, five hundred failures and five hundred blank decodes all release their handles; managed memory stays flat. |
| Environment | The native symbology list equals the enum; the resolver probes `runtimes/<rid>/native/` first. |

The Rust crate carries its own unit tests (the FFI primitives, the symbology parser, the SVG run
lengths, the PNG header) and an integration test that repeats the ten round trips at the library
level, so a regression is attributable to the half that caused it.

## 7. Packaging

The package ships the managed assembly for `net8.0`, `net9.0`, `net10.0` and `net11.0`, and the
native library per RID (`win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `linux-musl-x64`,
`osx`). CI produces the natives; a local pack simply omits the ones that are not built.

The version follows the suite, not the engine: `SuiteVersion` from the release tag, and `TextVersion`
for an untagged build. The engine version is reported at run time by `BarcodeEnvironment.EngineVersion`
rather than encoded in the package version, because rxing is an implementation detail a caller should
not have to care about until something fails to decode.
