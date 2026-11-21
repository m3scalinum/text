# Getting started

`M3scalinum.Text.Pdf` signs a PDF you already have. It takes bytes and returns bytes: it does not
render, it does not depend on the compiler, and it has no native library to load.

```bash
dotnet add package M3scalinum.Text.Pdf
```

```csharp
using M3scalinum.Text.Pdf;

var certificate = X509CertificateLoader.LoadPkcs12FromFile("signature.pfx", password);

byte[] signed = PdfSigner.Sign(pdf, certificate, new PdfSignatureOptions
{
    Reason = "Facture FA-2026-0042",
    Location = "Lille",
});
```

That is the whole API surface: one static method and one options record. Everything else — which
certificate, which key store, when the signature is applied — belongs to your application.

## What it is for

The document it was written for is an invoice: PDF/A-3, an XML attachment a customer asked for, and a
receiver who wants it signed. Signing it with a library that rewrites the whole file destroys either
the conformance or the attachment, so [`PdfSigner.Sign`](Pdf-Signing) appends an incremental update
instead.

## Where to go

- **[Signing](Pdf-Signing)** — what the signature declares, why the file is only appended to, and how
  it was verified.
