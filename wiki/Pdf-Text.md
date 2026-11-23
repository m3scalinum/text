# Reading text

`M3scalinum.Text.Pdf` can read the text of a PDF it was handed — the half that matters for ingestion,
archival and retrieval. Bytes in, bytes out: no compiler, no font engine, no network.

```csharp
using M3scalinum.Text.Pdf;

byte[] pdf = File.ReadAllBytes("received.pdf");

PdfTextResult result = PdfText.Extract(pdf);

foreach (PdfPageText page in result.Pages)
{
    Console.WriteLine($"--- page {page.PageNumber}");
    Console.WriteLine(page.Text);

    if (page.HasFallback)
    {
        // The font had no /ToUnicode map: these characters may not be what the producer meant.
        foreach (string font in page.FallbackFonts)
        {
            Console.WriteLine(font);
        }
    }
}
```

`ExtractPage(pdf, 1)` reads one page, zero-based. `PageCount` is the document's page count;
`Text` on the result joins every page in order.

## What you get

The characters the page draws, in the order its content streams draw them. A space where the layout
left a horizontal gap, a newline between lines, one trailing newline per page — the shape the
compiler's own per-page text has.

## What you do not get

- **Not reading order.** A two-column page reads across both columns, not down the first one. Glyph
  order is stream order.
- **No layout.** No margins, no paragraphs beyond line breaks, no bold or italic, no superscripts.
- **No structure.** A heading, a table cell and a footnote are all text on a line. A table is not a
  grid.
- **No OCR.** A scanned page is an image; it comes back empty, with no fallback and no error. The
  other half of the loop — writing recognised words back as an invisible layer — is
  [below](#making-a-scan-searchable).

## When the text is uncertain

A font with no `/ToUnicode` CMap is decoded through its `/Differences` or, failing that, as raw code
values. That is a documented fallback, and the page names the font in `FallbackFonts` (`HasFallback`
is the one-line check) instead of emitting mojibake silently. `SkippedContentStreams` counts content
streams whose filter the reader does not implement, so a page can say its text is incomplete rather
than empty.

## Encrypted documents

An encrypted document is decrypted with the password the caller supplies, through the same handler
`PdfEncryption.Open` uses, and then read like any other:

```csharp
PdfTextResult result = PdfText.Extract(pdf, "correct horse");
```

The password may be the user password or the owner password. With no password an encrypted document
is refused rather than read incorrectly; a password that opens neither is refused with its own code:

```csharp
try
{
    PdfTextResult result = PdfText.Extract(pdf, password);
}
catch (PdfTextException exception) when (exception.Error == PdfTextError.WrongPassword)
{
    Console.WriteLine(exception.Message);
}
catch (PdfTextException exception) when (exception.Error == PdfTextError.Encrypted)
{
    Console.WriteLine(exception.Message);   // no password supplied, or a handler this package does not decrypt
}
```

`PdfTextError.NotAPdf` and `PdfTextError.Unreadable` cover the other two ways a document has nothing
to extract. A page index outside the document is an `ArgumentOutOfRangeException`.

`ExtractPage(pdf, page, password)` takes the same password argument.

## Making a scan searchable

A scan is an image, so `Extract` has nothing to read from it. `PdfTextLayer.Add` closes the loop: it
writes the words an OCR engine read back into the document as an invisible layer positioned over the
image, and `PdfText` then reads them like the text of any other page.

```csharp
using M3scalinum.Text.Pdf;

byte[] scanned = File.ReadAllBytes("scan.pdf");

PdfTextLayerWord[] words =
[
    new PdfTextLayerWord(0, "Facture", 72, 690, 132, 710),
    new PdfTextLayerWord(0, "n°", 140, 690, 155, 710),
    new PdfTextLayerWord(0, "2026-0042", 163, 690, 240, 710),
];

byte[] searchable = PdfTextLayer.Add(scanned, words);

PdfPageText page = PdfText.ExtractPage(searchable, 0);

Console.WriteLine(page.Text);   // the words, in the order they were handed over
```

One `PdfTextLayerWord` per word: the zero-based page, the characters, and a box — `Left`, `Bottom`,
`Right`, `Top` — **in the page's default user space**: points, origin at the bottom-left corner of
the `/MediaBox`, the space `/MediaBox` itself is written in, not the rotated space a viewer displays.
The box's bottom edge becomes the baseline and its height the font size, and each word's horizontal
scale is fitted so that its advance spans the box.

The order of the list is the reading order. Within a page the layer writes the words in the order it
was given and never sorts them by position: reading order is the caller's fact — an OCR engine's
line and word order — and a layer that sorted by position would read a right-to-left line backwards.

### What the file gains

One incremental update, written the way `PdfSigner` and `PdfAttachments` write theirs: every object
is appended after the last byte and nothing before the update moves. That is what keeps a signature
over the earlier revision valid — `PdfSignatureVerifier` reports `IsIntact` true and
`CoversWholeFile` false, because the layer is part of the file and no earlier signature covers it.

The page's `/Contents` becomes an array holding the streams it had plus this one, last. The layer
draws with text rendering mode 3 (`3 Tr`): the glyphs are positioned and never painted, so it costs
no pixel. Its resources are a copy of what the page resolved to, with `/Font` replaced; the font is a
`Type0` with `/Encoding /Identity-H`, one embedded `CIDFontType2` descendant and a `/ToUnicode`
CMap, one `bfchar` entry per glyph. Glyph identifiers are assigned in the order the characters first
appear, so the bytes a stream writes are not the characters — the `/ToUnicode` map is the only thing
that turns them back into text, and `PdfText` reads them through it with no fallback font reported.

The embedded program is generated rather than subset from a file: the repository ships no font, and
the layer is never painted, so its glyphs are empty and only their advances matter. It is a valid
sfnt — nine tables, correct checksums, a `head.checkSumAdjustment` that makes the file sum to
`0xB1B0AFBA` — with every glyph empty and every advance half an em. A reader that validates the font
accepts it; a reader that shapes the text finds no ink, which is the point.

### What it refuses

| Situation | Result |
| --- | --- |
| The bytes are not a PDF, or the document cannot take an incremental update | `PdfOperationException` |
| The document is encrypted | `PdfOperationException` — an incremental update would append plaintext no reader decrypts; decrypt with `PdfEncryption.Open` first |
| A page carries `/Rotate` other than 0 | `PdfOperationException` — the boxes are in the page's own user space, the raster an OCR engine read is the displayed page, and the two differ by the rotation |
| A page already carries a font named `M3TextLayer` | `PdfOperationException` — a second layer would renumber the glyphs the first one draws |
| `words` is empty, a word has no text, a word carries a control character, or a box has no area | `ArgumentException` |
| A word names a page the document does not have | `ArgumentOutOfRangeException` |

### What it is not

- **Not a rendering font.** Every glyph is empty; the layer is invisible by construction and changes
  no pixel of the page.
- **Not a reading order for the page.** Nothing is inferred: the caller's order is kept verbatim,
  and a right-to-left line reads back one word per line because the extractor treats a jump to the
  left as a new line.
- **Not structure.** No heading, table or paragraph is described, and there is no `/ActualText` and
  no tagged content.
- **Not PDF/UA, and not PDF/A-4.** Accessibility needs a structure tree this does not write, and
  PDF/A-4 would need a real embedded font with outlines. The layer does not change what the document
  claims about itself.
- **Not exact for a single character.** Every glyph shares one advance, so selecting part of a word
  may be narrower or wider than the ink under it. The line and the characters are right.

## Where to go

- **[Pages and forms](Pdf-Pages-and-Forms)** — merge, extract, rotate and remove pages; fill and flatten
  a form.
- **[Signing](Pdf-Signing)** — sign what you produced.
- **[Checking conformance](Pdf-Conformance)** — establish what the file declares before you rely on it.
