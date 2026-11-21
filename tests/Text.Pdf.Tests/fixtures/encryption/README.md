# Encryption fixture

`qpdf-r6-user-password.pdf` is the one encrypted document the tests read from disk, and it is not
this library's output: it is `resources/encryption/r6-user-password.pdf` from the
[pypdf](https://github.com/py-pdf/pypdf) test suite, committed unchanged.

| | |
| --- | --- |
| Produced by | `qpdf --encrypt "asdfzxcv" "" 256 -- unencrypted.pdf r6-user-password.pdf` |
| Handler | standard security handler, AES-256, `V=5/R=6`, `/P -4` |
| User password | `asdfzxcv` |
| Owner password | none: qpdf was given an empty owner password, which qpdf replaces with the user one |
| Content | one page from WPS Writer whose text draws `pdfencryptiontestpassword:asdfzxcv` |
| Licence | BSD-3-Clause; the notice is in `THIRD-PARTY-NOTICES.md` at the repository root |

Its only job is to prove that the AES-256 reader agrees with a producer other than itself: a
round trip through this library's own writer would pass even if the key derivation were wrong in
both directions. The AES-128 case is covered by `TestEncryption.Aes128()`, which builds its fixture
from ISO 32000-1 in the test project rather than from the library.

The test project copies this directory with `None Update="fixtures\encryption\**\*"`, so the file is
read from `AppContext.BaseDirectory`.

## Regenerating

Any 256-bit AES PDF with a known user password would do, but the assertions name the text this page
draws: a replacement needs its own marker and its own password in `PdfEncryptionTests`.
