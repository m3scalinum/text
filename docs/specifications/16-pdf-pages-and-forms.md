# 16 — PDF page operations and form filling

## 1. Scope

`M3scalinum.Text.Pdf` gains two entry points that change a document instead of reading or signing
it:

- `PdfPages` — `Merge`, `Extract`, `Split`, `Rotate`, `Remove`.
- `PdfForm` — `Fill`, `Flatten`.

Both are bytes-in, bytes-out like the rest of the package: the caller compiles a document, or
receives one, and passes the bytes. No type here refers to the compiler, and no operation reaches
the network, the file system or a font.

**In scope.** Renumbering a page tree and writing a fresh cross-reference table; carrying each
page's content, resources, fonts, images and annotations; reading and writing `/Rotate`; setting
`/V` on an AcroForm field and redrawing the appearance a widget already carries; drawing widget
appearances into page content and removing the fields that were drawn.

**Out of scope, deliberately.**

| Not done | Why |
| --- | --- |
| Encryption and decryption | `PdfEncryption` owns both (see [19](19-pdf-encryption.md)); a page operation refuses an encrypted document rather than half-rewriting it. |
| Text extraction | `PdfText` owns it (see [18](18-pdf-text.md)); a page operation rebuilds bytes and never interprets characters. |
| Redaction, OCR | Not a page operation; both need interpretation of the page. |
| Rebuilding tagging, outlines or named destinations after a page change | A structure tree spans pages and has no meaning once pages are renumbered out of it. |
| Regenerating an appearance a field does not have | The value is written; the viewer draws it. A field the writer cannot draw is reported, not guessed. |

## 2. Rebuild, not incremental update

Every operation in this document builds a new file: objects are copied into a fresh numbering, the
page tree names the pages that survive, and one classic cross-reference table covers the result.
The input is never modified.

**Decision — rebuild instead of appending.** `PdfSigner` appends because a signature must cover
the bytes that already existed. A page operation has the opposite requirement: after removing
object 3, the remaining entries have to agree with the page tree, and an incremental update would
keep the removed page reachable and leave dangling kids. Rebuilding makes the result a plain PDF
with no history, which is what strict readers and pre-flight tools accept most readily.

**Consequence — signatures.** A signature covers the file it was made over. Filling, flattening,
rotating, extracting or merging a signed document therefore invalidates its signatures; this is a
property of PDF, not a limitation of the implementation.

A font shared by two pages is copied once: the copier maps each source object number to one target
number before it walks the object, so references to one font, image or content stream stay shared
and cycles terminate. Streams are copied with their encoded bytes and filter unchanged; only
`/Length` is rewritten.

## 3. `PdfPages`

```csharp
byte[] merged = PdfPages.Merge(first, second);
byte[] mergedFromStreams = PdfPages.Merge(firstStream, secondStream);

byte[] selected = PdfPages.Extract(merged, "1-3,5");
IReadOnlyList<byte[]> parts = PdfPages.Split(merged, 2);
byte[] rotated = PdfPages.Rotate(selected, 90, "1");
byte[] without = PdfPages.Remove(merged, "2-");
```

### 3.1 Page selections

`pageRanges` uses the semantics of the compiler's `PageRanges` export option: one-indexed,
comma-separated, a closed range (`1-3`), an open end (`2-`), an open start (`-3`), or a single
page (`5`). Whitespace around a part is ignored.

A selection is a **set**, not a sequence. `3,1` selects pages one and three and the result keeps
the document's own order; repeating a page does not repeat it in the output. A selection that
names a page the document does not have throws `ArgumentOutOfRangeException`; text that is not a
selection throws `ArgumentException`.

### 3.2 `Merge`

`Merge(params byte[][])` and `Merge(params Stream[])`, where each stream is read from its current
position to its end and is left open. Pages appear in the order the documents are given, and the
pages of each document in their own order. The output claims the highest header version among the
inputs.

The merged catalog is built for the purpose: `/Type`, `/Pages`, and the first document's `/Lang`.
Each page keeps its own dictionary, media box, crop box, rotation, resources and annotations, with
inherited attributes written onto the page because the inherited parent is gone. References
between documents do not exist, so nothing can dangle.

### 3.3 `Extract`, `Split`, `Remove`

`Extract` rebuilds the document with the selected pages. `Split` returns consecutive parts of at
most `pagesPerFile` pages, each a complete PDF. `Remove` keeps the pages the selection does not
name and refuses a selection that would leave none.

These three drop the document-level structures a page tree cannot carry: the structure tree and
`/MarkInfo`, outlines, named destinations, and the `/AcroForm` dictionary. Widget annotations stay
with their page and still draw their appearance, but the fields are no longer interactive. This is
a documented limit, not an accident: a form that spans several pages cannot keep its field tree
when half of those pages are gone.

An annotation whose destination or action points at a page the operation dropped is not written:
the copier reports the reference and the whole annotation is skipped, so no destination points
into a page the file does not hold.

### 3.4 `Rotate`

`Rotate(pdf, degrees, pageRanges)` adds `degrees` to the selected pages' `/Rotate`, clockwise,
normalising the sum to `0`, `90`, `180` or `270`. An angle that is not a multiple of 90 is refused.
The value is written directly on each page dictionary, inherited rotation included, so a reader
that ignores inheritance still sees it.

Unlike the other page operations, rotate copies the catalog as it is. `/Pages` resolves to a copied
page tree whose kids are the copies, so tagging, outlines and AcroForm fields remain coherent and a
rotated form stays fillable.

## 4. `PdfForm`

### 4.1 `Fill`

```csharp
byte[] filled = PdfForm.Fill(pdf, new Dictionary<string, string>
{
    ["customer.name"] = "ACME",
    ["accepted"] = "Yes",
});
```

Field names are fully qualified: the `/T` values from the root of the field tree joined with a dot.
A name that matches no field is refused rather than ignored, because a caller who believes a value
was written and finds a blank field has no way to notice.

- **Text and choice fields** (`/FT /Tx`, `/Ch`): `/V` receives the value. The value is written as
  Latin-1 when every character fits and as UTF-16BE otherwise.
- **Buttons** (`/FT /Btn`): the value names an appearance state. `Yes`, the name of a state, or
  `On` selects it; `Off`, `false`, `0` or an empty string clears it. `/AS` is set on every widget,
  and a state the form does not offer is refused instead of silently producing an unchecked box.
- **Signature fields** are refused: overwriting `/V` would destroy the signature.
- **Appearance.** When a widget already carries `/AP /N` as a stream, the stream is replaced by one
  that draws the new value with the font named by `/DA` (Helvetica when the form names none). The
  rectangle and resources of the old appearance are reused. A widget with no appearance stream gets
  `/V` only; a viewer with `NeedAppearances` draws it.

### 4.2 `Flatten`

`Flatten` draws each widget's current appearance into the page, as a Form XObject placed at the
widget's `/Rect`, then removes the widget from `/Annots` and its field from the form. It returns a
`PdfFormFlattenResult`:

```csharp
PdfFormFlattenResult result = PdfForm.Flatten(pdf);
byte[] flattened = result.Document;

foreach (PdfFormFlattenSkip skip in result.Skipped)
{
    Console.WriteLine(skip.FieldName + ": " + skip.Reason);
}
```

**Decision — a field that cannot be drawn is left alone.** Flattening is refused for a widget with
no usable appearance stream, an empty `/Rect`, an appearance with no usable `/BBox`, a widget that
is not listed by any page, and for a signature field. Those fields stay in the form and are
reported with a reason. A flattened file that lost its values looks finished and is wrong; a file
that still has one interactive field is visibly unfinished and correct. `/AcroForm` is removed only
when every field was flattened, which is what `IsComplete` reports.

**Decision — the appearance is placed, not regenerated.** The stream the widget already draws is
copied as a Form XObject and mapped from its `/BBox` onto the widget's `/Rect`. Its own
`/Resources` travel with it, so a field drawn in an embedded font keeps that font. The appearance's
`/Matrix` is not applied: a producer that stores a non-identity matrix on a widget appearance is
not something this writer can place correctly, and the alternative — a field drawn in the wrong
place — is worse than refusing it.

Flattening does not edit the structure tree. A tagged document that used `/OBJR` to point at a
widget keeps that reference; the visual result is correct and the tagging of the form is not
rebuilt, which is why flattening a tagged form should be followed by the conformance check.

## 5. Failure model

| Situation | Result |
| --- | --- |
| Bytes are not a PDF | `PdfOperationException` |
| Document is encrypted | `PdfOperationException`; nothing is written |
| Catalog or page tree unreadable | `PdfOperationException` |
| Page selection out of range | `ArgumentOutOfRangeException` |
| Page selection malformed | `ArgumentException` |
| Removing every page | `PdfOperationException` |
| Filling a name that is not a field | `ArgumentException` |
| Filling a signature field | `PdfOperationException` |
| Filling an appearance state the button does not have | `PdfOperationException` |
| Flattening a field it cannot draw | Field left interactive, reported in `Skipped` |

A partially written document is never returned: validation happens before the writer is invoked, and
the writer builds the whole file in memory.

## 6. Verification

The tests in `tests/Text.Pdf.Tests` operate on files `typst-pdf 0.15.1` produced through
`M3scalinum.Text.Typst` — the same PDF/UA-1, PDF/A-2b and PDF/A-4 fixtures the conformance
tests use, with two pages, compressed content streams, subset fonts and `/ToUnicode` maps.

- **Merge** asserts four pages in order across two documents, the rotation of each page as a marker
  of which document it came from, the A4 media box, and the text of both documents read back
  through the fonts' `/ToUnicode` maps by a test-only probe. The conformance checker then asserts
  that the merged file has no cross-reference, trailer, catalog, page-tree, object or font issue.
- **Extract, split, rotate, remove** assert page counts, that `/Rotate` reads back from the page
  dictionaries, that the text survives, and that a page the document does not have is refused.
- **Forms** take a real page and add a real widget to it as an incremental update: a text field with
  an appearance stream, a checkbox with two appearance states, and a signature field. Filling is
  read back from `/V` and from the appearance stream that was written. Flattening asserts that
  `/AcroForm` is gone, that the page content carries `/M3Form Do`, that the form XObject holds the
  value, and that the widget is no longer in `/Annots`. A field with no appearance stream and a
  signature field are asserted to be reported and left in place.
- **Refusals** are asserted on an encrypted trailer, on bytes that are not a PDF, on a page that
  does not exist, and on an angle that is not a quarter turn.

The probe is test code, not a package entry point. Reading a received document is a separate
deliverable, `PdfText`, specified in [18-pdf-text.md](18-pdf-text.md).
