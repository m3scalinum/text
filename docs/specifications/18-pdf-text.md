# 18 — PDF text extraction

## 1. Scope

`M3scalinum.Text.Pdf` can now read the text of a document it was handed. `PdfText` is the entry
point:

- `PdfText.Extract(ReadOnlySpan<byte> pdf, string? password = null)` returns a `PdfTextResult`: one
  `PdfPageText` per page, in page order, plus the document's page count.
- `PdfText.ExtractPage(ReadOnlySpan<byte> pdf, int page, string? password = null)` returns that one
  page, `page` being zero-based like the compiler's page export.

Bytes in, bytes out, as everywhere in this package: no compiler dependency, no file system, no font
engine, no network. The caller compiles a document or receives one, passes the bytes, and gets the
characters back.

**In scope.** Walking each page's content streams; decrypting an encrypted document when the caller
supplies its password, through `PdfEncryption.Open`, then walking the decrypted bytes; decoding the
filters the reader already supports (`FlateDecode`, `ASCIIHexDecode`); interpreting the
text-showing operators and the text state they depend on; mapping glyph codes to characters through
the font's `/ToUnicode` CMap; walking a Form XObject with its own resources; reporting, per page,
what could not be mapped faithfully.

**Out of scope, deliberately.**

| Not done | Why |
| --- | --- |
| Reading an encrypted document with no password, or one whose handler is RC4 or public-key | There is no key to read with; the document is refused with a typed error. |
| Layout reconstruction, column detection | The reader has the glyph positions and uses them only for word gaps and line breaks. Inferring a reading order from them is a research problem with no single right answer. |
| OCR | A scanned page holds an image, not glyphs. There is nothing to extract and the page comes back empty. |
| Table, list or heading recognition | The characters are recovered; the structure that produced them is not on the page. |
| Rendering | Nothing is painted, so nothing is checked against what a viewer would show. |
| A full glyph name list | A name the reader does not know falls back to the raw code and is reported. |

A scan is the one case where nothing can be read and something can still be done: the OCR engine of
`M3scalinum.Text.Ocr` reads the image, and §8 of this document writes what it read back into the
document as an invisible layer, which this reader then reads like any other text.

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

// An encrypted document is decrypted first, with the password the caller supplies.
PdfTextResult protectedDocument = PdfText.Extract(pdf, "correct horse");
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

An encrypted document with no password is refused, not half-read: its streams are ciphertext, so
decoding them as if they were plain text would produce garbage. The error is typed and names the
reason the file gives.

When the caller supplies a password, the document is decrypted first through `PdfEncryption.Open`
and the decrypted bytes are read like any other document. A password that matches neither the user
nor the owner password is its own typed error, `PdfTextError.WrongPassword`; it is not a corrupt
document and not an unhandled `PdfEncryptionException`. A security handler this package does not
implement — RC4, or public-key encryption — is refused as `Encrypted`, because no password could
open it.

```csharp
try
{
    PdfTextResult result = PdfText.Extract(pdf, "correct horse");
}
catch (PdfTextException exception) when (exception.Error == PdfTextError.WrongPassword)
{
    // The password matches neither the user password nor the owner password.
}
```

| Situation | Result |
| --- | --- |
| Bytes are not a PDF | `PdfTextException` with `PdfTextError.NotAPdf` |
| Document is encrypted and no password is supplied | `PdfTextException` with `PdfTextError.Encrypted`, message naming the handler |
| Password supplied for a supported handler but it does not open the document | `PdfTextException` with `PdfTextError.WrongPassword` |
| Document is encrypted with a handler this package does not decrypt (RC4, public-key) | `PdfTextException` with `PdfTextError.Encrypted`, whatever the password |
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
- **An encrypted document** — the refusal without a password is asserted with the typed error and
  the handler named; an AES-256 document written by `PdfEncryption.Encrypt` and the AES-128 fixture
  built from the specification both read their text back with the right password, through
  `Extract` and `ExtractPage`; a wrong password is `PdfTextError.WrongPassword`; an RC4 handler is
  still `Encrypted`; an empty password opens a document encrypted with one while no password does
  not; and an unencrypted document reads the same whether a password is passed or not.
- **A page with no content** — the empty string, and no fallback or skipped stream invented for it.

The older `PdfTextProbe` in the test project remains the conformance and page-operation tests' way of
reading a file back. It is a probe, not this API: it concatenates strings and ignores positioning,
which is enough for "the text survived a merge" and not enough for a caller reading a document it was
handed.

## 8. Making a scan searchable: the text layer

A scanned page holds an image and no glyphs, so §1's table is right about what this reader can read
from it: nothing. The other half of the loop is writing text back. `PdfTextLayer` appends an invisible
layer of positioned words to the page, in the font and the operators a reader already understands, and
this reader then extracts it like the text of any other document.

```csharp
using M3scalinum.Text.Pdf;

byte[] scanned = File.ReadAllBytes("scan.pdf");

PdfTextLayerWord[] words =
[
    new PdfTextLayerWord(0, "Facture", 72, 690, 132, 710),
    new PdfTextLayerWord(0, "n°", 140, 690, 155, 710),
    new PdfTextLayerWord(0, "2026-0042", 163, 690, 240, 710),
];

byte[] searchable = PdfTextLayer.Add(scanned, words);

PdfPageText page = PdfText.ExtractPage(searchable, 0);
```

### 8.1 What the caller hands over

One `PdfTextLayerWord` per word: the zero-based page, the characters, and a box — `Left`, `Bottom`,
`Right`, `Top` — **in the page's default user space**: points, origin at the bottom-left corner of the
`/MediaBox`, the space `/MediaBox` itself is written in, and not the rotated space a viewer displays.
The bottom edge becomes the baseline and the height becomes the font size; the horizontal scale of
each word is fitted so that its advance spans the box, which is what makes a reader highlight the
word it read and not an approximation of it.

The order of the list is the reading order. Within a page the layer writes the words in the order it
was given and never sorts by position: reading order is the caller's fact — an OCR engine's line and
word order — and a layer that sorted by position would read a right-to-left line backwards.

### 8.2 What the file gains

One incremental update, written the way `PdfSigner` and `PdfAttachments` write theirs: every object
is appended after the last byte, the page dictionaries are defined again later in the file, and
nothing before the update moves. That is what keeps a signature over the earlier revision valid, and
`PdfSignatureVerifier` reports it exactly that way: `IsIntact` stays true and `CoversWholeFile`
becomes false, because the layer is part of the file and no earlier signature covers it.

For a page that carries words, the update adds:

| Object | What it is |
| --- | --- |
| One content stream | The layer's operators, uncompressed so that a reader, a validator or a person with an editor can see them. |
| One `/Resources` dictionary | A copy of the resources the page resolved to — its own, or the page tree's — with `/Font` replaced. Copying rather than editing is what covers a page that inherits its resources, one that points at them, and one that carries them inline, and it never rewrites an object another page also uses. |
| One merged `/Font` dictionary per distinct set of fonts | The fonts the page already had, plus `/M3TextLayer`. A page that already declares that name is refused: a second layer would renumber the glyphs the first one draws. |
| A `Type0` font | `/Encoding /Identity-H`, one descendant, one `/ToUnicode`. |
| A `CIDFontType2` descendant | `/CIDToGIDMap /Identity`, `/DW 500`, a `/CIDSystemInfo` naming Adobe/Identity/0. |
| A font descriptor | `/Flags 4` (symbolic), `/FontBBox[0 -200 500 800]`, and `/FontFile2`. |
| A `/ToUnicode` CMap | One `bfchar` entry per glyph, in blocks of a hundred: the code the stream writes and the character it stands for. |
| A `/FontFile2` stream | The embedded program, Flate-compressed, with `/Length1`. |

The page's `/Contents` becomes an array holding the streams it had and this one last. Last is what
puts the layer *under* nothing and *over* nothing: the text is drawn invisible, so it paints no pixel,
while the extraction order — the order the streams are read — puts the layer's words after whatever
the page already drew, which on a scan is an image and therefore nothing.

The content stream is one block per page:

```
q
BT
3 Tr
/M3TextLayer 20 Tf
0.8571 0 0 1 72 690 Tm
<0002000300040005000600070004> Tj
...
ET
Q
```

`3 Tr` is the invisible text rendering mode: the glyphs are positioned and never painted. `Tm` places
each word, with the horizontal component carrying the fit to the box. `Tj` shows the word as one
string of two-byte codes — the glyph identifiers the CMap maps back to characters — followed by one
space glyph, which is what guarantees a word boundary in the extracted text. The space is invisible,
and a reader that trims a line trims it, so a separator is never doubled.

### 8.3 The font, and why it is generated

A `Type0` font is what carries a `/ToUnicode` map for arbitrary characters, and its descendant CID
font must be embedded: a non-embedded one is a document a reader substitutes a font for, and PDF/A
refuses it outright. The repository ships no font file to subset — `native/sharp-typst` embeds none,
and `Text.Pdf` has no font resource — so the layer writes its own program, once, for the glyphs the
document uses.

The program is a real sfnt: nine tables (`cmap`, `glyf`, `head`, `hhea`, `hmtx`, `loca`, `maxp`,
`name`, `post`), correct table checksums, and a `head.checkSumAdjustment` that makes the file sum to
`0xB1B0AFBA`. Every glyph is empty and every advance is 500 of 1000 em. That is the honest shape of
what a text layer needs: the characters are never painted, so an outline would never be seen, and the
one advance is compensated by fitting each word's horizontal scale to its box. The cost is 4 bytes of
`hmtx` and 2 bytes of `loca` per distinct character, plus about 500 bytes of fixed tables: a page of
Latin text adds well under a kilobyte of font, and the whole layer stays in single-digit kilobytes,
against the hundreds of kilobytes the scan itself takes.

Glyph identifiers are assigned in the order the characters first appear, so the codes a stream writes
are not the characters and not even close to their code points; the `/ToUnicode` CMap is the only
thing that turns them back into text. That is what a subset font does, and it is why the CMap is not
optional here.

### 8.4 What a reader gets back

`PdfText.Extract` reads the layer through the CMap, exactly as it reads any other `Type0` font, and
reports no fallback font for it. Words come back separated by a space, in the order the streams show
them; a new line is the reader's own rule — the baseline moved, or the pen jumped backwards — applied
to the boxes the layer placed. For a scan, the extracted text is the layer alone, in reading order:

```
Facture n° 2026-0042
```

The tests assert that shape on a hand-written scan fixture: the four words of two lines come back as
two lines with single spaces, the earlier revision is a prefix of the result, the growth is bounded,
the page's `/Contents` is an array, the embedded program parses (its directory is alphabetical,
`loca` is `2 × (numGlyphs + 1)` bytes, `hmtx` is `4 × numGlyphs`, every glyph offset is zero, and the
file sums to the sfnt constant), a document signed before the layer still verifies after it, an
encrypted document is refused, and so are a rotated page, a page that already carries a layer, a word
with no text, a word with no area, and a word carrying a control character. Outside the suite the
produced file was read by poppler's `pdftotext` (which prints the words, in order, with no warning
about the font) and the embedded program was read and re-saved by `fontTools`.

### 8.5 What it does not cover

| Not covered | Why, and what to do |
| --- | --- |
| A page with `/Rotate` other than 0 | The boxes are in the page's own user space, and the raster an OCR engine reads is the *displayed* page, so the two differ by the rotation. The layer refuses the page rather than placing words where the rotation does not put them; rotate the page to zero first, or map the boxes yourself. |
| A right-to-left line, or any line whose words go backwards in x | Extraction keeps every word and every character, but this reader treats a jump to the left as a new line, so an RTL line reads back one word per line. Nothing is lost and nothing is reordered; the line structure is. |
| A page whose content leaves a `cm` in force | The layer's coordinates are the default user space and it assumes the content it follows restores the graphics state (a balanced `q`/`Q`), which every producer's page does. |
| A document whose cross-reference is a stream | `PdfDocument` needs a classic trailer to append an update, the same limitation `PdfAttachments` and `PdfSigner` have. |
| An encrypted document | An incremental update would append plaintext no reader decrypts. Decrypt with `PdfEncryption.Open` first. |
| Tagged PDF, `/ActualText`, PDF/UA | The layer makes text extractable and selectable; it does not describe structure or mark the text as an artefact-free alternative of the image. |
| PDF/A-4 | The generated program has no outlines and the descriptor is symbolic; a claim of PDF/A would need a real embedded font. The layer does not change what the document claims about itself. |
| Selecting the word's exact glyphs | Every glyph shares one advance; the fitted horizontal scale makes the word span its box, and a reader's selection of a single character inside the word may be narrower or wider than the ink under it. The line is right, and the characters are right. |

