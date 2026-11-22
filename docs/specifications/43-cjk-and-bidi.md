# 43 — CJK and bidirectional text

## 1. Scope

The document model gains the two Typst features that a Chinese, Japanese, Arabic or Hebrew document
needs and that it did not expose: a language that is not one of its two number-pattern rows, and a
text direction. It gains them as additions; a document that asks for neither emits exactly the markup
it emitted before.

- `TypstLanguageTag` — a BCP 47 tag, the value of `text(lang: …)`.
- `TypstDocumentStyleBuilder.Language(TypstLanguageTag)` — the language of a whole document, in the
  style, beside the two-valued `Language(TypstLanguage)`.
- `TypstDocument.Language(TypstLanguageTag)` — the same rule, written explicitly and after every
  style rule.
- `TypstDocument.Direction(TypstDirection, TypstContent)` — a block whose paragraphs take that base
  direction.
- `TypstContent.Direction(TypstDirection, TypstContent)` — a run whose direction differs from its
  paragraph's.
- `TypstContent.Language(TypstLanguageTag, TypstContent)` — a run read in another language.

**In scope.** Setting the language a document or a run is laid out in; setting the base direction of
a block and the direction of a run; refusing the two directions Typst cannot apply to text; and
measuring, on the real compiler, what the resulting layout does — where a justified line ends, where
the glyphs of a mixed Arabic sentence sit, and which characters a Japanese line may begin with.

**Out of scope, deliberately.**

| Not done | Why |
| --- | --- |
| Detecting a paragraph's direction from its first strong character | Typst 0.15.1 has no such detection: `text(dir: auto)` resolves to the direction of the *language*, and the default language is left to right (§4.1). Inventing a detector in the model would disagree with the layout that actually runs. The caller names the language or the direction. |
| Arabic-Indic digits (٠١٢٣) | `TypstFormatting` writes ASCII digits, one table row per language, and a digit mapping is a numbering policy this model does not own. A caller that wants them writes the characters. |
| Vertical writing | `text(dir:)` is horizontal; the compiler refuses `ttb` and `btt` there (§4.5). Vertical text is a rotated block, a different primitive. |
| A bidi-aware hyphenator across a script boundary | Typst hyphenates per run, with that run's dictionary. A word that switches script mid-word is not a case any dictionary covers. |
| Shipping a font | The embedded set carries no CJK and no Arabic face (§8). Choosing and supplying one stays with the caller, as for every other document. |
| Per-paragraph alignment | `align` flips with the direction on its own; a second control beside it would be a way to contradict the direction. |

## 2. What the compiler already does

| Feature | Typst 0.15.1 | Reached through |
| --- | --- | --- |
| Hyphenation and line breaking per language | `text(lang:)`, keyed on the ISO 639 code | `TypstLanguageTag` |
| Base direction of a paragraph | `text(dir:)`, read from the paragraph's style chain | `TypstDocument.Direction` |
| Direction of a run inside a paragraph | `text(dir:)` on the run | `TypstContent.Direction` |
| Kinsoku line breaking for CJK | the UAX #14 line-break classes, no language needed | nothing to expose; measured in §5.1 |
| Justification | `par(justify: true)` | `TypstBodyStyle.Justify` |
| Space between a CJK and a Latin glyph | `text(cjk-latin-spacing:)`, `auto` by default | nothing to expose; measured in §5.3 |
| Bidirectional reordering | the Unicode bidi algorithm, after the collection pass | nothing to expose; measured in §6 |

## 3. The language

### 3.1 A tag beside `TypstLanguage`, not inside it

```csharp
var style = TypstDocumentStyle.Create()
    .Language(TypstLanguageTag.Japanese)
    .Build();
```

`TypstLanguage` is a closed table of patterns: `FormatNumber`, `FormatCurrency` and `FormatDate` read
a row per language, and a row that is missing throws. It answers "how does this language write
1 234,56", and it carries exactly the languages that question has an answer for.

A tag answers a different question — "how does this language break a line" — and Typst keys it on the
ISO 639 code, with a dictionary for some sixty languages and no dictionary at all for Chinese or
Japanese. Adding `Japanese` to `TypstLanguage` would have put a value in a table that has no date
pattern for it, and `TypstFormatting.FormatNumber(x, Japanese)` would have thrown at runtime.

**Decision.** A separate `TypstLanguageTag`, a value type over the tag string, with the well-known
tags as properties and `Of` for the rest. The two coexist: a style writes one `lang:` rule, and the
last call wins, so `Language(TypstLanguage.French).Language(TypstLanguageTag.Japanese)` writes `ja`
and nothing else. The rejected alternative — one enum for both jobs — would have needed a number
pattern for every language Typst can hyphenate.

The tag is validated as a BCP 47 tag: two or three ASCII letters, then subtags of one to eight ASCII
letters or digits separated by hyphens. `ja`, `zh-Hans`, `de-CH-1901` are read; `ja_JP`, `japanese`
and `ja-` are refused. Equality ignores case, because Typst does.

### 3.2 Where a tag is written

```typst
#set text(lang: "ja")            # the style's, or the document's
#text(lang: "ja")[東京]          # a run's
```

The document-level form is written *after* the style's rules, so an explicit
`document.Language(tag)` wins over a style that set a language of its own. A document that sets
neither writes no `lang` at all.

### 3.3 Measured: the tag reaches the hyphenator

Two sentences, one page of 8 × 6 cm with 1 cm margins, 10pt Libertinus Serif, justified, so the last
line breaks at the same word:

| Language | Lines | Evidence |
| --- | --- | --- |
| `en` | 3 | one line ends on `there`, the next begins `fore study.` — the word was hyphenated |
| `ja` | 3 | one line ends on `and`, the next begins `therefore study.` — the word moved whole |

Same paragraph, same measure: `lang` selects the dictionary, and Japanese has none.

### 3.4 Measured: Japanese line breaking does not depend on the tag

The three-line Japanese paragraph of §5.1 breaks at the same three places with and without
`lang: "ja"`, at the same right edges (367.01pt, 367.01pt, 332.52pt). That is not a defect: Japanese
has no hyphenation, and the kinsoku rules come from the Unicode line-break classes, not from a
per-language table. The tag still matters for a Japanese document — it is what makes `dir: auto`
left to right rather than a guess, what picks the smart quotes, and what the PDF declares — but it is
not what keeps `。` off the start of a line.

## 4. Direction

### 4.1 `dir: auto` is the language's direction

`TextElem::dir` is `auto` by default, and `auto` resolves through the language:

```rust
// typst-library-0.15.1/src/text/mod.rs
fn resolve(self, styles: StyleChain) -> Self::Output {
    match self.0 {
        Smart::Auto => styles.get(TextElem::lang).dir(),
        Smart::Custom(dir) => dir,
    }
}
```

`Lang::dir()` answers `RTL` for `ar`, `he`, `fa`, `ur`, `yi`, `ps`, `sd`, `ug`, `dv`, `ks` and `pa`,
and `LTR` for everything else. So there is **no first-strong-character detection**: an Arabic
paragraph in a document that declares no language is laid out with a left-to-right base direction.

Measured on `العدد 42 يساوي XLII حرفًا.`, Arial 10pt, A5 with 1.5 cm margins:

| Document | Leftmost glyph of the line | Right margin |
| --- | --- | --- |
| no language, no direction | 42.52pt (the left margin) | unused |
| `lang: "ar"` in the style | 283.47pt | 374.94pt (the right margin) |

The words come out in the same order either way — a whole Arabic sentence is one right-to-left island
inside a left-to-right paragraph, so it reads the same — but the paragraph is only turned around, and
aligned from the right, when the language says so. This is the reason the language tag is the primary
control and the direction a second one.

### 4.2 Three scopes

| Scope | Written as | Method |
| --- | --- | --- |
| The document | `#set text(dir: rtl)` | `TypstDocument.TextDirection`, which existed |
| A block, and the paragraphs in it | `#block[#set text(dir: rtl) …]` | `TypstDocument.Direction` |
| A run inside a paragraph | `#text(dir: rtl)[…]` | `TypstContent.Direction` |

The block form wraps in a `#block` rather than in a content block `#[…]`: `dir` is resolved from the
style chain of the paragraph, `block` has no `dir` argument in 0.15.1 to pass it down, and a content
block is spliced into the surrounding paragraph, where the rule would apply to a run and not to the
paragraph. A style that is applied *inside* a paragraph cannot change that paragraph's base
direction; only an enclosing block, or the document, can.

### 4.3 Measured: what the block form changes

`Le mot arabe العدد 42 signifie « le nombre ».`, Arial 10pt, French:

| Base direction | Page text, whitespace collapsed |
| --- | --- |
| left to right (a plain paragraph) | `Le mot arabe ددعلا42 signifie « le nombre ».` |
| right to left (`Direction(RightToLeft, …)`) | `Le mot arabe ددعلا 42 signifie « le nombre.»` |

The difference is the trailing neutrals: with a right-to-left base, the full stop after the closing
guillemet resolves into the right-to-left context and moves inside it. Nothing in the source changed,
and the Latin sentence keeps its order in both.

### 4.4 Measured: the inline form is an embedding, and a no-op when redundant

A run whose direction differs from its paragraph's is wrapped by the layout in an explicit
directional embedding before the bidi algorithm runs (`typst-layout-0.15.1/src/inline/collect.rs`):

```rust
if dir != config.dir {
    match dir { Dir::LTR => full.push_str(LTR_EMBEDDING), Dir::RTL => full.push_str(RTL_EMBEDDING), _ => {} }
}
… full.push_str(&elem.text); …
if dir != config.dir { full.push_str(POP_EMBEDDING); }
```

Measured on the French sentence above: wrapping `العدد 42` in `#text(dir: rtl)[…]` produces an SVG
byte-identical to the plain paragraph (21 403 bytes both), because the run is already right to left
and the embedding adds nothing. The embedding character does reach the text layer: `ExtractTextPages`
returns `العدد 42` followed by `U+202C` (§7).

So the inline form is for a run whose direction is *not* what the paragraph gives it — Latin inside
Arabic, or Arabic forced the other way — and it costs nothing when it is redundant.

### 4.5 Vertical is not a text direction

`TextDir`'s cast refuses a vertical value:

```rust
if v.is_custom_and(|dir| dir.axis() == Axis::Y) {
    bail!("text direction must be horizontal");
}
```

`TypstDocument.Direction` and `TypstContent.Direction` therefore refuse `TopToBottom` and
`BottomToTop` with `ArgumentOutOfRangeException` instead of writing a document the compiler rejects.
`TypstDirection` keeps its four members, and `TypstDocument.TextDirection` — which predates this
document and writes the same rule — still accepts all four; narrowing it would change an existing
public surface, and is left for a follow-up.

## 5. CJK: line breaking, justification and spacing

All measurements: A5 (419.53 × 595.28pt), 1.5 cm margins, so a measure of 334.49pt from a left margin
at 42.52pt; MS Gothic 10pt.

### 5.1 Kinsoku: no line begins with a closing mark

The paragraph of §3.4, justified, breaks into three lines that begin `東京は…`, `り、…`,
`訪れます。…`; the paragraph of full-width punctuation (§5.4) into four that begin `彼は「…`,
`した。…`, `価は、…`, `す。…`. Across both, no line begins with `。`, `、`, `，`, `．`, `！`, `？`,
`」`, `』` or `）`: the UAX #14 classes keep a closing mark on the line it closes, which is what the
kinsoku rules ask for, and the model has nothing to add.

### 5.2 Justification fills the measure

| Paragraph | Right edge of each line, in points |
| --- | --- |
| `par(justify: true)` | 367.01 / 367.01 / 332.52 |
| no justification | 362.52 / 362.52 / 352.52 |

A justified line ends on the measure less the advance of its last glyph — 42.52 + 334.49 − 10 = 366.99,
and 367.01 is where the last glyph's origin sits. The unjustified version stops short and breaks a
character earlier on each line.

### 5.3 CJK–Latin spacing is inserted by the layout

`彼はTypstを使って文書を作りました。` ends at 212.52pt with `cjk-latin-spacing: auto` (the default) and
at 207.52pt with `none`: 5pt over the two boundaries — `は|T` and `t|を` — that is 0.25em at 10pt,
per boundary. The characters are identical in both (`ExtractTextPages` returns the sentence as
written), so nothing is inserted into the text: the space is a layout adjustment, and a document that
wants the Latin run tight against the CJK glyphs turns it off with `Set("text(cjk-latin-spacing: none)")`.

### 5.4 Full-width punctuation is the caller's characters

`彼は「Typst 0.15」を使って、日本語の文書を作りました。…` compiles with no diagnostic and no tofu,
keeps a Latin number `0.15` inside the Japanese brackets without an inserted space, and round-trips
through the page text unchanged: `string.Concat(lines) == the paragraph`. The model escapes what
markup would read as markup and nothing else, so `，` `。` `、` `「` `」` reach the page as typed.

## 6. Bidi ordering, measured

### 6.1 Arabic with digits and a Latin word

`العدد 42 يساوي XLII حرفًا.`, Arial 10pt, `lang: "ar"`, A5. One visual line; the glyph runs, read
from the right edge:

| Run | Glyphs | x range (pt) | Reading |
| --- | --- | --- | --- |
| `العدد` | 5 | 362.18 – 374.94 | rightmost: the sentence starts here |
| `42` | 2 | 348.28, 353.84 | left of the word it follows, glyphs increasing |
| `يساوي` | 5 | 324.75 – 343.06 | |
| `XLII` | 4 | 304.19, 310.86, 316.42, 319.20 | left of `يساوي`, glyphs increasing |
| `حرفًا` | 5 | 283.47 – 296.11 | leftmost, with the full stop |

Which is what the algorithm gives: the paragraph runs right to left, each Arabic word is drawn with
its last letter on the left of the word, and the two left-to-right islands inside it — `42` and
`XLII` — keep their own order. The digits are not mirrored (`4` at 348.28, `2` at 353.84), the Latin
word is not mirrored, and neither is reversed with the Arabic around it.

One detail of the layout, not of the algorithm: the fatha of `حرفًا` is placed on a baseline of its
own (y = 48.48 against 49.68), which is why §7's warning about the text layer exists.

### 6.2 Hebrew

`שלום עולם 2026.`, Arial 10pt, `lang: "he"`: the full stop is the leftmost glyph at 308.03pt, `2026`
sits at 310.81 – 327.49pt with its glyphs increasing, and the Hebrew words at 335.83 – 370.06pt. A
neutral at the end of a right-to-left paragraph resolves to the paragraph's direction, so the period
lands at the left end of the line, not the right.

### 6.3 A French sentence citing Arabic

See §4.3: the Arabic words appear reversed in the text layer — `ددعلا` for `العدد` — because the text
layer is visual order, and the base direction changes where the trailing neutrals go.

## 7. The text layer is not a bidi oracle

`CompilationResult.ExtractTextPages` returns the layout's text in the order the frame tree yields it.
For right-to-left text that is **visual** order: an Arabic word comes out with its letters reversed,
run by run. It also carries the embedding characters of §4.4, and a combining mark can sit on a
baseline of its own, which the extractor reports as a line break — the Arabic paragraph of §6.1 comes
back as three "lines" that are one line on the page.

A document that needs to know what order the glyphs were drawn in reads the SVG export, where every
glyph has an absolute position: that is what the tests in §10 do. A caller that needs logical order
has the paragraph it wrote.

## 8. Fonts: what is embedded, and what the caller must supply

The compiler embeds four families:

| Family | Scripts |
| --- | --- |
| Libertinus Serif | Latin, Greek, Cyrillic |
| New Computer Modern | Latin, Greek, Cyrillic |
| New Computer Modern Math | mathematics |
| DejaVu Sans Mono | Latin, and not much else |

No CJK face, no Arabic face, no Hebrew face. A document in those scripts must name a family the
world can resolve — `TypstWorldBuilder.AddSystemFonts()`, `AddFontDirectory`, or `AddFont` with the
bytes — and the model does not choose one for it: a default family that draws tofu on one host and
text on another is worse than a document that says which face it wants.

Measured on the Windows 11 host this document was written on: `AddSystemFonts()` brings 84 families,
among them `MS Gothic`, `Yu Gothic`, `Yu Gothic UI`, `Microsoft YaHei`, `SimSun`, `NSimSun`, `Arial`,
`Tahoma`, `Segoe UI` and `Times New Roman`. There is no `Meiryo`, no `MS Mincho` and no `Noto` on it —
which is why the tests ask the font book instead of assuming, and skip with the reason when a script
has no face (§10).

Tofu is silent. A paragraph of Arabic compiled against the embedded set alone reports **no
diagnostic** and lays out the sentence — the glyphs are the face's missing-glyph box. Typst warns
about an unknown *family*, never about a missing glyph, so a test cannot conclude "it rendered" from
an empty diagnostic list. The tests check that the family exists before they compile; anything else
would report a rendering that never happened.

## 9. What the caller still owns

- **The font.** §8. The model names a family; it does not ship bytes.
- **The language of a block.** A document can be French and contain a Japanese quotation:
  `content.Language(TypstLanguageTag.Japanese, quote)`. The model writes the rule; deciding where it
  starts and ends is editorial.
- **The direction of a block in a mixed document.** §4.1: without a language or a direction the
  paragraph is left to right, whatever it contains.
- **Digits.** §1: Arabic-Indic digits are characters, not a setting.

## 10. Not covered, and why

| Case | State |
| --- | --- |
| First-strong-character detection of a paragraph | Not in Typst 0.15.1; the model does not invent it (§4.1). |
| Vertical runs (`ttb`, `btt` as text direction) | Refused by the compiler; refused by the new methods (§4.5). `TypstDocument.TextDirection` still accepts them. |
| A word that switches script mid-word | Bidi reorders it; no dictionary hyphenates it. |
| Arabic-Indic and other digit shapes | Not written by `TypstFormatting`; a numbering policy, out of scope. |
| Justification across a script boundary | Typst justifies the line; whether the inter-word spaces of an Arabic run inside a Japanese line look right is a typographic judgement the caller makes with `justify`. |
| Line breaking between two CJK scripts in one document | `lang` picks one language per run; the line-break classes are the same for `ja` and `zh`, so a document in either breaks the same way. |
| Automatic font fallback per script | Not in Typst: a family list is tried in order, and a face's coverage is not consulted. |

## 11. Tests

`tests/Text.Typst.Tests/CjkAndBidiTests.cs` — the markup, without the compiler: the style's and the
document's `lang` rule and their order, a run's `lang` and `dir`, the block's `#block[… #set
text(dir: …)]`, the tag's validation and equality, the refusal of a vertical direction, and the
characters themselves reaching the markup unescaped.

`tests/Text.Typst.Integration.Tests/CjkAndBidiIntegrationTests.cs` — the layout, on the real
compiler, reading glyph positions out of the SVG export:

| Test | What it measures |
| --- | --- |
| `A_japanese_paragraph_justifies_and_no_line_starts_with_closing_punctuation` | three lines, justified right edges 367.01 / 367.01, unjustified 362.52; no line starts with a closing mark |
| `Full_width_punctuation_survives_justification_without_an_inserted_space` | four lines, none starts with a closing mark, the text round-trips |
| `The_language_tag_selects_the_hyphenation_dictionary` | `en` splits `therefore`, `ja` does not |
| `Cjk_latin_spacing_inserts_a_quarter_em_at_each_boundary` | 5pt over two boundaries at 10pt; the text is unchanged |
| `An_arabic_paragraph_keeps_its_digits_and_its_latin_word_the_right_way_round` | `العدد` ends at the right margin, `42` and `XLII` increase, `XLII` left of the word it follows; the same paragraph without a language starts at the left margin |
| `A_hebrew_paragraph_runs_from_the_right_and_ends_its_period_on_the_left` | period 308.03 < digits 310.81–327.49 < words 335.83 |
| `A_right_to_left_block_resolves_the_trailing_neutrals_the_other_way` | `nombre ».` left to right, `nombre.»` right to left |

Every test that needs a script face checks the world's font families first and calls `Assert.SkipWhen`
with the list of faces it looked for: a host without one reports the reason, never a pass.
