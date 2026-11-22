# 34 — A reusable document style

## 1. Scope

`M3scalinum.Text.Documents` gains one object a caller declares once and applies to any document:

- `TypstDocumentStyle` — the frozen style, written by `TypstDocument.Apply`.
- `TypstDocumentStyleBuilder` — the fluent form, returned by `TypstDocumentStyle.Create` and closed by
  `Build`.
- `TypstBodyStyle` — family, size, line height, justification.
- `TypstHeadingStyle` — size, weight, spacing, for one heading level.

**In scope.** The presentation a document repeats today, per document: the page (format, margins,
orientation, page numbering), the body text (family, size, leading, justification), the three heading
levels a report uses (size, weight, spacing), the language, and a running header and footer with page
placeholders.

**Out of scope, deliberately.**

| Not done | Why |
| --- | --- |
| Conditional styles — a page that differs on the first page, an even/odd header | A style describes one presentation. `#set page` with a function, or `TypstPageSetup` used directly, is one `Set` call away, and a conditional is a layout decision, not a declaration. |
| Styles per section | The document model has no sections: `#set` rules are scoped by the markup they precede, and a document is one run of content. A caller that needs a boundary writes a raw `#[…]` block, as it does today. |
| Heading levels 4 to 6 | A document's outline is three levels deep in practice; the deeper levels are the pack's business. A style that covers them would need a fourth and fifth configuration for prose nobody writes. |
| A numbering pattern other than `1.1.1` | Numbering is a yes or no in this model, as the item states. A pattern language would duplicate Typst's `numbering` argument for no gain: a caller that needs `I.` or `a)` passes it through `Set`. |
| Composition, inheritance, "extends" | Two styles do not combine. Applying a second style replaces the first, which is the only behaviour that can be explained in one sentence. |
| Colours, fonts embedded in the style | The style names a family; it does not ship font bytes. Sources and fonts stay where they are (`03-compilation.md`). |
| Storing a style as a `.typ` module | The style is a managed object. A `.typ` file would be a second, drifting copy of the same knowledge and a package dependency for a document model that has none. |

## 2. The shape of the object

```csharp
var style = TypstDocumentStyle.Create()
    .Page(page => page.Paper("a4").Margin(TypstLength.Cm(2.5)).Landscape(false))
    .Body(body => body
        .Font("Libertinus Serif")
        .Size(TypstLength.Pt(11))
        .Leading(1.4)
        .Justify())
    .Heading(1, heading => heading
        .Size(TypstLength.Pt(18))
        .Weight("bold")
        .Spacing(TypstLength.Pt(18), TypstLength.Pt(8)))
    .Heading(2, heading => heading.Size(TypstLength.Pt(14)))
    .Heading(3, heading => heading.Size(TypstLength.Pt(12)))
    .HeadingNumbering()
    .Language(TypstLanguage.French)
    .Header("Compte rendu annuel")
    .Footer("{title} — page {page} / {pages}")
    .Build();

document.Apply(style);
```

**One object, one application.** There is no `DocumentStyleOptions` beside the style, no
`TypstDocument.Styled` flag, no `StyleEnabled` property: a document carries at most one style, and
`Apply` writes it. Applying a second style replaces the first.

**The builder is the only mutable half.** `TypstDocumentStyleBuilder` holds the configuration while it
is being written; `Build` renders the rules once and hands the frozen lines to `TypstDocumentStyle`,
which exposes no mutator at all. Reusing the builder after `Build` cannot change a style already
applied — `A_builder_can_build_twice_and_both_styles_are_independent` proves it.

**Page configuration is `TypstPageSetup`.** `Page` takes the same `Action<TypstPageSetup>` a document
already takes, so a style can express paper, custom size, margins, landscape, fill, columns and page
numbering without a second page vocabulary.

**Heading levels are 1 to 3.** `Heading(0, …)` and `Heading(4, …)` throw `ArgumentOutOfRangeException`;
a level the style does not configure emits nothing.

## 3. The markup it writes

Every rule is a `#set` or a `#show` at the top of the document, after the document's own rules and
before its body.

| Configuration | Emitted |
| --- | --- |
| `Page(p => p.Paper("a4").Margin(TypstLength.Cm(2.5)))` | `#set page(paper: "a4", margin: 2.5cm)` |
| `Page(p => p.Paper("a4").Landscape())` | `#set page(paper: "a4", flipped: true)` |
| `Page(p => p.Size(TypstLength.Mm(210), TypstLength.Mm(297)))` | `#set page(width: 210mm, height: 297mm)` |
| `Body(b => b.Font("Libertinus Serif").Size(TypstLength.Pt(11)))` | `#set text(font: "Libertinus Serif", size: 11pt)` |
| `Body(b => b.Leading(1.4).Justify())` | `#set par(leading: 1.4em, justify: true)` |
| `Language(TypstLanguage.French)` | `#set text(lang: "fr")`, merged into the body rule when both are set |
| `HeadingNumbering()` | `#set heading(numbering: "1.1.1")` |
| `Heading(1, h => h.Size(TypstLength.Pt(18)).Weight("bold").Spacing(TypstLength.Pt(18), TypstLength.Pt(8)))` | `#show heading.where(level: 1): it => { set text(size: 18pt, weight: "bold"); set block(above: 18pt, below: 8pt); it }` |
| `Header("Compte rendu")` | `header: context [ Compte rendu ]` inside `#set page` |
| `Footer("{title} — page {page} / {pages}")` | `footer: context [ #document.title — page #counter(page).display() / #counter(page).final().first() ]` |

Three details of that table are decisions.

**A heading level becomes a show rule, because Typst has no per-level `#set`.** `#set heading(…)`
takes the properties every level shares; a size or a weight that differs by level is a
`#show heading.where(level: n)`. The body of the show rule is a code block, so it stays valid on one
line and stays diffable in a golden test.

**The font and the language share one `#set text`, and the leading and the justification share one
`#set par`.** Two `#set` lines with the same element would be correct but would make a document's
preamble grow with the number of calls; one line per element, listing only the properties the style
declares, is the same markup a careful author writes by hand.

**Page numbering is `Numbering` on the page; heading numbering is `HeadingNumbering`.** The two are
different counters and stay separate calls; the item's "numérotation" is the second one.

## 4. "Only where it is configured"

A `#set` rule in Typst changes the properties it lists and leaves every other property as it was.
That is the whole mechanism, and it gives the two guarantees the item asks for:

- **A document without a style keeps exactly its markup.** `ToMarkup` writes the style's lines only
  when a style was applied, so the output of a document built before this specification is byte for
  byte the same. `A_document_without_a_style_keeps_exactly_its_markup` pins the full string.
- **A style leaves alone what it does not declare.** A document that set a page fill and a header keeps
  them when the style only declares a margin — two `#set page` lines, each listing its own properties,
  and Typst applies both. `A_style_and_the_documents_own_page_setup_coexist` proves it.

The style's lines come **after** the document's own `#set` rules and before the `#let`, `#import` and
`#show` lines the caller added with `Set`, `Let` and `Show`. In Typst the last rule that sets a
property wins, so a document that declares a margin and then applies a style declaring another margin
ends with the style's. That is the intended precedence: the style is the shared presentation, and a
document that must differ from it does not apply it.

Applying the same style twice is one application: the style is a field, not a list, so
`Applying_the_same_style_twice_does_not_duplicate_its_rules` sees the same markup once and a single
`#set page(`.

## 5. Header and footer placeholders

A running text is literal text with three placeholders:

| Placeholder | Becomes | Meaning |
| --- | --- | --- |
| `{page}` | `#counter(page).display()` | the number of the page being laid out |
| `{pages}` | `#counter(page).final().first()` | the number of the last page |
| `{title}` | `#document.title` | the title declared with `TypstMetadata.Title` |

The whole text is wrapped in `context [ … ]`. A page number is not known when `#set page` runs — it is
only known per page — so the header content has to be a context expression; without it, Typst refuses
the counter read. `{title}` needs no nested `context`: the surrounding block already resolved the
document, and `document.title` is then an ordinary field read.

Everything that is not a placeholder is literal text, escaped with the markup rules of
`TypstMarkupWriter`: `A * B` becomes `A \* B`, `[` and `]` become `\[` and `\]`. An unknown brace group
— `{reference}` — stays literal, so a header written for a mailing template is not silently emptied.

A blank header or footer is refused (`ArgumentException`): there is nothing to print, and an empty
running text is a caller's bug, not a rendering choice.

A style whose page setup declares a header through `TypstPageSetup.WithHeader` and which also calls
`Header` keeps the text: the two would be one `header:` argument, and the explicit running text of the
style is the one the caller asked for last.

## 6. Why the object exists

The point is the pair the item names: a style must serve a document that a template produced and a
document a caller built by hand.

```csharp
// A document the caller built, paragraph by paragraph.
var report = TypstDocument.Create()
    .Metadata(metadata => metadata.Title("Rapport annuel").Author("M3scalinum"))
    .Apply(style)
    .Heading(1, "Résultats")
    .Paragraph("Le chiffre d'affaires progresse de 12 %.");

// A document a template produced: the style is applied the same way, to the same type.
var brief = TypstDocument
    .FromMarkdown("# Synthèse\n\nLe trimestre est conforme.")
    .Apply(style);
```

`Apply` takes a `TypstDocument` and returns it, so it slots anywhere in a chain, and nothing in the
style knows where the document came from. `A_style_reaches_an_imported_document` covers the imported
case in this suite; the template pack's documents are the same type and take the same call.

## 7. What is not covered

Stated here so that no reader expects it:

- **No per-section or conditional style.** One style, one document, from the first page to the last.
  A first page without a header is `Page` on the document plus `Set("page(header: none)")`, or a raw
  block; it is not a style.
- **No style arithmetic.** Styles do not merge, do not `Extend` and do not fall back to a default. A
  document applies one style or none.
- **No content in the header beyond text.** A logo is `TypstPageSetup.WithHeader` with a `TypstContent`
  on the document itself; a style carries text and placeholders only, because a picture is an asset and
  an asset belongs to the document that ships it.
- **No font bytes, no font fallback list.** A style names one family; the world supplies it.
- **No effect on HTML or Markdown export.** Both read the document's nodes, not its preamble; a styled
  document exports exactly like the same document without a style.

## 8. Tests

`tests/Text.Typst.Tests/DocumentStyleTests.cs` — twenty-six tests, no native library.

| Behaviour | Test |
| --- | --- |
| Paper and margin | `A_page_format_and_a_margin_are_written_once` |
| Landscape | `A_landscape_page_sets_the_flipped_flag` |
| Custom size | `A_custom_page_size_writes_a_width_and_a_height` |
| Body family, size, leading, justification | `The_body_family_size_leading_and_justification_are_written` |
| No justification asked, none written | `A_body_without_justification_leaves_the_paragraph_alignment_alone` |
| Levels 1 to 3, each in its own show rule | `Each_heading_level_is_styled_by_its_own_show_rule` |
| An unconfigured level emits nothing | `A_heading_level_that_is_not_configured_emits_no_rule` |
| Heading numbering | `Heading_numbering_is_set_on_the_heading_element` |
| Numbering turned off | `Heading_numbering_can_be_turned_back_off` |
| Level 0 and level 4 refused | `A_heading_level_outside_one_to_three_is_refused` |
| Language code | `The_language_becomes_a_text_language_code` |
| Unknown language refused | `An_undefined_language_is_refused_rather_than_guessed` |
| One `#set text` for family and language | `The_body_family_and_the_language_share_one_text_rule` |
| Header text | `The_header_is_written_as_literal_text` |
| Footer placeholders | `The_footer_replaces_the_page_pages_and_title_placeholders` |
| Unknown placeholder stays literal | `An_unknown_placeholder_stays_literal_text` |
| Markup characters escaped | `Markup_characters_in_the_running_text_are_escaped` |
| Blank header refused | `A_blank_running_text_is_refused` |
| **Non-regression: no style, same markup** | `A_document_without_a_style_keeps_exactly_its_markup` |
| **Idempotence: applied twice, written once** | `Applying_the_same_style_twice_does_not_duplicate_its_rules` |
| Style beside the document's own page setup | `A_style_and_the_documents_own_page_setup_coexist` |
| Style reaches an imported document | `A_style_reaches_an_imported_document` |
| Order: after the document's rules, before the body | `The_style_rules_come_before_the_body_and_after_the_documents_own_rules` |
| Empty style writes nothing | `An_empty_style_writes_nothing` |
| Null style refused | `Applying_a_null_style_is_refused` |
| The builder can build twice | `A_builder_can_build_twice_and_both_styles_are_independent` |

The code blocks in this document are checked by `DocumentationDriftTests`: every `Typst…` type and
every method named here exists in the public surface.
