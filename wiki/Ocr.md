# OCR: reading a scanned page

`M3scalinum.Text.Ocr` reads text out of an image, and out of a PDF whose pages are images. The rest
of the suite reads what a page *draws*; a scan draws nothing, so this is the package that turns a
scanned document into text.

```csharp
using M3scalinum.Text.Ocr;

byte[] detection = File.ReadAllBytes("/opt/models/text-detection.rten");
byte[] recognition = File.ReadAllBytes("/opt/models/text-recognition.rten");

using OcrEngine engine = OcrEngine.FromModels(detection, recognition);

OcrResult result = engine.Recognize(File.ReadAllBytes("scan.png"));

Console.WriteLine(result.Text);
Console.WriteLine($"{result.Lines.Count} lines, mean confidence {result.MeanConfidence:F3}");

foreach (OcrLine line in result.Lines)
{
    Console.WriteLine($"{line.Bounds}: {line.Text}");
}
```

A scanned PDF, page by page, at 300 DPI:

```csharp
IReadOnlyList<OcrPageResult> pages = engine.RecognizePdf(
    File.ReadAllBytes("received.pdf"),
    new OcrPdfOptions { Dpi = 300, FirstPage = 0, LastPage = 4 });

foreach (OcrPageResult page in pages)
{
    Console.WriteLine($"--- page {page.PageIndex}");
    Console.WriteLine(page.Text);
}
```

## You bring the models

`FromModels` takes the two model files as bytes. Nothing in this package — or in the native library
behind it — downloads a model, ships one, or reads one from a default path. That is deliberate:
40 MB of weights do not belong in a NuGet package, and a library that reaches the network on first
use has made a call the host never authorised.

The models `ocrs` expects are the ones its `ocrs-models` project publishes. Pass their bytes from
wherever you keep them: a deployment asset, a licence you hold, an object store. `characterDictionary`
is the alphabet a recognition model was trained on, as a UTF-8 string; leave it null for the default
Latin model.

## What you get

- `Text` — every line, joined with `\n`.
- `Lines` — one `OcrLine` per line, in reading order, each with `Bounds` in pixels, `Confidence` and
  `Words` (each word with its own box and confidence).
- `MeanConfidence` — the mean over the lines.
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
- A page with no text is not an error: empty `Text`, no lines, `MeanConfidence` 0.
- A model that is not a model, an image that will not decode, a PDF that will not parse: an
  `OcrException` with a `Code` of `model`, `image` or `pdf`. A panic in the native code is caught at
  the boundary and reported the same way, never unwound into your process.

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
