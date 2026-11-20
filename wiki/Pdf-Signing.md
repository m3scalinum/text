# Pdf — signing

## What the signature is

PAdES-B: a detached CMS signature over the whole file, stored in the signature dictionary, with
`/SubFilter` set to `ETSI.CAdES.detached`. That is what a European receiver expects on a PDF/A-3
invoice, and what a reader shows as a valid, document-level signature.

```csharp
byte[] signed = PdfSigner.Sign(pdf, certificate, new PdfSignatureOptions
{
    FieldName = "Signature1",                  // what a reader lists
    Reason = "Facture FA-2026-0042",           // optional
    Location = "Lille",                        // optional
    SignedAt = new DateTimeOffset(2026, 2, 1, 9, 0, 0, TimeSpan.Zero),
});
```

`SignedAt` defaults to the current time. Set it when the signature must be reproducible: the same
document, certificate and timestamp produce the same bytes, which a test can assert on and a
re-run can compare.

## Why the file is appended to, not rewritten

`PdfSigner` reads the trailer, the cross-reference table and the catalog, then writes two new
objects and a new cross-reference section that chains to the previous one — an incremental update.
The original bytes are untouched.

That matters because a Factur-X invoice is three fragile things at once: PDF/A-3 conformance, an
embedded XML attachment, and possibly an earlier signature. A linearising rewrite breaks at least one
of them. An incremental update breaks none: the earlier revision is still there, byte for byte, and
the reader sees the signed revision on top.

## What it refuses

`PdfSignatureException` is thrown, with a sentence saying which, when:

- the data has no `startxref`, no trailer, no `/Root` or no `/Metadata` — it is not a PDF this writer
  can update;
- the certificate carries no private key;
- the document is empty.

It does not check that the document is PDF/A. That is the renderer's job, and a signature does not
change conformance.

## How it was verified

Two external tools, on documents the suite produces:

| Tool | What it answered |
| --- | --- |
| pyHanko | the signature is intact, the chain is trusted, and it covers the entire file |
| veraPDF | the signed Factur-X invoice is still PDF/A-3B |

Both are run against the samples, not against a hand-made PDF, because the interesting case is
signing a document that already carries an attachment and a conformance declaration.
