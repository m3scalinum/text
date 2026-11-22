# OCR: reading a scanned page

`M3scalinum.Text.Ocr` reads text out of an image, and out of a PDF whose pages are images. The rest
of the suite reads what a page *draws*; a scan draws nothing, so this is the package that turns a
scanned document into text.

```csharp
using M3scalinum.Text.Ocr;

byte[] detection = File.ReadAllBytes("/opt/models/text-detection.rten");
byte[] recognition = File.ReadAllBytes("/opt/models/text-recognition.rten");

using OcrEngine engine = OcrEngine.FromModels(detection, recognition);

OcrRecognition outcome = engine.Recognize(File.ReadAllBytes("scan.png"));

// A page below the confidence threshold is refused rather than returned as approximate text.
if (outcome is not OcrResult result)
{
    OcrRefusal refusal = ((OcrUnreadable)outcome).Refusal;
    Console.WriteLine($"refused ({refusal.Reason}): {refusal.Message}");
    return;
}

Console.WriteLine(result.Text);
Console.WriteLine($"{result.Lines.Count} lines, mean confidence {result.MeanConfidence:F3}");

foreach (OcrLine line in result.Lines)
{
    Console.WriteLine($"{line.Bounds}: {line.Text}");
}
```

A scanned PDF, page by page, at 300 DPI:

```csharp
IReadOnlyList<OcrPageOutcome> pages = engine.RecognizePdf(
    File.ReadAllBytes("received.pdf"),
    new OcrPdfOptions { Dpi = 300, FirstPage = 0, LastPage = 4 });

foreach (OcrPageOutcome page in pages)
{
    switch (page)
    {
        case OcrPageResult read:
            Console.WriteLine($"--- page {read.PageIndex}: {read.Text}");
            break;

        case OcrPageUnreadable refused:
            Console.WriteLine($"--- page {refused.PageIndex}: {refused.Refusal.Message}");
            break;
    }
}
```

Each page is either an `OcrPageResult` or an `OcrPageUnreadable`, exactly like the single-image call,
so one page the engine cannot read does not fail the batch. `OcrPageOutcome` carries `PageIndex`,
`Width`, `Height`, `Dpi` and the model identity for both.

## You bring the models

`FromModels` takes the two model files as bytes; `FromDirectory` takes a directory holding
`detection.rten` and `recognition.rten`:

```csharp
using OcrEngine engine = OcrEngine.FromDirectory("/opt/ocr-models");
```

Nothing in this package — or in the native library behind it — downloads a model, ships one, or reads
one from a default path. That is deliberate: 40 MB of weights do not belong in a NuGet package, and a
library that reaches the network on first use has made a call the host never authorised. The directory
form looks for those two file names and no others, and a directory missing one is refused with an
`OcrException` whose `FilePath` names the file that was expected.

The models `ocrs` expects are the ones its `ocrs-models` project publishes. Pass their bytes from
wherever you keep them: a deployment asset, a licence you hold, an object store. `characterDictionary`
is the alphabet a recognition model was trained on, as a UTF-8 string; leave it null for the default
Latin model.

### Which weights produced a page

`OcrEngine.Models` is the identity of the weights the engine loaded, and every result carries the
same one in `OcrRecognition.Models`:

```csharp
OcrModelIdentity? identity = engine.Models;

identity?.Detection.Sha256;     // sixty-four hex characters, from the bytes the engine loaded
identity?.Detection.Format;     // "rten" or "onnx", from the leading bytes
identity?.Detection.Length;
identity?.Sha256;               // one digest over the pair
```

Two hosts that load the same files report the same strings, which is what lets a report say which
models read a page. `Format` is read from the file's first bytes, not from its extension: the engine
accepts a `RTEN` container or an ONNX model. A file that is empty, too small, too large, truncated or
not a model of its role is refused with the `OcrModelDefect` that says which, plus the `Role` and the
`FilePath`.

## Confidence thresholds

`Recognize` accepts an `OcrOptions`; three of its values decide whether a reading is returned:

| Option | Default | Effect |
| --- | --- | --- |
| `MinimumPageConfidence` | 0.6 | The page's mean line confidence must reach it |
| `MinimumWordConfidence` | 0.5 | Words under it are marked rejected |
| `MaximumRejectedWordRatio` | 0.5 | A page with more rejected words than this is refused |

A value outside 0–1 — NaN included — is an `ArgumentOutOfRangeException` before anything is read. A
refusal names the rule that fired (`BlankPage`, `MeanConfidence` or `RejectedWords`) together with the
threshold, the value observed and the number of words concerned, so a log line carries the whole
story. A reading that passes reports the same counters on the result: `RejectedWordCount` and
`RejectedWordRatio`.

## Image ceilings

An image the engine cannot read costs memory long before it costs recognition, so `Recognize` bounds
what it will decode:

| Option | Default (0) means | Hard maximum |
| --- | --- | --- |
| `MaximumImagePixels` | 64 000 000 pixels (`MaximumImagePixelsCeiling`) | a caller may only lower it |
| `MaximumImageBytes` | 256 MiB (`MaximumImageBytesCeiling`) | a caller may only lower it |

```csharp
var options = new OcrOptions
{
    MaximumImagePixels = 40_000_000,          // tighten below the library's ceiling
    MaximumImageBytes = 64 * 1024 * 1024,
};

OcrRecognition outcome = engine.Recognize(File.ReadAllBytes("scan.png"), options);
```

The pixel ceiling is checked from the format header — PNG's `IHDR`, JPEG's `SOF`, BMP's information
header, GIF's screen descriptor, TIFF's first `IFD`, WebP's `VP8`/`VP8L`/`VP8X` block — so an image
that declares more is refused before its pixels are allocated. The byte ceiling is checked on the
length alone, before a header byte is read.

An option can only lower a ceiling. Zero means the library's; a negative value or one above the hard
maximum is an `ArgumentOutOfRangeException`, not a clamp, because a clamp would report a ceiling the
caller never wrote. The defaults are also what `M3scalinum.Text.Barcodes` applies to an image it
decodes: 64 000 000 pixels is an A4 page at 600 DPI (34.8 megapixels) with room to spare.

A refusal past either ceiling is an `OcrImageLimitException`, with `Limit` (`Pixels` or `Bytes`),
`Observed` and `Maximum` — the size the image declared and the ceiling that refused it. It is not an
`OcrUnreadable`: nothing was decoded, no threshold ran, and there is no page to judge, so the input
is refused rather than reported as unreadable text. The byte ceiling applies to an image read from
bytes; a PDF given to `RecognizePdf` is rasterised page by page by the native renderer at
`OcrPdfOptions.Dpi`, under that rasteriser's own bounds.

## Preprocessing a turned or noisy page

Three switches on `OcrOptions`, all off by default, clean the page before it is read:

```csharp
var options = new OcrOptions
{
    DetectRotation = true,   // 90°, 180° or 270°, detected from the page's ink and turned back
    Deskew = true,           // a skew of at most 10° either way, removed by one bilinear resample
    Denoise = true,          // a 3×3 median filter on the luma
};
```

The quarter-turn detection reads the direction the page's lines run; a page whose lines are
vertically symmetric — all capitals, a table rule — gives no signal and is left as it is rather than
guessed at. The deskew searches a quarter of a degree at a time and leaves a page level to within a
third of a degree untouched. The median filter removes isolated specks without softening stroke edges
the way a mean filter would.

What was done is reported on the result, because the boxes and the width and height are the
preprocessed page's:

```csharp
OcrResult result = /* … */;
result.Preprocess.RotationDegrees;   // 0, 90, 180 or 270
result.Preprocess.SkewDegrees;       // positive clockwise, in [-10, +10]
result.Preprocess.Denoised;
```

## What you get

- `Text` — every line, joined with `\n`.
- `Lines` — one `OcrLine` per line, in reading order, each with `Bounds` in pixels, `Confidence` and
  `Words` (each word with its own box and confidence).
- `MeanConfidence` — the mean over the lines.
- `Preprocess` — what the rotation, deskew and denoise stages did, if they were asked for.
- `RejectedWordCount` and `RejectedWordRatio` — how many words fell under the word threshold.
- `Models` — the identity of the weights that produced the reading.
- `Width`, `Height` — the image, or the rasterised page, in pixels. `OcrPageResult` adds `PageIndex`
  and `Dpi`.

Boxes are pixels with the origin at the top-left, so a box maps straight onto the image you supplied.

## What "confidence" means

It is the mean text probability the **detection** model assigned inside the box: how strongly the
detector saw text there. It is not the recogniser's certainty about the characters — `ocrs` does not
expose those scores, and inventing them would be worse than saying so.

Use it to triage: a crisp scan scores near 1, a faint one lower. Computing it costs a second
detection pass, so a page takes roughly a third longer than it would without it.

## What to expect

Good text from a 300 DPI scan of a printed page, Latin alphabet, 9 pt and up. Weaker on:

- small or thin type (below about 8 pt at 300 DPI);
- faint scans, faxes, photographs and anything that is not flat;
- skew beyond a few degrees;
- ligatures, maths, and symbols outside the model's alphabet;
- handwriting, stamps and embossing — which are not printed text at all.

You get text, not layout: a line is a string, a word is a run of non-space characters, a table
becomes lines of words. Two columns usually come out column by column.

**Do not OCR a born-digital page.** Its text layer is exact and this is not. Read the text layer with
`M3scalinum.Text.Pdf`'s `PdfText`, and reach for this package on the pages whose text layer is empty —
which is exactly what a scan looks like.

## Why `RecognizePdf` needs no other package

The native library carries its own CPU PDF rasteriser (hayro, pure Rust). `M3scalinum.Text.Ocr` does
not reference `M3scalinum.Text.Pdf` and does not need it installed: one package, one native library,
one call. The renderer is young, and unusual documents render less faithfully than a mature PDF engine
would; a scanned page — an image and a little text — is its easy case.

## Invariants

- Bytes in, bytes out. No file system, no network, no default model path.
- CPU only. No GPU, no driver, no device to choose.
- An engine is thread-safe once created; load it once and share it.
- A page with no text is a refusal, not an exception: `OcrUnreadable` with `BlankPage`, empty
  confidence and no text to reach. A blank scan in a batch does not throw.
- A model that is not a model, an image that will not decode, a PDF that will not parse: an
  `OcrException` with a `Code` of `model`, `image` or `pdf`. An image past a pixel or byte ceiling is
  an `OcrImageLimitException` with the code `image_limit`, refused before a pixel is decoded. A panic
  in the native code is caught at the boundary and reported the same way, never unwound into your
  process.

## Testing it

The repository ships no models, so the tests that recognise text skip unless you point
`M3SCALINUM_OCR_MODELS` at a directory holding `text-detection.rten` and `text-recognition.rten`:

```bash
export M3SCALINUM_OCR_MODELS=/opt/models
export M3SCALINUM_OCR_NATIVE_PATH=/path/to/sharp_text_ocr.dll
dotnet test tests/Text.Ocr.Tests -c Release
```

Without them the suite still proves the managed surface, the result documents and the ABI — and says
plainly, in every skip message, that recognition itself is unproven.
