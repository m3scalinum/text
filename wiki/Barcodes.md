# Barcodes

`M3scalinum.Text.Barcodes` writes and reads barcodes in-process: a QR Code on an invoice, a
DataMatrix on a label, a Code 128 on a shipping note. No external tool, no service, no temporary
file — the engine ships inside the package.

```csharp
using M3scalinum.Text.Barcodes;

BarcodeImage image = Barcode.Encode(BarcodeSymbology.QrCode, "BT-1=FA-2026-000123");

byte[] png = image.Png;     // ready to embed in a PDF page
string svg = image.Svg;     // ready to hand to a layout engine
int width = image.ModuleWidth;
```

and back:

```csharp
DecodeResult decoded = Barcode.Decode(png);

if (decoded.Success)
{
    Console.WriteLine($"{decoded.Symbology}: {decoded.Text}");
}
else
{
    Console.WriteLine($"no barcode here: {decoded.Error}");
}
```

## What it writes

Ten symbologies, each covered by an encode→PNG→decode round trip in the test suite.

| Symbology | Good for | Payload it accepts |
| --- | --- | --- |
| `QrCode` | anything, from a URL to an invoice header | any text; UTF-8 supported |
| `DataMatrix` | labels, GS1, small parts | any text |
| `Aztec` | dense payloads on small labels | any text |
| `Pdf417` | identity documents, boarding passes | any text |
| `Code128` | serial numbers, tracking references | full ASCII |
| `Code39` | the barcode industry has used longest | ASCII, uppercase-friendly |
| `Ean13` | retail | exactly 13 digits, check digit included |
| `Ean8` | small retail | exactly 8 digits |
| `Itf` | cartons, outer cases | digits, an even number |
| `Codabar` | libraries, blood banks, old systems | digits framed by `A`…`B` |

`Barcode.SupportedSymbologies` reads the list from the loaded native library, so a machine running an
older native half reports a shorter list rather than failing at the first encode.

## Two renderings of one symbol

`BarcodeImage` carries the module matrix and both renderings of it, produced from the same matrix, so
they cannot disagree about where a dark module sits.

**SVG** is emitted at one unit per module, with the `viewBox` set to the matrix. A layout engine
scales it to whatever physical size the page needs, and nothing is ever resampled: a bar edge stays a
bar edge. This is the rendering to use in a document.

**PNG** is the same matrix at an integer `Scale` (pixels per module, four by default), as an 8-bit
greyscale image of pure black and white pixels. For a preview, a web page, or a caller that has a
raster scanner.

```csharp
var options = new BarcodeOptions
{
    Scale = 8,          // 8 pixels per module in the PNG
    QuietZone = 6,      // modules of white on each side
    BarHeight = 60,     // one-dimensional symbols only, in modules
    ErrorCorrection = BarcodeErrorCorrection.High,   // QR Code only
};
```

## Reading one back

`Barcode.Decode(byte[] png)` returns the text, the symbology and the points the detector found. It
reads the same ten symbologies, and also the formats the engine knows but this package does not
write — a UPC-A comes back with `Symbology` null and `Format` naming the format.

A **readable image with no barcode is not an exception**: it is `Success == false` with `Error` set to
why. Scanning a stack of invoices should not throw once per blank page. Only bytes that are not a PNG
at all raise `BarcodeImageException`.

## What is refused, and how

| What | What you get |
| --- | --- |
| `Ean13` with twelve digits | `BarcodeFormatException`: thirteen digits are the whole code, check digit included. The engine would happily compute the check digit; this package refuses instead of guessing. |
| A character the symbology has no encoding for | `BarcodeFormatException` naming the symbology. |
| An empty payload, or one with a NUL | `BarcodeException`, `InvalidArgument`. |
| More than 4 096 bytes | `BarcodeException`, `Limit`. |
| Bytes that are not a PNG | `BarcodeImageException`. |
| An error correction level on a symbology that has none | `BarcodeException`, `InvalidArgument`: "Q" means nothing to a Code 128. |
| A page with no barcode | `DecodeResult.Success == false`. |

## One limitation worth knowing

An **Aztec** symbol needs at least two pixels per module to be read back, by any decoder of this
family. `Scale = 1` is accepted and produces a correct symbol that this package cannot read again;
the default is four. Every other symbology round-trips at one pixel per module.

## Shipping and licensing

The package carries its engine as a native library per runtime identifier — `win-x64`, `win-arm64`,
`linux-x64`, `linux-arm64`, `linux-musl-x64`, `osx` — under `runtimes/<rid>/native/`, the same layout
`M3scalinum.Text.Typst` uses. `BARCODES_NATIVE_PATH` points a build at a locally compiled library
instead.

The engine is [rxing](https://crates.io/crates/rxing) 0.9.4, a Rust port of ZXing, licensed
**Apache-2.0** — the licence of this repository, so nothing extra is required of a consumer. Its
notice is part of `THIRD-PARTY-NOTICES.md`, and the reasoning behind the choice (and why not a .NET
barcode library) is in Spec 20 §5.

`BarcodeEnvironment.EngineVersion` reports what is loaded — `rxing 0.9.4` — which is the first thing
to include in a bug report about a symbol that does not decode.
