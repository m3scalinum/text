# Pdf — signing and verifying

## What the signature is

PAdES: a detached CMS signature over the whole file, stored in the signature dictionary, with
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

`PdfSigner` reads the trailer, the cross-reference table and the catalog, then writes new objects and
a new cross-reference section that chains to the previous one — an incremental update. The original
bytes are untouched.

That matters because a Factur-X invoice is three fragile things at once: PDF/A-3 conformance, an
embedded XML attachment, and possibly an earlier signature. A linearising rewrite breaks at least one
of them. An incremental update breaks none: the earlier revision is still there, byte for byte, and
the reader sees the signed revision on top.

Signing an already signed document is the same operation. The new revision carries a second field, and
the first signature still covers the revision it was made over. pyHanko reports exactly that: the
first signature `ENTIRE_REVISION` with `FORM_FILLING` extensions — acceptable modifications — and the
second `ENTIRE_FILE`.

## A timestamp: PAdES-B-T

The signature says *who*; a timestamp says *when*, in a way the signer cannot choose. Name an
RFC 3161 authority and give the client that reaches it — both, because the library never opens a
connection on its own and never picks a TSA:

```csharp
byte[] signed = PdfSigner.Sign(pdf, certificate, new PdfSignatureOptions
{
    TimestampAuthority = new Uri("https://tsa.example.com/"),
    TimestampClient = httpClient,              // the caller owns and disposes it
});
```

The token covers the signature value and is attached as the `id-aa-signatureTimeStampToken` unsigned
attribute, so it does not change the signature. Its imprint is checked before anything is written: a
TSA that answers about some other signature produces an exception, not a document.

## A long-term store: PAdES-B-LT

A timestamp is evidence only while the TSA's certificate can still be checked. The store keeps what a
reader needs later: the chain, and the revocation data for it.

```csharp
byte[] signed = PdfSigner.Sign(pdf, certificate, new PdfSignatureOptions
{
    TimestampAuthority = new Uri("https://tsa.example.com/"),
    TimestampClient = httpClient,
    Dss = new PdfDssOptions
    {
        Certificates = [signerCertificate, rootCertificate, tsaCertificate],
        Crls = [crlBytes],
        OcspResponses = [ocspBytes],
    },
});
```

Every byte there is yours to collect. The library does not fetch a CRL and does not call an OCSP
responder: naming a URL the document chose, over a network the library does not own, is a leak of
where documents are opened, and a signature library has no business doing it silently. A store with
no revocation data still gets written — and the verifier reports it as `BaselineT`, not `BaselineLT`,
because that is what it proves.

## A visible signature

```csharp
byte[] signed = PdfSigner.Sign(pdf, certificate, new PdfSignatureOptions
{
    Appearance = new PdfSignatureAppearance
    {
        PageIndex = 0,                         // counted from zero
        X = 60, Y = 60, Width = 240, Height = 70,   // PDF points, origin bottom-left
        TextLines = ["Signé par Facture SARL", "Le 1er février 2026"],
        FontSize = 10,
    },
});
```

Set `Image` instead of `TextLines` to draw a PNG or a JPEG, as the bytes of the file. A JPEG is
embedded untouched; a PNG is decoded — 8-bit, non-interlaced, grey, RGB, indexed or RGBA — and its
alpha channel becomes a soft mask. Text is drawn with Helvetica and WinAnsi, so the characters outside
that set become `?`: an appearance is not the place for a font program.

The widget lands in the page's `/Annots` in the same incremental update, and a page that already had
annotations keeps them.

## Reading a signature back

`PdfSignatureVerifier.Verify` returns what it could establish, and `null` — never a guess — for what
it could not:

```csharp
var verification = PdfSignatureVerifier.Verify(pdf, new PdfTrustStore
{
    Anchors = [rootCertificate],
    Intermediates = [intermediateCertificate],
});

foreach (var signature in verification.Signatures)
{
    // signature.IsIntact, signature.CoversWholeFile
    // signature.SignerSubject, signature.SignerIssuer
    // signature.CertificateNotBefore, signature.CertificateNotAfter
    // signature.ClaimedSigningTime, signature.TimestampedSigningTime
    // signature.Level, signature.ChainStatus, signature.Error
}
```

The level is what the verifier could prove, not what the file claims:

| Level | Proven by |
| --- | --- |
| `BaselineB` | the CMS verifies over its byte range, with the signer's certificate embedded |
| `BaselineT` | plus a signature timestamp whose CMS verifies and whose imprint is this signature |
| `BaselineLT` | plus a store carrying the whole chain and usable revocation data for every certificate on it |
| `None` | neither: `Error` says why |

`ProvenLevel` on the result is the weakest of them, because a document is only as strong as its
weakest signature.

### The chain is yours to judge

Pass a store, or get `ChainStatus.NotEvaluated`. The machine's trust store is never consulted: a
verification that succeeds because the operating system happens to trust a root says nothing about
your policy. When a timestamp is present the chain is evaluated at that time; otherwise at the current
time. Never at the claimed signing time, which is whatever the file says.

`BaselineLT` and `ChainStatus` answer different questions. The first says the document carries enough
evidence to be checked later. The second says whether *you* trust the root it ends at. A document can
be `BaselineLT` and `Untrusted`, and both statements are useful.

## A document timestamp

An RFC 3161 token over the whole file, added after the signatures, attesting that the document existed
at that moment:

```csharp
byte[] stamped = PdfDocumentTimestamp.Add(signed, new Uri("https://tsa.example.com/"), httpClient);
```

It covers every byte, so it goes last. A signature made before it still verifies, and reports
`CoversWholeFile = false` — the file now has a revision the signature never saw, which is normal and
is not a defect.

## What it refuses

`PdfSignatureException` is thrown, with a sentence saying which, when:

- the data has no `startxref`, no trailer or no `/Root` — it is not a PDF this writer can update;
- the certificate carries no private key;
- the document is empty;
- a timestamp authority is named without a client, or a client without an authority;
- the TSA refuses, is unreachable, or answers a token whose imprint is not this signature;
- an appearance has no area, names a page that does not exist, or carries both text and an image;
- the appearance image is neither a PNG nor a JPEG, or is a PNG this reader does not handle.

It does not check that the document is PDF/A. That is the renderer's job, and a signature does not
change conformance.

## How it was verified

Two external tools, on documents the suite produces:

| Tool | What it answered |
| --- | --- |
| openssl (`cms -verify`) | the CMS verifies over the bytes the byte range names |
| pyHanko | the signature is intact and trusted, covers the entire file, the timestamp token is intact and trusted, and the store reads back as three certificates and one CRL |

Both are run against signed samples, not against a hand-made PDF, because the interesting case is
signing a document that already carries an attachment, a conformance declaration, or another
signature.
