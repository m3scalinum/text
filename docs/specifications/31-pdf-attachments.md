# 31 — PDF attachments

## 1. Scope

`M3scalinum.Text.Pdf` gains one entry point that reads and writes the files a PDF carries inside
itself:

- `PdfAttachments` — `Read`, `Extract`, `Add`, `Remove`.
- `PdfAttachment` — one embedded file: its name, MIME type, relationship, size, SHA-256 and bytes.
- `PdfAttachmentRole` — the `/AFRelationship` values ISO 32000-2 defines.

Like the rest of the package it is bytes-in, bytes-out: no file handle, no network, no compiler.

**In scope.** The `/Names /EmbeddedFiles` name tree, the catalog's `/AF` array, the file
specification, the embedded file stream and the filter that compresses it.

**Out of scope, deliberately.**

| Not done | Why |
| --- | --- |
| Signature and document timestamps | `PdfSigner` and `PdfDocumentTimestamp` own them; this document only promises that an update does not invalidate what they wrote. |
| PDF/A-4f conformance | A separate item, and a conformance question rather than an attachment one. §6 states what an addition implies for it. |
| Collections and portfolios | They reuse the same name tree but are a viewer feature; the files are read all the same. |
| `/Subtype` validation | The MIME type is reported as declared. Guessing what a producer meant by a wrong subtype would hide the defect. |
| External file specifications | An `/AF` entry with no `/EF` stream names a file that is not in the document. There is nothing to extract, so it is not an attachment. |

## 2. What a PDF attachment is

A document names its attachments twice:

- `/Names /EmbeddedFiles` is a name tree mapping a name to a **file specification** dictionary. This
  is what a reader shows in its attachments panel, and the only structure a PDF 1.7 reader looks at.
- The catalog's `/AF` array lists **associated files**: the file specifications that are part of the
  document as a whole rather than attached to a page or an annotation. PDF 2.0 and the PDF/A-3 and
  PDF/A-4f profiles are defined in terms of `/AF`.

The two usually name the same file specification objects, and `Read` reports such a file once. A
file that only an `/AF` entry names is still an attachment: nothing else would find it.

The file specification holds the names (`/F`, the PDFDocEncoding one, and `/UF`, the Unicode one),
the description (`/Desc`) and the relationship (`/AFRelationship`); the embedded file stream, named
by `/EF /F`, holds the bytes and their `/Subtype`. This is why the MIME type is read from the stream
and the relationship from the file specification, and why both are reported even when a producer
writes the relationship in only one of them.

## 3. Reading

```csharp
foreach (var attachment in PdfAttachments.Read(pdf))
{
    Console.WriteLine($"{attachment.Name} {attachment.MimeType} {attachment.Role} {attachment.Size} {attachment.Sha256}");
}

byte[] xml = PdfAttachments.Extract(pdf, "factur-x.xml");
```

`Read` returns the attachments in the order the name tree lists them, then the files only `/AF`
names. A document with no attachment returns an empty list rather than `null`. A name tree split
over intermediate nodes (`/Kids`) is walked to its leaves.

`Extract(pdf, name)` is `Read` plus a lookup: it returns `attachment.Content`, the bytes the sender
embedded, unchanged. Names are compared ordinally, exactly as `PdfAttachment.Name` reports them — a
caller that wants a case-insensitive match enumerates `Read` and compares itself.

### 3.1 Filters

An embedded file stream is almost always `FlateDecode`d, because a PDF embeds an XML invoice once and
every byte counts. The stream is decoded before anything is reported: `Size` and `Sha256` describe
the file, not the stream that carried it.

A stream whose filter chain the reader does not implement (LZW, ASCII85, RunLength, DCT, JPX) is
reported with its bytes **as stored**, and `PdfAttachment.IsStoredEncoded` is `true`. The alternative
— throwing — would make one unusual attachment hide every other one in the document. The caller who
needs those bytes decoded knows, from the flag, that it has to decode them itself.

### 3.2 What is refused

`Read` and `Extract` throw `PdfOperationException` for bytes that are not a PDF, and for an encrypted
document: its name tree and its streams are ciphertext. `PdfEncryption.Open` decrypts one and returns
the bytes to read, which is the same rule `PdfPages`, `PdfForm` and `PdfConformance` follow.

## 4. Adding and removing

```csharp
byte[] withInvoice = PdfAttachments.Add(
    pdf,
    "factur-x.xml",
    xmlBytes,
    "text/xml",
    PdfAttachmentRole.Data,
    "Invoice data");

byte[] withoutIt = PdfAttachments.Remove(withInvoice, "factur-x.xml");
```

**Decision — an incremental update, not a rebuild.** Both operations append objects and a
cross-reference section that chains to the previous one, exactly as `PdfSigner`,
`PdfDocumentTimestamp` and `PdfArchiveTimestamp` do. A rebuild would renumber every object, and a
signature covers the bytes it was made over: adding an invoice's XML to a signed document must not
revoke the signature. Appending is what makes "sign, then attach the XML" a valid order.

**Consequence — removal unlinks, it does not erase.** An incremental update cannot take bytes back.
`Remove` rewrites the name tree and the `/AF` array so that the document no longer offers the file —
`Read` no longer reports it, `Extract` no longer finds it — but the objects that carried it stay in
the file, unreachable. A caller whose requirement is that the bytes leave the file must rebuild the
document instead, and lose the signatures with it.

**What `Add` writes.** Three objects: the embedded file stream (Flate-compressed, `/Params /Size` set
to the uncompressed length), the file specification (`/F` and `/UF` both, so every reader shows the
name, `/AFRelationship` when one was asked for, `/Desc` when one was given) and a new name-tree leaf
holding every existing entry plus the new one, sorted by name as the format requires. The catalog —
or the object that holds an indirect name dictionary — is rewritten to point `/EmbeddedFiles` at the
new leaf, and the file specification is appended to `/AF`.

Two names that differ only in case are two names: the name tree orders and matches by the string the
producer wrote.

A name the document already carries is refused rather than replaced, so that "add the XML" cannot
silently become "replace the XML someone else signed".

### 4.1 What cannot take an update

`Add` and `Remove` need the classic trailer dictionary `PdfDocument.Parse` reads. A document whose
cross-reference section is a stream and whose trailer is that stream's dictionary — no `trailer`
keyword — cannot be updated here and is refused with a `PdfOperationException` that says so. Reading
such a document works: `Read` follows the cross-reference stream.

This is a limitation of the appender, not of the format, and it is stated rather than worked around:
rewriting a document to gain an update path would defeat the reason for the update.

### 4.2 Signatures

Adding an attachment after a signature leaves that signature valid: the covered byte range does not
move and the appended objects are outside it. Removing one does too, for the same reason — the
removal is also an append. A reader that reports a document as "modified after signing" will report
either operation, which is the truth, and is not the same statement as "the signature does not
verify".

## 5. Model

```csharp
public sealed class PdfAttachment
{
    public string Name { get; }        // /UF when present, /F otherwise, the name-tree key last
    public string? FileName { get; }   // the raw /F entry
    public bool HasFileName { get; }   // whether /F exists
    public string? MimeType { get; }   // the stream's /Subtype, as declared
    public PdfAttachmentRole Role { get; }
    public byte[] Content { get; }     // the file, decoded
    public long Size { get; }          // Content.Length
    public string Sha256 { get; }      // lowercase hexadecimal
    public bool IsStoredEncoded { get; }
}
```

`Sha256` is what makes a received document checkable: a caller compares the fingerprint against the
invoice it expected instead of reading megabytes of XML twice. It is computed over the decoded bytes,
which are the bytes `Extract` returns, so the two always agree.

`PdfAttachmentRole` has `Source`, `Data`, `Alternative`, `Supplement` and `Unspecified`. The first
four are the ISO 32000-2 values verbatim; `Unspecified` means the document declares nothing.

## 6. What an addition implies for PDF/A-4f

PDF/A-4f is the profile for a PDF/A-4 document carrying arbitrary embedded files; PDF/A-3 allows the
same, and the e-invoice (Factur-X, ZUGFeRD) is a PDF/A-3 with an XML file attached.

Neither profile is established here. What `Add` does that the profiles care about:

- **`/AFRelationship` is mandatory.** A file that is only in the name tree, or in `/AF` with no
  relationship, breaks PDF/A-4f. `Add` writes the relationship when the caller passes one, and writes
  nothing when the caller passes `Unspecified`: plain PDF stays valid, and a caller aiming at PDF/A
  passes `Data` or `Supplement` as the profile requires.
- **The embedded file stream must declare `/Subtype`.** `Add` always writes one, guessed from the
  extension when the caller does not name it, so a produced file never carries an untyped attachment.
  The value is written, not validated: `text/xml` on a JPEG is the caller's error, not this writer's.
- **The XMP metadata must agree with the attachment.** A PDF/A-3 or PDF/A-4 file that carries an
  attached file should describe it in the `pdfaExtension` schema of its XMP. `Add` does not touch the
  metadata stream — patching XMP is `PdfAConverter`'s territory — so a document that must claim
  conformance needs the metadata brought up to date afterwards. See
  [26-pdfa-conversion.md](26-pdfa-conversion.md).
- **A file cannot be both attached and unlisted.** `/AF` and the name tree are what the profile reads;
  `Add` writes both, which is the shape the profiles require.

## 7. Tests

`tests/Text.Pdf.Tests/PdfAttachmentTests.cs` builds its documents by hand, so each test can say which
object it changed:

| Behaviour | Test |
| --- | --- |
| No attachment, empty collection | `A_document_without_an_attachment_has_an_empty_collection` |
| Uncompressed stream, all properties | `An_uncompressed_attachment_reports_its_name_type_size_and_bytes` |
| FlateDecode undone, exact bytes | `A_flate_compressed_attachment_comes_back_as_the_file_it_was` |
| SHA-256 of the bytes | `The_sha256_is_the_hash_of_exactly_the_attachment_bytes` |
| Name-tree order, several files | `Several_attachments_keep_the_order_of_the_name_tree` |
| Intermediate name-tree nodes | `A_name_tree_with_intermediate_nodes_is_walked_to_its_leaves` |
| `/UF` preferred over `/F` | `The_unicode_name_is_preferred_and_the_raw_F_entry_still_reported` |
| `/AF`-only file | `A_file_listed_only_in_the_association_array_is_still_an_attachment` |
| Undecodable filter | `An_attachment_whose_filter_the_reader_cannot_undo_is_reported_as_stored` |
| Extraction, byte for byte | `Extracting_returns_the_bytes_the_sender_embedded` |
| Add then read, original bytes kept | `Adding_an_attachment_appends_an_update_that_reads_back` |
| Add keeps existing files | `Adding_a_second_attachment_keeps_the_first` |
| Remove keeps the others | `Removing_one_attachment_leaves_the_others` |
| Remove the last one | `Removing_the_last_attachment_leaves_a_document_that_carries_none` |
| Encrypted document refused | `An_encrypted_document_is_refused_rather_than_read_or_rewritten` |
| Cross-reference stream refused | `A_document_whose_cross_reference_is_a_stream_cannot_take_an_update` |
