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

**In scope.** Nineteen symbologies: fifteen in both directions — QR Code, DataMatrix, Aztec, PDF417,
Code 128, Code 39, EAN-13, EAN-8, ITF, Codabar, GS1-128, UPC-A, UPC-E, Code 93 and Telepen — and four
that are read only because the engine has no writer for them: GS1 DataBar Omnidirectional, GS1
DataBar Expanded, MaxiCode and Micro QR Code. SVG and PNG rendering from one module matrix. PNG
decoding of the same nineteen — the first symbol, or every symbol a page carries, with the box each
one occupies (§9). The GS1 side is a data model as well as a symbology: Application
Identifiers parsed into named fields, the GS1-128 payload form, and the GS1 Digital Link URL (§8).

**Out of scope, deliberately.**

| Not done | Why |
| --- | --- |
| `DecodeSvg` | Reading a barcode out of arbitrary SVG means rasterising SVG — a renderer, a font stack and a CSS subset. The vector half of this package is for output; a caller who needs to read a symbol rasterises it where it already has a renderer, or keeps the PNG. |
| Writing GS1 DataBar, MaxiCode and Micro QR Code | `rxing` 0.9.4 has readers for all four DataBar/MaxiCode/Micro-QR formats and no writer. Read only is the honest shape of that, not a gap to be worked around (§8.5). |
| Composite and structured-append symbols | Two symbols that share one payload are a different data model (§8.5). Two independent symbols on one page are supported: `DecodeAll` returns every one of them, with its box (§9). |
| AI semantics beyond the table | Dates stay `YYMMDD` strings, not `DateTime`s, and the measure identifiers (`310n` net weight…) are not in the table. Parsing them into values is a business model, not a symbology (§8.5). |
| Drawing text under a one-dimensional symbol | A human-readable line is typography, and this repository already has Typst for that. The bar pattern is all this package emits. |
| Colour, alpha, quiet-zone artwork | A barcode is black modules on white. Anything else is a claim the reader cannot verify. |

## 2. The two halves

| Layer | Project | What it does |
| --- | --- | --- |
| `native/text-barcodes` (Rust, edition 2024) | — | Encodes with `rxing`, renders the module matrix to SVG and PNG, decodes every barcode a PNG holds to text, symbology and position. Exposes the C ABI below. |
| `src/Text.Barcodes` (managed) | `M3scalinum.Text.Barcodes` | P/Invoke, handle lifetime, the exception taxonomy, and the public API. |

The managed package is the only caller of the native half. No type outside `Internal` mentions a
handle, a buffer or a status code.

## 3. Public surface

| Member | Contract |
| --- | --- |
| `Barcode.Encode(BarcodeSymbology, string, BarcodeOptions?)` | Returns `BarcodeImage`. Throws `BarcodeFormatException` when the payload cannot be represented, `BarcodeException` (`InvalidArgument`) for an empty or NUL-carrying payload and an out-of-range option, `BarcodeException` (`Limit`) past 4 096 bytes. |
| `Barcode.Decode(byte[] png)` | Returns `DecodeResult`. A readable image without a barcode is `Success == false`; only bytes that are not a PNG are `BarcodeImageException`. |
| `Barcode.DecodeAll(byte[], BarcodeDecodeOptions?)` | Returns `DecodePage`: every symbol on the image, each with its `Bounds`, and the image size. No symbol is an empty page, not an exception; an image past a ceiling is `BarcodeException` (`Limit`) (§9). |
| `DecodePage` | `Symbols`, `Count`, `Success`, `Width`, `Height`, `Error`. |
| `Barcode.SupportedSymbologies` | The symbologies the loaded native library reports, read from it rather than declared. |
| `Barcode.CanEncode(BarcodeSymbology)` | Whether this build can write the symbology. `false` for the four read-only ones (§8.1). |
| `Barcode.ApplicationIdentifiers` | The GS1 Application Identifier table, read from the native library: length, check digit, Digital Link key (§8.2). |
| `Barcode.EncodeGs1(Gs1Data, BarcodeOptions?)` | Encodes a parsed element string as a GS1-128. |
| `Gs1Data` | `Parse`, `FromDigitalLink`, `ToDigitalLink`, `Elements`, `Raw`, `HumanReadable` (§8.3, §8.4). |
| `BarcodeImage` | `Svg`, `Png`, `ModuleWidth`, `ModuleHeight`, `Symbology`, `Payload`, `AspectRatio`. |
| `DecodeResult` | `Success`, `Text`, `Symbology` (null when the format is outside the enum), `Format` (the engine's spelling), `Points`, `Bounds` (null when the detector reports no points), `Error`. |
| `BarcodeDecodeOptions` | `MaximumPixels` (default 64 000 000), `MaximumBytes` (default 256 MiB), and the hard maxima that bound them (§9.3). |
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
| GS1-128 | 5 | 40 | A GS1 element string; FNC1 in lead position (§8.3). |
| UPC-A | 9 | 40 | Exactly 12 digits, check digit included. |
| UPC-E | 9 | 40 | Exactly 8 digits, number system 0 or 1. At five modules the reader cannot find the symbol its own writer produced. |
| Code 93 | 5 | 40 | |
| Telepen | 5 | 40 | ASCII only. |
| DataBar Omnidirectional, DataBar Expanded, MaxiCode, Micro QR | — | — | Read only: no writer exists in the engine, so there is no default to render. |

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
(`BT_ERR_DECODE`), and an image past a ceiling sets `BT_ERR_LIMIT`.

`bt_decode_png` reads the first symbol, which is the fast path. Every symbol on the page — and the
PDF recipe that feeds a rendered page to it — is §9, with its own `BtDecodePage` handle.

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
| 9 | `BT_ERR_GS1` | A GS1 element string or Digital Link that is malformed (§8). |

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

The engine is built with only the features this package uses — encoders, decoders, the barcode
formats and the multi-symbol readers — so no image codec other than PNG, no SVG reader and no client
support is linked in. `image`
0.25.8 (MIT OR Apache-2.0) is a direct dependency for the PNG codec only, pinned to the version rxing
itself depends on so the tree holds one copy.

## 6. Tests

The suite is `tests/Text.Barcodes.Tests`, xUnit v3 on Microsoft.Testing.Platform, and it runs the
real engine: every assertion below is a real encode and a real decode, no mock.

| Test | Property |
| --- | --- |
| Round trip, all ten symbologies, `Scale` 4 | The payload comes back, and so does the symbology. |
| Round trip, all ten symbologies, `Scale` 2 | The raster is still exact at a smaller factor. |
| QR Code of an invoice header (nine `key=value` terms, one per line) | The payload this package exists for round-trips, line by line. |
| EAN-13 with twelve digits, EAN-8 with seven | `BarcodeFormatException`, with the expected length in the message. |
| A blank PNG | `Success == false`, no exception. |
| SVG shape | Starts with `<svg`, one `<path>`, the `viewBox` is the module matrix, every run inside it, runs fewer than dark modules where a wide bar merges them. |
| Options | `Scale` sets the raster and never the vector; `QuietZone` widens the matrix by two per side; `BarHeight` sets a one-dimensional symbol's height. |
| Aztec at `Scale` 1 | Fails to decode — the boundary is pinned rather than hidden. |
| Handle soaks | Five thousand encode/decode cycles, five hundred failures and five hundred blank decodes all release their handles; managed memory stays flat. |
| Environment | The native symbology list equals the enum; the resolver probes `runtimes/<rid>/native/` first. |
| GS1 round trip | A two-identifier and a three-identifier GS1-128 encode, decode back as `Gs1_128`, and parse into the same elements. |
| GS1 separators | A variable-length batch number followed by another identifier keeps its field separator: the case that would silently read `LOT-42271231` as one value. |
| GS1 refusals | An unknown identifier, a GTIN with a wrong check digit, and a value of the wrong length are `BarcodeGs1Exception`, before any symbol exists. |
| Digital Link | Built from elements and parsed back to them; a relative URL, a URL without a primary key and a URL with a bad GTIN are refused. |
| New symbologies | UPC-A, UPC-E, Code 93 and Telepen round-trip; DataBar, MaxiCode and Micro QR Code are read-only and say so. |
| Several symbols on one page | A QR Code and a Code 128 pasted on one image: both come back, with distinct symbologies and boxes that cannot overlap. |
| A page rendered from a PDF | A one-page PDF whose page is the symbol, rendered by `PdfRaster` at 300 DPI and decoded through `DecodeAll`; skipped when the rasteriser's native library is not on the path. |
| Image ceilings | An image of exactly the pixel ceiling decodes and one pixel past it is `Limit`; a byte ceiling one byte short of the image is `Limit`; a ceiling outside its range is `ArgumentOutOfRangeException`. |
| Page handles | Two hundred page decodes in the non-parallel handle collection: the page and decode counts return to their baseline. |

The Rust crate carries its own unit tests (the FFI primitives, the symbology parser, the GS1 table
and check digits, the Digital Link, the SVG run lengths, the PNG header) and an integration test that
repeats the fifteen round trips at the library level, plus the GS1-128 and read-only cases, so a
regression is attributable to the half that caused it.

## 7. Packaging

The package ships the managed assembly for `net8.0`, `net9.0`, `net10.0` and `net11.0`, and the
native library per RID (`win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `linux-musl-x64`,
`osx`). CI produces the natives; a local pack simply omits the ones that are not built.

The version follows the suite, not the engine: `SuiteVersion` from the release tag, and `TextVersion`
for an untagged build. The engine version is reported at run time by `BarcodeEnvironment.EngineVersion`
rather than encoded in the package version, because rxing is an implementation detail a caller should
not have to care about until something fails to decode.

## 8. GS1: GS1-128, DataBar and the Digital Link

GS1 data is a *string of elements*: an Application Identifier followed by its value, then the next
one. `01` and a fourteen-digit GTIN, `10` and a batch number, `17` and an expiry date. A symbology
carries that string; it does not carry the structure, and a caller who receives the string has no way
to know where one value ends. This section is the structure: the table of identifiers this build
enforces, the GS1-128 payload form, and the Digital Link URL.

```csharp
Gs1Data data = Gs1Data.Parse("0109501101530003\u001d10LOT-42");   // raw, ]C1… or (01)… forms
foreach (Gs1Element element in data.Elements)
{
    Console.WriteLine($"{element.Ai} {element.Title}: {element.Value}");
}

BarcodeImage carton = Barcode.EncodeGs1(data);        // GS1-128, FNC1 in lead position
string url = data.ToDigitalLink("https://example.com/gs1");
Gs1Data back = Gs1Data.FromDigitalLink(url);          // the same elements
```

### 8.1 The symbology set and its direction

`Barcode.SupportedSymbologies` lists all nineteen; `Barcode.CanEncode` says which of them this build
can write. The two lists differ, and the difference is the engine's, not a choice of this package:
`rxing` 0.9.4 ships a reader for every format below and a writer for fifteen of them.

| Symbology | ABI name | Write | Read | Notes |
| --- | --- | --- | --- | --- |
| QR Code | `QRCode` | ✓ | ✓ | `ErrorCorrection` L/M/Q/H. |
| DataMatrix | `DataMatrix` | ✓ | ✓ | ECC 200. |
| Aztec | `Aztec` | ✓ | ✓ | Two pixels per module minimum. |
| PDF417 | `PDF417` | ✓ | ✓ | |
| Code 128 | `Code128` | ✓ | ✓ | Full ASCII. |
| Code 39 | `Code39` | ✓ | ✓ | |
| EAN-13 | `EAN13` | ✓ | ✓ | Thirteen digits, check digit included. |
| EAN-8 | `EAN8` | ✓ | ✓ | Eight digits. |
| ITF | `ITF` | ✓ | ✓ | Digits, an even number of them. |
| Codabar | `Codabar` | ✓ | ✓ | Start and stop characters are stripped by the reader. |
| GS1-128 | `GS1-128` | ✓ | ✓ | Code 128 with FNC1; §8.3. |
| UPC-A | `UPCA` | ✓ | ✓ | Twelve digits; nine modules of quiet zone. |
| UPC-E | `UPCE` | ✓ | ✓ | Eight digits, number system 0 or 1; nine modules of quiet zone. |
| Code 93 | `Code93` | ✓ | ✓ | |
| Telepen | `Telepen` | ✓ | ✓ | ASCII. |
| GS1 DataBar Omnidirectional | `DataBarOmnidirectional` | — | ✓ | RSS-14. Read only. |
| GS1 DataBar Expanded | `DataBarExpanded` | — | ✓ | RSS Expanded. Read only. |
| MaxiCode | `MaxiCode` | — | ✓ | Read only. |
| Micro QR Code | `MicroQR` | — | ✓ | Includes the rectangular variant. Read only. |

Symbology names are parsed case-insensitively and hyphens, underscores and spaces are not
significant, so `GS1-128`, `gs1_128` and `GS1 128` all name the same symbology; `DataBar` is accepted
for `DataBarOmnidirectional`.

An EAN-13 whose first digit is `0` is a UPC-A and is reported as one: it is the same bars, and the
reader names the smaller format, exactly as ZXing does.

### 8.2 The Application Identifiers

The table below is enforced, not advisory: the parser reads the longest identifier that matches, takes
a fixed length literally, stops a variable one at the field separator, refuses an identifier that is
not here, checks the GS1 mod-10 check digit where the standard gives one, and refuses an identifier
that appears twice.

| AI | Meaning | Length | Value | Check digit | Digital Link key |
| --- | --- | --- | --- | --- | --- |
| AI | Meaning | Length | Value | Check digit | Digital Link key |
| --- | --- | --- | --- | --- | --- |
| `00` | SSCC | 18 | digits | yes | yes |
| `01` | GTIN | 14 | digits | yes | yes |
| `02` | CONTENT | 14 | digits | yes | — |
| `10` | BATCH/LOT | 1–20 | text | — | — |
| `11` | PROD DATE | 6 | digits | — | — |
| `12` | DUE DATE | 6 | digits | — | — |
| `13` | PACK DATE | 6 | digits | — | — |
| `15` | BEST BEFORE | 6 | digits | — | — |
| `16` | SELL BY | 6 | digits | — | — |
| `17` | USE BY/EXPIRY | 6 | digits | — | — |
| `20` | VARIANT | 2 | digits | — | — |
| `21` | SERIAL | 1–20 | text | — | — |
| `22` | CPV | 1–20 | text | — | — |
| `30` | VAR. COUNT | 1–8 | text | — | — |
| `37` | COUNT | 1–8 | text | — | — |
| `235` | TPI | 1–28 | text | — | — |
| `240` | ADDITIONAL ID | 1–30 | text | — | — |
| `241` | CUST. PART NO. | 1–30 | text | — | — |
| `242` | MTO VARIANT | 6 | digits | — | — |
| `243` | PACK COMPONENT | 1–20 | text | — | — |
| `250` | SECONDARY SERIAL | 1–30 | text | — | — |
| `251` | SOURCE ENTITY | 1–30 | text | — | — |
| `254` | GLN EXTENSION | 1–20 | text | — | — |
| `400` | ORDER NUMBER | 1–30 | text | — | — |
| `401` | GINC | 1–30 | text | — | — |
| `402` | GSIN | 17 | digits | yes | — |
| `403` | ROUTE | 1–30 | text | — | — |
| `410` | SHIP TO GLN | 13 | digits | yes | — |
| `411` | BILL TO GLN | 13 | digits | yes | — |
| `412` | PURCHASE FROM GLN | 13 | digits | yes | — |
| `413` | SHIP FOR GLN | 13 | digits | yes | — |
| `414` | LOCATION GLN | 13 | digits | yes | — |
| `415` | INVOICE FROM GLN | 13 | digits | yes | — |
| `416` | PROD/SERV LOCATION GLN | 13 | digits | yes | — |
| `417` | PARTY GLN | 13 | digits | yes | — |
| `420` | POSTAL CODE | 1–20 | text | — | — |
| `421` | CC WITH ISO COUNTRY | 3–12 | text | — | — |
| `422` | ORIGIN COUNTRY | 3 | digits | — | — |
| `423` | INITIAL PROCESS COUNTRY | 3–15 | text | — | — |
| `424` | PROCESS COUNTRY | 3 | digits | — | — |
| `426` | FULL PROCESS COUNTRY | 3 | digits | — | — |
| `7001` | NSN | 13 | digits | yes | — |
| `7002` | MEAT CUT | 1–30 | text | — | — |
| `7003` | EXPIRY DATE/TIME | 10 | digits | — | — |
| `7004` | ACTIVE POTENCY | 1–4 | text | — | — |
| `7005` | CATCH AREA | 1–12 | text | — | — |
| `7006` | FIRST FREEZE DATE | 6 | digits | — | — |
| `7007` | HARVEST DATE | 6–12 | text | — | — |
| `7008` | AQUATIC SPECIES | 1–3 | text | — | — |
| `7009` | FISHING GEAR | 1–10 | text | — | — |
| `7010` | PRODUCTION METHOD | 1–2 | text | — | — |
| `7020` | REF PROCESSOR | 1–20 | text | — | yes |
| `7040` | UIC EXTENSION | 4 | digits | — | — |
| `710` | NHRN PZN | 1–20 | text | — | — |
| `711` | NHRN CIP | 1–20 | text | — | — |
| `712` | NHRN CN | 1–20 | text | — | — |
| `713` | NHRN DRN | 1–20 | text | — | — |
| `714` | NHRN AIM | 1–20 | text | — | — |
| `715` | NHRN NDC | 1–20 | text | — | — |
| `8001` | DIMENSIONS | 14 | digits | — | — |
| `8002` | CIV | 1–20 | text | — | — |
| `8003` | GRAI | 14–30 | text | — | — |
| `8004` | GIAI | 1–30 | text | — | — |
| `8005` | PRICE PER UNIT | 6 | digits | — | — |
| `8006` | ITIP | 18 | digits | yes | yes |
| `8007` | IBAN | 1–34 | text | — | — |
| `8008` | PROD TIME | 8–12 | text | — | — |
| `8010` | CPID | 1–30 | text | — | yes |
| `8011` | CPID SERIAL | 1–12 | text | — | — |
| `8012` | VERSION | 1–20 | text | — | — |
| `8013` | GMN | 1–30 | text | — | — |
| `8017` | GSRN PROVIDER | 18 | digits | yes | yes |
| `8018` | GSRN RECIPIENT | 18 | digits | yes | yes |
| `8019` | SRIN | 1–10 | text | — | — |
| `8020` | REFUND RECEIPT | 25 | digits | — | — |
| `8026` | ITIP CONTENT | 18 | digits | — | — |

`Barcode.ApplicationIdentifiers` reports the same table at run time — identifier, title, length
range, whether the value is numeric, whether it carries a check digit, and whether it is a Digital
Link primary key. The managed and native halves cannot drift apart because there is one table.

**The length rules, precisely.** A fixed-length identifier takes exactly its length. A variable one
takes everything up to the next field separator (ASCII 29, `GS`), and nothing else bounds it, so a
value that runs into the next identifier is refused by length rather than silently absorbed: a
batch number of twenty-one characters fails as `AI 10 (BATCH/LOT) expects 1 to 20 characters`.

**Check digits.** AI `00`, `01`, `02`, `402`, `410`–`417`, `7001`, `8006`, `8017` and `8018`
carry a GS1 mod-10 check digit in their last character and are verified. A GTIN that fails it is a
`BarcodeGs1Exception` before any symbol is written.

### 8.3 GS1-128

A GS1-128 is a Code 128 whose first symbol character is FNC1 and whose variable-length values are
followed by FNC1. The payload this package builds is exactly that:

```
FNC1 01 09501101530003 FNC1 10 LOT-42        (01 then 10: FNC1 closes the variable value)
FNC1 01 09501101530003 17 271231             (both fixed: no FNC1 in the middle at all)
```

`Barcode.EncodeGs1(data)` and `Barcode.Encode(BarcodeSymbology.Gs1_128, …)` both take a GS1 element
string. Three spellings are accepted, because the same data reaches a caller in all three:

| Form | Example | Where it comes from |
| --- | --- | --- |
| Raw | `0109501101530003\u001d10LOT-42` | A reader, or another system's field separator |
| With a symbol identifier | `]C10109501101530003\u001d10LOT-42` | A reader that names the symbology it read; the `]C1` is dropped |
| Printed | `(01)09501101530003(10)LOT-42` | A label, a spreadsheet, a person |

**Decoding.** The decoder runs with `AssumeGs1`, so the `]C1` identifier a reader puts in front of a
GS1-128 is understood, and every FNC1 after the first becomes ASCII 29 in the text. This package then
strips the `]C1` and reports `DecodeResult.Symbology == BarcodeSymbology.Gs1_128`, so
`DecodeResult.Text` is the raw element string — `Gs1Data.Parse` reads it directly. A Code 128 that
carries no FNC1 is untouched: it still decodes as `Code128`.

### 8.4 GS1 Digital Link

The URL form of the same elements: primary keys in the path, everything else in the query.

```
https://example.com/gs1/01/09501101530003?10=LOT%2042&17=271231
└──────── base ────────┘└ primary key ┘└────── attributes ──────┘
```

| Rule | Behaviour |
| --- | --- |
| Base | Absolute `http` or `https` URL, no query, no fragment. A path is allowed and prefixes the identifiers (`https://example.com/gs1/01/…`). |
| Path | One `/{ai}/{value}` pair per primary key, in the order the elements carry them. |
| Query | `ai=value`, joined with `&`, for every element that is not a primary key. |
| Primary keys | `00` SSCC, `01` GTIN, `8006` ITIP, `8010` CPID, `8017` GSRN provider, `8018` GSRN recipient. |
| Escaping | Everything outside the RFC 3986 unreserved set is percent-encoded over UTF-8 (`LOT 42` → `LOT%2042`). |
| Requires a key | Elements with no primary key cannot become a Digital Link and say so. |
| Parsing | The pairs start at the first path segment that names a known identifier, so a resolver prefix is allowed; every identifier, length and check digit is validated; a duplicate identifier is refused; a fragment is dropped as presentation. |

An unknown query parameter is a `BarcodeGs1Exception` naming the parameter, not a field silently
ignored: an identifier the table does not hold is a misunderstanding of the data, and swallowing it
is how a wrong label reaches a pallet.

### 8.5 What is not supported, and why

| Not supported | Reason |
| --- | --- |
| Writing GS1 DataBar Omnidirectional, GS1 DataBar Expanded, MaxiCode and Micro QR Code | `rxing` 0.9.4 has the readers and no writers. Reading works today; writing is refused with `BarcodeUnsupportedException` (`BT_ERR_UNSUPPORTED`), whose message names the symbology and the reason. |
| `DXFilmEdge` | The engine reads it; it is a film-edge timestamp, not a document barcode, so it is not in the enum. |
| Composite symbols, structured append, multi-symbol pages | A different data model and a different result shape (§1). |
| Identifiers outside the table | Including `253` GDTI and `255` GCN (optional suffixes), `310n`–`369n` measures, `723x` protocol identifiers, and the `90`–`99` internal range. An unknown identifier is refused by name, with the list of known ones in the message. |
| AI semantics | A date is a `YYMMDD` string. Nothing is parsed into `DateTime`, nothing is converted to a decimal, no unit is implied. |
| Digital Link beyond identification | No `linkType`, no compressed form, no canonical-host rules, and no network: this package builds and parses the URL, it does not resolve it. |
| GS1 check-digit computation on encode | A key is taken as given and verified. A caller holding twelve digits of a GTIN computes the thirteenth — or uses the value the printer already put on the label. |

### 8.6 The ABI additions

```c
BtBuffer bt_symbology_capabilities(void);   /* [{"name":"UPCA","encode":true}, …] */
BtBuffer bt_gs1_identifiers(void);          /* the AI table, with lengths and flags */
int32_t  bt_gs1_parse(const char *raw, BtBuffer *out_json, BtError **out_error);
int32_t  bt_gs1_digital_link(const char *raw, const char *base_url,
                             BtBuffer *out_url, BtError **out_error);
int32_t  bt_gs1_from_digital_link(const char *url, BtBuffer *out_json, BtError **out_error);
```

`bt_gs1_parse`, `bt_gs1_from_digital_link` and `bt_gs1_digital_link` return the data as JSON, the
same shape `bt_decode_points` uses:

```json
{"raw":"0109501101530003\u001d10LOT-42",
 "humanReadable":"(01)09501101530003(10)LOT-42",
 "elements":[{"ai":"01","title":"GTIN","value":"09501101530003","primaryKey":true}]}
```

Status `9`, `BT_ERR_GS1`, is added for a malformed GS1 element string or Digital Link. The existing
numbers keep their meanings (§4.5); a new value is an addition, never a reinterpretation.

## 9. Reading a page: several symbols, and a document that carries them

`Barcode.Decode` answers "what does this image say" with one symbol. A page of a delivery note or an
invoice says several things: one symbol per line item, a QR Code in the header and a Code 128 by each
article. This section is that case — every symbol on an image, each with the box it occupies — and
the recipe for the document it usually arrives in, a PDF.

### 9.1 The API

```csharp
DecodePage page = Barcode.DecodeAll(imageBytes);          // every symbol, in detection order
foreach (DecodeResult symbol in page.Symbols)
{
    Console.WriteLine($"{symbol.Symbology} {symbol.Text} at {symbol.Bounds}");
}
```

| Member | Contract |
| --- | --- |
| `Barcode.DecodeAll(byte[] image, BarcodeDecodeOptions? options = null)` | Returns `DecodePage`: every symbol the engine found, each with `Points` and `Bounds`. An image with no symbol is `Success == false`, `Symbols` empty and `Error` set — never an exception. |
| `DecodePage` | `Symbols`, `Count`, `Success`, `Width` and `Height` of the decoded image, `Error`. |
| `DecodeResult.Bounds` | `BarcodeBounds?` — `X`, `Y`, `Width`, `Height`, `Right`, `Bottom`, in pixels of the input image. `null` when the symbology's detector reports no points. |
| `BarcodeDecodeOptions` | `MaximumPixels` (default `64_000_000`), `MaximumBytes` (default 256 MiB). A value outside `1 … HardMaximumPixels` / `HardMaximumBytes` is an `ArgumentOutOfRangeException`. |
| `Barcode.Decode(byte[] png)` | Unchanged: the first symbol, one scanner pass, no search for the others. The fast path for a document that carries one code. |

`BarcodeBounds` is the extent of `DecodeResult.Points` and nothing more. A quiet zone is not included:
where the white margin ends is the image's business, and reporting it would be a number the detector
never measured. A caller that wants the quiet zone adds the symbology's own margin (§3.1) scaled by
its module size.

`DecodeAll` runs `rxing`'s `GenericMultipleBarcodeReader`, which is a different scanner from the one
`Decode` uses: it tries harder, crops and re-scans the regions it has not explained, and therefore
costs more. That is why the two entries exist instead of one. A caller reading five thousand invoices
with one code each should not pay for the multi-symbol search, and a caller reading a delivery note
should not get only the first item.

The order of `Symbols` is the order the engine found them, which is neither the reading order on the
page nor sorted. A caller that needs top-to-bottom order sorts on `Bounds.Y` itself; a caller that
needs one symbol per row groups on `Bounds.Y` rather than assuming an index. The engine drops a repeat
of a symbol it has already returned, so the same code is not reported twice.

### 9.2 From a PDF

The entry point takes image bytes, so a PDF is two steps: render the page, then decode it. The
renderer is `M3scalinum.Text.Pdf.PdfRaster` (§35), and the pixels it produces go straight in:

```csharp
PdfPageImage page = PdfRaster.Render(pdfBytes, pageNumber: 1);
DecodePage barcodes = Barcode.DecodeAll(page.Bytes);
```

**Why the dependency is not there.** `M3scalinum.Text.Barcodes` does not reference
`M3scalinum.Text.Pdf`, and that is a decision rather than an omission:

- `Text.Pdf` does not reference `Text.Barcodes`, so the reference would not create a cycle — it is
  possible, and it was rejected on weight.
- A `ProjectReference` becomes a NuGet dependency for every consumer of the barcode package. Measured
  on this tree: `M3scalinum.Text.Pdf.dll` is 356 KB of managed code, it brings
  `System.Security.Cryptography.Pkcs` and `System.IO.Hashing` with it, and its native renderer
  `sharp_text_pdf` is 6.4 MB per runtime identifier, against 3.9 MB for the barcode engine. A caller
  who wants to read a QR Code out of a screenshot would download a signing stack and a PDF renderer
  to do it.
- The barcode package is a leaf by design (§2): managed P/Invoke over one cdylib, no other package.
  That is what keeps its surface small and its `IsAotCompatible` claim cheap.
- Reading a barcode out of a PDF is the rarer half of the job. Most callers hand this package an
  image they already hold — a camera frame, a scan, a page another step rendered — and the same
  `DecodeAll` call serves both.

The cost of the composition is that the caller writes two lines and names two packages. The
alternative was to make every barcode consumer pay for a renderer and a cryptography stack it may
never call.

**The resolution.** `PdfRaster` renders at **150 DPI by default**, which is a preview: an A4 page is
1240×1754, and a 30 mm symbol is about 180 pixels wide — enough for a QR Code, marginal for a dense
Code 128. A caller decoding a document that was not produced for a screen asks for 300:

```csharp
PdfPageImage page = PdfRaster.Render(pdfBytes, pageNumber, new PdfRasterOptions { Dpi = 300 });
```

The two ceilings compose rather than fight: `PdfRasterOptions.DefaultMaximumPixels` is 50 megapixels
and `BarcodeDecodeOptions.DefaultMaximumPixels` is 64, so a page the renderer agreed to produce is one
the decoder will agree to read, and both byte ceilings are 256 MiB for the same reason. A caller who
lowers the raster ceiling need not touch the decode ceiling; a caller who raises the raster one past
64 megapixels must raise the decode one too, or the decode refuses what was just rendered — and says
so, naming the size and the cap.

Rendering a page and finding nothing on it is not an error at either step: an image without a symbol
is an empty `DecodePage`. A page number outside the document is `PdfRasterException` with
`Error == PageOutOfRange`; an encrypted document is `PdfRasterException` with `Error == Encrypted` and
a message that says to open it with `PdfEncryption` first. Both come from `Text.Pdf` unchanged,
because they are facts about the document rather than about the barcode.

### 9.3 The ceilings

Before this section the decoder had none: `image::load_from_memory` allocated whatever dimensions the
PNG declared, and `29-threat-model.md` said so — "no dimension or byte cap … **no test**". Both
ceilings now exist, and each is tested on either side of its boundary:

| Ceiling | Default | Checked | Refusal |
| --- | --- | --- | --- |
| Pixels | 64 000 000 | The PNG's `IHDR` chunk, before the decoder allocates, and again on the decoded image | `BT_ERR_LIMIT` → `BarcodeException` with `BarcodeErrorCode.Limit`, naming the size and the cap |
| Bytes | 256 MiB | The length of the bytes handed in, before they are parsed | Same |

The pixel ceiling is **inclusive**: an image of exactly `MaximumPixels` pixels decodes, and one pixel
past it is refused. The header is a claim by the sender, so the bound is applied twice — once to the
claim, which is what stops a gigapixel header from becoming an allocation, and once to what the
decoder actually produced.

`Barcode.Decode` applies the same ceilings with their default values. A caller with a reason to accept
a larger image passes `BarcodeDecodeOptions` to `DecodeAll`; the fast path takes no options because
its only knob would be one it could not honour differently.

### 9.4 What the decode does not do

| Not done | Why |
| --- | --- |
| Deskew, perspective correction, deblurring | The detectors find a symbol within their own rotation and scale tolerance, and that is the whole of it. A page scanned at an angle and a symbol photographed from the side are a preprocessing problem — `M3scalinum.Text.Ocr` has that pipeline for text, and this package does not reimplement it. |
| Inverted symbols | White modules on a black background are not read: `rxing` has no inversion pass and this package does not flip the image for it. A caller that knows its source inverts the pixels before the call. |
| Damaged or partly occluded symbols | A symbol the engine cannot reconstruct is a symbol not found: the page comes back empty, with the engine's reason. There is no partial read and no best guess. |
| Several pages at once | The entry point is one image. A caller loops over pages, and `PdfRaster.PageCount` says how many there are. Rendering every page of a document to look for a symbol is a decision about cost, not a parser bound. |
| Structured append and composite symbols | Two symbols that share one payload are a different data model (§8.5). Two symbols on a page are two independent results, which is what this section returns. |
| Reading colour as a signal | Luminance only, as in §1: a red-on-white symbol reads if its luminance separates, and a pair of colours that do not is not a barcode. |

### 9.5 The ABI additions

```c
BtDecodePage* bt_decode_image(const uint8_t *data, size_t len,
                              uint64_t maximum_pixels, size_t maximum_bytes,
                              BtError **out_error);
uint32_t      bt_decode_page_count(const BtDecodePage *page);
uint32_t      bt_decode_page_width(const BtDecodePage *page);
uint32_t      bt_decode_page_height(const BtDecodePage *page);
int32_t       bt_decode_page_error(const BtDecodePage *page, BtBuffer *out_message, BtError **out_error);
BtDecode*     bt_decode_page_symbol(const BtDecodePage *page, uint32_t index, BtError **out_error);
void          bt_decode_page_free(BtDecodePage *page);
uint32_t      bt_decode_page_live_count(void);
int32_t       bt_decode_box(const BtDecode *decoded, BtBuffer *out_box, BtError **out_error);
```

A ceiling of `0` means the library default, so a caller with no opinion passes zeroes. A page handle is
counted like the other two, and `bt_decode_page_symbol` hands out a decode handle of its own — a copy,
so the page can be freed first — which is why the existing `bt_decode_*` readers work on it unchanged.
An `index` past the count is `BT_ERR_INVALID_ARGUMENT` naming the count, never a silent null.

`bt_decode_box` returns `{"x":12.5,"y":30,"width":80,"height":80}` in the same JSON shape
`bt_decode_points` uses, and an empty buffer when the detector reported no points.

## 10. Printable output: DPI, quiet zone and a document figure

A PNG at `scale` pixels per module says nothing about paper. The same file is a two centimetre
sticker or an eight centimetre label depending on what the consumer does with it, and no scanner can
tell from the bytes whether the module is one that the specification allows. §3 answered "how many
modules"; this section answers "how many millimetres", and puts the symbol in a document without the
caller touching a temporary file.

### 10.1 The two entry points

```csharp
BarcodePrint print = Barcode.Print(
    BarcodeSymbology.QrCode,
    "https://example.test/42",
    new BarcodePrintOptions { Dpi = 300, WidthMillimetres = 30 });

Console.WriteLine($"{print.WidthMillimetres} × {print.HeightMillimetres} mm, module {print.ModuleMillimetres} mm");
// 29.464 × 29.464 mm, module 1.016 mm

var document = TypstDocument.Create();
BarcodeFigure figure = Barcode.AddToDocument(
    document, BarcodeSymbology.Code128, "SN-000042",
    new BarcodePrintOptions { WidthMillimetres = 50 });
```

`Barcode.Encode` is unchanged: it keeps its historic quiet zones (§3.1) and returns the module matrix,
the SVG and the PNG at `Scale`. A caller who already sizes the symbol in a layout keeps that behaviour
and pays nothing for this section.

### 10.2 `BarcodePrintOptions`

| Member | Contract |
| --- | --- |
| `Dpi` | Output resolution, 300 by default; accepted range 72 … 2400. |
| `WidthMillimetres` | Width of the whole symbol, quiet zone included. Exclusive with `ModuleMillimetres`. |
| `ModuleMillimetres` | The X-dimension: the printed size of one module. Exclusive with `WidthMillimetres`. |
| — | With neither set, the module takes the symbology's minimum (§10.4). One width cannot fit every symbology: a Code 128 carries a hundred modules where a QR Code carries twenty-one, so the default is a floor, not a size. |
| `WidthModules` | The width the caller's layout reserves, in modules. Optional. A layout too narrow for the symbol plus its quiet zone is refused, never cropped. |
| `QuietZone` | Modules on each side; `null` takes the normative minimum (§10.3). Below the minimum is refused, above is accepted up to 64 modules. |
| `BarHeight` | Bar height in modules for a one-dimensional symbol; 40 by default. |
| `ErrorCorrection` | QR Code only, as in `BarcodeOptions`. |

`BarcodePrint` carries the answer: `Png`, `Dpi`, `ModuleMillimetres`, `ModulePixels`,
`QuietZone`, `WidthModules`, `HeightModules`, `WidthMillimetres`, `HeightMillimetres`,
`WidthPixels`, `HeightPixels`, and `AspectRatio`.

### 10.3 The quiet zone floor, per symbology

A print target renders at the floor below or wider. The floor is applied by padding the module
matrix in `encode.rs` — not by asking `rxing` for a margin: the DataMatrix writer ignores the margin
hint completely, so a printed DataMatrix came out with no quiet zone at all before this section.
Padding after encoding is the same arithmetic for every symbology and cannot be skipped by a writer
that forgets. A one-dimensional symbol is padded horizontally only; a two-dimensional one on all
four sides.

| Symbology | Floor (modules/side) | Source |
| --- | --- | --- |
| QR Code | 4 | ISO/IEC 18004:2015, quiet zone of four modules |
| Micro QR | 2 | ISO/IEC 18004:2015 |
| DataMatrix | 1 | ISO/IEC 16022:2006; the GS1 DataMatrix Guideline states the quiet zone is one X on each side |
| Aztec | 1 | ISO/IEC 24778:2024 requires none to decode; one module is kept as a print margin, which is this library's choice, not the norm's |
| PDF417 | 2 | ISO/IEC 15438:2015, 2X on each side |
| Code 128, GS1-128 | 10 | ISO/IEC 15417:2007, 10X on each side |
| Code 39 | 10 | ISO/IEC 16388:2007 |
| Code 93 | 10 | AIM USS-93 |
| ITF | 10 | ISO/IEC 16390:2007 |
| Codabar | 10 | AIM USS-Codabar |
| Telepen | 10 | the Telepen symbology specification |
| EAN-13 | 11 | ISO/IEC 15420:2009, eleven modules left and seven right |
| EAN-8 | 7 | ISO/IEC 15420:2009 |
| UPC-A, UPC-E | 9 | GS1 General Specifications |
| GS1 DataBar (Omnidirectional, Expanded) | 1 | GS1 General Specifications |
| MaxiCode | 1 | ISO/IEC 16023 |

EAN-13 and the UPC family are specified asymmetrically. The renderer pads symmetrically at the
**larger** side (EAN-13: 11 on both sides, not 11 and 7), because a symbol wider than the norm is
always readable while one narrower is not, and because a single `quiet_zone` per side is the shape
the matrix renderer has. The extra white on the narrow side costs paper, not compliance.

### 10.4 The module floor, per family

| Family | Floor (mm) | Source |
| --- | --- | --- |
| One-dimensional (Code 128, Code 39, Code 93, ITF, Codabar, Telepen, EAN-13, EAN-8, UPC-A, UPC-E, GS1-128, GS1 DataBar) | 0.25 | ISO 15394:2009 §6.1.3: the narrow element shall not be less than 0.25 mm for Code 39 and Code 128. The GS1 General Specifications set the same absolute floor for the GS1 members; their 0.495 mm is the nominal for general distribution, not the floor. |
| Two-dimensional (QR Code, Micro QR, DataMatrix, Aztec, PDF417, MaxiCode) | 0.40 | GS1 General Specifications, symbol specification table 1: the minimum X for a GS1 DataMatrix at retail point of sale is 0.396 mm, rounded up here to 0.40 mm |

The floor is enforced on the **printed** module, after pixel snapping, not on the number asked for:
a request for 0.30 mm that the raster can only realise at 0.25 mm is accepted and reported as
0.25 mm; one that would realise below the floor is refused.

### 10.5 The three refusals

| Refused | Message names | Why |
| --- | --- | --- |
| A quiet zone below the symbology's floor | the specification and the floor | A symbol a reader cannot find is a failed print job, and cropping is silent. |
| A module below the family floor | the family and the millimetre floor | The same reason, measured in millimetres. |
| A reserved width (`WidthModules`) too narrow for the symbol plus its quiet zone | the reserved width, the required width and the quiet zone | The zone is not cropped to fit a layout. |
| A quiet zone past 64 modules | the ceiling | A quiet zone is white space; past that the caller is sizing a page, usually a millimetre/module mix-up. |

`Barcode.MinimumQuietZone(symbology)` and `Barcode.MinimumModuleSize(symbology)` report the floors.
Both are read from the native capability report — the table a caller sees and the table the renderer
enforces are one table, reported by `bt_symbology_capabilities`.

### 10.6 How the geometry is computed

1. The quiet zone is resolved (`null` → floor), the symbol is encoded, the matrix is padded.
2. Pixels per module is an integer: `floor(width_mm ÷ 25.4 × dpi ÷ modules)` for a width target, so
   the printed symbol never exceeds the width asked for; `round(module_mm ÷ 25.4 × dpi)` for a module
   target. Zero pixels per module and more than 64 are refused.
3. The real module is `module_pixels × 25.4 ÷ dpi`, and every millimetre figure is that number times
   a module count — the raster's size, not the request's. The 100-megapixel raster ceiling of §4.3
   still applies.

### 10.7 A barcode in a document

```csharp
var document = TypstDocument.Create();
document.Heading(1, "Delivery note");

var figure = Barcode.AddToDocument(
    document, BarcodeSymbology.QrCode, "https://example.test/dn/42");

// …compile:
var builder = new TypstWorldBuilder().AddSource("main.typ", document.ToMarkup());
builder.AddAsset(figure.AssetName, figure.Png);
```

The figure is in the Typst source as `#figure(image("barcode-qrcode-5a3f41d7.png", width: …mm), alt: "QRCode barcode: …")`.
The alternative text defaults to the symbology and the payload — the text a screen reader needs in
place of the image — and `altText:` replaces it. The asset name is deterministic (symbology, payload,
resolution and module size hashed), so two builds of the same report refer to the same asset.

`Barcode.AddGs1ToDocument(document, gs1Data, …)` is the same call for a GS1-128, with the printed
form `(01)…(10)…` as the default alternative text.

**Why the PNG comes back on the result.** The document model (`M3scalinum.Text.Documents`) is
write-only Typst source and refers to images by virtual path; it carries no bytes, and the dependency
direction rules out changing that from here — `Text.Documents` must not learn about barcodes. So the
method lives in `Text.Barcodes` and takes a `TypstDocument`, adds the figure, and hands the raster
back as `BarcodeFigure.Png` under `BarcodeFigure.AssetName`. The caller registers one asset with the
world builder it was going to build anyway; no temporary file, no manual encoding, no byte array
assembled by hand.

**What is still the caller's:**

- registering the asset (`builder.AddAsset(figure.AssetName, figure.Png)`);
- the paper: format, orientation and page margins (`TypstDocument.Page`);
- the space around the figure, including the margin between the quiet zone and any text or border —
  the quiet zone is part of the image, but "keep 5 mm clear" is a layout decision;
- the human-readable line under a one-dimensional symbol, which this package does not draw.

### 10.8 The ABI addition

```c
int32_t bt_barcode_print(const char *symbology, const char *payload,
                         int32_t quiet_zone, int32_t bar_height,
                         const char *error_correction,
                         double dpi, double width_mm, double module_mm,
                         uint32_t reserved_modules,
                         BtBuffer *out_png, BtBuffer *out_json, BtError **out_error);
```

`quiet_zone` below zero takes the symbology floor, and zero is an explicit zero — refused.
`width_mm` or `module_mm` above zero sizes the symbol (setting both is an error); `reserved_modules`
zero leaves the layout check out. The geometry JSON is
`{"dpi":300,"moduleMm":1.016,"modulePixels":12,"quietZone":4,"widthModules":29,"heightModules":29,"widthMm":29.464,"heightMm":29.464,"widthPixels":348,"heightPixels":348}`.
`bt_symbology_capabilities` gained `quietZone` and `minimumModuleMm` on each entry.

### 10.9 Tests

| Layer | What is asserted |
| --- | --- |
| Rust, `print.rs` | The floor is applied per family (QR, DataMatrix, Aztec, PDF417, Code 128, EAN-13, EAN-8, UPC-A, Code 39, ITF, Codabar); a quiet zone below the floor and one past the ceiling are refused; a width target rounds down and a module target rounds to whole pixels; a module below the family floor is refused; a reserved width one module short is refused and the exact width is accepted; less than one pixel per module, more than 64, both sizing options and an out-of-range DPI are refused; the PNG is exactly the geometry's pixels. |
| Rust, `lib.rs` | `bt_barcode_print` returns the PNG and the geometry, and refuses a three-module QR quiet zone. |
| C# | `Print` reports the size it really prints, the raster matches, the symbol decodes back; the floors are the specification's; the refusals surface as `BarcodeException` with the specification named; `AddToDocument` emits a figure whose markup carries an `image(…)` and an `alt:`; the default alternative text is the symbology and the payload; the asset name is deterministic and payload-specific; a GS1 symbol uses the human-readable form; two symbols land as two figures. |

## 11. A styled symbol: colour, contrast and a reserved centre

A QR Code that leaves the logistics world stops being a black square: it carries a brand colour and
a mark in the middle. Both are appearance, not data, so nothing here touches the module matrix the
decoder reads — the encoder's matrix stays the truth and the renderer paints it. That separation is
what makes the two rules below stateable, and testable, at all.

### 11.1 The options

`BarcodeOptions` and `BarcodePrintOptions` gain the same three properties:

| Property | Type | Default |
| --- | --- | --- |
| `Foreground` | `BarcodeColor?` | `null`, black |
| `Background` | `BarcodeColor?` | `null`, white |
| `Logo` | `BarcodeLogo?` | `null`, nothing reserved |

`BarcodeColor` is a `readonly record struct` of three `byte`s — sRGB, the space WCAG defines its
coefficients in, so no conversion happens anywhere between the caller's bytes and the raster.
`BarcodeColor.FromHex("#1f3a93")` and `ToHex()` are the text form. `BarcodeLogo` is
`{ double Fraction, BarcodeColor? Fill }` with two factories, `BarcodeLogo.Cleared(fraction)` for a
region cleared to the background and `BarcodeLogo.Solid(fraction, colour)` for a flat plate.

Nothing is a flag: an option that is not set is not rendered, the default raster is the same
greyscale PNG byte for byte, and naming the defaults explicitly gives the same bytes back —
`The_default_rendering_is_unchanged_by_naming_the_default_colours` asserts it.

### 11.2 The contrast rule, and its source

The module must stay **darker** than the background — a QR Code is dark modules on a light ground
(ISO/IEC 18004), so white on black is refused even at 21:1 — and the pair must clear a ratio of
**4.5:1**. The ratio is the WCAG 2.1 one, `(L1 + 0.05) / (L2 + 0.05)` with the lighter relative
luminance on top, and each luminance is computed by the definition from the sRGB bytes: linearise
with the 0.03928 threshold, then weight 0.2126, 0.7152 and 0.0722 (the ITU-R BT.709 coefficients).

The floor is 4.5:1, WCAG 2.1 success criterion **1.4.5 Contrast (Minimum)** — the criterion written
for text — and not the 3:1 of **1.4.11 Non-text Contrast**. A module is a few tenths of a millimetre
wide when printed and is read through a camera lens at an angle; the weaker graphic floor is not
enough for that. It is a floor, not a target: black on white measures 21:1 and is what most symbols
should use.

Refusals carry the number they measured, because the caller's next move depends on it:

```
the module colour #9e9e9e on the background #ffffff measures a contrast of 2.68:1,
below the 4.5:1 floor (WCAG 2.1 SC 1.4.5, Contrast (Minimum)): darken the module or
lighten the background
```

`Barcode.ContrastRatio(a, b)` and `Barcode.MinimumContrastRatio` are the same arithmetic, public, so
a caller can check a palette before it encodes anything.

### 11.3 The reserved centre

`Fraction` is the share of the symbol's **area** the square covers, between 0 and 1; its side is the
square root of that share, rounded to whole modules so the region aligns with the module grid. It is
centred on the symbol, quiet zone excluded, and its colour is `Fill` or the background.

Three bounds apply, and the lowest wins:

1. **The level's capacity.** The nominal shares — 7 % at L, 15 % at M, 25 % at Q, 30 % at H — are the
   error correction capacities of ISO/IEC 18004 Table 9, which is where every QR table gets them. A
   share past the level's own figure is refused before anything is rendered.
2. **The timing patterns.** Both timing lines sit on module 6 of the symbol, so a reserved centre can
   start no earlier than module 7 and end no later than `side - 8`: at most `side - 14` modules,
   which is `(side - 14)² / side²` of the area. A 21-module symbol — version 1 — therefore holds
   about 11 %, and a share the symbol has no room for is refused with the room it does have.
3. **The read-back.** The raster is decoded with this build's own engine before it is returned, and a
   style whose payload does not come out is refused:

   ```
   a 25-module reserved centre leaves a symbol this package's own decoder cannot read
   back at error correction H: the level's nominal capacity is a per-codeword figure,
   and a rectangle also destroys every codeword it only partly covers, so the share it
   really tolerates is lower. Use a smaller logo, a longer payload or a higher level
   ```

   This is the third bound because the first is a promise about *codewords* and a rectangle spends
   more than its area: every codeword the square only partly covers is lost too, so the effective
   damage is the area plus a perimeter term, and the perimeter term matters most on small symbols.
   The nominal figure is a ceiling, in other words, not a budget.

**Measured.** The largest share that read back, on symbols from 21 to 57 modules, with the engine's
own reader: about **5 % at L, 10 % at M, 15 % at Q and 20 % at H**, and a light fill costs a couple of
points against a dark one of the same size (a cleared square at 20 % on a 45-module symbol does not
read back; a brand plate of the same 20 % does). **30 % at H did not read back at any of the eleven
symbol sizes measured** — 21, 25, 29, 33, 37, 41, 45, 49, 53 and 57 modules — so the shipped
behaviour is the nominal ceiling plus the read-back, and a 30 % request at H is refused with the
message above rather than rendered into a symbol a scanner misses. That gap between "the level
repairs 30 % of the codewords" and "a 30 % square still reads" is the reason the read-back exists.

The region is a *reservation*: this package clears it, and the caller's mark is composited in the
caller's own layout, or over the raster. Nothing is blended here.

### 11.4 What the style does not do

- **No gradients, no background image, no per-module colour.** Two colours and one flat rectangle.
  A gradient spends contrast unevenly, and no single ratio describes it.
- **No error correction on read.** The level is a property of the symbol, set when it is written; a
  reader is told nothing and repairs what it finds. A logo spends that capacity, it does not add to
  it.
- **No image compositing.** `LogoFill` is a colour. A photograph or a logotype with counters is the
  caller's to draw over the reserved region — and the SVG carries the same flat rectangle, so the
  vector and the raster cannot disagree.
- **No resampling, ever.** The raster scales by an integer number of pixels per module and the vector
  is one unit per module. Scaling a styled symbol after the fact blurs its module edges, which is the
  one thing no contrast rule can undo.
- **No logo on a one-dimensional symbol, a DataMatrix, an Aztec or a PDF417.** A Code 128 has no
  centre to spare and no level to spend: clearing anything from it destroys data nothing repairs.
  The call is refused and names the symbology.

### 11.5 The ABI additions

```c
int32_t bt_barcode_png_styled(const BtBarcode *barcode, uint32_t scale,
                              int32_t foreground, int32_t background,
                              double logo_fraction, int32_t logo_fill,
                              BtBuffer *out_png, BtError **out_error);

int32_t bt_barcode_svg_styled(const BtBarcode *barcode,
                              int32_t foreground, int32_t background,
                              double logo_fraction, int32_t logo_fill,
                              BtBuffer *out_svg, BtError **out_error);

int32_t bt_barcode_print_styled(const char *symbology, const char *payload,
                                int32_t quiet_zone, int32_t bar_height,
                                const char *error_correction,
                                double dpi, double width_mm, double module_mm,
                                uint32_t reserved_modules,
                                int32_t foreground, int32_t background,
                                double logo_fraction, int32_t logo_fill,
                                BtBuffer *out_png, BtBuffer *out_json, BtError **out_error);
```

Colours are `0xRRGGBB`, and a negative value takes the default (black for the module, white for the
background); `logo_fraction` of zero or less is no logo; a negative `logo_fill` paints the reserved
centre in the background colour. The level the logo bounds are read from is the one the handle was
encoded with, so `bt_barcode_encode` did not change and neither did `bt_barcode_png` — the plain path
is the same call it always was.

### 11.6 Tests

| Layer | What is asserted |
| --- | --- |
| Rust, `style.rs` | Black on white is 21:1 and #767676 on white clears 4.5:1 while #777777 does not; a dark brand blue is accepted and a pale module is refused with "2.68" and "4.5" in the message; a background darker than the module is refused even at 21:1; the level ceiling is 7/15/25/30 % and a share past it is refused with both numbers and the ISO reference; a logo on Code 128 is refused by name; the symbol box is the quiet-zone-free square; a centred 30 % region is centred, inside the ceiling and clear of both timing lines; a share a 21-module symbol cannot hold is refused with the seven modules it has; the same 19-module square reads back one module after the timing line and not on it, which is the measurement the guard exists for; the default style is the plain raster, byte for byte; a coloured raster carries exactly its three colours and its reserved centre; the coloured SVG carries the fills and one reserved rect. |
| Rust, `tests/styled.rs` | A logo reads back through `decode_png_all` at L, M, Q and H; 20 % reads back at H on a 45-module symbol; 30 % at H is refused with "cannot read back"; 30 % at Q is refused with "exceeds the 25% ceiling"; a brand module on cream renders in those colours and decodes; refusals for contrast, inversion, and the styled SVG. |
| Rust, `print.rs`, `lib.rs` | A styled print keeps its geometry and reads back; the styled ABI entry points write a PNG this build decodes and an SVG carrying the fill; a pale module is `BT_ERR_INVALID_ARGUMENT` across the ABI. |
| C# | A brand module on a light ground renders as a truecolour PNG and decodes with `DecodeAll`; the default rendering is unchanged byte for byte; a logo reads back at L, M, Q and H and at 20 % on H; a 30 % logo at H and 30 % at Q are refused with their messages; a pale module and an inverted pair are refused; a logo on Code 128 is a `BarcodeUnsupportedException`; the ratio is the WCAG one and the floor is 4.5; a styled `Print` and a styled `AddToDocument` figure decode back; `FromHex`/`ToHex` round trip and reject malformed text. |

## 12. MaxiCode and Aztec Rune: what the engine actually holds

Two symbologies were on the board, and the answer to both is a fact about `rxing` 0.9.4 rather than
a preference of this package. The list stays at nineteen: §8.1 is the whole of it, and nothing in
this section adds a member.

### 12.1 MaxiCode: read, not written

`rxing` 0.9.4 has `BarcodeFormat::MAXICODE`, a detector (`src/maxicode/detector.rs`) and a reader
(`src/maxicode/maxi_code_reader.rs`). It has no writer: `MultiFormatWriter::encode_with_hints` in
`src/multi_format_writer.rs` matches fifteen formats and falls through to
`"No encoder available for format …"` for the rest, and `MAXICODE` is in that rest.

So MaxiCode is **read only** here, and that is not new: `BarcodeSymbology.MaxiCode` has been in the
enum since §8.1 was written, `Barcode.CanEncode` returns `false` for it, and the refusal an encode
raises names the symbology instead of failing somewhere inside the engine. Nothing was added for
this item — the coverage was already the crate's coverage, and this section records the evidence.

### 12.2 Aztec Rune: absent from the crate, so absent from the package

Aztec Rune is the seventy-seven-module Aztec symbol that carries one byte: same bullseye, no mode
message, no error correction. It is a different symbol from an Aztec code, not a mode of one.

`rxing` 0.9.4 does not know it. Three checks, in the crate's own source:

- `BarcodeFormat` (`src/barcode_format.rs`) declares twenty-one variants and none of them is
  rune-shaped: `AZTEC`, `CODABAR`, `CODE_39`, `CODE_93`, `CODE_128`, `DATA_MATRIX`, `EAN_8`,
  `EAN_13`, `ITF`, `MAXICODE`, `PDF_417`, `QR_CODE`, `MICRO_QR_CODE`,
  `RECTANGULAR_MICRO_QR_CODE`, `RSS_14`, `RSS_EXPANDED`, `TELEPEN`, `UPC_A`, `UPC_E`,
  `UPC_EAN_EXTENSION`, `DXFilmEdge`. There is no `AZTEC_RUNE` and no alias that parses to one.
- The string `Rune` does not occur anywhere under `src/`: no reader, no writer, no constant.
- `src/aztec/` holds `aztec_writer.rs`, whose encoder always writes a full Aztec symbol with an
  error-correction level. A caller who asked for a rune and got one of those would have a symbol
  that means something else.

**The refusal.** `Symbology::parse` accepts the nineteen names of §8.1 and refuses everything else
by name, listing what it does accept. `AztecRune`, `Aztec Rune` and `Rune` therefore fail as
`BT_ERR_UNSUPPORTED` — the same path an unknown name takes, because it is an unknown name. A rune
the engine cannot write is not a symbology this package can pretend to have; the alternative,
quietly writing a full Aztec symbol, would put a symbol on a label that a rune reader cannot read.

`aztec_rune_is_refused_because_the_engine_has_no_such_format` in `symbology.rs` asserts exactly
that, for all three spellings.

**What would change the answer.** A crate version that declares the format, or a Rust encoder for
the rune added to `src/` behind a new member of `Symbology`. The guard below is what makes that a
deliberate change rather than a forgotten one.

### 12.3 The test that keeps the list and the table one list

`SymbologyCoverageTests` in `tests/Text.Barcodes.Tests` reads §8.1 out of this file, takes the ABI
name from every row, and compares the set with `Enum.GetValues<BarcodeSymbology>()`:

- a member the table does not document fails the test with the member named;
- a row the enum does not declare fails it with the row named;
- every row must say the symbology is readable, because that is what the table means;
- the Write column is compared with `Barcode.CanEncode`, so a symbology whose direction changes in
  the engine cannot stay documented the old way.

It enumerates both sides rather than comparing counts, so the next symbology added — or removed —
fails it on the day it is added, whatever its direction.

