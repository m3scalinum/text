# 14 — PDF signatures

## 1. Scope

`M3scalinum.Text.Pdf` signs a PDF with PAdES, reads those signatures back, and is where
post-processing of the documents M3scalinum.Text.SharpTypst produces will live. It has no dependency
on the compiler: it takes bytes and returns bytes, so it signs a document from any source.

**In scope.** A PAdES signature over the whole file, with the certificate the caller supplies; a
signature timestamp (PAdES-B-T); a document security store carrying the chain and caller-supplied
revocation data (PAdES-B-LT); a visible signature appearance, text or image; a document timestamp;
and a verifier that says what it can prove about what it read.

**Out of scope, for now.** PAdES-B-LTA as a claim (a document timestamp can be added, but the
verifier does not report the LTA level), encryption, redaction, OCR, merging, extracting pages, form
filling other than the signature field, and fetching revocation data. Each is a separate piece of
work, and none of them is needed to make a document signed and readable.

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
- The signature covers the file as it is. Signing a document that is later modified invalidates it,
  which is the point.
