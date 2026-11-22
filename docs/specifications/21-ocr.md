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
