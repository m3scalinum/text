# 41 — Columns and margin notes: a page-level layout, and a note level with its mark

## 1. Scope

Two layouts an article or a scientific report asks for and the model could not express:

- **columns for a whole document.** `TypstDocument.Columns(count, content)` already wrapped a piece
  of content in the flow-level `columns` container, and `TypstPageSetup.Columns(count)` already set
  `page(columns: n)` — but neither carried a gutter, and nothing said which of the two primitives a
  multi-page document must use. They do not behave the same: one of them refuses a page break.
- **margin notes.** A marker in the flow and a short text in the outer margin, on the marker's line.

`M3scalinum.Text.Documents` gains three things, all optional, none of which changes the markup of a
document that asks for neither:

| Member | Emits | Default |
| --- | --- | --- |
| `TypstPageSetup.Columns(int count, TypstLength gutter)` | `columns: n` in `#set page(…)`, plus `#set columns(gutter: …)` | single column, unchanged markup |
| `TypstColumnLayout(count, gutter?)`, `TypstPageSetup.Columns(TypstColumnLayout)` | the same, prepared once and reused | — |
| `TypstDocument.Columns(TypstColumnLayout, TypstContent)` | `#columns(n, gutter: …)[…]` — the flow-level container | — |
| `TypstDocument.MarginNote(marker, content, offset, width?)`, `TypstDocument.MarginNote(TypstMarginNote)` | `#box(place(start, dx: …)[…])#super[marker]` | — |

```csharp
var article = TypstDocument.Create()
    .Page(page => page.Paper("a4").Margin(TypstLength.Cm(2)).Columns(2, TypstLength.Em(1)))
    .Heading(1, "A two-column article")
    .Paragraph("The first column fills to the foot of the page before the second begins.")
    .MarginNote("1", "A note in the outer margin.", TypstLength.Cm(17.2), TypstLength.Cm(3.5))
    .Paragraph("…");
```

**In scope.** The column count and the gutter of a page body, the same pair for a flow-level block, a
marker in the flow and a note placed level with it.

**Out of scope, deliberately.**

| Not done | Why |
| --- | --- |
| Balancing the columns of the last page | Typst's `columns` does not balance (`typst-library` 0.15.1, `layout/columns.rs`: "It will currently not balance the height of the columns"), and a page-level layout has no last-page balance either. A balanced last page is a layout algorithm this suite does not own — see [00 §2.3](00-overview.md#23-the-document-test). |
| Computing the note's offset from the page geometry | The model does not lay out, and a margin may be a percentage; see §4. The caller passes the offset, in the same spirit as `TypstPageSetup.Margin`. |
| Note numbering, note style, note collation | The marker is the caller's string (`1`, `*`, `a`), and the note's text is ordinary content: size it with `#text(…)` in the content, number it with the caller's own counter. |
| Automatic de-overlap of two notes | Placed content takes no space in the flow, so Typst does not push a note down. Measured in §3 and asserted in §5. |
| Notes in the margin of a *column* rather than of the page | `place` is relative to the marker's box, not to the column frame; a per-column margin would need a `columns` layout with a per-column gutter band, which Typst does not expose. |

## 2. The primitive: page-level columns, not the `columns` container

Both primitives exist in Typst 0.15.1, and both fill column-major. They part on pagination:

| | `#set page(columns: n)` (page level) | `#columns(n)[…]` (flow level) |
| --- | --- | --- |
| Fills the first column of a page before the second | yes | yes |
| Continues on the next page | yes | yes |
| A `#pagebreak()` inside | works | **compile error**: `pagebreaks are not allowed inside of containers` |
| A `#colbreak()` inside | works | works |
| Footnotes | page foot, full width, at y = 785 pt | same layout, measured |
| Where the gutter comes from | `column`'s style: `#set columns(gutter: …)` | its own `gutter:` argument |

Typst's own reference pushes a whole document to the page-level form (**Decision**; the rejected
alternative is wrapping the body in `#columns`):

> "If you need to insert columns across your whole document, use the `page` function's `columns`
> parameter instead. This will create the columns directly at the page-level rather than wrapping all
> of your content in a layout container. As a result, things like pagebreaks, footnotes, and line
> numbers will continue to work as expected."
> — `typst-library` 0.15.1, `layout/columns.rs`

The measured page break is the decisive one: a report that starts on a fresh page after its cover
cannot be written with the container at all. `TypstPageSetup.Columns` therefore stays the way a
document asks for columns, and now carries the gutter; `TypstDocument.Columns(TypstColumnLayout, …)`
remains for a *block* of columns inside a page.

## 3. The markup, and what it does

### Columns

```typ
#set page(paper: "a4", margin: 2cm, columns: 2)
#set columns(gutter: 1em)
```

The page-level gutter is not an argument of `page`: Typst reads it from the `columns` element's style
(`typst-layout` 0.15.1, `pages/run.rs`: `styles.get(ColumnsElem::gutter)`), so the model writes the
`#set columns(gutter: …)` rule right after the page rule. Measured on A4 with 2 cm margins, the second
column starts at:

| Gutter | Second column x |
| --- | --- |
| none (Typst's 4 % default) | 307.0 pt |
| `1em` (11 pt) | 303.0 pt |
| `2cm` | 326.0 pt |

`TypstPageSetup.Columns(count)` alone still writes exactly `columns: 2` and nothing else: the
documents that already asked for a count keep the markup they had.

The flow-level form is unchanged for one column and gains the gutter for more:

```typ
#columns(2, gutter: 8pt)[…]
```

### Margin notes

```typ
#box(place(start, dx: 17.2cm)[#block(width: 3.5cm)[A note in the outer margin.]])#super[1]
```

- `#box(place(…))` is an inline, zero-size box on the marker's line. Placement is therefore relative
  to the marker's own position, vertically **and** horizontally, and the paragraph is not broken: the
  zero-size box inserts no block into the flow.
- `place(start, …)` keeps the note's first line on the line's baseline. Measured on A4: the body line
  is drawn at y = 71.2 pt and the note at y = 71.2 pt, while its superscripted marker sits at
  y = 63.9 pt, that is 7.3 pt above the baseline — the marker is raised, the note is on the line.
- `#block(width: …)` gives the note a width to wrap at; without a width the note runs on one line.
- `#super[marker]` prints the marker in the flow, after the placement so that the note belongs to the
  text before it in reading order.

**Why not `place(float: true)`.** A floating placement is laid out at the top or the bottom of its
container, never level with its anchor, and the parent-scoped form (`scope: "parent"`) accepts only
`top` or `bottom` (`typst-library` 0.15.1, `layout/place.rs`). Measured: two notes floated
`top + right` both landed at y = 63.9 pt, the first line of the page, regardless of where their
markers were. The overlaid form is the only one that follows the mark.

**Why not a top-level `place`.** Without a box, a `place` call in the middle of a paragraph inserts a
block-level element, and Typst's own reference says it "can affect the layout by breaking the current
paragraph". Measured: the text after the marker started a new line at the left edge (x = 56.7 pt)
instead of continuing the sentence.

## 4. What remains to the caller

| Left to the caller | Why |
| --- | --- |
| The note's offset, in Typst units | The model knows no page geometry: a margin may be a percentage, and a note placed relative to a marker moves with that marker. A4 with 2 cm margins has a 17 cm text area, so `TypstLength.Cm(17.2)` puts a note in the outer margin. In a two-column body the offset must clear the *column*, not the page: 2 columns with a 1 em gutter on the same page are 235.4 pt (8.3 cm) wide, so a note needs an offset of about 8.7 cm. |
| The note's width and style | `width` is a parameter; the size, colour and family of the note are ordinary content (`#text(…)` in the `TypstContent`). |
| Keeping notes apart | Two markers closer than one note is tall give two overlapping notes: placed content does not displace the next placement. The document form of `MarginNote` writes each note as its own body item, so two notes are a full paragraph apart; a note taller than the paragraph before the next marker still overlaps. |
| A note that must break across pages | A placed note cannot be split. A long note belongs in a footnote (`#footnote`), which Typst does paginate. |
| Fitting the note inside the paper | Typst draws placed content wherever it is asked to, including past the page edge. With A4 and 3.5 cm margins, an offset of 14.3 cm and a width of 3 cm keep the note inside the paper. |
| A gutter declared in a `TypstDocumentStyle` | `TypstDocumentStyleBuilder` renders `#set page(…)` only, so a `Columns(count, gutter)` configured there keeps the count and drops the gutter rule. Set the page columns on the document (`Page(page => page.Columns(2, …))`) when the gutter matters. |

## 5. What the tests establish, and what only compilation can

The markup tests live in `tests/Text.Typst.Tests/ColumnAndMarginNoteTests.cs`: the column count and
the gutter rule and their order, a layout applied to the page, the flow-level arguments with and
without a gutter, a document that asks for no columns writing no column setting, the plain paragraph
that must stay `x\n`, the placed note before its marker, a note without a width, two notes in document
order, an escaped marker, and the rejected arguments.

The layout cannot be proved by looking at markup. Six tests in
`tests/Text.Typst.Integration.Tests/ColumnAndMarginNoteIntegrationTests.cs` drive the real compiler,
read `CompilationResult.ExtractTextPages` for the page boundaries and the SVG export for the position
of every glyph run:

- sixty paragraphs in two columns: page 1 holds a contiguous run of paragraphs starting at the first,
  page 2 continues at the next one, and every run of page 1 is drawn column after column — the left
  column is complete before the right one starts, which a row-first layout would interleave;
- the same document without columns: every run starts at the left text edge;
- a margin note: its runs are drawn past the right text edge, level with the marker's line, and below
  the first line of the page (so the note follows its marker rather than the page top);
- two notes on one page with different offsets: the upper note's last line is above the lower note's
  first line;
- two notes whose markers are one line apart: their runs **do** overlap — the documented limit, kept
  as a test so the trade is visible rather than discovered in production.

The column test reads the x of every glyph run instead of counting pages: both primitives paginate
the same way, and only the drawing order tells a column-first fill from a row-first one.
