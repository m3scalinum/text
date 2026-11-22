# 38 — Table pagination: a repeated header, and a row that must not be cut

## 1. Scope

A table taller than the page it starts on is laid out across several pages, and Typst decides where
the break falls. Two things about that break were out of reach of the document model:

- the header row was written as `table.header(…)`, which does repeat, but nothing in the model said
  so, nothing could turn it off, and a golden-markup test could not tell a repeating header from a
  fixed one;
- a row could be split down the middle — a cell's paragraphs cut between page 3 and page 4 — with no
  way to ask for the row to be kept whole.

`M3scalinum.Text.Documents` gains two knobs, both optional, both defaulting to what the suite already
emitted or what Typst already did:

| Member | Emits | Default |
| --- | --- | --- |
| `TypstTable.RepeatHeader(bool repeat = true)` | `table.header(repeat: true, …)` / `table.header(repeat: false, …)` | `true` |
| `TypstTableCell.Breakable` (`bool?`) | `table.cell(breakable: false, …)` / `table.cell(breakable: true, …)` | `null` — no argument |

```csharp
document.Table(table => table
    .Columns(TypstLength.Fr(3), TypstLength.Fr(1))
    .RepeatHeader()                                  // the default; written down
    .HeaderRow("Description", "Amount")
    .Row(
        new TypstTableCell("A row whose two halves must stay on the same page") { Breakable = false },
        new TypstTableCell("1 200 EUR") { Breakable = false }));

document.Table(table => table
    .Columns(TypstLength.Fr(3), TypstLength.Fr(1))
    .RepeatHeader(repeat: false)                     // the long appendix: header once
    .HeaderRow("Appendix", "Page")
    .Row("Item 1", "12"));
```

**In scope.** The repetition of the header row, the choice not to repeat it, and per-cell control over
whether the rows that cell covers may land on two pages.

**Out of scope, deliberately.**

| Not done | Why |
| --- | --- |
| Widow and orphan control for rows — never leave one row alone at the foot of a page | Typst's grid layouter has no such rule: a row group is either unbreakable as a whole or broken by content. A `breakable: false` cell keeps *one* row together; keeping two rows together is `breakable: false` on both. Anything more (a minimum of two rows before a break) is a layout algorithm this suite does not own — see [00 §2.3](00-overview.md#23-the-document-test). |
| Repeating a *footer* row (`table.footer`) | A footer row with totals is a different document decision, and the item asks for the header. The markup is one call away through a raw block until a caller needs it. |
| Multi-level headers (`table.header(level: …)`) | A header that repeats in stages needs a model of header groups; the model holds one header row per table today. Two-row headers remain expressible as one header row of spanned cells, which is what the Word and HTML importers produce. |
| Where the break falls — keeping a table on one page | Correct layout is Typst's; the model exposes the two switches Typst offers, not a paginator. |
| A page-level running header showing the table's columns | That is `TypstPageSetup`'s header, which knows nothing of tables. |

## 2. The markup

A table with a header row, two equal columns, and one body row:

```typ
#table(
  columns: (1fr, 1fr),
  table.header(repeat: true, [Item], [Amount]),
  [Hosting], [1 200 EUR],
)
```

With `RepeatHeader(repeat: false)` the first argument becomes `repeat: false`. The header cell forms
are unchanged: a scoped header cell still wraps its content, and a spanned one still goes through
`table.cell`:

```typ
table.header(repeat: true, pdf.header-cell(scope: "both")[Quarter], [Revenue]),
table.header(repeat: true, pdf.header-cell(table.cell(colspan: 2)[Both], scope: "both")),
```

A cell that must not be cut carries the argument beside its spans and its alignment, in one call:

```typ
table.cell(colspan: 2, breakable: false)[kept whole]
```

A table with no header row is unchanged in every respect: `RepeatHeader` is ignored, no
`table.header` is written, and a one-page table emits exactly the markup it emitted before this
document existed (`A_table_with_no_header_emits_no_header_and_ignores_the_repeat_setting`,
`A_table_that_fits_on_one_page_keeps_the_markup_it_always_had`).

## 3. What Typst guarantees

Read from `typst-library` 0.15.1 (`model/table.rs`) and `typst-layout` 0.15.1 (`grid/`):

- **Repetition.** `table.header(repeat: …)` defaults to `true`; Typst draws the header at the top of
  every page region the table continues into, and subtracts its height from the space every
  following region has for rows. The argument the model writes is the one the reference documents —
  it is written out so the markup states the intent instead of relying on a default a later Typst
  version could revisit.
- **Header rows are unbreakable.** `grid/layouter.rs` treats header rows as an unbreakable group: a
  header is never split, and a page break never lands inside it.
- **Accessibility is unaffected.** `table.header` is what makes Typst tag the cells beneath it as
  data cells of a column, and `pdf.header-cell(scope: …)` keeps working inside it — the scope
  argument is what marks a corner cell (`both`) or a header column (`row`). Repeating the header
  repeats that tagging on every page.
- **`breakable: false` keeps a row whole.** A cell that is not breakable starts an unbreakable row
  group (`grid/rowspans.rs`, `simulate_unbreakable_row_group`); the layouter skips to the next
  region when the group does not fit, so the row moves to the next page instead of being cut.
  Consecutive unbreakable cells extend the same group, which is how a row is kept together: set
  `Breakable = false` on its cells.
- **`breakable: auto` is the default and is context-dependent.** A cell spanning only fixed-size
  rows is unbreakable; a cell spanning at least one `auto`-sized row is breakable. A single-row cell
  therefore follows the row it sits in, which is why the model leaves this to Typst unless asked.
- **A kept row taller than a page overflows.** Typst measures an unbreakable auto row at its full
  height and does not split it, so the row is drawn past the page edge rather than broken. This is
  the trade a caller accepts with `Breakable = false`, and the reason it is not the default.

## 4. What the tests establish, and what only compilation can

The markup tests live in `tests/Text.Typst.Tests/TableHeaderRepeatTests.cs`: the default argument,
the opt-out, a scoped header cell inside a repeating header, a merged one, a cell that must not be
cut, a span and a breakability sharing one `table.cell` call, and the two non-regression shapes.

The pagination itself cannot be proved by looking at markup: whether the header really reappears on
page 2 is a property of the layout. `tests/Text.Typst.Integration.Tests/TablePaginationTests.cs`
drives the real compiler and reads the text of every page with `CompilationResult.ExtractTextPages`:

- a two-hundred-row table on A4 spans more than two pages and the header text is on every one of
  them;
- the same table with `RepeatHeader(repeat: false)` holds the header on page 1 only;
- with every cell `Breakable = false`, no row's two halves appear on two different pages;
- a table whose second row is taller than the space left on page 1 splits that row when the cell is
  left alone — the two halves land on different pages — and moves it whole to page 2 when the cell is
  `breakable: false`.

The last two are the pair that proves the option does something rather than coinciding with rows
that happened never to be split.
