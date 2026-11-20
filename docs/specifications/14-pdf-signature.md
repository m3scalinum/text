# 14 — PDF signatures

## 1. Scope

`M3scalinum.Text.Pdf` signs a PDF with PAdES, and is where post-processing of the documents M3scalinum.Text.SharpTypst
produces will live. It has no dependency on the compiler: it takes bytes and returns bytes, so it
signs a document from any source.

**In scope.** A PAdES-B signature over the whole file, with the certificate the caller supplies.

**Out of scope, for now.** Timestamps (PAdES-T), long-term validation (PAdES-LT/LTA), visible
signature appearances, merging documents, extracting pages. Each is a separate piece of work, and
none of them is needed to make a document legally signed.

## 2. What a PAdES signature is

A detached CMS `SignedData` over the file, minus the bytes that hold the signature itself. The
signature dictionary carries a `/ByteRange` naming the two ranges that were signed, and `/Contents`
holding the CMS structure as a hex string.

The consequence that shapes the implementation: **the byte range is inside the signed content**. The
file must therefore be built first, with a fixed-size placeholder where the signature will go, and
the range computed on the result.

## 3. How it is written

1. Parse the trailer for `/Root`, `/Size`, `/Prev` and `/ID`, and read the catalog.
2. Append, as an incremental update:
   - the catalog, rewritten under its own object number with an `/AcroForm` holding the field;
   - a signature field (`/FT /Sig`) pointing at the signature dictionary;
   - the signature dictionary, with `/ByteRange` and `/Contents` as fixed-width placeholders.
3. Compute the two ranges, and fill `/ByteRange` in place — the placeholder is ten digits wide, so
   filling it does not move anything.
4. Sign the two ranges with `SignedCms`, detached, SHA-256.
5. Overwrite the `/Contents` placeholder with the signature, padded with zeros.

**Decision — an incremental update, not a rewrite.** Everything before the appended block is
untouched, so a document that was PDF/A stays PDF/A, an embedded Factur-X attachment stays readable,
and a document that already carries a signature keeps that signature valid. This is the same
mechanism the Factur-X XMP patch uses, and it is the reason signing a Factur-X invoice works at all.

**Decision — 8 KiB reserved for the CMS.** A certificate chain fits in a fraction of that. A longer
chain fails with a message naming the size rather than producing a truncated signature.

## 4. Verification

The tests verify the signature the way a reader does: they read `/ByteRange`, slice the two ranges,
decode the CMS and call `CheckSignature`. A string assertion would pass on a signature that does not
verify.

Beyond the tests, the sample is checked with **pyHanko**, the reference implementation:

```
signature     : intact=True valid=True trusted=True coverage=ENTIRE_FILE
attachment    : factur-x.xml 8168 bytes — intact après signature
```

and the signed Factur-X invoice still passes **veraPDF** in PDF/A-3B. Those two checks are what the
claim rests on: a signature that breaks the archive conformance, or an incremental update that
corrupts the attachment, would be worse than no signature.

## 5. What the caller must know

- The certificate needs its private key; a certificate without one is refused before anything is
  written.
- `SignedAt` exists so a signature can be reproducible in a test. Left unset, the current time is
  used.
- The signature covers the file as it is. Signing a document that is later modified invalidates it,
  which is the point.
