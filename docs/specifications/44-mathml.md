# 44 — MathML

## 1. Scope

The model writes formulas in Typst syntax: `TypstDocument.Math("sum_(k=1)^n k")` and
`TypstContent.Math(…)` take a string the compiler parses. Content that comes from the web or from an
editor arrives as MathML instead, so it had no door: the caller had to rewrite the formula by hand, and
the HTML export had nothing to write but the Typst source inside a `<span class="math">`.

This document adds the door and the way back.

- `TypstMathMlExpression.Import(string)` — the entry point: a `<math>` element in, a tree out.
- `TypstMathMlExpression.ToTypst()` — Typst math markup, without the surrounding `$`.
- `TypstMathMlExpression.ToMathMl()` — the MathML the tree came from, written back.
- `TypstDocument.MathMl(string)` and `TypstDocument.MathMl(TypstMathMlExpression)` — a block equation.
- `TypstContent.MathMl(string)` and `TypstContent.MathMl(TypstMathMlExpression)` — inline math.
- `TypstMathMlException` — a refusal, classified by `TypstMathMlError`, naming the element it refused.

**In scope.** A subset of Presentation MathML that covers what a formula from a browser, a wiki or a
math editor actually uses; the Typst markup each element writes; the same tree written back as MathML
for the HTML export; and refusals that name what they refuse.

**Out of scope, deliberately.**

| Not done | Why |
| --- | --- |
| Content MathML (`<apply>`, `<ci>`, `<csymbol>`) | It describes the meaning, not the layout; Typst lays out content. Mapping it would mean implementing the semantics of every operator. |
| `munder`, `mover`, `munderover`, `mmultiscripts`, `mfenced`, `mphantom`, `menclose`, `semantics`, `annotation` | Each is a distinct rendering the model has no node for. They are refused by name, not approximated, because an approximation renders a formula that is not the one that was sent. |
| `mathvariant`, `displaystyle`, `scriptsizemultiplier`, `linethickness`, `columnalign`, all presentation attributes | Attributes are ignored. `mi` is upright only when it holds more than one character, `mo` takes its class from Typst's own table. The Typst markup has no per-run style channel that would carry them, and inventing one would need a node per attribute. |
| An accessibility tree, `alttext`, `annotation-xml` | The model's alt text belongs to figures. A formula's accessible name would have to reach both the PDF's tagged content and the HTML export, which is a different feature. |
| Rendering MathML itself | The PDF is laid out by Typst from the Typst markup; there is no MathML layout engine here. What Typst cannot draw from the markup it receives is not drawn, exactly as for a hand-written formula. |
| Normalising the tree | An `mrow` in the input is an `mrow` in the tree, and an `mi` holding `sin` stays one token. Normalising would make the export write something the caller did not send, and the round trip would no longer be comparable. |

## 2. The supported subset

| MathML | Meaning | Typst the node writes | HTML the export writes |
| --- | --- | --- | --- |
| `math` | The root | nothing of its own; its children are the formula | `<math xmlns="…" display="block\|inline">` |
| `mi` | An identifier | one character verbatim; more than one as `upright("sin")` | `<mi>sin</mi>` |
| `mn` | A number | its digits, verbatim | `<mn>2</mn>` |
| `mo` | An operator | the Typst name from §3, else the character verbatim | `<mo>+</mo>` |
| `mtext` | Literal text | a quoted string, `"if x"` | `<mtext>if x</mtext>` |
| `mrow` | A group | its children, separated by a space | `<mrow>…</mrow>` |
| `msup` | Base and superscript | `x^2`, or `attach(x + y, t: 2)` when a part is not one token | `<msup>…</msup>` |
| `msub` | Base and subscript | `x_1`, or `attach(x, b: 1)` | `<msub>…</msub>` |
| `msubsup` | Base, subscript, superscript | `x_1^2`, or `attach(x, b: 1, t: 2)` | `<msubsup>…</msubsup>` |
| `mfrac` | A fraction | `frac(1, 2)` | `<mfrac>…</mfrac>` |
| `msqrt` | A square root | `sqrt(x)` | `<msqrt>` |
| `mroot` | An n-th root | `root(x, 3)` — radicand first, index second, as Typst's own signature | `<mroot>…</mroot>` |
| `mtable` | A matrix | `mat(a, b; c, d)`, one `;` per row | `<mtable>…</mtable>` |
| `mtr` | A table row | the cells, comma-separated | `<mtr>…</mtr>` |
| `mtd` | A table cell | its content; an empty cell is `#[]` | `<mtd>…</mtd>` |

Anything else under `<math>` is refused as `Unsupported` with the element's name (§5).

### 2.1 The shapes each element requires

The import refuses an element whose children do not match its definition rather than guessing:

| Element | Children |
| --- | --- |
| `mi`, `mn`, `mo`, `mtext` | none: text only, trimmed, and not empty |
| `mrow` | one or more elements; text directly inside is refused |
| `msup`, `msub`, `mfrac`, `mroot` | exactly two |
| `msubsup` | exactly three: base, subscript, superscript |
| `msqrt` | exactly one |
| `mtable` | one or more `mtr`, all the same width |
| `mtr` | one or more `mtd` |
| `mtd` | zero or more elements; an empty cell is legal and becomes `#[]` |

A `mtable` whose rows differ in width is refused: Typst's `mat()` builds a rectangle, and a ragged
table would silently gain or lose a column.

### 2.2 A matrix cell holding a separator

`mat(a, b; c, d)` reads `,` as a cell separator and `;` as a row separator, so a cell whose markup
contains either — `f(x, y)`, say — is wrapped as a content block: `mat(#[$ f ( x , y ) $], 0)`. The
block is one argument again, and its contents are parsed as their own math expression.

## 3. Operators

The operators with a Typst spelling are written with it, so the markup reads the way a hand-written
formula does and Typst's own spacing rules apply:

| MathML | Typst | MathML | Typst |
| --- | --- | --- | --- |
| `+` `-` `=` `<` `>` | same | `≤` `≥` `≠` | `<=` `>=` `!=` |
| `×` | `times` | `·` `⋅` | `dot` |
| `÷` | `div` | `±` `∓` | `plus.minus` `minus.plus` |
| `≈` `≡` `∝` | `approx` `equiv` `prop` | `∞` | `infinity` |
| `∑` `∏` `∫` | `sum` `product` `integral` | `∂` `∇` | `diff` `nabla` |
| `→` `←` `↔` | `arrow.r` `arrow.l` `arrow.l.r` | `⇒` `⇐` | `arrow.r.double` `arrow.l.double` |
| `∈` `∉` | `in` `not in` | `⊂` `⊆` | `subset` `subset.eq` |
| `∪` `∩` | `union` `sect` | `∀` `∃` `…` | `forall` `exists` `...` |

An operator that is not in the table is written verbatim and Typst renders the glyph. The two
exceptions are the separator characters `,` and `;` inside a matrix cell, which are wrapped as §2.2
describes.

## 4. The Typst markup

Two rules decide the shape of what is written.

1. **A token is a token.** A single character, an identifier such as `times`, and a call such as
   `frac(1, 2)` are atomic: a script attaches to them with `^` or `_` directly, as in `x^2` and
   `frac(1, 2)^2`. Text is escaped with a backslash where it would otherwise end the run or open code
   (`#`, `$`, `"`, `@`, `` ` ``, `\`).
2. **A group is not.** A base or a script that is more than one token goes through `attach()`, not
   through parentheses: `attach(x + y, t: 2)`. Parentheses would print, and the MathML had none —
   `<msup><mrow>…</mrow><mn>2</mn></msup>` renders no brackets, so the markup must not add any.

`display="block"` on the `<math>` element is kept: `TypstDocument.MathMl` writes `$ … $` with the
spaces this model uses for display math, and `TypstContent.MathMl` writes `$…$` inline regardless,
because an inline run is inline wherever the source said it was block.

## 5. The XML policy

MathML is XML written by a third party, so it is read under the same policy the suite applies to every
XML part it parses ([22 §10](22-docx.md#10-the-xml-policy)): no document type declaration, no entity,
an explicit character cap, an explicit nesting cap and an explicit element count.

The `M3scalinum.Text.Documents` project has no dependency at all, and cannot borrow `PdfLotl`'s reader,
so `TypstMathMlReader` repeats the settings `PdfLotl` uses. They are one object, created in one place:

| Rule | Bound | Constant | Refused as |
| --- | --- | --- | --- |
| `<!DOCTYPE` or `<!ENTITY` present | none permitted | — | `XmlPolicy` |
| `XmlReaderSettings.DtdProcessing` | `Prohibit` | — | `XmlPolicy` (by `Guard`) |
| `XmlReaderSettings.XmlResolver` | `null` | — | — |
| One formula | 1 MiB of characters | `MaxCharacters` | `Limit` |
| Element nesting | 64 levels | `MaxDepth` | `Limit` |
| Elements in one formula | 10 000 | `MaxElements` | `Limit` |

The declaration is refused by scanning the string, before any parser sees it, which is why the answer
is `XmlPolicy` and not a parser message; `DtdProcessing.Prohibit` is the backstop should the scan ever
be bypassed. A formula that is not well-formed XML, or that has no root, is `Malformed`.

A refusal is typed and names what it refused:

| `TypstMathMlError` | Raised when |
| --- | --- |
| `Unsupported` | The root is not `math`, an element is outside §2, or an element carries a foreign namespace. `ElementName` names it. |
| `Malformed` | The XML is not well formed, a token element is empty or holds elements, text sits outside a token element, the root is empty, or an element's children do not match §2.1. `ElementName` names it when one element is at fault. |
| `XmlPolicy` | A document type declaration or an entity declaration is present. |
| `Limit` | The formula passes the character, depth or element cap. |

`Import(null)` and `Import("  ")` are `ArgumentNullException` and `ArgumentException`: a missing
argument is not a MathML refusal.

## 6. The HTML export

`ToHtml()` writes the tree, not the Typst markup: a formula imported from MathML reaches the fragment
as a `<math>` element in the MathML namespace, with the elements it was read from. The export adds
nothing and removes nothing, so importing the exported MathML gives back the tree it came from — which
is what the round-trip test compares.

| Where the formula sits | What `ToHtml()` writes |
| --- | --- |
| A block equation | `<div class="math"><math xmlns="http://www.w3.org/1998/Math/MathML" display="block">…</math></div>` |
| An inline run | `<math xmlns="http://www.w3.org/1998/Math/MathML" display="inline">…</math>`, in place |
| A formula written in Typst syntax | unchanged: `<div class="math">…</div>` or `<span class="math">…</span>`, with the Typst source escaped |

A `<math>` element is flow content and could sit in the body unwrapped; the `<div class="math">`
wrapper is kept so a block equation looks the same in the output whether it was imported or written in
Typst, and a stylesheet has one class to target.

### 6.1 Usage

```csharp
const string Formula = """
    <math display="block">
      <mrow><mfrac><mn>1</mn><mn>2</mn></mfrac><mo>+</mo><msup><mi>x</mi><mn>2</mn></msup></mrow>
    </math>
    """;

var equation = TypstMathMlExpression.Import(Formula);
var document = TypstDocument.Create().MathMl(equation);

var markup = document.ToMarkup();      // $ frac(1, 2) + x^2 $
var html = document.ToHtml().Html;     // <div class="math"><math …>…</math></div>
var mathml = equation.ToMathMl();      // the tree, written back as MathML
var typst = equation.ToTypst();        // frac(1, 2) + x^2, without the $
```

`Import` is called once per formula: the node keeps both the Typst markup and the tree, so the two
cannot drift apart, and a document that is laid out and exported writes the same formula both ways.

## 7. Tests

`tests/Text.Typst.Tests/MathMlTests.cs` pins the import, the markup and the refusals; every test runs
without the native library.

| Test | What it pins |
| --- | --- |
| `a_simple_formula_imports_to_the_expected_typst_markup` | `x^2 + y^2 = z^2`, and `display="block"` kept |
| `a_fraction_imports_to_frac`, `a_square_root_imports_to_sqrt`, `an_nth_root_imports_to_root_with_its_index` | The three radical and fraction shapes |
| `a_matrix_imports_to_mat` | Rows joined by `;`, cells by `,` |
| `a_matrix_cell_holding_a_comma_is_wrapped_so_mat_keeps_it_whole` | §2.2 |
| `a_grouped_base_with_a_script_uses_attach` | `attach(x + y, t: 2)`, not `(x + y)^2` |
| `a_round_trip_through_the_model_preserves_the_tree` | Import → export → import, compared node by node |
| `the_html_export_writes_the_formula_as_mathml` | The block fragment, byte for byte |
| `an_unknown_element_is_refused_by_name`, `an_unknown_element_deep_in_the_tree_is_refused_by_name` | `Unsupported`, with `ElementName` |
| `a_document_type_declaration_is_refused`, `an_entity_declaration_is_refused` | `XmlPolicy` |
| `a_formula_over_the_character_cap_is_refused`, `a_formula_nested_past_the_depth_cap_is_refused` | `Limit` |
| `an_element_with_the_wrong_number_of_children_is_refused`, `a_table_whose_rows_differ_in_width_is_refused`, `text_outside_a_token_element_is_refused` | §2.1 |

`tests/Text.Typst.Integration.Tests/MathMlIntegrationTests.cs` compiles the imported formulas with the
real compiler: the markup has to be Typst the compiler accepts — `attach()` and `mat()` included — and
the page text has to carry the symbols of the formula. The extraction of a math formula returns the
mathematical italic block (`𝑥` is `U+1D465`), because that is the glyph the layout really drew, so the
tests assert those code points rather than ASCII letters.

## 8. What the export does not do

- **No Content MathML.** The tree is Presentation MathML; `<semantics>` and `<annotation>` are outside
  the subset and refused on import, so nothing is ever written back as a semantic layer.
- **No native MathML rendering.** The PDF is Typst's layout of the Typst markup. A construct Typst
  cannot draw — a glyph its math font lacks, a construct the markup cannot express — is missing from
  the PDF exactly as it would be for a hand-written formula. The HTML keeps the MathML, and the
  browser renders it.
- **No attribute fidelity.** Attributes are dropped on import, so `mathvariant="bold"` or
  `columnalign="right"` is lost and does not reappear in the export. `display` is the one exception,
  because it selects the block or inline track of the model.
- **No `<math>` through the HTML importer.** `TypstHtmlImporter.Import` still drops a `<math>`
  subtree with the other elements whose text is not prose, as it did before this document. A caller
  that holds the formula as XML imports it with `TypstMathMlExpression.Import` and adds it to the
  content; teaching the HTML importer to hand a `<math>` element to this reader is a change to its
  element policy, not to this one.
- **No `mtext` spacing fidelity.** `mtext` is trimmed and written as a string; runs of spaces inside it
  collapse in Typst's layout as they do in prose.
