# DOCX fixture

`report.docx` is the one package the reader tests open from disk. It was not produced by Word, or by
any writer library: it is built from raw OOXML by `DocxFixture` in the test project, one part at a
time, and zipped with `ZipArchive`. Every byte the reader reacts to is therefore named in
`DocxFixture.cs`, and a failing test points at the property that broke rather than at a producer's
opinion.

| | |
| --- | --- |
| Produced by | `DocxFixture.Build()` — `System.IO.Compression.ZipArchive` over the parts in `DocxFixture.Parts()`, timestamps fixed at 2026-01-01T00:00:00Z |
| Parts | `[Content_Types].xml`, `_rels/.rels`, `word/document.xml`, `word/_rels/document.xml.rels`, `word/styles.xml`, `word/numbering.xml`, `word/media/image1.png` |
| Content | heading 1 and heading 2; a body paragraph with bold and italic runs; a paragraph with underline, colour `C00000`, 14 pt and superscript, plus a struck run; a two-item bulleted list; a three-column table whose first cell spans two columns and whose `Left` cell merges vertically into row three; a 2x2 PNG; an A4 section with 72 pt top and bottom margins |
| Picture | 74 bytes, a 2x2 PNG written by `zlib` and committed as a byte array in `DocxFixture.Png` |
| Licence | created for this repository, Apache-2.0 like the rest of it |

## Regenerating

Change `DocxFixture`, then run the fixture test with the regeneration variable set:

```bash
export SHARPTEXTDOCX_NATIVE_PATH=...                 # the sharp_text_docx library
M3SCALINUM_DOCX_REGENERATE=1 dotnet test tests/Text.Docx.Tests -c Release \
  -- --filter-method "*holds_the_parts*"

cp tests/Text.Docx.Tests/bin/Release/net11.0/fixtures/report.docx tests/Text.Docx.Tests/fixtures/
```

The test writes the file into its output directory and then compares it, part by part, with what the
builder produces — so running it without the variable is how a drift between the committed binary and
the builder is caught. The zip is only a container: the comparison is over the decompressed parts and
their names, not over the archive's bytes, so a different `zlib` build producing a different deflate
stream is not a failure.

## Why a committed binary at all

Because a test that only reads bytes it just built never proves the file on disk is still readable: a
reader can agree with a builder and both can be wrong about what a package is. The committed file is
what an editor wrote once, and the tests hold the reader to it.
