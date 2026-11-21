# Pages and forms

`M3scalinum.Text.Pdf` can change a document as well as sign it: merge several PDFs, keep, split,
rotate or remove pages, and fill or flatten an AcroForm. Everything is bytes in, bytes out, with no
dependency on the compiler.

## Page operations

```csharp
byte[] first = File.ReadAllBytes("report.pdf");
byte[] second = File.ReadAllBytes("appendix.pdf");

byte[] merged = PdfPages.Merge(first, second);
byte[] chapter = PdfPages.Extract(merged, "1-3,5");
IReadOnlyList<byte[]> parts = PdfPages.Split(merged, 2);
byte[] turned = PdfPages.Rotate(chapter, 90, "1");
byte[] trimmed = PdfPages.Remove(merged, "2-");
```

A page selection is one-indexed and uses the shape of the compiler's `PageRanges` option: `1-3,5`,
`2-`, `-3` or `5`. It is a set, so `3,1` gives pages one and three in document order.

Each operation rebuilds the file: objects are renumbered, the page tree names the pages that
survive, and a fresh cross-reference table is written. Pages keep their content, resources, fonts,
images and annotations, and a font two pages share is copied once.

`Rotate` adds to each page's `/Rotate` and normalises it; an angle that is not a multiple of 90 is
refused. It copies the catalog whole, so tagging, outlines and form fields survive a rotation.

## Filling a form

```csharp
byte[] filled = PdfForm.Fill(pdf, new Dictionary<string, string>
{
    ["customer.name"] = "ACME",
    ["accepted"] = "Yes",
});
```

Names are fully qualified: the field tree's `/T` values joined with a dot. Text and choice fields
receive `/V`; a checkbox or radio button takes an appearance state (`Yes`, `Off`, or the state the
form names). When the widget already carries an appearance stream, it is redrawn with the field's
`/DA` font so a viewer that does not regenerate appearances shows the new value.

A name that matches no field is refused rather than ignored, and a signature field is never
overwritten.

## Flattening a form

```csharp
PdfFormFlattenResult result = PdfForm.Flatten(filled);
byte[] flattened = result.Document;

foreach (PdfFormFlattenSkip skip in result.Skipped)
{
    Console.WriteLine(skip.FieldName + ": " + skip.Reason);
}
```

Flattening draws each widget's current appearance into the page as a Form XObject at the widget's
`/Rect`, removes the widget from `/Annots`, and removes the field from the form. `/AcroForm` is
removed when every field was flattened, which `IsComplete` reports.

A field it cannot draw — no appearance stream, an empty rectangle, no usable `/BBox`, a widget no
page lists, or a signature field — is left interactive and reported in `Skipped`. A flattened file
that lost its values would look finished and be wrong; one that still has a field is visibly
unfinished and correct.

## What is refused

An encrypted document is refused by every operation: encryption is out of scope, and rewriting an
encrypted file would produce either a broken file or a decrypted one. Bytes that are not a PDF, a
document whose catalog or page tree cannot be read, a selection naming a page that does not exist,
and a selection that would remove every page all throw rather than write a partial file.

Merge, extract, split and remove drop the document-level structures a page tree cannot carry:
tagging (`/StructTreeRoot`, `/MarkInfo`), outlines, named destinations and `/AcroForm`. Widget
annotations stay with their page and still draw, but the fields are no longer interactive. Rotate
is the exception and keeps them.

Text extraction, redaction, OCR and encryption are not part of this API.

## Signatures

A signature covers the bytes it was made over. Filling, flattening, rotating or reordering a signed
document invalidates its signatures; the verifier will report what it can prove.
