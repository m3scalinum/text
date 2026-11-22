# 37 — Footnotes and figures: one sequence per document, or one per section

## 1. Scope

Two numbered elements of the document model gain a choice of what their number counts from:

- `TypstDocument.Footnote(content, scope)` — a note mark and its text.
- `TypstDocument.Figure(configure, scope)` — an image figure with a caption.

`scope` is `TypstNumberingScope`:

| Value | What the number counts from |
| --- | --- |
| `Document` | One sequence for the whole document. The default, and the behaviour every document had before this choice existed. |
| `Section` | The sequence restarts at each section, that is, at each level-1 heading. |

The model computes no number. Typst numbers footnotes with `counter(footnote)` and figures with
`counter(figure)`, and the only thing this package can add is the rule that makes those counters
behave differently. A section scope therefore writes a small preamble — two rules at most — and
leaves the counting to the compiler.

**In scope.** The reset rule for footnotes; the section-prefixed numbering and the reset for figures;
the default that writes none of it; the tests that prove each numbering on a compiled document.

**Out of scope, deliberately.**

| Not done | Why |
| --- | --- |
| Endnotes | Typst 0.15.1 has one note mechanism, `footnote`, and it lays the note on the page that carries the mark. An endnote is a caller's collection and a second rendering; §8 says what a caller does instead. |
| Notes numbered per page | Restarting `counter(footnote)` on every page is not expressible as a counter reset: there is no "current page changed" hook in markup. §8 records the manual route. |
| A configurable section level | "Section" is the level-1 heading. A document whose parts are level 2 is a document that labelled its level-1 title, and a caller who needs another level writes the one-line show rule themselves. |
| Non-decimal numbering | The model writes `"1"` and `"1.1"`. A caller who wants roman numerals or dashes sets `footnote(numbering: …)` or `figure(numbering: …)`; Typst's later rule wins, and §8 says what that costs. |
| Numbering tables per section | A table is a figure of kind `table` and keeps the numbering it had. The section rules are scoped with `figure.where(kind: image)` on purpose. |
| A per-document flag as well as the parameter | Two ways to say the same thing is one way too many. `Footnote()` and `Figure()` keep their signature and gain an optional parameter; `TypstDocumentStyle` is untouched. |

## 2. What Typst already does

| Counter | Stepped by | Reset by | Printed by |
| --- | --- | --- | --- |
| `counter(footnote)` | each `#footnote[…]` | `counter(footnote).update(0)` | the note mark and the note text |
| `counter(figure.where(kind: image))` | each image `#figure(…)` | `…update(0)` | `Figure 1`, `Figure 2` |
| `counter(heading)` | each numbered heading | Typst, per level | the heading number |

Two facts drive the whole design:

1. A `#set`/`#show` rule is a *document-wide* switch; it cannot be attached to a single call. A
   per-call `scope` therefore arms a document-level rule. That is fine as long as the rule is only
   armed when a call asks for it, and it is the reason mixing scopes in one document is not
   meaningful (§6).
2. `counter(heading)` only advances when headings are numbered. The figure prefix needs it, so a
   section-scoped figure turns heading numbering on unless the document already did — the same
   `#set heading(numbering: "1.1")` the model already writes when a heading is labelled.

## 3. The two forms measured against Typst 0.15.1

Four candidates were compiled on the embedded compiler. The retained form is the one whose result
does not depend on a document the caller has not written yet.

### 3.1 Footnotes

| Form | Markup | Compiled result |
| --- | --- | --- |
| Composite pattern | `#set footnote(numbering: "1.1")` | **Ignored.** The notes still print 1, 2, 3: footnote numbering has one component and the pattern's second is dropped. |
| Composite function | `#set footnote(numbering: n => numbering("1.1", counter(heading).get().first(), n))` | Prints `0.1, 0.2` until heading numbering is on, then `1.1, 1.2`; it also needs a reset to give `1.1, 2.1` per section. Two dependencies for one behaviour. |
| **Reset** | `#show heading.where(level: 1): it => { counter(footnote).update(0); it }` | **Retained.** Prints 1, 2 in the first section and 1, 2 again in the second, with no heading numbering and no footnote numbering set. |

### 3.2 Figures

| Form | Markup | Compiled result |
| --- | --- | --- |
| Per-figure pattern | `#figure(…, numbering: "1.1")` | **Ignored.** Prints `Figure 1`, `Figure 2`, `Figure 3`. |
| Composite function | `#show figure.where(kind: image): set figure(numbering: n => numbering("1.1", counter(heading).get().first(), n))` | `Figure 0.1, Figure 0.2` without heading numbering, `Figure 1.1, Figure 1.2` with it — but the second section continues the count: `Figure 2.3`. The section prefix alone is not enough. |
| Hand-stepped section counter | A `#let section = counter("…")` stepped in the heading show rule | Correct on a plain document — `1.1, 2.1` — and **wrong as soon as the document has an outline**: the outline re-walks the headings, the show rule steps the counter again, and the figures print `2.1, 3.1`. Rejected: the numbering would depend on whether the document has a table of contents. |
| **Composite function + per-section reset** | The two rules of §4.2 | **Retained.** `Figure 1.1, Figure 1.2` then `Figure 2.1`, with or without an outline, and a plain `Figure 1` for a figure that precedes the first section. |

The compiler's own `counter(heading)` is the section number: it is maintained by Typst, it survives
introspection, and a document that queries its headings cannot move it.

## 4. What the model writes

### 4.1 A section-scoped footnote

```typst
#show heading.where(level: 1): it => {
  counter(footnote).update(0)
  it
}
```

A show rule rather than a reset written before each heading: the model writes the body in call
order, so when `Footnote(…)` runs it does not yet know where the next section begins.

### 4.2 A section-scoped figure

```typst
#set heading(numbering: "1.1")            // only when the document sets no heading numbering
#show heading.where(level: 1): it => {
  counter(figure.where(kind: image)).update(0)
  it
}
#show figure.where(kind: image): set figure(numbering: n => {
  let section = counter(heading).get().first()
  if section == 0 { numbering("1", n) } else { numbering("1.1", section, n) }
})
```

- The first rule restarts the image figure counter, so the first figure of a section is `1`.
- The second prints `section.figure`, and a plain number when no section precedes the figure —
  `if section == 0` is what makes a figure in a preface `Figure 1` and not `Figure 0.1`.
- `figure.where(kind: image)` keeps tables out of both rules: a table stays `Table 1`, `Table 2`
  across the document, exactly as before.
- The heading-numbering line is only added when the document carries none. A document that already
  numbers its headings (or that labelled a section, which turns `#set heading(numbering: "1.1")` on)
  keeps its own rule.

Figures written with `scope: Section` are still ordinary `#figure(…)` calls: the rules are additive,
so `alt`, `caption`, `kind` and the label keep the markup they had, and `#ref(<label>)` resolves to
`Figure 1.1` because the number is the figure's own.

## 5. The API

```csharp
using M3scalinum.Text.Documents;

var report = TypstDocument.Create()
    .Heading(1, "Revenue")
    .Paragraph(c => c.Text("Revenue grew 12%").Footnote("Audited by the group."))
    .Footnote("Source: the ledger.", TypstNumberingScope.Section)
    .Figure(
        f => f.Image("charts/revenue.png").WithCaption("Revenue by month"),
        TypstNumberingScope.Section)
    .Heading(1, "Costs")
    .Footnote("Source: the purchase ledger.", TypstNumberingScope.Section);
```

The default is the document-wide numbering every existing caller has:

```csharp
var unchanged = TypstDocument.Create()
    .Footnote("one")                       // 1
    .Figure(f => f.Image("logo.png"));     // Figure 1
```

`Footnote` keeps its `TypstContent` parameter and gains an optional `scope`; the implicit conversion
from `string` still applies, so `Footnote("a note")` compiles as before. An undefined enum value is
rejected with `ArgumentOutOfRangeException`, named after the parameter.

**Decision.** The scope is a parameter on the two methods, not a document-level flag and not an
option of `TypstDocumentStyle`.

- A document-level flag would be a second switch for the same behaviour, and it would claim a
  document-wide decision that only some elements may want.
- `TypstDocumentStyle` describes appearance (page, body, headings, language, header, footer). Its
  audience is a template pack, not a per-element choice.
- The parameter is where the caller already is: `Footnote(…)` and `Figure(…)` are the calls that
  emit the element, and one overload keeps the default intact.

Rejected alternative: a separate `SectionNumbering(true)` call. It reads as a document-wide mode,
which is what the rule is, but it is a second way to reach the same markup — and the first call that
forgets it produces a document that looks numbered by section yet is not.

## 6. Defaults and non-regression

- `TypstNumberingScope.Document` is the default value of both parameters. No rule is written, and
  the emitted markup is byte-identical to the markup before this feature: `#footnote[…]` and
  `#figure(…)` and nothing else.
- The rules are written once per document however many calls ask for them; a document that asks for
  footnotes and figures writes both show rules, and Typst applies each of them once per heading
  (measured: adding a `#show heading: it => block(…)` style rule beside them changes neither
  counter).
- Mixing scopes in one document is not meaningful: the rules are counters, so once a rule is armed,
  every note or figure after a heading restarts, whatever scope its own call asked for. The XML
  documentation of `Footnote` says so.
- A document with no level-1 heading numbers 1, 2, 3 whichever scope it asks for: the rule exists but
  nothing triggers it.

## 7. Tests

Markup, `tests/Text.Typst.Tests/FootnoteNumberingTests.cs`:

| Test | Invariant |
| --- | --- |
| `A_document_footnote_writes_the_markup_it_always_did` | default scope writes no rule |
| `A_section_footnote_adds_the_reset_rule_and_keeps_the_note_markup` | the reset rule and `#footnote[…]` unchanged |
| `The_footnote_reset_is_written_once_however_many_notes_ask_for_it` | one rule per document |
| `A_document_figure_writes_the_markup_it_always_did` | default figure markup unchanged |
| `A_section_figure_adds_the_section_rules` | heading numbering, reset and numbering function |
| `A_section_figure_keeps_the_heading_numbering_the_document_already_set` | no duplicate `#set heading(numbering: …)` |
| `The_section_figure_rules_are_written_once` | one copy of each rule |
| `Footnote_and_Figure_scope_names_are_the_only_accepted_ones` | `ArgumentOutOfRangeException` |
| `Footnote_still_accepts_a_bare_string` | the implicit conversion still applies |

Compiled, `tests/Text.Typst.Integration.Tests/FootnoteNumberingTests.cs` — the only proof that counts
for numbering:

| Test | What the compiler printed |
| --- | --- |
| `A_document_without_sections_numbers_its_notes_one_two_three` | `1one 2two 3three` |
| `A_section_scope_restarts_the_notes_at_each_section` | `1first note one 2first note two 1second note one 2second note two` |
| `Two_sections_number_their_figures_one_one_and_two_one` | `Figure 1.1: one`, `Figure 1.2: two`, `Figure 2.1: three` |
| `A_section_figure_before_the_first_section_keeps_a_plain_number` | `Figure 1: standalone` |
| `The_default_scope_keeps_figures_numbered_across_the_document` | `Figure 1: one`, `Figure 2: two` |
| `A_reference_to_a_section_numbered_figure_prints_the_section_number` | `See Figure 1.1` |
| `An_outline_does_not_disturb_the_section_numbering` | `Figure 1.1`, `Figure 2.1` beside a table of contents |

## 8. What remains manual

| Case | What the caller does |
| --- | --- |
| Endnotes | Typst lays a footnote on the page that carries its mark. Collecting the notes and printing them at the end is a second document: write them where they belong, or emit the collection as blocks and accept that the marks are no longer links. Nothing in this package moves a note. |
| Notes numbered per page | `counter(footnote)` cannot be reset "at each page break" in markup — a show rule on `page` is not a document hook. The manual route is `#show footnote: …` with a counter of the caller's own, reset by an explicit `#pagebreak()` the caller writes: possible, and outside the typed API. |
| Roman, alphabetic or dashed numbers | `Set("footnote(numbering: \"a\")")` keeps working with a section scope — the reset does not touch the pattern. For figures, a `figure(numbering: …)` rule of the caller's own is written *after* the model's, so it wins: the section prefix is then the caller's business. |
| A section level other than 1, or parts | Write the show rule by hand: `#show heading.where(level: 2): it => { … }`. One line, and it composes with the model's rules — but the model will not guess it. |
| Section numbers in the table of contents | Typst prints them, because enabling heading numbering is what the figure prefix needs. A caller who wants figures numbered by section but headings unnumbered writes a show rule that hides the heading number; the model does not, and §4.2's `#set` is the visible consequence of the choice. |
| Notes inside a heading | A footnote in a heading is not the element the reset describes; a document that puts one there gets Typst's numbering, not this contract's. |
