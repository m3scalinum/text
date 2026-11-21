# Text extraction fixtures

`PdfTextTests` reads the text of documents this package did not compile. Two files are real
producer output; the rest of the cases — a CMap in every shape, a font with no CMap, a form XObject,
an encrypted trailer — are written by hand in the test file, where they can be stated exactly.

| File | Produced with | Covers |
| --- | --- | --- |
| `extract.pdf` | `fixtures/text/extract.typ`, compiled by Typst 0.15.1 through `M3scalinum.Text.Typst` | Two pages, a marker per page, a three-column table, a two-column table, a footnote, embedded subset fonts with `/ToUnicode` |
| `foreign.pdf` | `foreign.html`, printed by headless Google Chrome 157 | A different producer: heading, paragraph, table, footnote, embedded fonts with `/ToUnicode` |

## How `extract.pdf` was produced

`extract.typ` was compiled with:

```csharp
new TypstCompilationOptions { DocumentKind = TypstDocumentKind.Paged }
```

and exported with:

```csharp
new PdfExportOptions
{
    Timestamp = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
}
```

The fixed timestamp makes the export reproducible: the same source, fonts and timestamp give the
same bytes.

## How `foreign.pdf` was produced

```bash
chrome --headless --disable-gpu --no-pdf-header-footer \
  --print-to-pdf=foreign.pdf "file:///.../foreign.html"
```

Chrome writes subset Type0 fonts with `/ToUnicode` maps and compresses its content streams, which is
the point: the reader must not depend on the shape one producer happens to use.

## Regenerating

Add `extract.typ` to a world and compile it with the options above; run the Chrome command against
`foreign.html`. The tests do not regenerate these files; they read them from `AppContext.BaseDirectory`,
copied by the test project's `None Update="fixtures\text\**\*"` item.
