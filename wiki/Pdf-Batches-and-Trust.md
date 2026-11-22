# Batches and trust

Two structures answer the questions an archive asks after the fact: whether a lot of files is still
the lot that was stored, and whether a signer was a qualified service when it signed. Neither one
opens a socket.

## Sealing a batch

`PdfBatchManifest` seals a directory tree as a manifest of SHA-256 fingerprints and checks it later:

```csharp
using M3scalinum.Text.Pdf;

PdfBatchManifestDocument manifest = PdfBatchManifest.Build(@"D:\archive\2026-03");
File.WriteAllBytes(@"D:\archive\2026-03.manifest", manifest.ToBytes());
```

Every regular file under the root gets one entry — an identifier, the size, the fingerprint, the
file's last-write time and the instant the manifest was sealed — and the entries are sorted by
identifier. The identifier is the file's path relative to the root with `/` separators, so
`2026-01/invoice.pdf` and `2026-02/invoice.pdf` are two entries and nothing is compared by bare name.

`Build(root, addedAt)` records one fixed instant for every entry instead of the current clock, which
makes two builds over the same files byte-identical.

The manifest carries the files' fingerprints, not the files. Store it outside the batch directory: a
build re-run over a batch that holds its own manifest seals that manifest too, and the next
verification reports it as an added file.

## Verifying a batch

```csharp
PdfBatchManifestDocument sealedManifest = PdfBatchManifest.Load(File.ReadAllBytes("2026-03.manifest"));

PdfBatchVerification verification = PdfBatchManifest.Verify(@"D:\archive\2026-03", sealedManifest);

if (!verification.IsIntact)
{
    foreach (string reason in verification.Breaks)   // one sentence per modified, missing or added file
    {
        Console.WriteLine(reason);
    }
}
```

`Load` checks the seal as it parses: an entry line altered by one byte is refused, and so is a seal
line that no longer describes the entries above it — `PdfOperationException` in both cases, as for
bytes that are not a manifest of a version the reader knows.

`Verify` returns one `PdfBatchEntryResult` per sealed file and then one per file the batch carries
that nothing seals, each with its `State` (`Current`, `Modified`, `Missing` or `Added`),
`ExpectedSha256` and `ActualSha256` when it was hashed. `Hashed` says how many files this pass read:
`PdfBatchVerificationMode.Full` hashes every file and costs one read of the whole batch;
`PdfBatchVerificationMode.Fast` hashes only the files whose length or last-write time moved. Fast is
what makes a thousand-file check affordable, and its blind spot is a same-length edit with the
timestamp restored, which only Full sees.

The manifest proves that no sealed file changed, that none went missing and that nothing was added.
It does not prove who sealed it. A caller that needs a proof it can show somebody else signs the
manifest's bytes with `PdfSigner` or with any other signature over those bytes.

## Reading a trust list

`PdfLotl` parses the XML of a European list of trusted lists, or a national trusted list, into the
services it declares:

```csharp
PdfTrustedList list = PdfLotl.Read(xmlBytes, location: "https://ec.europa.eu/tools/lotl/eu-lotl.xml");

foreach (PdfTrustService service in list.Services)
{
    Console.WriteLine($"{service.ProviderName} — {service.Name} — qualified: {service.IsQualified}");
}

foreach (PdfTrustListPointer national in list.NationalLists)
{
    Console.WriteLine($"{national.Territory}: {national.Location}");
}
```

The reader reports the list's `Territory`, `Version`, `SequenceNumber`, `IssueDateTime`,
`NextUpdate` and, for the European list of lists, `IsListOfLists`. `IsExpiredAt(instant)` answers
whether the list's next-update time has passed.

`FindServices(certificate, at)` and `FindService(…, at)` match a certificate by its parsed form or by
its SHA-256 thumbprint, at a given instant: a service counts only while one of its `Periods` covers
that instant, which is why the deadline is a parameter rather than the clock. `Evaluate(verification)`
goes the other way and says, for every signature the verifier produced, which service of the list
covers its signer.

Nothing here fetches. A list of lists names the national lists as pointers; downloading one, caching
it and deciding when to replace it belongs to the caller. The reader refuses a document with a DTD,
with an entity, or one past its bounds — `MaxXmlBytes` (8 MiB), `MaxXmlDepth` (64),
`MaxXmlElements` (1,000,000), `MaxServices` (100,000), `MaxPointers` (4,096) and `MaxCertificates`
(64) — with `PdfTrustListException` naming the bound, never a parser message to interpret.

## Judging a signature against a list

```csharp
var report = PdfSignatureVerifier.ReadEidas(pdf, trustStore, list);

foreach (var verdict in report.Signatures)
{
    // verdict.SignerQualification is Qualified, NotQualified or NotEvaluated
    // verdict.TimestampAuthorityQualification asks the same of the authority
}
```

The two questions stay apart: whether a qualified signature or seal service covers the signing
certificate, and whether the timestamp authority is a qualified service. Neither is answered by the
certificate's own text. A certificate the list does not know makes the qualification
`NotQualified`; no list at all makes it `NotEvaluated`, which is not the same answer. What the
verifier proved about the bytes is reported unchanged underneath — see [Signing](Pdf-Signing).

The specification behind the manifest is `docs/specifications/33-archive-integrity.md`; the trust-list
rules are in `docs/specifications/36-trust-lists.md`.
