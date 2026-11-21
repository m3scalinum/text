# 14 — PDF signatures

## 1. Scope

`M3scalinum.Text.Pdf` signs a PDF with PAdES, reads those signatures back, and is where
post-processing of the documents M3scalinum.Text.SharpTypst produces will live. It has no dependency
on the compiler: it takes bytes and returns bytes, so it signs a document from any source.

**In scope.** A PAdES signature over the whole file, with the certificate the caller supplies; a
signature timestamp (PAdES-B-T); a document security store carrying the chain and caller-supplied
revocation data (PAdES-B-LT); a visible signature appearance, text or image; a document timestamp,
archiving the document at PAdES-B-LTA and refreshing that archive; and a verifier that says what it
can prove about what it read.

**Out of scope, for now.** Redaction, OCR, merging, extracting pages, form filling other than the
signature field, and fetching revocation data. Each is a separate piece of work, and none of them is
needed to make a document signed and readable. Encryption is no longer out of scope — [19-pdf-encryption.md](19-pdf-encryption.md) reads and writes it — but signing an encrypted
document is refused: an incremental update to ciphertext can never verify. Decrypt first, sign the
bytes `PdfEncryption.Open` returned, and re-encrypt the result knowing the signature no longer
covers it.

## 2. What a PAdES signature is

A detached CMS `SignedData` over the file, minus the bytes that hold the signature itself. The
signature dictionary carries a `/ByteRange` naming the two ranges that were signed, and `/Contents`
holding the CMS structure as a hex string.

The consequence that shapes the implementation: **the byte range is inside the signed content**. The
file must therefore be built first, with a fixed-size placeholder where the signature will go, and
the range computed on the result.

```
%PDF-1.7  …  %%EOF                                    the document as it was
1 0 obj <</Type/Catalog … /AcroForm<</Fields[6 0 R]/SigFlags 3>>>> endobj
4 0 obj <</Type/Page … /Annots[6 0 R]>> endobj        the page, rewritten to list the widget
6 0 obj <</Type/Annot/Subtype/Widget/FT/Sig …>> endobj
7 0 obj <</Type/Sig/SubFilter/ETSI.CAdES.detached/ByteRange[0 a b c]/Contents<…>>> endobj
xref …  trailer <</Size 8/Root 1 0 R/Prev …>>
```

## 3. How it is written

1. Scan the file for object headers, read the last trailer for `/Root`, `/Size`, `/Prev` and `/ID`,
   and read the catalog — the last definition of each object wins, as it does for a reader.
2. Append, as an incremental update:
   - the catalog, rewritten under its own object number with an `/AcroForm` holding the field, and
     with a `/DSS` when a store is asked for;
   - the signature field (`/FT /Sig`) pointing at the signature dictionary, with a `/Rect`, a `/P`
     and an `/AP` when the signature is visible;
   - the appearance form XObject, and the image XObject when there is a picture;
   - the signature dictionary, with `/ByteRange` and `/Contents` as fixed-width placeholders;
   - the security store and its streams, when asked for.
3. Compute the two ranges, and fill `/ByteRange` in place — the placeholder is ten digits wide, so
   filling it does not move anything.
4. Sign the two ranges with `SignedCms`, detached, SHA-256.
5. Overwrite the `/Contents` placeholder with the signature, padded with zeros.

**Decision — an incremental update, not a rewrite.** Everything before the appended block is
untouched, so a document that was PDF/A stays PDF/A, an embedded Factur-X attachment stays readable,
and a document that already carries a signature keeps that signature valid. This is the same
mechanism the Factur-X XMP patch uses, and it is the reason signing a Factur-X invoice works at all.

Object numbers are allocated above every number in the file, and the cross-reference section is
written in ascending order, one subsection per run of consecutive numbers: an update that rewrites
the page (object 4) and appends objects 6 to 13 therefore produces two subsections and marks nothing
free. The `/AcroForm` and the `/DSS` of an earlier revision are **merged**, not replaced, so a second
signature keeps the first field and the first store.

**Decision — 8 KiB reserved for the CMS.** A certificate chain, and a timestamp token inside it, fit
in a fraction of that. A longer structure fails with a message naming the size rather than producing
a truncated signature.

## 4. PAdES-B-T: the timestamp

`PdfSignatureOptions.TimestampAuthority` names an RFC 3161 authority and
`PdfSignatureOptions.TimestampClient` supplies the `HttpClient` that reaches it. Both are required:
the library never picks a TSA, never reads an ambient proxy configuration, and never opens a
connection on its own.

The order matters. The CMS is computed first, then its signature value is hashed with SHA-256 and
sent as a `TimeStampReq` (`certReq` set, no nonce — the imprint already binds the token to this
signature). The token that comes back is parsed and checked **before** anything is written: its own
CMS must verify, and its imprint must be the hash of this signature value. A token that does not
match means no signature is produced at all. The token is then attached as the
`id-aa-signatureTimeStampToken` unsigned attribute (`1.2.840.113549.1.9.16.2.14`), which does not
change the signature value — evidence of the signature, not part of it.

## 5. PAdES-B-LT: the document security store

`PdfSignatureOptions.Dss` carries the material a reader needs years later, when the certificates have
expired and the responder is gone:

- `Certificates`: the chain, DER-encoded, typically the signer and the root, plus the timestamp
  authority's certificate when the signature carries a timestamp;
- `OcspResponses` and `Crls`: the revocation data, as the responder or the issuer returned it.

Every list is caller-supplied on purpose. Fetching a CRL or an OCSP response means naming a URL the
document chose, over a network the library does not own; a signature library that does that silently
is a signature library that leaks where its documents are opened. The signature is written with
whatever the caller collected, and the verifier reports how far that material goes.

The store is a dictionary the catalog points at (`/DSS`, indirect), with `/Certs`, `/OCSPs` and
`/CRLs` arrays of streams holding the DER bytes. It is written in the same revision as the signature,
inside the signed range. **Decision — no `/VRI`.** VRI entries are keyed by the hash of a signature's
`/Contents`, which does not exist yet when the store is written in that same revision; readers fall
back to the top-level arrays, which is what this store provides.

## 6. The visible appearance

`PdfSignatureAppearance` places the widget on a page — `PageIndex`, counted from zero — at a
rectangle in PDF points, with the origin at the bottom-left of the page, as PDF defines it. The
widget is added to the page's `/Annots`, and the page object is rewritten in the same incremental
update, keeping the annotations it already had.

The appearance is drawn in a form XObject whose `/BBox` is the rectangle's size:

- **Text**: Helvetica, WinAnsi, at `FontSize` points with `Padding` of inset, one `Tj` per line and
  `T*` between them. Characters outside the WinAnsi range that matches Latin-1 (`0x20`–`0x7E`,
  `0xA0`–`0xFF`) become `?` — an appearance is not the place for a font program, and pretending
  otherwise would draw boxes.
- **Image**: a PNG is decoded — 8-bit, non-interlaced, grey, RGB, indexed or RGBA — unfiltered row by
  row, and flate-compressed; RGBA becomes RGB plus a greyscale soft mask, and an indexed image is
  expanded through its palette. A JPEG is **embedded as it is**, with `/DCTDecode`, and only its
  frame header is read, for the size and the component count that pick the colour space. The bytes of
  a JPEG are never decoded, so nothing is re-encoded and nothing is lost.

## 7. The document timestamp

`PdfDocumentTimestamp.Add` appends a signature dictionary with `/Type /DocTimeStamp` and
`/SubFilter /ETSI.RFC3161`, whose `/Contents` is an RFC 3161 token over the whole file. It attests
that the document — with every signature it already carries — existed at the moment the authority
issued the token.

Because it covers the whole file, it must be added **after** the last signature. A signature made
before a later update covers its own revision, not the file, which is normal: the verifier reports
`CoversWholeFile` separately from `IsIntact` for exactly that reason.

### PAdES-B-LTA: archiving, and refreshing

B-LT proves a signature for as long as the revocation data its store carries stays usable. B-LTA adds
a document timestamp over the file as it stands — the store included — signed by an authority whose
own certificate chain the store also carries. That timestamp is the sentence "this store existed at
this moment", and it is what a ten-year retention actually rests on.

What each level proves, in the words of the verifier:

| Level | What it establishes | What it still depends on |
| --- | --- | --- |
| `BaselineB` | the CMS verifies over its byte range | the signer's certificate being readable |
| `BaselineT` | plus the signature existed when the authority says | the authority's certificate and its digest |
| `BaselineLT` | plus the store carries the chain and revocation data | that revocation data staying current, and the digests staying sound |
| `BaselineLTA` | plus a document timestamp over the whole file, whose authority's chain the store carries | nothing in the file: the evidence needed to re-check it is itself attested |

`PdfArchiveTimestamp.Refresh(pdf, options)` appends one document timestamp over the whole current
file, with the caller's TSA and `HttpClient`; calling it on the result extends the chain, and each
refresh leaves the earlier timestamps intact and covering their own revision. `Refresh` refuses a
document that carries no signature — a timestamp over an unsigned file attests that a file existed,
which is not an archival level. `Refresh` does not judge what it stamps: it appends, and
`Inspect` reports.

`PdfArchiveTimestamp.IsArchived(pdf)` is the yes or no. `PdfArchiveTimestamp.Inspect(pdf)` is the
report: one `PdfArchiveEntry` per signature and per document timestamp, in file order, carrying what
it covers (`CoveredStart`, `CoveredEnd`), whether it is intact, whether it still reaches the end of
the revision it was written into (`CoversItsRevision`), and whether it covers the store. `Breaks`
lists, in sentences, every reason the chain does not hold.

**The chain is verified, not assumed.** The analysis walks every entry and re-checks each one over
the bytes it covers, then requires that:

1. every entry verifies — the CMS, or the token's imprint;
2. every entry still covers the whole revision it was written into, which is the `%%EOF` that follows
   it: a timestamp whose coverage stops short reports the byte where it stops, and the document is
   not archived;
3. no entry covers fewer bytes than the entry before it;
4. the newest entry is a document timestamp that reaches the last byte of the file — a signature
   added after the last timestamp leaves the newest revision unattested, and the document is not
   archived, however intact the other entries are;
5. a document timestamp covers the store and the streams it points at; and
6. the store proves the chain of every signature, and carries and proves the chain of the authority
   that issued every document timestamp.

**When to refresh.** Before any of the primitives a timestamp rests on ages out: when the digest it
used is being deprecated, when the authority's certificate approaches its expiry, or when a stronger
digest becomes the requirement. Refreshing does not change the earlier signatures or timestamps; it
adds one more revision, so it costs a TSA call and one incremental update, and it can be scheduled
years apart. Record the refresh in the retention policy, not in the document.

**Why the store must travel with the file.** The level is a property of the bytes, not of a database
on the signer's side. Move the PDF without its `/DSS` and its timestamps and only what the CMS itself
carries survives: the OCSP answer that said `good` was fetched at signing time, from a responder that
will not be there in ten years. The store is written inside the signed range of the revision that
created it, and every later timestamp covers it again, which is what makes the whole thing movable.

## 8. Verification

`PdfSignatureVerifier.Verify(bytes)` returns one entry per signature, and one per document timestamp,
with the facts it could establish — and `null` for the facts it could not:

| Fact | Where it comes from |
| --- | --- |
| `IsIntact` | the CMS verifies over the two ranges, sliced out of this file |
| `CoversWholeFile` | the second range reaches the last byte |
| `SignerSubject`, `SignerIssuer`, `SignerCertificate` | the certificate embedded in the CMS |
| `CertificateNotBefore`, `CertificateNotAfter` | that certificate's validity window, in UTC |
| `ClaimedSigningTime` | `/M` in the signature dictionary — a claim, not evidence |
| `TimestampedSigningTime` | the signature timestamp token, when its imprint is this signature |
| `HasDocumentTimestamp`, `IsDocumentTimestampValid` | a document timestamp that covers this revision |
| `Level` | see below |
| `ChainStatus`, `ChainError` | the chain against the caller's trust store |

### The levels the verifier can prove

- **`BaselineB`** — the CMS verifies and embeds the signer's certificate.
- **`BaselineT`** — plus a signature timestamp whose CMS verifies and whose imprint is the hash of
  this signature's value.
- **`BaselineLT`** — plus a store that carries the whole chain from the signer to a root it also
  carries, and a usable revocation entry for every certificate on that path: a CRL that verifies
  with its issuer's key and does not list the certificate, or an OCSP answer that verifies and says
  `good` for that certificate's serial and issuer hash.
- **`BaselineLTA`** — plus the document is archived: a document timestamp over the whole file covers
  the store, its own CMS verifies, and the store carries and proves the chain of the authority that
  issued it. `PdfArchiveTimestamp.Inspect` reports the chain entry by entry; the level is only
  granted when that chain holds as a whole, since a signature cannot be more archived than the
  document that carries it.
- **`None`** — anything else; `Error` says why.

`PdfSignatureVerification.ProvenLevel` is the **weakest** level across the signatures: a document is
only as strong as its weakest signature.

### The trust store

The chain is judged only against `PdfTrustStore`, passed explicitly: `Anchors` are the certificates
to treat as roots, `Intermediates` the ones to offer the builder. With no store, or an empty one,
every signature reports `ChainStatus.NotEvaluated`. The machine's trust store is never consulted, no
intermediate is downloaded, and revocation is not fetched: a verification that succeeded because the
operating system happened to trust a root says nothing about the caller's policy.

The chain is evaluated at the timestamped time when there is one, otherwise at the current time —
never at the claimed signing time, which is whatever the file says.

### What it will not claim

- Level `BaselineLT` says the store carries the evidence and that the evidence holds together. It
  says nothing about whether the caller trusts the root — that is `ChainStatus`.
- An OCSP answer signed by a delegated responder is not recognized: its certificate would have to be
  embedded and authorized by the issuer, and this reader does not follow that path.
- CRLs are read as they are: no delta CRLs, no indirect CRLs, no reason codes. RSA PKCS#1 v1.5 and
  ECDSA signatures are checked; other algorithms are not.
- The reader finds objects by scanning for `N 0 obj` headers rather than by following the
  cross-reference table, so a PDF that keeps its objects in object streams (compressed xref) is not
  read. Every PDF this package writes, and every PDF M3scalinum.Text.SharpTypst produces, uses plain
  object tables.

## 9. Verification of this work

The tests verify the signature the way a reader does: they read `/ByteRange`, slice the two ranges,
decode the CMS and call `CheckSignature`, and they compare the byte-range arithmetic against the file
length. A string assertion would pass on a signature that does not verify.

The slices are taken from the object table, and the length of the structure in `/Contents` comes from
its ASN.1 header: the hex string is zero-padded to a fixed width, so trimming trailing zeros — which
an earlier helper did — cuts real bytes off a DER that happens to end in `0x00`, one run in a hundred
and twenty-eight. That is the flake the extraction used to have; it is now deterministic, and a test
asserts the declared length for two differently sized certificates.

The archival level is asserted through `PdfSignatureVerifier` itself, never through a private helper:
a B-LT document archived once proves `BaselineLTA`; a document archived twice carries two intact
timestamps and the second covers the first; a byte changed after archiving drops the level; and a
document whose store lacks the authority's certificate stays at `BaselineLT` while
`PdfArchiveTimestamp.Inspect` says which certificate is missing.

Beyond the tests, the samples are checked with two independent tools:

- **openssl 3.5** — `openssl cms -verify -binary -inform DER -in sig.der -content content.bin
  -noverify` answers `CMS Verification successful` on the bytes the range names;
- **pyHanko 0.38** — reports `intact=True valid=True trusted=True`, `coverage=ENTIRE_FILE`,
  `modification_level=NONE` for the signed document; adds `TIMESTAMP_TOKEN<INTACT:TRUSTED>` for the
  timestamped one; reports `ENTIRE_REVISION` with `EXTENDED_WITH_FORM_FILLING`,
  `ACCEPTABLE_MODIFICATIONS` for the first of two signatures, which is what an incremental update
  over a signed document is supposed to look like; reports `EXTENDED_WITH_LTA_UPDATES` once a
  document timestamp is added; and reads the store back as three certificates and one CRL.

Those checks are what the claims rest on. A signature that breaks the archive conformance, an
incremental update that corrupts the attachment, or a store a reader cannot parse would be worse than
no signature at all.

**No external AdES validator was run for the archival level.** pyHanko is not installed in the
environment this work was done in, so nothing outside this package has been asked whether it reads
the B-LTA output as LTA. What was checked, on a sample this package produced and archived twice:
`openssl cms -verify -binary -inform DER -in sig.der -content content.bin -noverify` answers
`CMS Verification successful` for the signature over the bytes its range names, and
`openssl ts -reply -token_in -in token.der -text` reads both document timestamps and shows an imprint
equal to the SHA-256 of the bytes each timestamp names. `openssl ts -verify` declines those tokens
because the stand-in authority used in this environment does not add the ESS signing-certificate
attribute RFC 3161 requires; that is a property of the authority, not of the archived document, and
the tokens' own CMS verifies. The LTA claim itself rests on the verifier and the tests above.

## 10. What the caller must know

- The certificate needs its private key; a certificate without one is refused before anything is
  written.
- `SignedAt` exists so a signature can be reproducible in a test. Left unset, the current time is
  used.
- A timestamp needs both `TimestampAuthority` and `TimestampClient`; supplying one without the other
  is refused, and a token that does not match is not written.
- A visible signature needs a rectangle with a positive area and either text lines or an image. The
  library does not clamp it to the page: the caller owns the geometry.
- A security store is evidence only when it carries the whole chain and revocation data for it.
  Collecting that data is the caller's job; this library names no URL and fetches nothing.
- Archiving is a second call, and refreshing is the same call again: `PdfArchiveTimestamp.Refresh`
  needs the authority and the client, refuses a document that carries no signature, and appends one
  timestamp rather than rewriting anything. `IsArchived` answers yes or no; `Inspect` answers why not.
- The archive is the file: keep the PDF with its `/DSS` and its timestamps, and refresh it before the
  digest or the authority's certificate ages out. A database row pointing at the signature is not
  evidence of anything.
- The signature covers the file as it is. Signing a document that is later modified invalidates it,
  which is the point.
