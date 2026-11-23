# Barcodes

`M3scalinum.Text.Barcodes` writes and reads barcodes in-process: a QR Code on an invoice, a
DataMatrix on a label, a GS1-128 on a shipping note. No external tool, no service, no temporary
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

Nineteen symbologies in the closed set `BarcodeSymbology`. Fifteen of them are both written and read;
four are read only in this build because the engine has no writer for them, and `Barcode.CanEncode`
says which is which before a caller tries.

| Symbology | Direction | Good for | Payload |
| --- | --- | --- | --- |
| `QrCode` | write, read | anything, from a URL to an invoice header | any text; UTF-8 supported |
| `DataMatrix` | write, read | labels, GS1, small parts | any text |
| `Aztec` | write, read | dense payloads on small labels | any text |
| `Pdf417` | write, read | identity documents, boarding passes | any text |
| `Code128` | write, read | serial numbers, tracking references | full ASCII |
| `Code39` | write, read | the symbology industry has used longest | ASCII, uppercase-friendly |
| `Code93` | write, read | full ASCII, denser than Code 39 | ASCII |
| `Telepen` | write, read | full ASCII, sixteen modules per character | ASCII |
| `Ean13` | write, read | retail | exactly 13 digits, check digit included |
| `Ean8` | write, read | small retail | exactly 8 digits |
| `UpcA` | write, read | North American retail | exactly 12 digits |
| `UpcE` | write, read | small North American retail | 8 digits, number system 0 or 1 |
| `Itf` | write, read | cartons, outer cases | digits, an even number |
| `Codabar` | write, read | libraries, blood banks, old systems | digits framed by `A`…`B` |
| `Gs1_128` | write, read | the GS1 element string (see below) | a `Gs1Data` element string |
| `DataBarOmnidirectional` | read only | GS1 retail marks | — |
| `DataBarExpanded` | read only | GS1 variable-measure marks | — |
| `MaxiCode` | read only | parcel sortation | — |
| `MicroQr` | read only | tiny marks, and its rectangular variant | — |

`Barcode.SupportedSymbologies` reads the list from the loaded native library, so a machine running an
older native half reports a shorter list rather than failing at the first encode.

## GS1

GS1 data is a string of elements — each one an Application Identifier and its value — and the
symbology carries that string without carrying the structure. `Gs1Data` is the structure:

```csharp
Gs1Data data = Gs1Data.Parse("(01)09501101530003(10)LOT-42");

data.HumanReadable;    // (01)09501101530003(10)LOT-42
data.Raw;              // 010950110153000310LOT-42, with ASCII 29 after a variable-length value
data.Elements;         // one Gs1Element per AI, each with its Ai and Value

BarcodeImage symbol = Barcode.EncodeGs1(data);
```

`Parse` accepts the raw form a reader returns (with ASCII 29 field separators), the same form with a
symbol identifier in front (`]C1…`), and the printed `(01)…(10)…` form. An identifier this build does
not know, a value outside the length its identifier allows, or a check digit that does not match is a
`BarcodeGs1Exception` — nothing is guessed.

`Barcode.ApplicationIdentifiers` lists the identifiers this build parses, each with its title, its
minimum and maximum length, whether its value is numeric, whether it ends in a check digit, and
whether it is a Digital Link primary key. The list is read from the native table that enforces it.

The same elements also serialise as a GS1 Digital Link URL:

```csharp
string url = data.ToDigitalLink("https://example.com/01");
Gs1Data back = Gs1Data.FromDigitalLink(url);
```

Primary keys go in the URL's path, every other element as a query parameter; a base URL that is not
absolute, or elements with no primary key to identify the item, is refused.

## Putting one in a document

`Barcode.AddToDocument` encodes and adds a figure to a `TypstDocument` in one call, with an
alternative description a screen reader can read:

```csharp
BarcodeFigure figure = Barcode.AddToDocument(
    document,
    BarcodeSymbology.QrCode,
    "https://example.com/invoice/FA-2026-0042");

worldBuilder.AddAsset(figure.AssetName, figure.Png);   // register the raster under the virtual path
```

The document model is write-only Typst source and refers to images by virtual path, so the raster
cannot travel inside it. It comes back on the result instead: register `BarcodeFigure.Png` under
`BarcodeFigure.AssetName` with the world builder that compiles the document. The figure carries the
alt text (the symbology and the payload unless you pass one), the print target, and the physical
width and height. `Barcode.AddGs1ToDocument` is the same for a `Gs1Data`, with the printed form as
its alternative description.

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

## For paper: `Barcode.Print`

`Encode` keeps the historic quiet zones so an existing document never changes size. A print target is
the other contract: a resolution, a physical size, and the quiet zone the symbology's specification
requires.

```csharp
BarcodePrint print = Barcode.Print(
    BarcodeSymbology.Ean13,
    "3560070131075",
    new BarcodePrintOptions
    {
        Dpi = 300,                        // the default
        WidthMillimetres = 40,            // the label's width, quiet zone included
        ModuleMillimetres = 0.30,         // at or above the symbology's floor
    });

print.Png;                 // the raster, at that DPI
print.ModuleMillimetres;   // the module actually used
print.WidthMillimetres;    // the size it really prints at, after rounding
print.QuietZone;           // modules on each side, as rendered
```

`BarcodePrintOptions.Dpi` defaults to 300. `WidthMillimetres` and `ModuleMillimetres` set the target
from either side — one derives the other; `WidthModules` instead pins the symbol to a bar count and
refuses a layout that cannot hold it plus its quiet zone rather than cropping it. `QuietZone` and
`BarHeight` override the symbology's defaults, and `ErrorCorrection` applies to the symbologies that
have it.

The floors are enforced, not merely documented: `Barcode.MinimumQuietZone(symbology)` returns the
normative quiet zone in modules (four for a QR Code, one for a DataMatrix, ten for a Code 128, eleven
for an EAN-13, nine for a UPC, two for a PDF417) and `Barcode.MinimumModuleSize(symbology)` the
smallest module in millimetres — 0.25 mm for a one-dimensional symbol, 0.40 mm for a
two-dimensional one. `Print` refuses anything narrower.

## Reading one back

`Barcode.Decode(byte[] png)` returns the text, the symbology and the points the detector found. It
reads every symbology in the set, including the four this build does not write — one of those comes
back with its `Symbology` and its `Format` set, not as an unknown.

`Barcode.DecodeAll` reads every symbol in one image, which is what a delivery note needs: one symbol
per line item.

```csharp
DecodePage page = Barcode.DecodeAll(
    File.ReadAllBytes("delivery-note.png"),
    new BarcodeDecodeOptions { MaximumPixels = 64_000_000, MaximumBytes = 256 * 1024 * 1024 });

foreach (DecodeResult symbol in page.Symbols)
{
    Console.WriteLine($"{symbol.Symbology}: {symbol.Text} at {symbol.Bounds}");
}
```

The defaults are 64 megapixels and 256 MiB, and a caller may raise them to
`BarcodeDecodeOptions.HardMaximumPixels` (250 megapixels) and `HardMaximumBytes` (1 GiB). Past a
ceiling the call fails with `BarcodeException` and `BarcodeErrorCode.Limit`; it never silently reads a
downscaled image. `DecodePage` carries `Count`, `Width`, `Height`, and — when nothing was found —
`Error`.

The image `DecodeAll` takes is a PNG, and `M3scalinum.Text.Pdf`'s `PdfRaster.Render(pdf, page).Bytes`
is exactly that shape, so a scanned page needs one render and this call.

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
| A read-only symbology | `BarcodeUnsupportedException`: it can be decoded, not encoded. |
| A GS1 payload the table does not know, or a check digit that does not match | `BarcodeGs1Exception`. |
| A print target under the module or quiet-zone floor | `BarcodeException`: the symbol would not scan at that size. |
| Bytes that are not a PNG | `BarcodeImageException`. |
| An error correction level on a symbology that has none | `BarcodeException`, `InvalidArgument`: "Q" means nothing to a Code 128. |
| A page with no barcode | `DecodeResult.Success == false`, or an empty `DecodePage`. |

## One limitation worth knowing

An **Aztec** symbol needs at least two pixels per module to be read back, by any decoder of this
family. `Scale = 1` is accepted and produces a correct symbol that this package cannot read again;
the default is four. Every other symbology round-trips at one pixel per module.

## A shipping label

`ShippingLabel` produces the one page a logistics label is: an SSCC in a GS1-128 symbol, the same
number printed for a human, the delivery address and a parcel reference. The page is written by this
package itself, so a label needs neither `M3scalinum.Text.Pdf` nor the compiler.

```csharp
ShippingLabel label = ShippingLabel.Create("106141411234567897", new ShippingLabelOptions
{
    Recipient = "Camille Dubois",
    AddressLines = ["12 rue de la Paix", "75002 Paris", "France"],
    Reference = "LS-2026-000481",
});

byte[] pdf = label.Pdf;                   // one page, 100 × 150 mm by default
BarcodePrint code = label.Code;           // the symbol, at the size it really prints
string printed = label.HumanReadable;     // (00)106141411234567897
```

`ShippingLabelFormat` has two portrait pages: `Millimetres100x150`, the default and the 4 × 6 in
stock a thermal label printer is loaded with, and `A6`, 105 × 148 mm. On the default page the
symbol gets 92 mm — the width less 4 mm of clearance on each side — and is centred at its own
printed width, so a wider module moves the symbol rather than shrinking the page; a symbol that
does not fit is refused with both widths in the message. The text margin is 10 mm.

| Option | Default | Accepted |
| --- | --- | --- |
| `Format` | `Millimetres100x150` | `Millimetres100x150`, `A6` |
| `Dpi` | 300 | 72 to 2400 |
| `ModuleMillimetres` | 0.495 (the GS1 nominal) | any positive number; the print target refuses a module under 0.25 mm |
| `BarHeightModules` | 64 (about 32 mm at 0.495 mm) | 1 to 1024 |
| `Recipient` | none | at most 42 characters |
| `AddressLines` | none | 1 to 5 lines, at most 42 characters each |
| `Reference` | none | at most 32 characters |

An empty line, a line over its length or a control character is a `BarcodeException` naming the
field and its length, never a clipped address. At least one address line is required: an SSCC and
no address is not a deliverable parcel.

### The SSCC

The key is eighteen digits: an extension digit, a sixteen-digit body and the GS1 mod-10 check digit.
A seventeen- or nineteen-digit string is refused, and so is a key whose check digit does not match —
`Gs1Data` names both digits:

```text
AI 00 (SSCC) ends in check digit 8, but '10614141123456789' needs 7:
the value '106141411234567898' fails the GS1 mod-10 check
```

`Create(Gs1Data, options)` is the other entry point: it writes whatever element string the data
carries, provided AI `00` is one of them, and refuses data without it. The symbol is GS1-128, the
element string with FNC1 in lead position.

### The page

The label is one PDF page written in the package's own `Internal/PdfLabelPage`: two standard
Helvetica faces, one greyscale image XObject, no colour, no logo, no second page. Text is encoded
Windows-1252, so an accented address prints as written and a character outside that encoding becomes
`?`. There is no timestamp and no file identifier, so the same SSCC, address and options produce the
same bytes, and a test asserts that equality; another test decodes the rendered page back to the
SSCC where the PDF rasteriser is available. `samples/labels/sscc-label.json` is the same label as
data, and a test builds it so the sample cannot go stale.

## Shipping and licensing

The package carries its engine as a native library per runtime identifier — `win-x64`, `win-arm64`,
`linux-x64`, `linux-arm64`, `linux-musl-x64`, `linux-musl-arm64`, `osx-arm64`, `osx-x64` — under
`runtimes/<rid>/native/`, the same layout `M3scalinum.Text.Typst` uses. macOS gets two thin dylibs,
one per architecture, not one universal file: an arm64 process resolves `runtimes/osx-arm64/native/`,
an Intel one `runtimes/osx-x64/native/`, and neither copies the other's bytes. `BARCODES_NATIVE_PATH`
points a build at a locally compiled library instead.

The engine is [rxing](https://crates.io/crates/rxing) 0.9.4, a Rust port of ZXing, licensed
**Apache-2.0** — the licence of this repository, so nothing extra is required of a consumer. Its
notice is part of `THIRD-PARTY-NOTICES.md`, and the reasoning behind the choice (and why not a .NET
barcode library) is in Spec 20 §5.

`BarcodeEnvironment.EngineVersion` reports what is loaded — `rxing 0.9.4` — which is the first thing
to include in a bug report about a symbol that does not decode.
