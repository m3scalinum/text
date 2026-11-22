# Signing and verifying

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

That matters because an archive-ready invoice is three fragile things at once: PDF/A-3 conformance, an
embedded attachment, and possibly an earlier signature. A linearising rewrite breaks at least one
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
| `BaselineLTA` | plus a document timestamp over the whole file, covering the store, whose authority's chain the store carries |
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

## A person or a legal person

`PdfSignatureOptions.Kind` says which eIDAS act the call performs. `PdfSignatureKind.Person` (the
default) is a natural person signing; `PdfSignatureKind.Seal` is a legal person sealing, and the
signer requires the certificate to carry `id-kp-documentSigning` (1.3.6.1.5.5.7.3.36):

```csharp
byte[] sealedPdf = PdfSigner.Sign(pdf, sealCertificate, new PdfSignatureOptions
{
    Kind = PdfSignatureKind.Seal,
    Reason = "Facture FA-2026-0042",
});
```

Reading it back keeps the two questions apart — what the bytes prove, and what a trust list states:

```csharp
var eidas = PdfSignatureVerifier.ReadEidas(pdf);   // no list: every qualification is NotEvaluated

var judged = PdfSignatureVerifier.ReadEidas(pdf, trustStore, trustList);

foreach (var verdict in judged.Signatures)
{
    // verdict.Kind, verdict.SignerQualification, verdict.SignerExplanation,
    // verdict.TimestampAuthorityQualification, verdict.Level
}

foreach (var timestamp in judged.DocumentTimestamps)
{
    // timestamp.Qualification, timestamp.Explanation
}
```

`ReadEidas` is answered from the signing certificate's extended key usage, not from anything the PDF
says about itself, and from the trust list the caller read with `PdfLotl.Read`. Nothing is
downloaded. Without a list the report says `NotEvaluated` — never "not qualified".

## Supplying the revocation evidence

A `PdfDssOptions` hands the store every byte up front. When the evidence lives behind the caller's own
cache or policy, `IRevocationSource` is asked one certificate at a time instead:

```csharp
sealed class CacheRevocation : IRevocationSource
{
    public PdfRevocationAnswer GetRevocation(X509Certificate2 certificate, X509Certificate2 issuer)
        => _cache.TryGet(certificate, out var answer) ? answer : PdfRevocationAnswer.None;
}

PdfSignatureResult result = PdfSigner.SignWithReport(pdf, certificate, new PdfSignatureOptions
{
    RevocationSource = new CacheRevocation(),
});

byte[] signed = result.Content;
result.Level;                  // what the call was configured to reach
result.Revocation;             // one entry per certificate and kind that went into the store
result.MissingRevocation;      // certificates no evidence covers; empty at PAdES-B-LT
```

The source is called once per certificate that needs an entry, in chain order, and it may block — the
writer calls it synchronously. `Sign` returns the bytes alone; `SignWithReport` is the same operation
with the account of what the seam contributed. A source that returns `PdfRevocationAnswer.None` for a
certificate the level requires refuses the signature with `PdfRevocationException` rather than
silently writing a weaker level.

`PdfSignatureVerifier.ReadRevocation(pdf)` reads the other direction: for every certificate the store
embeds, which evidence covers it and when that evidence was issued. Nothing is fetched and nothing is
judged there.

### Who signed the evidence

An OCSP answer or a CRL in the store is only worth what its own signature is worth. `ReadRevocationSignatures`
reads every response the store embeds — answers first, then lists, in store order — and reports what
its signature proved:

```csharp
foreach (PdfRevocationSignatureReading reading in PdfSignatureVerifier.ReadRevocationSignatures(pdf))
{
    Console.WriteLine($"{reading.Kind} for {reading.Subject ?? "the CRL's scope"}: {reading.Status}");
}
```

`Kind` says whether the response is an OCSP answer or a CRL; `Status` is one of four:

| Status | What it means |
| --- | --- |
| `Valid` | the signature verifies over the signed bytes, and its signer is the certificate's issuer itself, or a responder certificate that issuer issued with `id-kp-OCSPSigning` (1.3.6.1.5.5.7.3.9) |
| `NoSignature` | the response carries no signature this reader can check: an empty signature value, no signed bytes, or an algorithm outside the set the package implements |
| `Invalid` | a signature is present and verifies with no key the file carries; the bytes changed after signing, or the signer's key is not embedded — a reader cannot tell the two apart, and neither is evidence |
| `UnexpectedSigner` | the signature verifies, but its signer was not entitled to answer for that certificate: a delegated responder without `id-kp-OCSPSigning`, a responder issued by another authority, or a CRL naming an authority no embedded certificate is |

The entry names the certificate the response answers for (`Subject`, `Issuer`, `SerialNumber`, null
for a CRL and for a certificate the store does not carry) and the certificate that signed it
(`SignerSubject`, `SignerSerialNumber`), so a report can say "the OCSP answer embedded for CN=… was
not signed by its issuer" without parsing the store.

It answers one question: who wrote this, and were they allowed to. It says nothing about what the
answer states — an answer about a revoked certificate still reads `Valid` — and it fetches nothing
and applies no policy beyond the issuer's own authorisation. Neither an OCSP response nor a CRL is
timestamped here, so `Valid` is a statement about bytes, not about when they were produced.

## A document timestamp

An RFC 3161 token over the whole file, added after the signatures, attesting that the document existed
at that moment:

```csharp
byte[] stamped = PdfDocumentTimestamp.Add(signed, new Uri("https://tsa.example.com/"), httpClient);
```

It covers every byte, so it goes last. A signature made before it still verifies, and reports
`CoversWholeFile = false` — the file now has a revision the signature never saw, which is normal and
is not a defect.

## An archive: PAdES-B-LTA

A store proves a signature only while the revocation data in it stays usable. The archival level adds
a document timestamp over the file as it stands — the store included — issued by an authority whose
own chain the store carries. That is what a ten-year invoice retention rests on: the evidence needed
to re-check the signature is itself attested.

```csharp
byte[] archived = PdfArchiveTimestamp.Refresh(signedOrArchived, new PdfArchiveTimestampOptions
{
    TimestampAuthority = new Uri("https://tsa.example.com/"),
    TimestampClient = httpClient,              // the caller owns and disposes it
});

bool ok = PdfArchiveTimestamp.IsArchived(archived);      // yes, or no
```

Refreshing is the same call on the result: each one appends a timestamp over the whole file, so a
document refreshed twice carries two, both intact, and the second covers the first. Refresh a file
before the digest it used is deprecated or the authority's certificate approaches its expiry — a TSA
call and one incremental update, and nothing earlier changes. A document that carries no signature is
refused: a timestamp over an unsigned file attests that a file existed, not that anything was signed.

`IsArchived` is the yes or no; `Inspect` is the reason:

```csharp
var chain = PdfArchiveTimestamp.Inspect(archived);

foreach (var entry in chain.Entries)          // signature and timestamp, in file order
{
    // entry.Kind, entry.Offset
    // entry.CoveredStart, entry.CoveredEnd, entry.CoversItsRevision
    // entry.IsIntact, entry.CoversWholeFile, entry.CoversDocumentSecurityStore
    // entry.AttestedTime, entry.SignerSubject, entry.Error
}

foreach (var reason in chain.Breaks)          // empty when the document is archived
{
    Console.WriteLine(reason);
}
```

The chain is verified, not assumed: every entry is re-checked over the bytes it covers, each one must
still reach the end of the revision it was written into, the newest must be a timestamp over the whole
file, and the store must prove the chain of every signature and of every authority that issued a
timestamp. So a signature added after the last timestamp leaves the newest revision unattested and the
document is not archived; a store missing the authority's certificate says so by name; and a byte
changed after archiving drops the level at once.

The archive is the file. Move the PDF without its store and its timestamps and only what the CMS
carries survives — the OCSP answer was fetched years earlier from a responder that is gone. Store the
bytes, refresh them, and let the level, not a database row, say what they prove.

## What it refuses

`PdfSignatureException` is thrown, with a sentence saying which, when:

- the data has no `startxref`, no trailer or no `/Root` — it is not a PDF this writer can update;
- the certificate carries no private key;
- a seal is asked for a certificate that does not carry `id-kp-documentSigning`;
- the document is empty;
- a timestamp authority is named without a client, or a client without an authority;
- an archive is asked for a document that carries no signature;
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

For the archival level, **no external AdES validator was run**: pyHanko is not installed in the
environment this was built in, so no third-party tool has been asked whether it reads this package's
B-LTA output as LTA. What was checked on an archived sample: openssl 3.5 answers
`CMS Verification successful` for the signature's DER over the bytes its range names, and
`openssl ts -reply -text` reads both document timestamps with an imprint equal to the SHA-256 of the
bytes each one names. `openssl ts -verify` declines those tokens because the stand-in authority used
in tests omits the ESS signing-certificate attribute RFC 3161 wants — a property of the test authority,
not of the document. The level itself rests on `PdfSignatureVerifier` and the tests.
