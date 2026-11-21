# PDF/UA conformance fixtures

These three files are real output from the compiler, committed as bytes because
`Text.Pdf.Tests` deliberately has no reference on `M3scalinum.Text.Typst`: the checker is
bytes-in/bytes-out, so its fixtures cannot be produced at test time without pulling the compiler
into the test project.

| File | Produced with | Claims |
| --- | --- | --- |
| `ua1.pdf` | `Standards = [PdfStandard.PdfUa1]` | PDF/UA-1, header 1.7 |
| `a2b.pdf` | `Standards = [PdfStandard.PdfA2b]` | PDF/A-2b, header 1.7 |
| `a4.pdf` | `Standards = [PdfStandard.PdfA4]` | PDF/A-4, header 2.0 |

Two more files are **not** compiler output and are not conformance fixtures: `xref-stream.pdf` and
`objstm.pdf` are the smallest PDF 2.0 files that put their objects in a cross-reference stream and in
an object stream, written by hand to cover those two reader paths. Their only job is to prove the
parser reads a file whose objects are not plain `N 0 obj` at a table offset; the conformance tests
use them solely to show that a minimal PDF is *not* PDF/UA.

All three come from `document.typ` with `logo.png`, compiled by Typst 0.15.1 through
`M3scalinum.Text.Typst`, with:

```csharp
new PdfExportOptions
{
    Standards = [/* the standard named above */],
    Timestamp = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
}
```

The timestamp and a fixed clock make the export reproducible: the same source, fonts and timestamp
give the same bytes.

The document is deliberately not minimal. It carries a title and an author (so `/Info /Title` and
the XMP `dc:title` exist), a language (`#set text(lang: "fr")` becomes `/Lang`), a figure whose
image has `alt:` text (so the structure tree holds a `/Figure` with `/Alt`), a table, two pages and
two embedded Libertinus fonts with `/ToUnicode` CMaps. Every check the conformance suite implements
therefore has something real to pass on.

## Regenerating

Add `document.typ` and `logo.png` to a world, compile, and export with the `PdfExportOptions` above.
The tests do not regenerate these files; they read them from `AppContext.BaseDirectory`, copied by
the test project's `None Update="fixtures\pdfua\**\*"` item.
