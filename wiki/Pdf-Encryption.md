# Encryption

`M3scalinum.Text.Pdf` opens a password-protected PDF and writes one. Everything else in the
package takes bytes and returns bytes, and encryption is how those bytes become readable again.

## Reading a protected document

```csharp
using M3scalinum.Text.Pdf;

byte[] protectedPdf = File.ReadAllBytes("contract.pdf");

PdfDecryptionResult opened = PdfEncryption.Open(protectedPdf, "correct horse");

byte[] pdf = opened.Pdf;                              // a complete, unencrypted PDF
PdfPermissions allowed = opened.Permissions;          // what /P declares
bool owner = opened.OpenedWithOwnerPassword;          // whether the owner password was used
```

`Open` returns the decrypted file rather than a decrypting reader, so every other operation works on
it unchanged:

```csharp
byte[] chapter = PdfPages.Extract(PdfEncryption.Open(protectedPdf, "correct horse").Pdf, "1-3");
```

The password may be the user password or the owner password; both are tried. A password that
matches neither throws `PdfEncryptionException`, whose message says so — the document is not
damaged, it is simply still locked. An empty or omitted password is tried as it is, which is what a
document that opens without asking carries:

```csharp
var opened = PdfEncryption.Open(protectedPdf);   // "" as the password
```

Opening a document that is not encrypted returns it unchanged, with `PdfPermissions.All` and a
`Revision` of zero, so a caller does not have to know in advance whether a file is protected.

### What is supported

| Handler | Read | Write |
| --- | --- | --- |
| AES-256, `V=5/R=6` | yes | yes, and it is what `Encrypt` writes |
| AES-128, `V=4/R=4` with an `/AESV2` crypt filter | yes | n/a |
| RC4 (revisions 2 and 3, `V=4` with `/V2` filters) | no | no |
| Public-key (certificate) encryption | no | no |

The unsupported handlers are refused by name — the message carries the `V` and `R` or the crypt
filter the file declares, so a caller knows what it is looking at.

## Writing an encrypted document

```csharp
byte[] encrypted = PdfEncryption.Encrypt(
    pdf,
    userPassword: "correct horse",
    permissions: PdfPermissions.Print | PdfPermissions.Copy,
    ownerPassword: "battery staple");     // optional; defaults to the user password
```

`Encrypt` writes AES-256 (`V=5/R=6`), the strongest handler the format defines. A fresh file key, a
fresh `/ID` and fresh salts are generated per call, so encrypting the same document twice produces
two files that share no bytes and both open with the same password.

The permissions are the bits of `/P`:

| Member | Permits |
| --- | --- |
| `PdfPermissions.Print` | printing |
| `PdfPermissions.Modify` | changing the content |
| `PdfPermissions.Copy` | copying text and graphics out |
| `PdfPermissions.Annotate` | adding or changing annotations and fields |
| `PdfPermissions.FillForms` | filling existing fields, applying signatures |
| `PdfPermissions.Accessibility` | extracting text for accessibility |
| `PdfPermissions.Assemble` | inserting, rotating or deleting pages |
| `PdfPermissions.HighQualityPrint` | printing that reproduces the document faithfully |
| `PdfPermissions.All` | all of the above |

They are a statement of intent, not a lock: a reader that ignores `/P` can still show the document,
and a reader that knows the owner password is granted everything. An empty user password gives a
document that opens without asking while still declaring permissions, which is the usual way to
state "no copying" without a password to type.

`Encrypt` refuses bytes that are not a PDF, a document with no catalog, and a document that is
already encrypted — the last one because the answer is `Open` first.

## Encryption and signatures

Encryption and PAdES signatures do not compose:

- Signing an encrypted document is refused with `PdfSignatureException`. An incremental update
  would append plaintext that no reader would decrypt.
- Encrypting a signed document leaves its signature dictionary in place but invalidates the
  signature: the bytes `/ByteRange` covers are now ciphertext.

Sign first and encrypt last if the signature only has to be present in the decrypted document;
accept that the signature in the encrypted copy does not verify.

## What refuses an encrypted document

`PdfPages`, `PdfForm`, `PdfSigner`, `PdfDocumentTimestamp` and `PdfSignatureVerifier` all refuse a
protected document with a message naming `PdfEncryption.Open`, rather than half-rewriting it.
`PdfConformance.Check` never throws: it reports a single `PDF.ENCRYPTED` finding and stops, because
nothing inside the file can be checked before it is decrypted.

```csharp
var report = PdfConformance.Check(protectedPdf, PdfConformanceStandard.PdfA2b);
// report.Has(PdfConformanceCodes.Encrypted) == true

var decrypted = PdfConformance.Check(
    PdfEncryption.Open(protectedPdf, "correct horse").Pdf,
    PdfConformanceStandard.PdfA2b);
```
