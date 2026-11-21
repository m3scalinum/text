# 21 — OCR: reading text out of a page image

## 1. Scope

`M3scalinum.Text.Ocr` reads the text of an image, and of a PDF whose pages are images. It closes the
one gap the rest of the suite leaves open: `PdfText` recovers the characters a page draws, and a
scanned page draws none, so a scanned document is a scan of a document and nothing more. This
package turns that page into text.

`OcrEngine` is the entry point:

- `OcrEngine.FromModels(byte[] detectionModel, byte[] recognitionModel, byte[]? characterDictionary = null)`
  loads two models the caller supplies.
- `OcrEngine.Recognize(byte[] imageBytes)` returns an `OcrResult`: the text, one `OcrLine` per line
  with its box in pixels and its words, and a `MeanConfidence`.
- `OcrEngine.RecognizePdf(byte[] pdf, OcrPdfOptions? options = null)` rasterises each requested page
  at a given DPI and recognises it, returning one `OcrPageResult` per page.

**In scope.** Model loading from caller-supplied bytes; text detection and line recognition on the
CPU through `ocrs` and `rten`; PNG, JPEG, BMP, GIF, TIFF and WebP input; PDF page rasterisation
through a pure-Rust renderer; per-line and per-word boxes; a confidence signal; a native ABI that
cannot unwind into the host.

**Out of scope, deliberately.**

| Not done | Why |
| --- | --- |
| Shipping or downloading models | ~40 MB of weights, and a library that reaches the network behind the caller's back is a library that cannot be audited. The caller passes the bytes. |
| Handwriting, tables, reading-order reconstruction | The recogniser returns lines and words with boxes. Grouping them into a table or a paragraph is a different problem with no single right answer. |
| Non-Latin scripts by default | The default alphabet is the Latin one the published models were trained on. Another script needs a model and its alphabet. |
| GPU acceleration | `rten` runs on the CPU. There is no CUDA dependency, no driver requirement, and no device to configure. |
| Text *extraction* from a born-digital PDF | That is `M3scalinum.Text.Pdf`'s `PdfText`, and it is exact. Running a recogniser over rendered glyphs would be strictly worse; a caller with both should try the text layer first. |

## 2. The API

```csharp
using M3scalinum.Text.Ocr;

// The models are the caller's: read from a licence the caller controls, a blob store a caller
// owns, or an asset directory a caller deployed. Nothing here fetches them.
byte[] detection = File.ReadAllBytes("/opt/models/text-detection.rten");
byte[] recognition = File.ReadAllBytes("/opt/models/text-recognition.rten");

using OcrEngine engine = OcrEngine.FromModels(detection, recognition);

OcrResult result = engine.Recognize(File.ReadAllBytes("scan.png"));

Console.WriteLine(result.Text);
Console.WriteLine($"{result.Lines.Count} lines, mean confidence {result.MeanConfidence:F3}");

foreach (OcrLine line in result.Lines)
{
    Console.WriteLine($"{line.Bounds}: {line.Text}");

    foreach (OcrWord word in line.Words)
    {
        Console.WriteLine($"    {word.Bounds} {word.Text} {word.Confidence:F3}");
    }
}
```

A scanned document, page by page:

```csharp
IReadOnlyList<OcrPageResult> pages = engine.RecognizePdf(
    File.ReadAllBytes("received.pdf"),
    new OcrPdfOptions { Dpi = 300, FirstPage = 0, LastPage = 4 });

foreach (OcrPageResult page in pages)
{
    Console.WriteLine($"--- page {page.PageIndex} ({page.Width}x{page.Height} at {page.Dpi} dpi)");
    Console.WriteLine(page.Text);
}
```

`OcrResult` carries `Text`, `Lines`, `MeanConfidence`, `Width` and `Height`. `OcrLine` carries `Text`,
`Bounds`, `Confidence` and `Words`. `OcrWord` carries `Text`, `Bounds` and `Confidence`. `OcrBox` is
`X`, `Y`, `Width`, `Height` in pixels, with the origin at the top-left. `OcrPageResult` adds
`PageIndex` and `Dpi`.

An engine is safe to share: `OcrEngine` may be used from several threads at once, and the models are
loaded once.

## 3. The models are the caller's

There is no default path, no environment variable pointing at weights, and no first-run download. If
the caller passes no models, `FromModels` is never called and there is no engine; if the bytes are
not a model, `OcrException` says so with `Code == "model"`.

This is a rule, not a convenience. A library that silently downloads 40 MB has made a network call
the host did not authorise, in a process that may be behind a proxy that blocks it, on a machine
where the bytes arrive from a source nobody verified. It also makes the package weigh 40 MB more
than its code.

The published models that `ocrs` expects are the ones the project's `ocrs-models` repository
publishes; a caller who has them passes their bytes. `characterDictionary` is the alphabet the
recognition model was trained on, as a UTF-8 string; it is only needed for a model that is not the
default Latin one, and the tests and docs for the model in hand are the source of truth for it.

## 4. Confidence: what the number is

`OcrLine.Confidence` is the mean text probability the **detection** model assigned inside the line's
box, in [0, 1]. `OcrWord.Confidence` is the same measure over the word's box, and
`MeanConfidence` is the mean over the lines.

It is not a recognition likelihood. `ocrs` 0.14 exposes the recogniser's characters and their boxes,
not the decoder's per-character scores, so a number built from the characters would be an invention
dressed as a measurement. What is measured is how strongly the detector saw text where the line
was found: a crisp scan scores near 1, a faint or noisy one scores lower, and a line the detector
was unsure about scores low enough to be worth reviewing.

The cost is real and documented: computing it runs the detection model a second time, because the
line boxes are produced by a detection pass this crate cannot read the mask of. On a 300 DPI A4 page
that is roughly a third more time. A caller who does not need the number should be aware it is being
computed; a caller who needs a threshold should prefer it to a constant.

## 5. What recognition is worth

Expect usable text on a 300 DPI scan of printed pages, in the Latin alphabet, at 9 pt and above, with
an ordinary amount of degradation. Expect errors on:

- small text (below roughly 8 pt at 300 DPI) and on thin or condensed faces;
- low-contrast scans, faxes, and photographs where the page is not flat;
- skewed pages beyond a few degrees, which the line detector handles but the recogniser does less
  well;
- ligatures, mathematical notation, and characters the model's alphabet does not contain;
- anything that is not printed: handwriting, stamps, and embossing.

The output is text, not layout: lines come in reading order as the detector groups them, a line is a
string, and a word is a run of non-space characters. A table becomes lines of words. A two-column
page usually comes out column by column because the detector groups by proximity, and occasionally
does not.

**A born-digital page should not be OCR'd.** Its text layer is exact and this is not. A caller that
has both should read the text layer with `PdfText` and fall back to this package for the pages whose
text layer is empty — which is what a scan looks like.

## 6. Why the rendering lives in the native library

`RecognizePdf` renders pages with `hayro`, a pure-Rust CPU rasteriser compiled into the same native
library as the recogniser. It never references `M3scalinum.Text.Pdf`, and the package has no project
or package dependency on it.

**Decision — no reference between the two packages.** The alternative was for `Text.Ocr` to depend
on `Text.Pdf` and render through it. `Text.Pdf` cannot render: it reads, writes, signs and
post-processes, and its page model has no rasteriser, so the dependency would have bought nothing
and would have coupled two packages released on separate tags. A second alternative — a callback
the caller implements, receiving the PDF and returning one image per page — pushes a rasteriser onto
every caller, including the ones who only want `Recognize`. Rendering inside the native library
keeps `Text.Ocr` self-contained: one package, one native library, one call.

The cost is honest: `hayro` is young, and its rendering of unusual documents is less complete than a
mature PDF engine's. For the scanned pages this feature exists to read, a page is an image and a
little text, which is its easy case.

## 7. The native ABI

`native/text-ocr` is a `cdylib` following the compiler crate's conventions exactly:

| Convention | How it holds |
| --- | --- |
| Guarded entry points | Every export runs inside `catch_unwind`; a panic becomes `TO_ERR_PANIC`, never an unwind into C. |
| Owned buffers | Results and errors cross as `ToBuffer`, allocated by Rust and freed by `to_buffer_free`. |
| One handle type | `ToEngine` owns both models and drops them; `to_engine_free` is the only way to release it. |
| A live-handle counter | `to_engine_live_count` is exact, because the handle is only ever created in one place and `Drop` is the only decrement. |
| An error object | `to_error_code`, `to_error_json`, `to_error_traceback`, `to_error_free`, as in `sharp-typst`. |

Exports: `to_abi_version`, `to_abi_version_string`, `to_build_info`, `to_error_*`,
`to_buffer_free`, `to_engine_create`, `to_engine_free`, `to_engine_live_count`,
`to_engine_recognize`, `to_engine_recognize_pdf`.

Results cross as UTF-8 JSON, parsed in one place on the managed side:

```json
{"width":2480,"height":3508,"text":"INVOICE 2026","meanConfidence":0.913,
 "lines":[{"text":"INVOICE 2026","bounds":{"x":120,"y":300,"width":540,"height":40},
           "confidence":0.927,
           "words":[{"text":"INVOICE","bounds":{"x":120,"y":300,"width":300,"height":40},
                     "confidence":0.935}]}]}
```

`RecognizePdf` returns `{"pages":[...]}`, each page carrying `pageIndex`, `width`, `height`, `dpi`
and the same `text`/`lines`/`meanConfidence` triple. A PDF read takes `first_page` and `page_count`
(zero meaning "to the end") so a caller reading five pages of a thousand never renders the other
nine hundred and ninety-five.

## 8. Refusals

| Situation | Result |
| --- | --- |
| A model argument is null or empty | `ArgumentNullException` / `ArgumentException`, before any native call |
| The bytes are not a model | `OcrException` with `Code == "model"` |
| The image cannot be decoded | `OcrException` with `Code == "image"` |
| The bytes are not a PDF | `OcrException` with `Code == "pdf"` |
| The DPI is outside 72–1200 | `ArgumentOutOfRangeException` |
| `LastPage` precedes `FirstPage` | `ArgumentOutOfRangeException` |
| A page renders past 120 million pixels | `OcrException` with `Code == "pdf"`, naming the page; a hostile document must not exhaust memory |
| The engine was disposed | `ObjectDisposedException` |
| An image or page holds no text | Not an error: an empty `Text`, no lines, `MeanConfidence` 0 |

## 9. Licences

| Component | Licence | Role |
| --- | --- | --- |
| `ocrs` 0.14 | MIT OR Apache-2.0 | Text detection and line recognition |
| `rten` 0.28 | MIT OR Apache-2.0 | Inference runtime the models run on |
| `rten-tensor` 0.28 | MIT OR Apache-2.0 | The tensors `rten` returns |
| `hayro` 0.9 | Apache-2.0 OR MIT | Pure-Rust PDF rasteriser |
| `image` 0.25 | MIT OR Apache-2.0 | PNG, JPEG, BMP, GIF, TIFF and WebP decoding |
| `serde_json` 1 | MIT OR Apache-2.0 | The result documents |

All are permissive and all are compatible with this repository's Apache-2.0. The models are not
part of the package and are not covered here: a caller distributing a model distributes it under
whatever terms the model carries, which is between the caller and the model's publisher.

`THIRD-PARTY-NOTICES.md` carries the notices; this spec states what they are for. The text to add is
in §10.

## 10. NOTICE text

The following belongs in `THIRD-PARTY-NOTICES.md`, under the native libraries section:

```
native/text-ocr links the following crates:

  ocrs 0.14            Copyright 2022 Robert Knight
  rten 0.28            Copyright 2023 Robert Knight
  rten-tensor 0.28     Copyright 2023 Robert Knight
  hayro 0.9            Copyright 2024 Laurenz Stampfl
  image 0.25           Copyright 2014 The image-rs Developers
  serde_json 1         Copyright 2016 Serde Developers

  Licensed under either of

    Apache License, Version 2.0 (the "License");
    you may not use this file except in compliance with the License.
    You may obtain a copy of the License at

        http://www.apache.org/licenses/LICENSE-2.0

    or the MIT license

        https://opensource.org/licenses/MIT

  at your option.

  Unless required by applicable law or agreed to in writing, software
  distributed under the License is distributed on an "AS IS" BASIS,
  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
  See the License for the specific language governing permissions and
  limitations under the License.

  No machine-learning model is distributed with this repository or with the
  M3scalinum.Text.Ocr package. The caller supplies the models.
```

## 11. Verification

The tests in `tests/Text.Ocr.Tests` separate what can be proved from what cannot, and say which is
which.

- **The managed surface** — validation, result shape, disposal, the page-range arithmetic and the
  `ObjectDisposedException` after dispose — is asserted against a deterministic fake backend, because
  it is the surface's contract and needs no model.
- **The result documents** — the exact JSON `native/text-ocr` writes, including a page with no text
  and a page with no lines — are parsed and asserted field by field. This is the contract between the
  two halves, and it is tested without a native library.
- **The ABI** — the version handshake, the build information naming `ocrs`/`rten`/`hayro`, a model
  that is not a model being refused with `Code == "model"` and no handle leaked — runs against the
  real library, which is where a broken export or a panicking boundary shows up.
- **Recognition itself** needs two models, and this repository ships none: the published models weigh
  roughly 40 MB together and are not redistributable under terms a fixture can accept, and the library
  never downloads them. Those tests are in `ModelBackedTests`: they skip unless `M3SCALINUM_OCR_MODELS`
  names a directory holding `text-detection.rten` and `text-recognition.rten`, and their skip message
  says so rather than passing quietly. **On a machine without those models, the recognition path is
  unproven by this suite** — the code runs, and no test in this repository has watched it read a
  character.
