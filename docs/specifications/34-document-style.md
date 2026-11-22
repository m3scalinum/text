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
| Styles per section | A style is one presentation for the whole document. The boundary between parts is `TypstSection` (§8), a page setup scoped to a block, not a style: two styles do not combine and a section carries no body font, no heading size and no language. |
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

- **No per-section style.** One style, one document, from the first page to the last. A part that
  changes header, footer, watermark or page numbering is a `TypstSection` (§8): a page setup scoped to
  a block, which carries no typography and does not combine with the style.
- **No style arithmetic.** Styles do not merge, do not `Extend` and do not fall back to a default. A
  document applies one style or none.
- **No content in the header beyond text.** A logo is `TypstPageSetup.WithHeader` with a `TypstContent`
  on the document itself; a style carries text and placeholders only, because a picture is an asset and
  an asset belongs to the document that ships it.
- **No font bytes, no font fallback list.** A style names one family; the world supplies it.
- **No effect on HTML or Markdown export.** Both read the document's nodes, not its preamble; a styled
  document exports exactly like the same document without a style.

## 8. Sections: a header, a footer and a watermark per part

A `TypstDocumentStyle` describes one presentation from the first page to the last. A contract changes
footer at every part and opens on a cover, so the model has a boundary that carries its own page
setup:

```csharp
var report = TypstDocument.Create()
    .Apply(style)
    .Section(
        section => section.NoPageNumber().NoHeader().Footer("COUVERTURE"),
        body => body.Heading(1, "Rapport annuel").Paragraph("Page de garde"))
    .Section(
        section => section
            .Header("Partie I")
            .Footer("Partie I — page {page}")
            .Watermark("BROUILLON"),
        body => body.Heading(1, "Contexte").Paragraph("Le contrat commence."))
    .Section(
        section => section.Header("Partie II").Footer("Partie II — page {page}"),
        body => body.Heading(1, "Annexes").Paragraph("Les annexes suivent."));
```

- `TypstSection` — `Create`, `Header`, `NoHeader`, `Footer`, `NoFooter`, `Watermark`, `NoWatermark`,
  `PageNumbering`, `NoPageNumber`, `StartsOnNewPage`, and `Body`, a whole `TypstDocument`.
- `TypstWatermark` — the word drawn across the page, with `Angle`, `Size`, `Color` and `Opacity`.
- `TypstDocument.Section(TypstSection)` and `TypstDocument.Section(configure, body)` — the second
  builds the section and its content in one call and writes the same markup as the first.

A section replaces what the document and its style declared, for its own pages only: a section with a
footer and no header keeps the document's header, `NoHeader` removes it, and `NoFooter` removes the
footer. The three `{page}`, `{pages}` and `{title}` placeholders are the ones `Header` and `Footer` on
a `TypstDocumentStyle` accept, written by the same helper.

### 8.1 The primitive, and what the measurements ruled out

Typst has no section element, so the candidates were compiled and read rather than assumed.

| Candidate | What a compiled document showed |
| --- | --- |
| `#set page(header: …)` at the top of the document, before each part's content | Works: three parts produced three headers on three pages. **Not usable as a section**: the rule has no end, so the last part's setup stays in force for every page after it and the document's own footer never comes back. |
| The same rule inside a `#[ … ]` content block | **Chosen.** Three sections produced three footers, and content written after the last block came back to the document's footer (`Rapport — page 3` in the measurement). Typst scopes the rule to the block. |
| `context` + `counter(page)` to pick a header from a page range | Not needed: a scoped `set page` already gives the right header per page. A range test would duplicate the page numbers in the source and break as soon as a part grew by one page. |
| A `header` function that reads a `state` the section updates | Works — page 1 read `PARTIE UN`, page 2 read `PARTIE DEUX` — but it needs a named `#let` and an `update` call per section, and the value is read where the page is laid out, so a section that starts mid-page changes the header of that page. The scoped rule needs no name and closes with the block, so the model writes that. |
| `#set page` inside a `#let` function | Applies to the pages the function's content lands on and ends with it (measured: the function's page carried `DANS`, the next page the document's `DOC`). The model does not use it: a block is the caller-visible boundary. |

Two facts of the layout shape the API, and both were measured:

- **A change of page setup starts a new page.** A 400-word paragraph, then a content block that sets
  a new footer, then another paragraph produced three pages: page 1 the paragraph and the document
  footer, page 2 only the block, page 3 the trailing paragraph and the document footer again. The
  same document with a content block that sets nothing produced two pages. Two sections therefore
  never share a page, and `StartsOnNewPage` matters only for a section that overrides nothing.
- **The block closes the override.** Content after `]` is laid out with the document's own setup and
  its style, as the third page of that measurement shows.

### 8.2 A cover with no page number

`NoPageNumber` writes `numbering: none` — the page number is not displayed, the counter is not
touched. Measured on a cover followed by two numbered pages: the cover carries no number, the next
page reads `2`, the one after `3`. `PageNumbering("i")` writes another format for the section, which
is how a report numbers its preliminaries in roman and its body in arabic.

A cover with neither header nor page number is therefore one section:

```csharp
document.Section(section => section.NoPageNumber().NoHeader(), body => body.Heading(1, "Titre"));
```

### 8.3 The watermark

A watermark is the page's `background`, not content: `place(center + horizon, rotate(-30deg, text(…)))`
centred on the page, drawn before the body on every page the section covers, at `15 %` by default. The
defaults are a 30° counter-clockwise slant, 72 pt, and `luma(0.7).transparentize(85%)`; `Opacity(1)`
drops the transparency, and `Angle`, `Size` and `Color` write the rest.

It is text, so it is extractable: `ExtractTextPages` returns the mark's word on each covered page, at
the start of the page's text, before the body it is drawn behind. A caller that extracts text and
wants the mark kept apart filters the word it passed to `Watermark`; the extraction does not label it.
The mark does not disturb the body: the words of the paragraph come out in reading order, contiguous,
and a later section's `NoWatermark` removes it for that section only.

### 8.4 What is not covered

- **No `firstPage` flag.** Typst 0.15.1 has no first-page selector on `set page`; a distinct first page
  is a section whose content is that page, which is what §8.2 shows.
- **No header content beyond text.** A logo is `TypstPageSetup.WithHeader` with a `TypstContent` on the
  document; a section carries text and placeholders, like a style.
- **No section numbering of its own.** A part title is a `Heading` in the section's body; the outline
  and the heading counter are the document's, not the section's.
- **Sections do not merge.** Each writes its own `#set page` inside its own block; a section that
  declares nothing writes an empty block, which is a page break and nothing else.

## 9. Tests

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

`tests/Text.Typst.Tests/SectionHeaderTests.cs` — twenty-seven tests, no native library; they pin the
markup a section writes.

| Behaviour | Test |
| --- | --- |
| Footer written inside a content block | `A_section_writes_its_footer_inside_a_content_block` |
| A section that declares no header keeps the document's | `A_section_that_declares_no_header_leaves_the_documents_header_alone` |
| `NoHeader` writes `none` | `A_section_can_remove_the_header_with_none` |
| `NoFooter` writes `none` | `A_section_can_remove_the_footer_with_none` |
| `NoWatermark` writes `none` | `A_section_can_remove_the_documents_watermark` |
| Watermark is a centred, slanted background | `The_watermark_is_a_page_background_centred_and_slanted` |
| Opacity 1 draws the colour as it is | `A_watermark_with_full_opacity_is_drawn_in_its_own_colour` |
| Any other opacity is transparency | `A_watermark_opacity_is_written_as_a_transparency` |
| Cover numbering | `A_cover_page_is_numbered_none_and_the_counter_keeps_counting` |
| A section's own numbering format | `A_section_can_number_its_pages_with_its_own_format` |
| A format undoes a hidden number | `A_hidden_page_number_is_undone_by_a_later_format` |
| `StartsOnNewPage` writes the break before the block | `A_section_that_starts_on_a_new_page_writes_the_break_before_its_block` |
| No break unless asked | `A_section_that_does_not_ask_for_a_break_writes_none` |
| A section overrides only what it declares | `A_section_only_overrides_what_it_declares` |
| A section that declares nothing writes an empty block | `A_section_that_declares_nothing_writes_an_empty_block` |
| The style's three placeholders, verbatim | `The_section_footer_uses_the_same_three_placeholders_as_a_style` |
| An unknown placeholder stays literal | `An_unknown_placeholder_in_a_section_footer_stays_literal` |
| Blank header, footer, watermark refused | `A_blank_section_header_is_refused`, `A_blank_section_footer_is_refused`, `A_blank_watermark_is_refused` |
| Opacity outside 0 to 1 refused | `An_opacity_outside_zero_to_one_is_refused` |
| Null section refused | `A_null_section_is_refused` |
| The body keeps the document API | `A_section_body_keeps_the_document_api_inside_the_block` |
| Prepared and configured sections write the same markup | `A_prepared_section_and_a_configured_section_write_the_same_markup` |
| Three sections, three page rules | `Three_sections_write_three_page_rules` |
| No section, no block | `A_document_without_a_section_writes_no_block` |
| A body's own `#set` stays inside the block | `A_set_rule_inside_a_section_body_stays_inside_the_block` |

`tests/Text.Typst.Integration.Tests/SectionHeaderIntegrationTests.cs` — nine tests against the real
compiler, through `SHARPTYPST_NATIVE_PATH`; each one reads the pages of the produced PDF, not the
markup.

| Behaviour | Test |
| --- | --- |
| Three sections, three footers, cover unnumbered | `Three_sections_carry_three_footers_and_the_cover_has_no_page_number` |
| A header is drawn on every page of its section | `A_header_is_drawn_on_every_page_of_its_section_and_stops_at_the_next` |
| The watermark is on the page and the body is intact | `The_watermark_is_drawn_on_the_page_and_the_body_text_stays_intact` |
| A later section drops the watermark | `A_later_section_can_drop_the_watermark` |
| A section's numbering format reaches the pages | `A_section_page_numbering_format_reaches_the_pages` |
| A section keeps the footer it does not override | `A_section_keeps_the_document_footer_it_does_not_override` |
| A setup change starts a new page | `A_change_of_page_setup_starts_a_new_page_so_two_sections_never_share_one` |
| The document footer returns after the block | `The_document_footer_comes_back_after_the_section_closes` |
| No section, same rendering | `A_document_without_a_section_still_renders_its_own_footer` |

The code blocks in this document are checked by `DocumentationDriftTests`: every `Typst…` type and
every method named here exists in the public surface.
