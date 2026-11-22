# Outlines and attachments

`M3scalinum.Text.Pdf` reads and writes the two document-level structures an invoice or an archive
actually needs: the bookmark tree a reader shows in its sidebar, and the files embedded in the
document. Both work on bytes, and both write an incremental update — the original bytes stay where
they were.

## Reading the bookmarks

```csharp
using M3scalinum.Text.Pdf;

PdfOutline outline = PdfOutline.Read(pdf);

foreach (PdfOutlineEntry entry in outline.Entries)
{
    Console.WriteLine($"{entry.Title} → page {entry.PageNumber}");
}
```

`PdfOutline.Read` returns an outline with no entry when the document carries none, rather than
throwing. An entry knows its `Title`, its one-based `PageNumber` (`null` when it names no page or a
page that could not be resolved), its `Children`, whether the viewer opens it (`IsOpen`), and whether
its title is `IsBold` or `IsItalic`.

`Destination` restates what the file gave the entry — the fit type (`XYZ`, `Fit`, `FitH`, `FitBH`,
`FitV`, `FitBV`, `FitR` or `FitB`), the position in points when it carries one, the zoom for `XYZ`,
and the name when the entry pointed at a named destination. It describes what was read; a write
takes the page number.

An encrypted document is refused with a message naming `PdfEncryption.Open`; decrypt it first.
Bookmark colours, icons and the other presentation entries some viewers read are not exposed.

## Writing the bookmarks

```csharp
PdfOutlineEntry chapter = new PdfOutlineEntry("Chapter 2", pageNumber: 3)
{
    IsOpen = true,
    IsBold = true,
};

chapter.Add(new PdfOutlineEntry("2.1 Totals", pageNumber: 4));

byte[] withOutline = PdfOutline.Write(pdf, [new PdfOutlineEntry("Chapter 1", 1), chapter]);
```

Pages are one-based, the way a reader numbers them. The write appends the new items, a new outline
root and the updated catalog as an incremental update followed by a cross-reference section, so an
existing signature over the file still verifies. `Write` refuses an entry that names no page or a
page the document does not have, a document it cannot update, and one whose catalog holds something
it cannot restate. An empty collection points the catalog at an empty outline, which removes it.

## Attachments

```csharp
IReadOnlyList<PdfAttachment> attachments = PdfAttachments.Read(pdf);

byte[] invoiceXml = PdfAttachments.Extract(pdf, "factur-x.xml");

byte[] withFile = PdfAttachments.Add(
    pdf,
    "factur-x.xml",
    xmlBytes,
    mimeType: "text/xml",
    role: PdfAttachmentRole.Data,
    description: "Factur-X invoice data");

byte[] without = PdfAttachments.Remove(withFile, "factur-x.xml");
```

`PdfAttachment` restates what the file specification says: `Name` (the Unicode `/UF` entry when it
has one, `/F` otherwise), `FileName`, `MimeType` (`null` when the file declares none), `Role`,
`Content`, `Size`, `Sha256` of exactly those bytes, and `IsStoredEncoded` when the stream carries a
filter such as `FlateDecode` rather than the bytes themselves.

`Add` stores the content Flate-compressed and hands it back unchanged; `Extract` returns the exact
bytes. A MIME type is guessed from the extension when none is given, and falls back to
`application/octet-stream`. Adding a name the document already carries is refused — remove it first.
`Extract` and `Remove` throw `PdfOperationException` for a name no attachment has. Removal unlinks
the file from the document's name tree but cannot take bytes back — an incremental update never does
— so the removed content is still in the file, and only a rewrite removes it.

### The role matters for PDF/A-4f

`PdfAttachmentRole` is the `/AFRelationship` the document declares: `Source`, `Data`, `Alternative`,
`Supplement`, or `Unspecified` for none. Plain PDF accepts an attachment without one; PDF/A-4f does
not. The conformance checker reports `PDFA4F.RELATIONSHIP_MISSING` for an embedded file that states
none, and the converter refuses to claim PDF/A-4f over such a document — see
[Converting to PDF/A](Pdf-Conversion). A Factur-X invoice attaches its XML with `Data`.

## What the page operations do to them

`PdfPages.Rotate` copies the catalog whole, so outlines and attachments survive a rotation. Merge,
extract, split and remove drop the document-level structures a page tree cannot carry — that
includes outlines — so add the bookmarks again on the merged file. See
[Pages and forms](Pdf-Pages-and-Forms).
