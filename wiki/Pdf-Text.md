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
- **No OCR.** A scanned page is an image; it comes back empty, with no fallback and no error.

## When the text is uncertain

A font with no `/ToUnicode` CMap is decoded through its `/Differences` or, failing that, as raw code
values. That is a documented fallback, and the page names the font in `FallbackFonts` (`HasFallback`
is the one-line check) instead of emitting mojibake silently. `SkippedContentStreams` counts content
streams whose filter the reader does not implement, so a page can say its text is incomplete rather
than empty.

## Encrypted documents

An encrypted document cannot be read without decrypting it, and decryption is not implemented in this
package. It is refused with a typed error that names the security handler the file declares:

```csharp
try
{
    PdfTextResult result = PdfText.Extract(pdf);
}
catch (PdfTextException exception) when (exception.Error == PdfTextError.Encrypted)
{
    Console.WriteLine(exception.Message);
}
```

`PdfTextError.NotAPdf` and `PdfTextError.Unreadable` cover the other two ways a document has nothing
to extract. A page index outside the document is an `ArgumentOutOfRangeException`.

## Where to go

- **[Pages and forms](Pdf-Pages-and-Forms)** — merge, extract, rotate and remove pages; fill and flatten
  a form.
- **[Signing](Pdf-Signing)** — sign what you produced.
- **[Checking conformance](Pdf-Conformance)** — establish what the file declares before you rely on it.
