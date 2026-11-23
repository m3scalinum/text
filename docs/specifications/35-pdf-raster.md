# 35 — PDF raster: one page as an image

## 1. Scope

`PdfRaster` renders one page of a PDF as a PNG or a JPEG. It exists for two callers that need pixels
rather than a document: a preview pane that shows the first page of a file the user just received,
and a recogniser that has to read a page whose text layer is empty (`M3scalinum.Text.Ocr` takes the
image this returns). PNG is the default; JPEG is what the preview asks for when the bytes on the wire
matter, and section 6 is the measurement that says when each one wins.

`PdfRaster` is the entry point:

- `PdfRaster.Render(byte[] pdf, int page, PdfRasterOptions? options = null)` returns a
  `PdfPageImage`: the encoded bytes, the format, the pixel size, the resolution asked for and the
  resolution the pixels actually carry.
- `PdfRaster.PageCount(byte[] pdf)` returns the page count, for a preview that has to number its
  pages before it renders one.

**In scope.** Rendering any page of a document to PNG or JPEG at a caller-chosen resolution, with a
caller-chosen JPEG quality; a pixel ceiling and a memory ceiling that are checked before the renderer
allocates; typed errors for the four ways a page fails to render; a native ABI that cannot unwind
into the host.

**Out of scope, deliberately.**

| Not done | Why |
| --- | --- |
| Decryption | The package already has one entry point that returns plaintext bytes, `PdfEncryption.Open`. A second one, with its own key derivation, is a second place for that decision to go wrong. An encrypted document is refused and the message names the way out. |
| SVG, text, or a canvas | Three different products, each with its own package. This one returns pixels. |
| Rendering more than one page in one call | A caller with a page range loops. One call renders one page, which is the granularity at which a ceiling makes sense. |
| Thumbnail-scale resolutions | Below 72 DPI a page is a downscale of a rendered page, not a render: the renderer's antialiasing has nothing left to average at one pixel per point. A thumbnail is a cheaper resolution plus a resize. |

## 2. The API

```csharp
using M3scalinum.Text.Pdf;

byte[] pdf = File.ReadAllBytes("received.pdf");

PdfPageImage preview = PdfRaster.Render(pdf, 1);

Console.WriteLine($"{preview.Page}/{preview.PageCount} at {preview.Width}x{preview.Height}, {preview.Format}");
File.WriteAllBytes("preview.png", preview.Bytes);
```

A higher resolution, and the two ceilings lowered for a hostile upload:

```csharp
PdfPageImage large = PdfRaster.Render(
    pdf,
    3,
    new PdfRasterOptions
    {
        Dpi = 300,
        MaximumPixels = 20_000_000,
        MaximumBytes = 64L * 1024 * 1024,
    });
```

A page that cannot be rendered throws, and the reason is typed:

```csharp
try
{
    PdfPageImage image = PdfRaster.Render(pdf, 4);
}
catch (PdfRasterException exception) when (exception.Error == PdfRasterError.PageOutOfRange)
{
    // The document has fewer pages than the caller asked for; exception.Message names the count.
}
```

A page rendered as a JPEG at the default quality, which is the shape a preview wants:

```csharp
PdfPageImage thumbnail = PdfRaster.Render(
    pdf,
    1,
    new PdfRasterOptions { Format = PdfPageImageFormat.Jpeg });
```

`PdfPageImage` carries `Bytes`, `Format`, `Page`, `PageCount`, `Width`, `Height`, `Dpi` and
`EffectiveDpi`. `PdfRasterOptions` carries `Dpi`, `Format`, `Quality`, `MaximumPixels` and
`MaximumBytes`. `PdfRasterError` is `NotAPdf`, `Encrypted`, `PageOutOfRange`, `Limit`, `Unsupported`,
`InvalidArgument`, `Internal` or `Panic`.

Pages are numbered from **1**, the way a reader numbers them. A page number below 1 is an
`ArgumentOutOfRangeException` from the managed side, without loading the library; a page past the
last one is a `PdfRasterException` with `Error == PageOutOfRange` from the native side, because only
the document knows how many pages it has.

## 3. The resolution, and what "effective" means

`PdfRasterOptions.DefaultDpi` is **150**. A4 at 150 DPI is 1240×1754 pixels: a preview that stays
sharp at 100 % on a HiDPI screen and still the floor the OCR documentation calls readable. A caller
that feeds the image to a recogniser asks for 300, which is `OcrPdfOptions`'s own default.

`Dpi` is between `MinimumDpi` (72) and `MaximumDpi` (1200). The renderer works in points and scales
by `Dpi / 72`, truncating each dimension to a whole pixel. That truncation is why `PdfPageImage`
carries two numbers: `Dpi` is what was asked for, and `EffectiveDpi` is `Width` divided by the
page's width in inches — 595 pt at 150 DPI is 1240 px wide and 149.94 DPI, not 150. A caller that
aligns this image with another renderer's should scale by `EffectiveDpi`.

## 4. The ceilings

Two numbers, both checked against the page geometry **before the renderer allocates a pixmap**.

| Option | Default | Hard maximum | What it bounds |
| --- | --- | --- | --- |
| `MaximumPixels` | 50 000 000 (A2 at 300 DPI) | 200 000 000 | `width × height` of the page |
| `MaximumBytes` | 256 MiB | 1 GiB | `width × height × 4`, the RGBA8 pixmap the renderer draws into |

A ceiling may be lowered as far as the caller likes. Raising one past its hard maximum is refused
with an `ArgumentOutOfRangeException` rather than clamped: a silently lowered cap is a promise the
caller believes it has.

This is the security property of the module. A page's `MediaBox` comes from the sender, so without
the ceiling a 200-inch square page at 1200 DPI is 24 gigapixels — an allocation that succeeds on a
machine with enough memory and kills the process on the one that does not. A page that exceeds
either ceiling is a `PdfRasterException` with `Error == Limit` naming the size it would have had.

The ceiling is not a proof of total memory use: the renderer holds its own caches and the document
is parsed first. `docs/specifications/29-threat-model.md` is the accounting; this section states
what the two knobs bound.

## 5. The output

Both formats are 8-bit and opaque. The renderer's background is set to white rather than left
transparent, so a page with no background paint — which is most pages — comes out as text on paper
instead of a black rectangle with an alpha channel. That is also what a recogniser needs: it reads
luminance, and a transparent background reads as black.

The bytes are byte-exact to what the encoder emitted: this package does not re-encode, crop, or touch
up the result. `PdfPageImage.Format` is the format the caller asked for, and it is the format of the
bytes — section 6 is what that means for each one.

## 6. PNG or JPEG: the format and its quality

`PdfRasterOptions.Format` is `PdfPageImageFormat.Png` by default and `PdfPageImageFormat.Jpeg` when
the caller names it. `PdfRasterOptions.Quality` is the JPEG quality, **1 to 100, default 85**. A
quality outside the range, and a `Format` that is not a member of the enum, are an
`ArgumentOutOfRangeException` from the managed side before the library is loaded; the native side
refuses the same combinations with `InvalidArgument` if they are reached another way.

The renderer draws once, into the same premultiplied RGBA8 pixmap, whichever format is asked for.
PNG comes from the renderer's own encoder; JPEG comes from the `image` crate's baseline encoder,
handed the pixmap's own RGB channels. Nothing is converted silently: the alpha channel is dropped for
JPEG because a JPEG has none, and every page this module produces is opaque white behind its content,
so dropping it changes no pixel. A pixel the renderer did leave translucent is composited over white
rather than muted towards black, which is what a reader would see on paper.

### The dependency

`image` 0.25 is **already in this crate's graph**: `hayro` uses it to decode PNG. Declaring it
directly adds the `jpeg` feature and nothing else — no new package, no new entry in `Cargo.lock`
beyond the dependency edge, and the `zune-jpeg`/`zune-core` pair the feature pulls in is already
locked through `hayro-syntax`. It is MIT OR Apache-2.0, the same terms as the rest of the suite, and
it already has a row in
[`dependencies.md`](dependencies.md), so the policy in `deny.toml` and
`tools/check-dependencies.py` is satisfied without a new decision. The alternative — writing a
baseline JPEG encoder by hand — is several hundred lines of DCT, quantisation and Huffman tables to
match an encoder that is already downloaded and already reviewed. The manifest keeps
`default-features = false` and enables only `jpeg`, so `rayon` and the dozen other codecs stay out of
the build.

### The measured sizes

A4 at 150 DPI, 1239×1754 pixels, one render of the same page through each encoder. The fixtures are
in the tests (`cargo test --lib` prints the crate's figures; `PdfRasterJpegTests` measures the same
relation through the package).

**A scanned page** — the page is a full-page image at the page's own resolution, the way an incoming
scan arrives:

| Encoding | Bytes | Ratio |
| --- | --- | --- |
| PNG | 3 096 451 (2.95 MiB) | — |
| JPEG, quality 85 (default) | 386 594 (378 KiB) | **8.0× smaller** |
| JPEG, quality 50 | 156 879 | 19.7× |
| JPEG, quality 95 | 836 859 | 3.7× |
| JPEG, quality 1 | 62 961 | 49× |
| JPEG, quality 100 | 1 537 351 | 2.0× |

The managed test measures the same page through the package with a slightly different scan and gets
PNG 3 061 049 bytes against JPEG 473 325 bytes at quality 85 — 6.5×.

**A page of vector text** — 62 lines of 9 pt Helvetica, nothing else:

| Encoding | Bytes |
| --- | --- |
| PNG | 321 917 |
| JPEG, quality 85 | 598 599 |

This is the honest half of the answer and the reason the default is still PNG: at 150 DPI a page of
crisp glyphs is high-frequency content that a DCT spends bits on and a deflate stream does not, so
**the PNG is the smaller file for flat vector art**. JPEG wins on the continuous-tone page, which is
the page a preview usually has to show — a scan, a photograph, a screenshot of a map — and there it
wins by most of an order of magnitude. A caller that cannot tell which it has should measure, or
render PNG and keep JPEG for the cases it knows.

The ceiling accounting shifts a little for JPEG: the pixmap is four bytes per pixel and the RGB
buffer handed to the encoder is three more, so the peak of a JPEG render is at most 7/4 of
`MaximumBytes`. Both are proportional to the pixel count the ceiling already bounds. The ceilings are
checked, as before, on the page geometry before the renderer allocates anything.

### What JPEG does not do

| Not available | Consequence |
| --- | --- |
| Transparency | JPEG has no alpha channel. A page is opaque here, so nothing is lost today; a future option that renders a transparent background cannot be JPEG. |
| More than 8 bits per channel | The pixmap is 8-bit and the JPEG encoder takes 8-bit. A 16-bit render would not survive. |
| Losslessness | The compression is irreversible: glyph edges ring, thin hairlines soften, and the artefacts are in the file for good. A page that has to be read rather than shown — an OCR pass, a signature's appearance compared byte for byte — should stay PNG, which is byte-exact. |
| Chroma subsampling or a progressive scan | The encoder writes baseline 4:4:4 at the quality given. There is no knob for either, and the size figures above are what 4:4:4 costs. |

## 7. What the render does not do

The renderer is `hayro` 0.9, a pure-Rust CPU rasteriser. Its own documentation lists what it does not
implement yet, and this package inherits the list:

| Not drawn | Consequence |
| --- | --- |
| Annotations and form fields | A page's `/Annots` (a signature's appearance, a text field's content, a stamp) are not part of the page content stream and are not drawn. An empty rectangle where a signed field should be is expected. `PdfForm` and `PdfSignatureInfo` are what report them. |
| Optional content groups (layers) | The default configuration is drawn; a layer the document hides is still drawn if it is in the content stream. |
| Advanced transparency: blending and isolation groups, knockout groups | A page that relies on them renders with the wrong colours in the places they apply. Ordinary alpha compositing is fine. |
| Colour-key masking and a range of smaller image features | An image using them may render with the wrong transparency. |
| JavaScript and interactive behaviour | Nothing here executes anything, which is the point. |
| Fonts the document does not embed | `hayro` carries the standard fonts and does not look at the host's font files, so a page that names an absent font falls back rather than failing. Text may be wider or narrower than the producer intended. |

Two of these are worth saying twice, because they are what a preview gets wrong. **A signature does
not appear on the rendered page**: a rendered PDF is not a verified PDF, and the preview of a signed
contract will not show the signature's box unless the producer baked an annotation into the content.
**A form renders empty**: the field values live in `/AcroForm`, not on the page.

## 8. The native ABI

`native/text-pdf` is a `cdylib` following the compiler crate's conventions exactly, with the `tp_`
prefix so a process that loads several of this suite's libraries never has to guess which answered:

| Convention | How it holds |
| --- | --- |
| Guarded entry points | Every export runs inside `catch_unwind`; a panic becomes `TP_ERR_PANIC`, never an unwind into C. |
| Owned buffers | The image and the metadata cross as `TpBuffer`, allocated by Rust and freed by `tp_buffer_free`. |
| Split payloads | The encoded page crosses as bytes in one buffer and its description as JSON in another: base64 in the JSON would cost a third more memory for nothing. |
| An error object | `tp_error_code`, `tp_error_json`, `tp_error_free`, as in `sharp-typst`. |
| A version handshake | `tp_abi_version` returns 1, and `tp_build_info` names the crate and the `hayro` series it carries. |

| Export | Purpose |
| --- | --- |
| `tp_abi_version` | The ABI revision this library implements. |
| `tp_build_info` | `{"crate","version","renderer","format","formats"}`, valid for the life of the process. `format` is the encoding a render gets when the caller names none; `formats` is every encoding the build can write. |
| `tp_buffer_free` | Frees a buffer this library returned. |
| `tp_error_free`, `tp_error_code`, `tp_error_json` | The error object. |
| `tp_page_count` | `data`, `len`, `out_error` → the page count, or `-1` with the error set. |
| `tp_page_render` | `data`, `len`, `page`, `options_json`, `out_image`, `out_json`, `out_error` → a status. |

The status codes keep the numbers the compiler's ABI gave them: `0` ok, `1` invalid argument, `3`
panic, `9` limit, `10` unsupported; `11` not a PDF, `12` encrypted, `13` page out of range and `14`
internal are this library's own. The `code` inside the error document is what
`PdfRasterException.Error` reports, so a status may be added without the managed side learning a
status table.

`options_json` is optional;
`{"dpi":300,"maxPixels":20000000,"maxBytes":67108864,"format":"jpeg","quality":85}` is the shape, and
an absent field takes its default. `format` is `"png"` or `"jpeg"` and anything else is refused with
`InvalidArgument` naming what was asked for; `quality` is 1 to 100 and is refused outside that range.
The metadata the render returns carries the `format` of the bytes it wrote, not the one that was
asked for, so a caller that reads the two can never be misled by a mapping table.

The managed side loads the library through `SHARPTEXTPDF_NATIVE_PATH` when it is set, then through
the `runtimes/<rid>/native/` layout under the application directory. The seven RIDs the package ships
are `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `linux-musl-x64`, `linux-musl-arm64` and `osx`;
a musl entry is a staticlib linked by hand with `musl-gcc`, because Rust cannot emit a cdylib for a musl
target.

## 9. What proves it

The crate's own tests render a one-page PDF built inside the test — real cross-reference offsets, a
200×100 pt page — and assert the pixel size, the PNG signature, the `IHDR` dimensions, the
page-range refusal and both ceilings. The JPEG tests render two fixtures of their own: a page of 62
lines of text and a scanned page whose content is a full-resolution image, and assert that a JPEG is
much smaller than the PNG of the scanned page, that quality 100 is a larger file than quality 1, that
the bytes carry a JPEG's SOI/APP0/EOI markers and not a PNG's signature, that the JPEG decodes back
to the declared pixel size in RGB, that an unknown format name and a quality outside 1–100 are
refused, and that the format the caller asked for is the one `format` reports. They run on every RID
of the native matrix, `cargo test --lib` under `--target` included, so a page renders identically on
musl and on MSVC.

The managed tests render for real where the library is present and skip with a message naming
`SHARPTEXTPDF_NATIVE_PATH` where it is not. The job that gates `M3scalinum.Text.Pdf`'s coverage
downloads the `linux-x64` library, so the raster paths are counted rather than skipped; the
encrypted-document test reads the same AES-256 fixture the encryption tests read, and asserts the
refusal is `PdfRasterError.Encrypted` with the package's own message — including when a JPEG was
asked for, because the refusal happens before any encoding. `PdfRasterJpegTests` walks the JPEG
segment chain to read the frame header's width, height and component count, so the managed side
proves the file is a JPEG of the right size without referencing an imaging library of its own.
