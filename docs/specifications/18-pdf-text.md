# 18 — PDF text extraction

## 1. Scope

`M3scalinum.Text.Pdf` can now read the text of a document it was handed. `PdfText` is the entry
point:

- `PdfText.Extract(ReadOnlySpan<byte> pdf)` returns a `PdfTextResult`: one `PdfPageText` per page,
  in page order, plus the document's page count.
- `PdfText.ExtractPage(ReadOnlySpan<byte> pdf, int page)` returns that one page, `page` being
  zero-based like the compiler's page export.

Bytes in, bytes out, as everywhere in this package: no compiler dependency, no file system, no font
engine, no network. The caller compiles a document or receives one, passes the bytes, and gets the
characters back.

**In scope.** Walking each page's content streams; decoding the filters the reader already supports
(`FlateDecode`, `ASCIIHexDecode`); interpreting the text-showing operators and the text state they
depend on; mapping glyph codes to characters through the font's `/ToUnicode` CMap; walking a Form
XObject with its own resources; reporting, per page, what could not be mapped faithfully.

**Out of scope, deliberately.**

| Not done | Why |
| --- | --- |
| Decryption | An encrypted document is refused with a typed error; another branch owns decryption. |
| Layout reconstruction, column detection | The reader has the glyph positions and uses them only for word gaps and line breaks. Inferring a reading order from them is a research problem with no single right answer. |
| OCR | A scanned page holds an image, not glyphs. There is nothing to extract and the page comes back empty. |
| Table, list or heading recognition | The characters are recovered; the structure that produced them is not on the page. |
| Rendering | Nothing is painted, so nothing is checked against what a viewer would show. |
| A full glyph name list | A name the reader does not know falls back to the raw code and is reported. |

## 2. The API

```csharp
using M3scalinum.Text.Pdf;

byte[] pdf = File.ReadAllBytes("received.pdf");

PdfTextResult result = PdfText.Extract(pdf);

Console.WriteLine(result.PageCount);

foreach (PdfPageText page in result.Pages)
{
    Console.WriteLine($"--- page {page.PageNumber}");
    Console.WriteLine(page.Text);

    foreach (string font in page.FallbackFonts)
    {
        Console.WriteLine($"no /ToUnicode for {font}");
    }
}

PdfPageText second = PdfText.ExtractPage(pdf, 1);
```

`PdfTextResult` carries `Pages` and `PageCount`, and `Text`, which concatenates the pages in order
for a caller that only wants one string to index. `PdfPageText` carries `PageNumber` (one-based),
`Text`, `FallbackFonts`, `HasFallback` and `SkippedContentStreams`.

## 3. What the walk does

### 3.1 Content streams

A page's `/Contents` may be one stream or an array of streams. The array is one logical stream split
for the writer's convenience, so the pieces are decoded and joined, and the text state crosses the
boundary. A stream whose filter the reader does not implement is counted in
`SkippedContentStreams` rather than silently treated as an empty page.

The interpreter reads the operators that change what text is shown, and ignores everything that
paints. The text state is the one the specification defines:

| Operator | What it contributes |
| --- | --- |
| `BT`, `ET` | Start and end a text object; `BT` resets the matrices. |
| `Tf` | The font and its size. |
| `Tc`, `Tw`, `Tz`, `TL`, `Ts` | Character spacing, word spacing, horizontal scaling, leading and rise. |
| `Td`, `TD`, `Tm`, `T*` | Where the next string starts. |
| `Tj`, `TJ`, `'`, `"` | The strings themselves, and the kern adjustments `TJ` carries. |
| `q`, `Q` | Save and restore the text state together with the transform. |
| `cm` | The current transform, which places text drawn in a cell or a form. |
| `Do` | A Form XObject, walked with its own resources. |
| `BI` | An inline image: its bytes are skipped to `EI`. |

Paths, colours, shadings and images are skipped: none of them changes a character.

### 3.2 Where a space and a newline come from

The reader tracks the position the text matrix and the current transform put each shown string at.
Before a string it compares that position with where the previous string ended:

- a baseline that moved by more than 0.4 em is a newline;
- a pen that jumped back by more than 0.5 em is a newline (a new column, or a new run);
- a pen that moved forward by more than 0.15 em is a space.

The thresholds are relative to the font size, so they hold at 8 pt and at 24 pt. A run of spaces is
collapsed to one, a space never opens a line, and a page with text ends with exactly one newline —
the shape the compiler's per-page text has. Nothing else is inserted: a table row is three words
with spaces between them because the cells are far apart, not because the reader knows it is a
table.

**Decision — positions separate words, they do not order them.** The alternative, sorting strings
by `y` then `x`, would turn a two-column page into a plausible reading order and would be wrong on
every page where the producer draws text in an order that is not the visual order, which is most
tagged documents. The glyphs therefore come out in content-stream order, and the documentation says
so.

### 3.3 Fonts and their codes

A producer subsets its fonts and writes glyph identifiers, not characters: the page cannot be read
without the font's CMap. For each font the page names:

1. **`/ToUnicode` when present.** The CMap is parsed for its code-space ranges (which say whether a
   code is one byte or two) and for `bfchar` and `bfrange` in their single-destination and array
   forms. A `bfrange` with one destination increments it per code, as the specification says.
2. **The encoding when there is no CMap.** For a simple font, `/Encoding` supplies a base encoding
   (`WinAnsiEncoding` exactly, the others approximated by Latin-1) overridden by the glyph names of
   `/Differences`. A glyph name is resolved through a table of the ASCII, Latin-1 and typographic
   names; a name the table does not know falls back to the raw code.
3. **The raw code when there is neither.** For a Type0 font, or a simple font with no encoding, the
   code is read as a Unicode scalar value.

Widths come from `/Widths` for a simple font and from the descendant's `/W` array for a Type0 font;
they are used only to know how far the pen moved, which is what makes a word gap detectable. A
missing width is a documented default, not a guess about the glyph.

### 3.4 Form XObjects

`Do` with a `/Subtype /Form` stream walks that stream's operators with the form's own `/Resources`,
falling back to the page's. A form may draw itself: the walk is depth-limited and a form already open
on the path is not entered again, so a cycle terminates. The form's coordinates are its own, so its
text becomes a run of its own — a newline before and after — rather than a continuation of the page
line.

## 4. What is recovered, and what is lost

**Recovered.** The characters the content stream shows, in the order it shows them, with a space
where the layout left a horizontal gap and a newline between lines. Ligatures and hyphenation come
out as the font mapped them. Text on a form XObject is included.

**Lost.**

- Reading order. A two-column page reads across both columns in stream order, not down the first and
  then the second. Text drawn in a table comes out row by row only if the producer drew it that way.
- Layout. No margins, no indentation, no paragraph boundaries beyond the newlines a line change
  produces, no superscript or subscript marking, no bold or italic.
- Structure. A heading, a list item, a table cell and a footnote are text on a line.
- Anything not drawn as text: images, vector artwork, form field appearances that were never drawn
  into the page.

**A scanned page yields nothing.** It holds one image, and an image has no glyphs. The page comes
back with an empty string, no fallback font and no skipped stream — an honest "no text here", not a
failure.

## 5. When the result is not trustworthy

The page says so rather than emitting mojibake silently.

| Signal | Meaning |
| --- | --- |
| `FallbackFonts` names a font | That font has no `/ToUnicode`; its codes were read through its encoding or as raw values. The characters may not be the ones the producer meant. |
| `HasFallback` | At least one font on the page was in that position. |
| `SkippedContentStreams` is not zero | That many content streams used a filter this reader does not implement and were not read, so the page's text is incomplete. |

A caller indexing a document can refuse, flag or quarantine the pages `HasFallback` names, instead
of storing characters nobody can vouch for.

## 6. Refusals

An encrypted document is refused, not half-read: its streams are encrypted, so decoding them as if
they were plain text would produce garbage. The error is typed and names the reason the file gives.

```csharp
try
{
    PdfTextResult result = PdfText.Extract(pdf);
}
catch (PdfTextException exception) when (exception.Error == PdfTextError.Encrypted)
{
    // The message names the security handler the trailer declares.
}
```

| Situation | Result |
| --- | --- |
| Bytes are not a PDF | `PdfTextException` with `PdfTextError.NotAPdf` |
| Document is encrypted | `PdfTextException` with `PdfTextError.Encrypted`, message naming the handler and its revision |
| Catalog unreadable | `PdfTextException` with `PdfTextError.Unreadable` |
| Page index outside the document | `ArgumentOutOfRangeException` |
| A font has no CMap | Not an exception: text plus `FallbackFonts` |
| A content stream uses an unsupported filter | Not an exception: the page is short its text and `SkippedContentStreams` says how much |

A page that draws no text is not an exception either: it is a page whose text is the empty string.

## 7. Verification

The tests in `tests/Text.Pdf.Tests/PdfTextTests.cs` read real files and hand-written ones, because
each states something the other cannot.

- **Compiler output** — `fixtures/text/extract.pdf`, produced by `typst-pdf 0.15.1` through
  `M3scalinum.Text.Typst` from `fixtures/text/extract.typ`: two pages, a marker on each, a
  three-column table, a two-column table and a footnote. The tests assert that each marker is on its
  own page and absent from the other, that a table row reads as words separated by spaces, that the
  footnote is on the page that draws it, that the text ends with exactly one newline, and that no
  page needed a fallback.
- **Another producer** — `fixtures/text/foreign.pdf`, printed from `fixtures/text/foreign.html` by
  headless Chrome: a heading, a paragraph, a table and a footnote in embedded fonts. The reader must
  not be tuned to one producer's content stream.
- **A Type0 font and its CMap** — a hand-written file whose `bfchar`, array `bfrange` and
  incrementing `bfrange` each supply characters, with a `TJ` adjustment that must become a space.
- **No `/ToUnicode`** — a simple font whose `/Differences` renames code 65, and a Type0 font with no
  CMap at all: the documented fallbacks are asserted together with the flag and the font name.
- **A Form XObject** — a page whose text continues in a form with its own `/Resources`.
- **An encrypted document** — the refusal is asserted with the typed error and the handler named.
- **A page with no content** — the empty string, and no fallback or skipped stream invented for it.

The older `PdfTextProbe` in the test project remains the conformance and page-operation tests' way of
reading a file back. It is a probe, not this API: it concatenates strings and ignores positioning,
which is enough for "the text survived a merge" and not enough for a caller reading a document it was
handed.
