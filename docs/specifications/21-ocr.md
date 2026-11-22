# 21 — OCR: reading text out of a page image

## 1. Scope

`M3scalinum.Text.Ocr` reads the text of an image, and of a PDF whose pages are images. It closes the
one gap the rest of the suite leaves open: `PdfText` recovers the characters a page draws, and a
scanned page draws none, so a scanned document is a scan of a document and nothing more. This
package turns that page into text.

`OcrEngine` is the entry point:

- `OcrEngine.FromModels(byte[] detectionModel, byte[] recognitionModel, byte[]? characterDictionary = null)`
  loads two models the caller supplies.
- `OcrEngine.Recognize(byte[] imageBytes, OcrOptions? options = null)` returns an `OcrRecognition`: either an
  `OcrResult` (the text, one `OcrLine` per line with its box in pixels and its words, and a `MeanConfidence`),
  or an `OcrUnreadable` naming the threshold the page failed.
- `OcrEngine.RecognizePdf(byte[] pdf, OcrPdfOptions? options = null)` rasterises each requested page
  at a given DPI and recognises it, returning one `OcrPageOutcome` per page — an `OcrPageResult` or an
  `OcrPageUnreadable`.

**In scope.** Model loading from caller-supplied bytes; text detection and line recognition on the
CPU through `ocrs` and `rten`; PNG, JPEG, BMP, GIF, TIFF and WebP input; PDF page rasterisation
through a pure-Rust renderer; per-line and per-word boxes; a confidence signal; confidence
thresholds that refuse a page instead of returning approximate text; a native ABI that cannot
unwind into the host.

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

OcrRecognition outcome = engine.Recognize(File.ReadAllBytes("scan.png"), new OcrOptions
{
    MinimumPageConfidence = 0.6,     // the defaults; below them, the page is refused
    MinimumWordConfidence = 0.5,
    MaximumRejectedWordRatio = 0.5,
});

if (outcome is not OcrResult result)
{
    // OcrUnreadable: no text, and the reason it was refused.
    var refusal = ((OcrUnreadable)outcome).Refusal;
    Console.Error.WriteLine(refusal.Message);
    return;
}

Console.WriteLine(result.Text);
Console.WriteLine($"{result.Lines.Count} lines, mean confidence {result.MeanConfidence:F3}");
Console.WriteLine($"{result.RejectedWordCount} word(s) below the word threshold");

foreach (OcrLine line in result.Lines)
{
    Console.WriteLine($"{line.Bounds}: {line.Text}");

    foreach (OcrWord word in line.Words)
    {
        var doubt = word.Rejected ? "  <- not reliable" : string.Empty;
        Console.WriteLine($"    {word.Bounds} {word.Text} {word.Confidence:F3}{doubt}");
    }
}
```

A scanned document, page by page:

```csharp
IReadOnlyList<OcrPageOutcome> pages = engine.RecognizePdf(
    File.ReadAllBytes("received.pdf"),
    new OcrPdfOptions { Dpi = 300, FirstPage = 0, LastPage = 4 });

foreach (OcrPageOutcome page in pages)
{
    Console.WriteLine($"--- page {page.PageIndex} ({page.Width}x{page.Height} at {page.Dpi} dpi)");

    switch (page)
    {
        case OcrPageResult reading:
            Console.WriteLine(reading.Text);
            break;
        case OcrPageUnreadable refused:
            Console.WriteLine(refused.Refusal.Message);
            break;
    }
}
```

`OcrResult` carries `Text`, `Lines`, `MeanConfidence`, `RejectedWordCount`, `RejectedWordRatio`,
`Width` and `Height`. `OcrLine` carries `Text`, `Bounds`, `Confidence` and `Words`. `OcrWord` carries
`Text`, `Bounds`, `Confidence` and `Rejected`. `OcrBox` is `X`, `Y`, `Width`, `Height` in pixels, with
the origin at the top-left. `OcrPageResult` adds `PageIndex` and `Dpi`.

An `OcrResult` and an `OcrUnreadable` share the `OcrRecognition` base, which carries `Width`, `Height`
and `Outcome`; a page's two outcomes share `OcrPageOutcome`, which adds `PageIndex` and `Dpi`. Only
the reading carries `Text`: **the compiler has no way to reach the text of a refusal**, which is the
point of §12.

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

That constant is exactly what `OcrOptions` carries, and §12 states the rule it drives.

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

`to_abi_version` is **2**. Version 1's two recognise exports took no thresholds; version 2 takes
three `f64`s — page confidence, word confidence, tolerated rejected-word ratio — in that order,
between the existing arguments and the out-parameters. A v1 host against a v2 library would pass
the wrong number of arguments, so the version is the handshake that stops it, and the managed side
asserts it before it calls anything.

Results cross as UTF-8 JSON, parsed in one place on the managed side:

```json
{"width":2480,"height":3508,"status":"recognized","text":"INVOICE 2026","meanConfidence":0.913,
 "wordCount":2,"rejectedWordCount":1,
 "lines":[{"text":"INVOICE 2026","bounds":{"x":120,"y":300,"width":540,"height":40},
           "confidence":0.927,
           "words":[{"text":"INVOICE","bounds":{"x":120,"y":300,"width":300,"height":40},
                     "confidence":0.935,"rejected":false},
                    {"text":"2026","bounds":{"x":440,"y":300,"width":220,"height":40},
                     "confidence":0.412,"rejected":true}]}]}
```

A refused page carries `"status":"unreadable"`, an empty `text`, no lines, and a `refusal` object:

```json
{"width":2480,"height":3508,"status":"unreadable","text":"","meanConfidence":0.214,
 "wordCount":6,"rejectedWordCount":6,"lines":[],
 "refusal":{"reason":"mean_confidence","threshold":0.6,"observed":0.214,"wordCount":6}}
```

`reason` is one of `blank_page`, `mean_confidence` or `rejected_words`; `threshold` is the one that
rule compares against and `observed` is what the page produced. A `status` the managed side does not
know, or a `reason` it does not know, is refused rather than guessed — but a document with **no**
`status` reads as a reading, because only a native library older than this feature writes one, and
reading it as a refusal would turn every page into nothing.

`RecognizePdf` returns `{"pages":[...]}`, each page carrying `pageIndex`, `width`, `height`, `dpi`
and the same document one image gets. A PDF read takes `first_page` and `page_count` (zero meaning
"to the end") so a caller reading five pages of a thousand never renders the other nine hundred and
ninety-five.

## 8. Refusals

| Situation | Result |
| --- | --- |
| A model argument is null or empty | `ArgumentNullException` / `ArgumentException`, before any native call |
| The bytes are not a model | `OcrException` with `Code == "model"` |
| The image cannot be decoded | `OcrException` with `Code == "image"` |
| An image declares more pixels, or occupies more bytes, than a ceiling (§16) | `OcrImageLimitException` with `Code == "image_limit"`, naming the size observed and the ceiling; the input is refused before anything is decoded |
| The bytes are not a PDF | `OcrException` with `Code == "pdf"` |
| The DPI is outside 72–1200 | `ArgumentOutOfRangeException` |
| `LastPage` precedes `FirstPage` | `ArgumentOutOfRangeException` |
| A threshold is outside 0–1, or NaN | `ArgumentOutOfRangeException` naming the property, before any native call |
| A page renders past 120 million pixels | `OcrException` with `Code == "pdf"`, naming the page; a hostile document must not exhaust memory |
| The engine was disposed | `ObjectDisposedException` |
| An image or page holds no text | Not an error and not an empty string: `OcrUnreadable` / `OcrPageUnreadable` with `Refusal.Reason == BlankPage` |
| A page is too faint, or too many words are doubtful | `OcrUnreadable` / `OcrPageUnreadable`, naming the rule, the threshold, the value and the word count |
| One word is doubtful | Not a refusal: the word is returned with `Rejected == true`, its box and its score |

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
- **The result documents** — the exact JSON `native/text-ocr` writes, including a page with no text,
  a page with no lines, and a refused page — are parsed and asserted field by field. This is the
  contract between the two halves, and it is tested without a native library.
- **The refusal rule** — a blank page, a page with every word below the word threshold, a page with a
  minority of doubtful words, a correct page, and thresholds at 0 and at 1 — is proved in Rust, in
  `native/text-ocr/src/engine.rs`, on readings built without models: `cargo test --lib` runs it.
  Nothing in the C# suite can prove it, because nothing in the C# suite has an engine.
- **The options reach the ABI unchanged** — the three thresholds cross as three `f64`s and are
  asserted at the boundary, without a model.
- **The ABI** — the version handshake (2), the build information naming `ocrs`/`rten`/`hayro`, a model
  that is not a model being refused with `Code == "model"` and no handle leaked — runs against the
  real library, which is where a broken export or a panicking boundary shows up.
- **Recognition itself** needs two models, and this repository ships none: the published models weigh
  roughly 40 MB together and are not redistributable under terms a fixture can accept, and the library
  never downloads them. Those tests are in `ModelBackedTests`: they skip unless `M3SCALINUM_OCR_MODELS`
  names a directory holding `text-detection.rten` and `text-recognition.rten`, and their skip message
  says so rather than passing quietly. **On a machine without those models, the recognition path is
  unproven by this suite** — the code runs, and no test in this repository has watched it read a
  character.

## 12. Confidence thresholds, and refusing instead of guessing

A page read at 40 % mean confidence is not a success: the caller has no way to know it must not use
it. Worse, a blank page used to come back as `Success` with an empty string, which is
indistinguishable from a page the recogniser looked at and found nothing on. This section states the
rule that replaces both.

### 12.1 The three thresholds

They are properties of `OcrOptions`, and `OcrPdfOptions` inherits them, so one set of numbers governs
images and pages alike. There is no second scale: each is compared directly against the detection
probability of §4, a number already in [0, 1], and no conversion happens anywhere.

| Property | Default | Meaning |
| --- | --- | --- |
| `MinimumPageConfidence` | 0.6 | A page whose mean line confidence is below this is refused. |
| `MinimumWordConfidence` | 0.5 | A word **strictly** below this is flagged (`OcrWord.Rejected`) and is not reliable text. |
| `MaximumRejectedWordRatio` | 0.5 | A page with more than this proportion of flagged words is refused, even when its mean confidence passes. |

A value outside [0, 1] — and NaN, which would silently disable every comparison — is refused with
`ArgumentOutOfRangeException` on the managed side and `invalid_argument` at the ABI. The ABI range test
is not redundant: a host written in C can call it directly.

**No opt-in flag, and no second API.** The thresholds are part of the options; the refusal is what
the engine does. A caller who wants yesterday's behaviour sets the thresholds to 0 and accepts
`MaximumRejectedWordRatio` 1 — which leaves exactly one refusal in force, the blank page, because no
text is not a reading at any threshold.

### 12.2 The rule

Checked in this order; the first that fires is the reason reported:

1. **`BlankPage`** — no line was found at all. Observed is 0, and no word is concerned. This fires
   whatever the thresholds are: an empty string is never a reading.
2. **`MeanConfidence`** — the page's mean line confidence is below `MinimumPageConfidence` and the
   page holds at least one line. Observed is that mean; the word count is every word on the page.
3. **`RejectedWords`** — the proportion of words below `MinimumWordConfidence` exceeds
   `MaximumRejectedWordRatio`. Observed is that proportion; the word count is the number of flagged
   words.

A page that fails none of them is an `OcrResult`, and every word that was below the word threshold is
returned anyway — with its box, its score and `Rejected == true` — because knowing *where* the doubt
was is useful, and dropping the word would silently shorten the line.

A refusal carries no text and no lines. It is an `OcrUnreadable` (or `OcrPageUnreadable`) with:
`Reason`, `Threshold` (the number that rule compares against), `Observed` (what the page produced),
`WordCount`, and a `Message` naming all four in one sentence. That is the whole point of a separate
type: `Refusal.Reason` can be switched on, and `Text` cannot be reached by accident.

Each page of a PDF is assessed on its own. One blank page in a hundred does not turn the other
ninety-nine into refusals, and the page index travels with the outcome.

### 12.3 What the threshold does not do

- **It does not correct recognition.** It compares a score to a number. Text over a photograph is
  still wrong in the same way; it is now refused instead of returned.
- **It is not a recognition likelihood.** The number is the detector's probability, per §4. A page
  can score 0.95 and still read "INV0ICE".
- **It does not make a bad scan readable.** Raising `Dpi`, or a better scan, is the fix; a lower
  threshold only accepts the same bad reading.
- **It does not delete the flagged words from the text.** A reading keeps them and marks them. A
  caller who needs only reliable text filters on `OcrWord.Rejected`.
- **It does not apply to the models.** Nothing here inspects a model's quality; it inspects one page's
  reading.

### 12.4 Choosing a value

The defaults leave a crisp 300 DPI scan of printed text untouched: such a page scores above 0.75 on
every line and every word. Raise `MinimumPageConfidence` toward 0.8 when a false reading is much more
expensive than a refusal (an invoice pipeline, an archive); lower it toward 0.4 when a refusal costs a
human review and a slightly wrong string is cheaper. `MaximumRejectedWordRatio` is the lever for "the
page is mostly fine but a whole column is noise": at 0.5, half the words may be doubtful before the
page is refused.

## 13. Preprocessing: turning, levelling and denoising

A page that reaches a scanner through a person's hands is not always straight, level or
clean. Three stages fix the three usual mistakes, and they run on the decoded image before
the recogniser sees it. They are off by default: a correct 300 DPI scan needs none of them,
and every stage costs time.

| `OcrOptions` | Default | What it does |
| --- | --- | --- |
| `DetectRotation` | `false` | Detects a page turned by 90°, 180° or 270° and turns it back. |
| `Deskew` | `false` | Estimates a fine skew of at most 10° either way and removes it. |
| `Denoise` | `false` | Applies a 3×3 median filter to the page's luma. |

`OcrPdfOptions` inherits all three, so one set of switches governs images and pages. Every
page of a document is preprocessed on its own: one page a scanner turned does not turn the
others.

### 13.1 The algorithms, one sentence each

- **Turning.** The mean length of the ink runs across the page is compared with the mean
  length down it — letters are separated by two or three pixels, so horizontal text has
  long horizontal runs, and text turned a quarter turn swaps the two; that picks the axis
  and, for the axis, the ink in a line's top quarter is compared with its bottom quarter,
  because printed Latin text has more ascenders than descenders, which is what tells upright
  from upside down.
- **Levelling.** The page is projected onto the vertical axis at every angle from −10° to
  +10° in quarter-degree steps, and the angle whose projection is the tightest — text lines
  becoming one thin spike each — is the skew, which is then removed by one bilinear
  resampling.
- **Denoising.** A 3×3 median of the luma channel, not a mean: the median removes an isolated
  speck without softening the edge of a stroke, which a mean would blur into a thinner,
  greyer one.

The whole page is handled in greyscale after the first step, and the image handed to the
recogniser is greyscale RGB; the median is computed on luma, and pairing it with the colour
channels it did not filter would be a lie about what was filtered. Ink is separated from
paper by Otsu's threshold, computed per page, because a fixed threshold would be a constant
guessed from one scanner.

### 13.2 What the result reports

`OcrResult.Preprocess` and `OcrPageResult.Preprocess` are an `OcrPreprocessReport`:

| Property | Meaning |
| --- | --- |
| `RotationDegrees` | The clockwise quarter turns applied to make the page upright: 0, 90, 180 or 270. |
| `SkewDegrees` | The fine rotation the deskew applied, in degrees, positive clockwise, in [−10, +10]; 0 when it was off or found the page level. |
| `Denoised` | Whether the median filter ran. |
| `Changed` | Whether anything was applied at all. |

A document with nothing to report carries no `preprocess` object, and a report with no
`preprocess` reads as `OcrPreprocessReport.None` — a native library older than this feature
can only ever describe an untouched page.

**The boxes and the size belong to the preprocessed page.** `Width`, `Height` and every
`OcrBox` are the image the recogniser saw, which after a quarter turn has its sides swapped
and after a deskew has a white border where the rotation left the page's rectangle. A caller
that has to map a box back to the bytes it passed in must apply the inverse of the reported
rotation and skew; a caller that only wants the text needs neither.

### 13.3 The ranges and the cost

- Rotation: 0, 90, 180 or 270 degrees. Anything else is not a page a scanner produces, and
  it is the deskew's job only up to 10°.
- Skew: ±10°, in quarter-degree steps. A page tilted further is a page that was not fed
  straight, and the estimate saturates at the edge of the window rather than guessing.
- Denoise: 3×3. A stroke one pixel wide is a minority in its own window and is removed with
  the noise it sits in; a 300 DPI scan's strokes are 2–4 pixels, so that is the limit, and it
  is stated rather than tuned away.

The cost was measured on a 2480 × 3508 page (A4 at 300 DPI) in a release build on one core:
**about 150 ms with all three stages on**, against a recognition pass measured in seconds.
The deskew dominates it, and it is bounded by design — at most 81 angles over a page sampled
down to 600 px on its longest side — so a bigger page costs the same. With every stage off
the input is returned byte for byte and nothing is computed.

### 13.4 What preprocessing does not do

| Not done | Why |
| --- | --- |
| Perspective correction | A photographed page has four corners that converge; that needs corner detection and a homography, which is a different feature with its own failure modes. A photograph at an angle is still read worse than a scan. |
| Tables, columns, reading order | The recogniser returns lines with boxes. Reconstructing a table is a grouping problem with no single right answer, and rotation is not what prevents it. |
| Handwriting | Rotating a handwritten page does not make the recogniser able to read it. That needs a different model. |
| Colour restoration, background removal, thresholding to black and white | The luma split is enough for the detector, and a binarised page throws away the anti-aliasing the recogniser's own preprocessing uses. |
| Detecting a page that is upside down *and* symmetric | A page of digits, all capitals or a table has no ascender/descender imbalance to measure. The dead zone answers "no signal" and the page is left as it is, because a wrong 180° turn is worse than none. This is the honest limit of a model-free heuristic. |
| An inverted page (white on black) | Out of scope: the ink map follows Otsu, and a page whose paper is the dark side is a page a caller should invert before handing it over. |

### 13.5 The ABI: two additive exports, revision unchanged

Two entry points are added, and no existing signature changes:

```c
int32_t to_engine_recognize_preprocessed(
    ToEngine *engine, const uint8_t *image, size_t length,
    double page_confidence, double word_confidence, double max_rejected_word_ratio,
    int32_t detect_rotation, int32_t deskew, int32_t denoise,
    ToBuffer *out_json, ToError **out_error);

int32_t to_engine_recognize_pdf_preprocessed(/* as above, plus dpi, first_page, page_count */);
```

The three flags are `int32_t` — 0 leaves a stage off, any other value turns it on — because
§1 admits nothing else across the boundary. `to_abi_version` stays **2**: nothing that was
there moved, so a host written against revision 2 runs unchanged, and a host that wants
preprocessing calls a symbol it can check for. The managed side takes the old call when every
switch is off, so the overwhelmingly common path is the one that was already proved; if the
library predates the exports and a caller asks for a stage, the failure is
`OcrException` with `Code == "unsupported"` naming the missing symbol, not a page read at the
wrong angle.

### 13.6 What is proved, and where

- **The three stages** are proved in Rust, in `native/text-ocr/src/preprocess.rs`: a straight
  page is returned byte for byte; a page turned 90°, 180° or 270° is turned back to exactly
  the bytes it started from; a page skewed by 3° reports a correction within half a degree
  and its line profile gets sharper; a 25° tilt is not guessed past the window; a speckled
  page loses most of its isolated blobs *and* keeps its line profile sharper than before; a
  blank page is left alone. `cargo test --lib` runs them. The fixtures are drawn, not
  photographed: they are glyph-shaped marks with the proportions of printed Latin text
  (a wide body, common thin ascenders, rare thin descenders), so the orientation heuristic is
  exercised on the property it uses.
- **The mapping** — every switch reaching the ABI as the flag it names, no stage taking the
  new call, the two exported symbols resolving, a library without them failing as
  `unsupported` — is proved in `tests/Text.Ocr.Tests` against a double and, where the library
  loads, against the real library.
- **The report** — the `preprocess` object becoming `OcrPreprocessReport`, a document without
  it reading as `None`, a partial object filling its gaps, and a refused page still parsing —
  is proved on the exact documents the native half writes.
- **That a turned, skewed, noisy *scan* now reads better** needs the models, and this
  repository ships none: it is the one claim in this section no test here has watched. What
  is proved is the geometry, which is where the correction happens.

## 14. Model management: validation, identity and discovery

§3 says the models are the caller's. Until this section it meant only that: `FromModels` took two
byte arrays and handed them to `rten` unexamined. A file that was swapped, truncated by a download
that stopped halfway, or exported by another generation of the training pipeline either failed
somewhere inside the loader with a message about FlatBuffers, or — worse — loaded and produced a
reading that looked like every other reading, with nothing in the result saying which weights had
produced it. Nothing distinguished "this went wrong" from "this went wrong quietly".

Three questions are answered here, in this order: **is this file a model of the role it is meant
for**, **which model is it, reproducibly**, and **where does a caller's set of two live**.

### 14.1 What is validated, and in which order

Each layer refuses with a named defect, so an error says *what* is wrong rather than that something
is. The names are the `defect` field of the native error document and `OcrException.Defect` on the
managed side.

| Layer | Check | `defect` |
| --- | --- | --- |
| Container | The file holds no byte | `empty` |
| Container | `RTEN` magic present but the file stops inside the 32-byte v2 header | `truncated_header` |
| Container | Magic is neither `RTEN` nor an ONNX signature; the message shows the first bytes | `unrecognized_format` |
| Container | `RTEN` version is not 2 | `unsupported_version` |
| Container | The declared graph/tensor sections do not fit in the file as it is | `inconsistent_layout` |
| Container | Smaller than 4096 bytes, larger than 1 GiB | `too_small` / `too_large` |
| Graph | `rten` cannot parse the container it accepted | `unparsable_graph` |
| Graph | The input is not the shape the role needs | `input_shape` |

The two size bounds are screens, not measurements. 4096 bytes is far below the smallest published
weights and far above the text file, JSON document or empty download that usually arrives by
mistake; 1 GiB is fifty times the published weights and small enough that a caller who named the
wrong file gets an error rather than an out-of-memory kill. The floor is applied *after* the
container checks, so a truncated `RTEN` file is reported as truncated rather than as small.

The layout check is the one that catches a half download: the v2 header declares
`[magic:u8x4][version:u32][model_offset:u64][model_len:u64][tensor_offset:u64]`, all little-endian,
and those declared ranges are compared against the file's own length. A file that says its graph
runs from 32 to 12 MB but stops at 100 bytes is refused here, with both numbers in the message.

The shape check runs after the parse and before the engine is built, because `ocrs` dereferences
those dimensions at recognition time: the recognition path indexes the third dimension and the
detection path destructures four fixed ones. A recognition model of the wrong rank therefore does
not fail on load — it panics on the first page read. The check is what turns that into an
`OcrException` naming the role, the shape found and the shape expected. Detection needs a rank-4
NCHW input with one channel and fixed height and width; recognition needs rank 4, one channel, and a
non-zero fixed height if it fixes one.

The managed side keeps one exception type. `OcrException.Code` is `model`, and `Defect`, `Role` and
— when the file came from a directory — `FilePath` carry the rest. A `defect` value from a newer
library that this package does not know reads as `OcrModelDefect.Unspecified`; the file is still
refused and the message still says why.

One note on the format itself. `rten` only reads `.rten` files when the `rten` feature of `ocrs` is
enabled, and this crate now enables it (`ocrs`'s default features cover ONNX only). Without that
feature the `.rten` files §2 and §3 name would have been refused at load, so the validation here
would have blessed a file the loader could not open. Enabling it brings `rten-model-file` and
`flatbuffers` in transitively, through `ocrs`, not as direct dependencies of this crate.

### 14.2 What is not validated — read this before trusting the seal

| Not checked | What that means |
| --- | --- |
| **That the weights behave** | A well-formed container with a plausible graph and adversarial weights passes every check and can recognise wrongly. Validation proves a file is a model of the right *shape*, never that it is the *right* model. A hostile model that is well formed remains hostile. |
| **Who made it** | The SHA-256 of §14.3 is a fingerprint, not a signature. Two files with the same digest are the same bytes; nothing here says who wrote them. Provenance is the caller's policy. |
| **Graph semantics** | Operators, attributes, constant values and everywhere-but-the-input shapes are the loader's business. A model made of operators this build does not register is refused by `rten` as `unparsable_graph`, which is a load-time refusal, not a semantic audit. |
| **ONNX, beyond a sniff** | An ONNX model has no magic; it is a protocol-buffer message. `0x08` followed by a small version byte is what tells it from a text file or a PNG. The parse is the authoritative check, and a corrupt ONNX file that survives the sniff fails as `unparsable_graph`. |
| **`RTEN` v1** | The v1 format has no header, so it is indistinguishable from any other FlatBuffers message, and it is refused as `unrecognized_format`. `rten-convert` emits v2 by default; re-export if you hold a v1 file. |
| **A path that changes under you** | `OcrModelSet.LoadFromDirectory` hashes the files it reads; `OcrEngine.FromDirectory` reads them again. If something can write that directory between the two, pin the bytes instead: inspect, then `FromModels` with the same arrays. |
| **A panic inside an operator** | `catch_unwind` at every entry point turns it into `TO_ERR_PANIC` rather than an unwind into C, but it is a containment, not a prevention, and it is not specific to models. |
| **Non-UTF-8 paths** | The ABI takes UTF-8 bytes, not NUL-terminated strings and not a platform string type. A Windows path that is not valid UTF-8 is refused by name; it is never mangled. |

There is no download, no environment variable, no default directory and no fallback model: the
directory named is the directory read, and the only file names looked for are `detection.rten` and
`recognition.rten`.

### 14.3 Identity

Each model gets the SHA-256 of its bytes, as sixty-four lowercase hexadecimal characters, computed
from the same bytes the engine loaded. The pair gets

```
set = SHA-256(detection_digest || recognition_digest)
```

— the two 32-byte digests concatenated in that order. It is a pure function of the two files: two
hosts that load the same weights report the same string, swapping detection and recognition changes
it, and replacing either file changes it. The algorithm is implemented in
`native/text-ocr/src/sha256.rs` rather than pulled in as a crate, and is pinned to the published
FIPS 180-4 test vectors, so an error there fails `cargo test` instead of producing a
plausible-looking identity.

The identity surfaces in four places, all reading the same native computation:

- `OcrEngine.Models` — the identity of a loaded engine, read once and kept.
- `OcrRecognition.Models` and `OcrPageOutcome.Models` — on every result, so a report can say which
  weights produced the page. The native result document carries it as a `models` object:
  `{"detection":{…},"recognition":{…},"set":"…"}`.
- `OcrModelSet.Identity` — the preflight, without loading.
- `to_engine_models` — the ABI entry point the first three use.

An absent `models` object reads as no identity rather than as an error: a native library older than
this feature can only have loaded the bytes it was given, so there is nothing to invent.

### 14.4 Directory discovery

A caller who keeps its weights on disk names the directory and gets validation, identity and load in
one step:

```csharp
using var engine = OcrEngine.FromDirectory("/opt/ocr-models");
Console.WriteLine(engine.Models);   // models <set>: detection <digest>, recognition <digest>
```

or, when it wants to look before it loads forty megabytes:

```csharp
var set = OcrModelSet.LoadFromDirectory("/opt/ocr-models");
if (set.Identity.Sha256 != expected) throw new InvalidOperationException("unexpected weights");
```

`OcrModelSet` reads and hashes the two files and nothing else: no parse, no weights kept. A missing
file is an `OcrException` with `Defect == OcrModelDefect.MissingFile` and `FilePath` set to the path
that was looked for, so the message can name a file the caller did not type. A directory that does
not exist, or a path that is a file, has its own defect (`missing_directory`, `not_a_directory`).

`FromDirectory` takes the default Latin alphabet. A recognition model trained on another alphabet
needs the dictionary, which only `FromModels` carries, so that caller reads the two files and passes
their bytes — the validation is identical either way.

### 14.5 What the caller still has to check

The library validates shape, not trust. A host that cares which weights it ran should:

1. **Pin the identity.** Keep the expected pair digest in configuration and compare it against
   `OcrModelSet.Identity.Sha256` at start-up — before an engine exists, not after the first page.
2. **Treat a model file as code.** It is read into a process that runs it. Load only from a path the
   application controls, never from a directory an untrusted party can write.
3. **Re-check after an update.** A new digest is not an error; it is a change that a report will
   record, and that a policy may or may not accept.
4. **Act on the defect, not the prose.** `OcrException.Defect` is stable; the message is for a log.
5. **Record it.** `result.Models` on every result is what makes a later investigation answerable:
   this text was read with these weights.

What none of that fixes is the first row of §14.2: a well-formed model with hostile weights is still
hostile. Validation says the file is a model; only provenance says whose.

### 14.6 The ABI: four additive exports, revision unchanged

```c
int32_t to_model_inspect(const uint8_t *model, size_t length, int32_t kind,
                         ToBuffer *out_json, ToError **out_error);
int32_t to_model_set_inspect(const uint8_t *directory, size_t length,
                             ToBuffer *out_json, ToError **out_error);
ToEngine *to_engine_create_from_directory(const uint8_t *directory, size_t length,
                                          const uint8_t *dictionary, size_t dictionary_len,
                                          ToError **out_error);
int32_t to_engine_models(const ToEngine *engine, ToBuffer *out_json, ToError **out_error);
```

`kind` is `0` for detection and `1` for recognition; anything else is `invalid_argument`. Paths are
UTF-8 bytes and a length, like every other string on this ABI. `to_abi_version` stays **2**: nothing
that was there moved, so a host written against revision 2 runs unchanged, and a host that wants
model management calls a symbol it can check for. `FromModels` keeps its signature and its
behaviour — it now validates before it loads — so no caller changes.

### 14.7 What is proved, and where

- **The container rules** are proved in Rust, in `native/text-ocr/src/models.rs`: an empty file, a
  file that stops inside the header, a file cut at 100 bytes whose header still declares a 1 KB
  graph, a text file with no magic, an `RTEN` v3 header, a zero-length graph, an ONNX file accepted
  by its signature, and a directory missing a file (whose message names it). `cargo test --lib` runs
  them.
- **The digests** are pinned to the FIPS 180-4 vectors — empty input, `abc`, the quick brown fox,
  56 and 64 zero bytes — and the pair digest is proved order-sensitive, in `src/sha256.rs`.
- **The identity** is proved stable for the same bytes and different for a changed one, and the pair
  digest different when either model changes, in `models.rs`.
- **The managed mapping** — every defect name to `OcrModelDefect`, the role and file fields, an
  unknown defect still refusing as a model error, an `ocr` error staying a plain `OcrException`, the
  identity in a recognised page, in a refused page and on every page of a PDF, and the engine
  reporting the identity the native side recorded — is proved against a double in
  `tests/Text.Ocr.Tests`, and the directory path end to end against the real library in
  `NativeModelTests`: a missing file, junk bytes with their hex in the message, a truncated
  download, and a directory whose headers are well formed but whose graph is zeros — accepted by the
  preflight, refused by the loader as `unparsable_graph`, with no panic.
- **That a real model still loads and reads** needs the weights; those tests skip, and say so, on
  every machine that holds none.

## 15. A document in two languages: choosing the model set per page

A client folder holds a contract whose first page is French and whose second is the English
translation. §3 makes the models the caller's, and §14 makes one pair of them an engine — one
detection model, one recognition model, and one alphabet the recognition model was trained on.
Read that document with the French engine and the English page comes back as accented noise; read
it with the English one and the French page loses its diacritics. The two pages need two pairs.

This section is how a page gets the pair it needs, what happens when the pair is not there, and
what the result says about it.

### 15.1 The set per language, and the default

`OcrLanguageModelSet` pairs a language tag with an `OcrModelSet` — the validated, identified pair of
§14.4. `OcrEngine.FromLanguages` takes a list of them and one tag as the default:

```csharp
using var engine = OcrEngine.FromLanguages(
    "fr",
    [
        OcrLanguageModelSet.Load("fr", "/srv/models/fr"),
        OcrLanguageModelSet.Load("en", "/srv/models/en"),
    ]);

var pages = engine.RecognizePdf(document, languageForPage: index => index == 1 ? "en" : "fr");
```

The default tag must itself be one of the sets: a catalogue whose default has no weights would have
nothing to fall back to. A tag names one set, and one set only. Tags are compared trimmed and
case-insensitively, so `fr`, `FR` and ` fr ` are the same language; there is no language registry
here and no BCP-47 expansion — `fr-FR` is not `fr` unless the caller says so.

One alphabet applies to every set, which is what `FromLanguages`'s `characterDictionary` argument
is: a plan whose recognition models were trained on different alphabets cannot be expressed, and
that is deliberate — the dictionary is the alphabet of the whole engine in this package, and a host
that needs two alphabets reads the bytes and builds the sets itself.

### 15.2 The language is the caller's fact, so the caller supplies the rule

`RecognizePdf` takes `Func<int, string?> languageForPage`: a zero-based page index in, a language
tag (or `null`/empty for "no request") out. `Recognize` takes a single tag. The rule is asked once
per page, in page order, before the document is rendered, and the answers cross to Rust as an
aligned list.

The alternative — detect the language from the text of the page before, or from the page's own
first draft — was rejected, for three reasons:

| Rejected | Why |
| --- | --- |
| Detect the language from the previous page's text | It makes a reading depend on the other pages of the document: the same page, alone or in another order, is read by different weights. Nothing in the result would distinguish that from a deterministic read. |
| Recognise each page with every set and keep the best score | `ocrs` 0.14 exposes no decoder confidence (§4), so "best" would be the detection probability, which measures the ink, not the language. The loop has no fixed point and no honest tie-break. |
| Guess and re-run on low confidence | The guess fails exactly where it matters — a short page, a table, a page of figures — and it doubles the cost of every page to do it. |

A language is metadata the host already has: the folder it came from, the customer's locale, a
per-page annotation, a hand-written index. Passing it means the reading is reproducible from the
call alone, and a wrong answer is traceable to the rule that produced it.

### 15.3 The fallback, and what the result reports

A page whose language has no set is **not** an error, and it is **not** silent. It is read with the
default set, and the result carries a `language` object:

```json
{"requested":"de","used":"fr","fallback":true,
 "reason":"missing_language",
 "message":"the page asked for language 'de', which has no model set; it was read with the default set 'fr' (missing_language)"}
```

On the managed side that is `OcrRecognition.Language` (for an image) or `OcrPageOutcome.Language`
(for a page):

| Member | Meaning |
| --- | --- |
| `Requested` | The tag the page asked for, normalised; `null` when the page named none. |
| `Used` | The tag whose set actually read it — the default, when it fell back. |
| `FellBack` | Whether the request was substituted. |
| `Reason` | `OcrLanguageFallbackReason.MissingLanguage` when it was; `Unspecified` otherwise. |
| `Message` | One sentence naming the request, the set used and the reason; `null` when nothing was substituted. |

Three distinctions the shape is there to keep:

- **No request is not a fallback.** A page that names no language is read with the default set and
  reports `requested: null, fallback: false`. Nothing was substituted, so nothing is reported as
  substituted.
- **A fallback is a real reading.** The text is returned, with its lines and boxes, exactly as a
  page read with the right set would be. `FellBack` is what says to treat it with suspicion; the
  alternative — refusing the page — would throw away the text of a language the host simply did not
  supply.
- **A reason is a name, not a sentence.** `missing_language` is stable and a host can switch on it;
  `Message` is for a log or an error message, like every other prose in this package.

A document read through a language plan carries `languages: {default, available}` at its root, so a
consumer can tell a one-language document from a mistyped tag without reading every page.

### 15.4 The ABI: four additive exports, revision unchanged

```c
ToEngine *to_engine_create_languages(const uint8_t *plan, size_t length,
                                     ToError **out_error);

int32_t to_engine_recognize_language(
    ToEngine *engine, const uint8_t *image, size_t length,
    double page_confidence, double word_confidence, double max_rejected_word_ratio,
    const uint8_t *language, size_t language_len,
    int32_t detect_rotation, int32_t deskew, int32_t denoise,
    ToBuffer *out_json, ToError **out_error);

int32_t to_engine_recognize_pdf_languages(
    ToEngine *engine, const uint8_t *pdf, size_t length,
    uint32_t dpi, uint32_t first_page, uint32_t page_count,
    double page_confidence, double word_confidence, double max_rejected_word_ratio,
    const uint8_t *page_languages, size_t page_languages_len,
    int32_t detect_rotation, int32_t deskew, int32_t denoise,
    ToBuffer *out_json, ToError **out_error);

int32_t to_pdf_page_count(const uint8_t *pdf, size_t length,
                          ToBuffer *out_json, ToError **out_error);
```

Two variable-length shapes have to cross a boundary that admits only scalars and byte ranges, and
both cross as JSON in a byte buffer:

- **`plan`** is `{"default":"fr","dictionary":null,"sets":[{"language":"fr","directory":"…"}]}`.
  Each directory is read exactly as `to_engine_create_from_directory` reads it — two fixed file
  names, validated and identified, nothing downloaded.
- **`page_languages`** is an array with one entry per page of the rendered range, in page order:
  `["fr", "en"]`, or `null` for a page that names none. A plan whose entry count does not match the
  range is `invalid_argument`, never shifted: a shift would read a page with another page's
  language and say nothing.

`to_pdf_page_count` exists because a caller that reads to the end of a document does not know how
many pages it holds, and the plan has to be aligned before the render. It reads the page tree and
rasterises nothing.

`to_abi_version` stays **2**. Nothing that existed moved: every entry point of revisions 1 and 2
keeps its signature and its behaviour. On a handle built from a language plan they read with the
**default** set, which is what a call naming no language can honestly mean. A handle built from two
models refuses `to_engine_recognize_language` and `to_engine_recognize_pdf_languages` as
`invalid_argument` rather than guessing which set was meant; the managed side refuses earlier, with
an `InvalidOperationException` naming `FromLanguages`.

The managed side takes the old calls when no language is named, so the common path is the one that
was already proved, byte for byte. When a language is named and the library predates the exports,
the failure is `OcrException` with `Code == "unsupported"` naming the missing symbol.

### 15.5 What recognition does not do

| Not done | Why |
| --- | --- |
| Detect the language | It is the caller's fact (§15.2). The result reports what it was asked for, never a guess it made. |
| Split a page between two languages | A set is chosen per page, and the whole page is read with it. A bilingual page — two columns, a French heading over an English body — is read by one model, chosen by the rule. |
| Pick a set by script | A page of Chinese in a French plan is read with the French set and reports no fallback, because `zh` was never asked for. A caller that cares names `zh` and supplies its set, or gets a fallback. |
| Share memory between sets | Each set is one loaded engine. Two languages cost two copies of the weights; `FromLanguages` does not deduplicate two directories holding the same bytes. |
| Change the default per document at read time | The default is fixed when the engine is created. A document that wants another one builds another engine. |

### 15.6 What is proved, and where

- **The routing** is proved in Rust, in `native/text-ocr/src/language.rs`, with no model loaded:
  page 1 and page 2 of one plan route to two different sets, and the identities the two pages
  report differ — the fixture builds both from well-formed `RTEN` container headers, so the two
  sets are genuinely different bytes, and the test would fail if the loop reported one identity for
  both pages. A language with no set routes to the default and reports `missing_language`; a plan
  that asks for nothing reads every page with the default; the first page of a range takes the
  first entry; a plan whose length does not match the range is refused with both counts in the
  message; a blank tag is a request for nothing; a catalogue with a default that has no set, a
  repeated tag or an empty default is refused. `cargo test --lib` runs them.
- **The routing is the loop's**: `LanguageEngines::routes` is what the recognition loop iterates,
  and it pins each page's identity to the identity of the engine that will actually run it, so the
  report cannot name a set the loop did not use.
- **The mapping** — a language reaching `to_engine_recognize_language`, a rule resolved once per
  page in page order and reaching `to_engine_recognize_pdf_languages`, a `null` answer becoming a
  null entry, an empty tag taking the old call, and a language on a single-set engine refused rather
  than ignored — is proved in `tests/Text.Ocr.Tests` against a double, without a model.
- **The report** — the `language` object becoming `OcrLanguage`, all four of its fields, a page's
  own `models` winning over the document's, an absent object reading as no language, an unknown
  reason still reading as a fallback, and a refused page still carrying its language — is proved on
  the exact documents the native half writes.
- **The exports** are proved against the real library when it is loadable: the page count of a
  one-page document is 1, a document that is not a PDF is a `pdf` error, a plan naming a directory
  that is not there is a `model` error with `missing_directory`, a plan that is not JSON is
  `invalid_argument`, and every new symbol resolves.
- **That a two-page French/English PDF now reads correctly** needs two real model pairs, and this
  repository ships none: it is the one claim in this section no test here has watched. What is
  proved is that each page is read by the set its rule names, that the identity on each page is
  that set's, and that a missing set is reported instead of guessed.

## 16. The image ceilings: a header is read, not a page

Every other surface of the suite has had a ceiling of its own; `Text.Barcodes` got its decode ceiling
in #73, and §5 of [29](29-threat-model.md) said in as many words that the OCR image input had none:
`image::load_from_memory` ran under the decoder's default `Limits`, 512 MiB of allocation and no width
or height. An image handed to `Recognize` arrives from whoever sent the document, and nothing in this
package bounded it. This section is that bound.

### 16.1 The two numbers, and why they are these

| Ceiling | Value | Where | Why this number |
| --- | --- | --- | --- |
| Pixels, width × height | 64 000 000 | `limits::HARD_MAXIMUM_PIXELS` | An A4 page at 600 DPI is 4961 × 7016 = 34.8 megapixels, so this is nearly twice the largest page a scanner produces. It is the same ceiling `native/text-barcodes` puts on an image it decodes. |
| Encoded bytes | 256 MiB | `limits::HARD_MAXIMUM_BYTES` | The same ceiling `text-barcodes` applies, and the same order as the rasteriser's, so an image cannot be small enough to pass one gate and too large for the other. |
| Decoded allocation | pixels × 4 | `ImageBounds::decoder_limits`, `max_alloc` | The widest eight-bit layout any accepted format produces is RGBA8, so 64 megapixels cannot decode to more than 256 MiB. This is the decoder's own limit, set from the pixel ceiling. |

The PDF page ceiling is 120 megapixels (`pdf::MAX_PIXELS`) and this one is not: a rendered page's size
comes from the document's own MediaBox at a DPI the caller chose, which is a document *format* fact;
an encoded image's size comes from a header with no format constraint on either dimension, so the
tighter ceiling is the honest one. The two are also different units of work: a caller reads a page of
a document it already decided to read, while an image can be any file at all.

### 16.2 Where the size is read, and what it costs

`limits::admit` runs before the decoder allocates anything, in this order:

1. **The byte ceiling, on the length alone.** Nothing is read: an input that announces two gigabytes is
   refused by counting the bytes the caller already holds. The count never depends on the contents.
2. **The dimensions in the header.** `ImageReader::with_guessed_format` recognises the format from the
   first bytes, then `into_dimensions()` builds that format's decoder and asks it for its size. That
   reads the header and no image data:

| Format | Where the size is stated |
| --- | --- |
| PNG | The `IHDR` chunk: width and height as two big-endian `u32`. |
| JPEG | The `SOFn` frame header, the first one reached among the markers. |
| BMP | The `BITMAPINFOHEADER` (or its successor): width and height as two `i32`. |
| GIF | The logical screen descriptor: width and height as two little-endian `u16`. |
| TIFF | The first `IFD`'s `ImageWidth` and `ImageLength` tags. |
| WebP | The `VP8L` bitstream's first four bytes, the `VP8X` canvas size, or the `VP8` frame header. |

3. **The pixel ceiling, on width × height.** Only now, with both numbers in hand, is the product
   compared with the ceiling. A refusal names the width, the height, the pixel count and the ceiling.

The cost of a refusal is measured, not assumed: a counting global allocator (`allocation_probe`, a
per-thread high-water mark) records the largest single allocation the refusing call makes. It is one
decoder read buffer of about 128 KiB — 123 136 bytes for a 320 × 200 image, 131 072 for the 40 000 ×
40 000 claim — and the test compares the two, so it fails if the cost ever starts to follow the claim.
The pixel buffer is never produced, because the refusal happens before the step that would produce it.

### 16.3 What the option may change

`OcrOptions.MaximumImagePixels` and `OcrOptions.MaximumImageBytes` carry the caller's own ceilings.
Both default to 0, which means "the library's", and both may only be **lowered**:

- a value above `OcrOptions.MaximumImagePixelsCeiling` (64 000 000) or
  `MaximumImageBytesCeiling` (256 MiB) is an `ArgumentOutOfRangeException` naming the hard maximum,
  before any native call, and the ABI refuses the same request as `invalid_argument`;
- it is never clamped, because a clamp would report a ceiling the caller never wrote and the exception
  would name a number that appears nowhere in the call;
- a value of 0 is a valid request (the library's ceiling), not an absent one.

The managed side takes the `_limited` exports only when a caller asks for a ceiling, so a call that
names none goes through the path that was already proved, byte for byte. The option does not exist for
a PDF: `RecognizePdf` rasterises pages, and `pdf::MAX_PIXELS` governs those.

### 16.4 What the bound does not protect

| Not protected | Why, and what follows |
| --- | --- |
| A header that lies about its own size | The pixel check sees the claim, not the truth. The decoder's `Limits` — width, height and `max_alloc`, derived from the same ceilings — are the second line, and they refuse rather than allocate. A file whose header understates its size is therefore bounded by the decoder, not by this check, and the refusal it produces is the decoder's `image` error rather than the typed `image_limit` one. |
| A wide pixel format at the ceiling | `max_alloc` is the pixel ceiling at four bytes per pixel. A 64-megapixel RGBA16 image needs 512 MiB and is refused by the allocation, at half the pixel ceiling, with the decoder's message. Budget 256 MiB per decoded image, not less. |
| The peak while preprocessing | The decoded RGB8 (3 bytes per pixel) and the preprocessed copy coexist, so a 64-megapixel image costs about 2 × 192 MiB at its peak. The ceilings bound each buffer, not the sum. |
| Time | Nothing here bounds wall-clock time. A 64-megapixel image within every ceiling is read as slowly as it is read; a host that accepts uploads needs its own timeout and process isolation (threat model §6). |
| The rest of the file | `to_image_admit` reports the size and refuses past a ceiling. It does not promise the decoder will accept the remaining bytes: a truncated or corrupt image still fails later, as an `image` error. |
| Pages of a PDF | A page's raster is bounded by `pdf::MAX_PIXELS`, a different ceiling, checked after the pixmap exists. |

### 16.5 The ABI: four additive exports, revision unchanged

| Export | What it adds |
| --- | --- |
| `to_image_admit(image, length, maximum_pixels, maximum_bytes, out_json, out_error)` | Reports `{"width","height","pixels","format","maximumPixels","maximumBytes"}` or refuses. It is the same check the recognition path runs, reachable with no engine and no model, which is what lets the managed tests prove the refusal without weights. |
| `to_engine_recognize_limited` | `to_engine_recognize` with the two ceilings. |
| `to_engine_recognize_preprocessed_limited` | `to_engine_recognize_preprocessed` with the two ceilings. |
| `to_engine_recognize_language_limited` | `to_engine_recognize_language` with the two ceilings. |

A refusal crosses as a new status, `TO_ERR_IMAGE_LIMIT` (8), whose code name is `image_limit`. The
error object carries `limit` (`pixels` or `bytes`), `observed`, `maximum`, and for a pixel refusal the
`width` and `height` it measured, so a host does not have to parse the sentence. The managed side turns
that code into `OcrImageLimitException`, which exposes `Limit`, `Observed` and `Maximum` and still
derives from `OcrException`, so a caller that already catches that keeps working.

No existing signature changed and `to_abi_version` is still **2**: a host of revision 2 sees four new
symbols and no other difference, and the old entry points keep the default ceilings.

### 16.6 What is proved, and where

- **The refusal happens before the allocation** — `limits.rs`, in
  `a_png_that_announces_forty_thousand_square_is_refused_before_a_pixel_is_allocated`: the fixture is a
  PNG whose `IHDR` declares 40 000 × 40 000 with no picture data at all, and the counting allocator
  shows the refusal allocates one fixed read buffer, the same size as for a 320 × 200 image. The test
  compares the two measurements, so it fails if the cost ever starts to follow the claim.
- **The byte ceiling is applied to the length alone** —
  `an_input_of_two_gibibytes_is_refused_on_bytes_without_being_read` proves the arithmetic at 2 GiB,
  which no test materialises: the check takes a length, and the refusal names `2147483648` and
  `268435456`. `an_image_past_the_byte_ceiling_is_refused_before_its_header_is_read` pins the same
  ceiling end to end, on a real PNG one byte past a lowered ceiling.
- **A normal image passes, and a lowered ceiling refuses one that passed** —
  `a_normal_image_is_admitted_and_decoded` and `a_lowered_ceiling_refuses_an_image_that_passed_the_default`,
  which asserts `observed: 800` and `maximum: 100` in the error object and both numbers in the sentence.
- **Every accepted format is measured from its header** —
  `every_format_this_build_accepts_is_measured_from_its_header` covers PNG, JPEG, BMP, GIF, TIFF and
  WebP (a hand-written lossless header, because `image` encodes no WebP), and
  `the_header_of_every_format_is_read_without_allocating_pixels` measures the cost of each.
- **The option may only lower** — `a_ceiling_above_the_hard_maximum_is_refused_rather_than_clamped`,
  `zero_takes_the_default_ceiling`, `a_ceiling_below_the_hard_maximum_is_honoured`, and the decoder
  limits they produce.
- **The bound is load-bearing** — with `check_pixels` removed from `admit`, five tests fail (the 40 000
  square refusal, the WebP one, the lowered-ceiling one, the `prepare_image` one and the ABI one).
- **The ABI maps** — `admitting_a_forty_thousand_square_png_is_a_typed_refusal_through_the_abi` and
  `admitting_a_normal_image_reports_the_size_its_header_states` in `lib.rs`; managed-side in
  `tests/Text.Ocr.Tests/ImageLimitTests.cs`: eleven tests. Seven need no native library — five for the
  option's range rules, and two driven through a recording `IOcrNative` that prove a lowered ceiling
  reaches the limited export with the numbers the caller wrote and that a call naming none keeps the
  plain one. Four run against the real library when it is loadable: the typed exception with both
  numbers, the header report, the byte refusal, and a ceiling above the maximum.
- **Counts** — `cargo test --lib` in `native/text-ocr`: 94 passed, 0 failed (76 before this change);
  `dotnet test tests/Text.Ocr.Tests -c Release` with the native library present: 222 total, 218 passed,
  0 failed, 4 skipped by their own guards (211 before, and the 11 new tests).
- **What is not proved here** — that a *real* 64-megapixel scan reads: that needs models, and this
  repository ships none. That a real WebP file decodes: only its header path is proved. And that the
  2 GiB byte ceiling refuses a real 2 GiB buffer: the buffer is never produced, so the arithmetic is
  what is proved.
