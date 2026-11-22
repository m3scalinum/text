# 19 — PDF encryption

## 1. Scope

`M3scalinum.Text.Pdf` gains `PdfEncryption`, the entry point of the package that opens a protected
document or produces one (`PdfText` also decrypts, on its way to reading text):

```csharp
PdfDecryptionResult opened = PdfEncryption.Open(encrypted, "correct horse");
byte[] encryptedAgain = PdfEncryption.Encrypt(pdf, "correct horse", PdfPermissions.Print | PdfPermissions.Copy);
```

Both are bytes-in, bytes-out like the rest of the package. No type refers to the compiler, and
nothing here reaches the file system, the network, or a certificate store.

**In scope.** The standard security handler, password-authenticated, in the two revisions real
documents carry: AES-256 with `V=5/R=6`, and AES-128 with `V=4/R=4` and an `/AESV2` crypt filter.
Key derivation for both, the `/O` and `/U` checks, the owner-password path, and per-object
decryption of strings and streams.

**Out of scope, deliberately.**

| Not done | Why |
| --- | --- |
| Public-key (certificate) encryption, `/SubFilter /adbe.pkcs7.s5` and the `PubSec` handler | The file key is wrapped for a recipient's certificate, not for a password: it needs a CMS recipient list, a key-agreement path and a key store, which is a different feature with a different threat model. |
| RC4, revisions 2 and 3, and a revision 4 with `/V2` filters | The cipher is broken and no producer has written it for fifteen years. A document that uses it is refused by name, with the `V` and `R` it declares. |
| Revision 5 (AES-256 as withdrawn in ISO 32000-2 drafts) | Superseded by revision 6 before publication; authors who wrote it must re-encrypt. |
| Encrypting with a public key, or re-encrypting to a new owner | Out of scope with the handler above. |
| Reading permissions as a restriction | `/P` is a statement of intent that conforming readers honour; it is not enforced technically and is reported, not applied. |
| Signature validation over an encrypted document | A signature covers ciphertext, so it cannot verify before decryption. Decrypt, then verify. |

## 2. The two handlers

| | AES-256 | AES-128 |
| --- | --- | --- |
| `/V` / `/R` | 5 / 6 | 4 / 4 |
| Crypt filter | `/AESV3`, 32-byte key | `/AESV2`, 16-byte key |
| File key from the password | Algorithm 2.B: SHA-256, then the hardened loop over AES-128 and SHA-256/384/512 | Algorithm 2: MD5 over the padded password, `/O`, `/P` and `/ID`, then 50 iterations |
| Password check | `/U` holds the 32-byte hash, then a validation salt and a key salt | `/U` holds 16 bytes of an RC4 chain over `/ID`, then 16 bytes that do not matter |
| Owner password | `/O` and `/OE`, validated the same way with `/U` folded into the hash | `/O` holds the padded user password, encrypted with RC4 under a key derived from the owner password |
| Key per object | none: the file key encrypts every object | MD5 of the file key, the object number, the generation and `sAlT`, cut to 16 bytes |
| Cipher of strings and streams | AES-256-CBC, random 16-byte IV in front, PKCS#7 padding | AES-128-CBC, the same shape |

The RC4 code in the implementation exists only for `/O` and `/U`, which revision 4 defines in terms
of RC4 whatever its crypt filters say. No content is ever decrypted with RC4.

**Decision — the password check is constant-time.** `/U` is compared with
`CryptographicOperations.FixedTimeEquals`. The comparison is over a hash of the password, so timing
it leaks nothing usable, but a byte-by-byte comparison in a loop is the kind of detail that becomes
a finding in an audit; there is no reason to write it that way.

## 3. Reading

`PdfEncryption.Open(byte[] pdf, string? password = null)` returns a `PdfDecryptionResult`:

| Member | Meaning |
| --- | --- |
| `Pdf` | The decrypted document, as whole bytes. A complete PDF: same objects, page tree, annotations, signatures and information dictionary, with no `/Encrypt` in its trailer. |
| `Permissions` | The bits of `/P` the document declares, as `PdfPermissions`. |
| `IsEncrypted` | Whether the source was encrypted at all. |
| `Revision` | 6, 4, or 0 when the source was not encrypted. |
| `OpenedWithOwnerPassword` | Whether the password matched `/O` rather than `/U`. |

The password may be the user password or the owner password; both are tried, user first. A password
that matches neither is a `PdfEncryptionException` whose message says so, **not** a corrupt
document: the bytes of an encrypted file parse perfectly, and only the password decides whether
their content can be recovered. An empty or omitted password is tried as the empty password, which
is what a document that opens without asking carries.

An unencrypted document is returned unchanged, with `Permissions` set to `PdfPermissions.All` and
`Revision` zero. Opening is idempotent in that sense: a caller that does not know whether a file is
protected does not have to ask first.

**Decision — return bytes, not a decrypting reader.** `Open` rebuilds the file rather than wrapping
the reader in a key. Every other operation of the package (`PdfPages`, `PdfForm`, `PdfConformance`,
`PdfSigner`) then works on a protected document without knowing it was protected, and the caller can
see and store the decrypted bytes. The rebuild keeps each object's number and generation, so
references, the page tree and any signature dictionary stay consistent; streams keep the filters
they declare, with `/Length` corrected for the decrypted size, and are never re-compressed.

An object that a reader could not decrypt — a wrong key, a truncated block — is reported as an
unreadable object rather than thrown across the whole document, which is the same treatment a broken
object gets on an unencrypted file.

## 4. Writing

`PdfEncryption.Encrypt(byte[] pdf, string userPassword, PdfPermissions permissions, string? ownerPassword = null)`
writes AES-256, `V=5/R=6`:

- a fresh 32-byte file key and a fresh 16-byte `/ID`, from `RandomNumberGenerator`;
- `/U`, `/UE`, `/O` and `/OE` by the algorithms of ISO 32000-2, with the random salts the format
  requires, so two encryptions of one document share no bytes;
- `/Perms`, the encrypted copy of `/P` a revision 6 reader checks;
- every string and every stream of every object encrypted with AES-256-CBC, a random 16-byte IV in
  front and PKCS#7 padding, except the `/Encrypt` dictionary itself and the trailer's `/ID`, which
  the format leaves in the clear.

`ownerPassword` defaults to `userPassword`. The two roles are what `/O` means: a reader that knows
the owner password is granted every permission whatever `/P` says, and `Open` reports which of the
two matched.

`permissions` maps directly onto the bits of `/P`, which is why `PdfPermissions` numbers its members
by bit position rather than densely:

| Member | Bit of `/P` | Permits |
| --- | --- | --- |
| `Print` | 3 | printing |
| `Modify` | 4 | changing the content |
| `Copy` | 5 | copying text and graphics out |
| `Annotate` | 6 | adding or changing annotations and fields |
| `FillForms` | 9 | filling existing fields, applying signatures |
| `Accessibility` | 10 | extracting text for accessibility |
| `Assemble` | 11 | inserting, rotating or deleting pages |
| `HighQualityPrint` | 12 | printing that reproduces the document faithfully |

`PdfPermissions.All` writes `-4`, the value a document with no stated restriction carries. The
reserved bits the specification fixes are always written as it fixes them, so a caller cannot
produce a `/P` a reader would reject.

`Encrypt` refuses bytes that are not a PDF, a document with no catalog, and a document that is
already encrypted: the answer to that last case is `Open` first, and the message says so.

## 5. What encryption does to a signature

An encrypted document can carry a signature dictionary, and this package can read one — but
encryption and signing do not compose in the direction a reader might hope:

- **Encrypting a signed document invalidates its signatures.** The signature covers the bytes of
  the file it was made over, including the ciphertext of every stream. Re-encrypting rewrites the
  file, so every `/ByteRange` stops matching. The dictionary survives; the signature does not
  verify.
- **Signing an encrypted document is refused.** `PdfSigner` and `PdfSignatureVerifier` throw
  `PdfSignatureException` when the trailer names `/Encrypt`. An incremental update to an encrypted
  file would append plaintext that no reader would decrypt, and a signature over those bytes could
  never verify.
- **No order keeps both with a PAdES signature.** Signing an encrypted document is refused, and
  encrypting a signed one rewrites the bytes its `/ByteRange` covers. A signature that must survive
  encryption has to be computed over the encrypted bytes and kept outside the encrypted region —
  a producer format this package does not implement. The honest answer is: encrypt after signing,
  and treat the earlier signature as void.

This is a property of the format, not a limitation of the implementation, and the specification
section of `14-pdf-signature.md` says the same from the other side.

## 6. The rest of the package

Every other operation in `M3scalinum.Text.Pdf` refuses an encrypted document rather than half-read
it, with a message that names `PdfEncryption.Open`. The one exception is `PdfText`, which can be
handed the password directly and decrypts before reading.

| Operation | Refusal |
| --- | --- |
| `PdfPages.Merge`, `Extract`, `Split`, `Rotate`, `Remove` | `PdfOperationException` |
| `PdfForm.Fill`, `Flatten` | `PdfOperationException` |
| `PdfSigner.Sign`, `PdfDocumentTimestamp.Add`, `PdfSignatureVerifier.Verify` | `PdfSignatureException` |
| `PdfConformance.Check` | not an exception: a report whose only finding is `PDF.ENCRYPTED`, because the checker never throws on a document |
| `PdfText.Extract`, `PdfText.ExtractPage` with a password | no refusal: the document is decrypted first and its text read from the plaintext |
| `PdfText.Extract`, `PdfText.ExtractPage` without a password | `PdfTextException`; a wrong password is `PdfTextError.WrongPassword` |
| `PdfEncryption.Open` on an unencrypted document | no refusal: the bytes come back unchanged |

The point of the refusal is that a half-rewritten encrypted file is worse than no file: the output
would look like a PDF and hold ciphertext where a reader expects text.

## 7. Security posture

- **Passwords are not stored, logged or echoed.** They live in the call for the length of the call;
  no exception message, no problem record and no report contains a password or a key.
- **Permission bits are advisory.** `/P` tells a conforming reader what the author intended. It
  cannot stop a reader that ignores it, and this package reports the bits without enforcing them.
- **AES-256 with revision 6 is the default and the only thing written.** A document encrypted by
  this package cannot be opened by a PDF 1.6 reader, which is the price of using the strongest
  handler the format defines.
- **No key is cached between calls.** Each `Open` derives its key from the password it was given
  and drops it when it returns; there is no process-wide key, no static state and no ambient
  credential.
- **A wrong password is indistinguishable from a wrong key.** The `/U` check is the only
  authentication the format offers; a document that passes it but fails to decrypt a stream is
  reported as having an unreadable object.

## 8. Tests and fixtures

`tests/Text.Pdf.Tests/PdfEncryptionTests.cs` covers both directions and every refusal. Two of its
inputs are deliberately not this library's own output:

| Input | What it proves |
| --- | --- |
| `TestEncryption.Aes128()`, a fixture built in the test project from ISO 32000-1 Algorithms 1, 2, 3 and 5 | the AES-128 reader agrees with a second implementation of the key derivation rather than with itself |
| `fixtures/encryption/qpdf-r6-user-password.pdf`, written by qpdf with 256-bit AES | the AES-256 reader agrees with the producer whose output real documents carry |

The round trip is asserted on what a reader sees — page count, and a text marker read back through
the fonts' `/ToUnicode` maps — not on the bytes differing, which a writer that encrypts and never
decrypts would also achieve.
