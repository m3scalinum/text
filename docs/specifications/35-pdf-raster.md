# 35 — PDF raster: one page as an image

## 1. Scope

`PdfRaster` renders one page of a PDF as a PNG. It exists for two callers that need pixels rather
than a document: a preview pane that shows the first page of a file the user just received, and a
recogniser that has to read a page whose text layer is empty (`M3scalinum.Text.Ocr` takes the image
this returns).

`PdfRaster` is the entry point:

- `PdfRaster.Render(byte[] pdf, int page, PdfRasterOptions? options = null)` returns a
  `PdfPageImage`: the encoded bytes, the format, the pixel size, the resolution asked for and the
  resolution the pixels actually carry.
- `PdfRaster.PageCount(byte[] pdf)` returns the page count, for a preview that has to number its
  pages before it renders one.

**In scope.** Rendering any page of a document to PNG at a caller-chosen resolution; a pixel ceiling
and a memory ceiling that are checked before the renderer allocates; typed errors for the four ways
a page fails to render; a native ABI that cannot unwind into the host.

**Out of scope, deliberately.**

| Not done | Why |
| --- | --- |
| Decryption | The package already has one entry point that returns plaintext bytes, `PdfEncryption.Open`. A second one, with its own key derivation, is a second place for that decision to go wrong. An encrypted document is refused and the message names the way out. |
| JPEG | The renderer's own encoder writes PNG. A JPEG encoder is a second dependency for a format a caller can produce in three lines with any imaging library; the enum exists so it can be added without a breaking change. |
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

`PdfPageImage` carries `Bytes`, `Format`, `Page`, `PageCount`, `Width`, `Height`, `Dpi` and
`EffectiveDpi`. `PdfRasterOptions` carries `Dpi`, `MaximumPixels` and `MaximumBytes`.
`PdfRasterError` is `NotAPdf`, `Encrypted`, `PageOutOfRange`, `Limit`, `Unsupported`,
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

PNG, always 8-bit, always opaque. The renderer's background is set to white rather than left
transparent, so a page with no background paint — which is most pages — comes out as text on paper
instead of a black rectangle with an alpha channel. That is also what a recogniser needs: it reads
luminance, and a transparent background reads as black.

The PNG is byte-exact to what the encoder emitted: this package does not re-encode, crop, or touch up
the result.

## 6. What the render does not do

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

## 7. The native ABI

`native/text-pdf` is a `cdylib` following the compiler crate's conventions exactly, with the `tp_`
prefix so a process that loads several of this suite's libraries never has to guess which answered:

| Convention | How it holds |
| --- | --- |
| Guarded entry points | Every export runs inside `catch_unwind`; a panic becomes `TP_ERR_PANIC`, never an unwind into C. |
| Owned buffers | The image and the metadata cross as `TpBuffer`, allocated by Rust and freed by `tp_buffer_free`. |
| Split payloads | The PNG crosses as bytes in one buffer and its description as JSON in another: base64 in the JSON would cost a third more memory for nothing. |
| An error object | `tp_error_code`, `tp_error_json`, `tp_error_free`, as in `sharp-typst`. |
| A version handshake | `tp_abi_version` returns 1, and `tp_build_info` names the crate and the `hayro` series it carries. |

| Export | Purpose |
| --- | --- |
| `tp_abi_version` | The ABI revision this library implements. |
| `tp_build_info` | `{"crate","version","renderer","format"}`, valid for the life of the process. |
| `tp_buffer_free` | Frees a buffer this library returned. |
| `tp_error_free`, `tp_error_code`, `tp_error_json` | The error object. |
| `tp_page_count` | `data`, `len`, `out_error` → the page count, or `-1` with the error set. |
| `tp_page_render` | `data`, `len`, `page`, `options_json`, `out_image`, `out_json`, `out_error` → a status. |

The status codes keep the numbers the compiler's ABI gave them: `0` ok, `1` invalid argument, `3`
panic, `9` limit, `10` unsupported; `11` not a PDF, `12` encrypted, `13` page out of range and `14`
internal are this library's own. The `code` inside the error document is what
`PdfRasterException.Error` reports, so a status may be added without the managed side learning a
status table.

`options_json` is optional; `{"dpi":300,"maxPixels":20000000,"maxBytes":67108864}` is the shape, and
an absent field takes its default.

The managed side loads the library through `SHARPTEXTPDF_NATIVE_PATH` when it is set, then through
the `runtimes/<rid>/native/` layout under the application directory. The six RIDs the package ships
are `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `linux-musl-x64` and `osx`; the musl entry is
a staticlib linked by hand with `musl-gcc`, because Rust cannot emit a cdylib for a musl target.

## 8. What proves it

The crate's own tests render a one-page PDF built inside the test — real cross-reference offsets, a
200×100 pt page — and assert the pixel size, the PNG signature, the `IHDR` dimensions, the
page-range refusal and both ceilings. They run on every RID of the native matrix, `cargo test --lib`
under `--target` included, so a page renders identically on musl and on MSVC.

The managed tests render for real where the library is present and skip with a message naming
`SHARPTEXTPDF_NATIVE_PATH` where it is not. The job that gates `M3scalinum.Text.Pdf`'s coverage
downloads the `linux-x64` library, so the raster paths are counted rather than skipped; the
encrypted-document test reads the same AES-256 fixture the encryption tests read, and asserts the
refusal is `PdfRasterError.Encrypted` with the package's own message.
