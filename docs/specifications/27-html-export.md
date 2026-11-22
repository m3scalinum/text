# 27 — HTML export

The document model can be written as HTML as well as Typst source. `TypstDocument.ToHtml()` turns
the same object graph into a semantic HTML5 body fragment, and reports the nodes HTML cannot
express instead of approximating them.

## 1. Why the model, not the compiler

Spec 06 derives Markdown and plain text from the HTML the compiler produces for a content tree
(`typst_html::html_document`). That path needs a compilation, which needs the native library, and it
starts from a laid-out artefact.

HTML export is the mirror image of import: it walks the model itself. That choice buys three
things and costs one:

- **No compiler.** `ToHtml()` is pure managed code, in `M3scalinum.Text.Documents`, next to the
  importer. A service that only reshapes documents does not load the native library.
- **Semantics before layout.** The model already knows a heading's level, a table's header row and
  a cell's alignment. Nothing has to be recovered from geometry.
- **Determinism.** The same document produces the same fragment byte for byte, with `\n` line
  endings on every platform. Round-trip tests and golden files depend on it.
- **The cost:** the output describes the model, not what Typst would render. A page setup, a show
  rule, a custom font and a `#place` call have no HTML equivalent, so they are reported, not
  emulated (section 6).

## 2. Contract

```csharp
using M3scalinum.Text.Documents;

var document = TypstDocument.Create()
    .Heading(1, "Revenue")
    .Paragraph(c => c.Text("Growth was ").Bold("12%").Text("."));

TypstHtmlExportResult result = document.ToHtml();

string html = result.Html;
int losses = result.UnrepresentableCount;
int raw = result.CountOf("raw");
```

- `Html` is a **body fragment**: no `html`, `head` or `body` element, no doctype. The caller owns
  the page around it. Metadata, page setup and the Typst preamble do not reach the fragment: they
  describe a page, not content.
- `Unrepresentable` lists the nodes that were not written, sorted by name, with their number of
  occurrences. Each entry is a `TypstHtmlDroppedElement` — the type the importer already uses for
  the elements it drops — so a caller reports losses from either direction with one shape.
- `UnrepresentableCount` is the sum of those counts; `CountOf(name)` is the count for one name, or
  zero when it does not appear.
- A node that cannot be represented is **counted once and its subtree is not visited**. Nothing of
  it reaches the HTML, so nothing inside it can reach it either.
- The document is not consumed and may be exported repeatedly; the result is identical each time.

## 3. Block mapping

| Model | HTML |
| --- | --- |
| `Heading(level, …)` | `h1` … `h6` of that level |
| `Paragraph(…)` | `p` |
| `Add(content)` | `p` around the inline run |
| `BulletList` / `NumberedList` | `ul` / `ol`, each item an `li`; nested lists nest |
| `Table(…)` with `HeaderRow` | `table`, `thead`, `tr`, `th` |
| `Table(…)` with `Row` | `tbody`, `tr`, `td` |
| cell `Align` | `align="left"`, `align="center"` or `align="right"` |
| cell `colspan` / `rowspan` | `colspan` / `rowspan` attributes |
| `Code(language, code)` | `pre` > `code`, with `class="language-…"` when a language is set |
| `Quote(content)` | `blockquote`; a block-level quote wraps its inline runs in `p` |
| `Quote(content, block: false)` | `q` |
| `TermList(…)` | `dl`, `dt`, `dd` |
| `Figure(…)` | `figure`; the label becomes `id` |
| figure image | `img src="…" alt=""` |
| figure caption | `figcaption` |
| `Divider()` | `hr` |
| `Math(expression)` | `div class="math"` with the linear expression |

## 4. Inline mapping

| Model | HTML |
| --- | --- |
| `Text` | escaped text |
| `Bold` | `strong` |
| `Emphasis` | `em` |
| `Code` | `code` |
| `Link(url, text)` | `a href="…"`; the URL is the text when none is given |
| `LineBreak` | `br` |
| `Label` | `span id="…"` |
| `Reference` | `a href="#…"` with `@label` as its text |
| `Footnote` | `span class="footnote"` |
| `Cite` | `cite data-key="…"` with `@key` as its text |
| `Styled` | `span` with the text; the Typst text style itself has no HTML equivalent |
| `Underline`, `Strikethrough` | `u`, `s` |
| `Subscript`, `Superscript` | `sub`, `sup` |
| `Upper`, `Lower`, `SmallCaps`, `Highlight` | `span class="upper"`, `"lower"`, `"smallcaps"`, `"highlight"` |
| `Math` (inline) | `span class="math"` |

A paragraph whose only node is display math is written as the `div`, not as a `p` around it.

## 5. Escaping

Text escapes `&`, `<` and `>`. Attribute values escape those three plus `"`. Nothing else is
escaped: the output is HTML, not Typst, so `*`, `_` and `#` are ordinary characters and stay as
they are.

Line endings in text and code are normalised to `\n`; `\r\n` and a lone `\r` both become `\n`.

## 6. What HTML cannot hold

Everything below is counted in `Unrepresentable` and leaves no element:

| Node | Name in the result |
| --- | --- |
| `Raw(markup)` | `raw` |
| `PageBreak()`, `ColumnBreak()` | `pagebreak`, `colbreak` |
| `Divider()` | written as `hr`; not counted |
| `ParagraphBreak()` | written as nothing; a block boundary already separates paragraphs |
| `Align(…)`, `Box(…)`, `Block(…)`, `Place(…)`, `Hide(…)` | the callee: `align`, `box`, `block`, `place`, `hide` |
| `Grid`, `Stack`, `Pad`, `Columns`, `Repeat`, `Rotate`, `Scale`, `Move`, `Skew` | the callee |
| `HSpace`, `VSpace` | `h`, `v` |
| `Context`, `Include`, `Asset`, `Expression`, `Let`, `Import`, `Function`, `Set`, `Show` | `context`, `include`, `asset`, `expression` — the preamble is not part of the fragment at all |
| `Outline()`, `Bibliography(…)`, `Lorem(…)` | `outline`, `bibliography`, `lorem` |
| `MetadataEntry(…)` | `metadata` |
| an unknown node type | its type name, lower-cased, without the `Node` suffix |

The rule is one sentence: **content is written, code and layout are counted.** A caller that wants
to know whether an export lost anything reads `UnrepresentableCount` once; a caller that wants the
detail reads `Unrepresentable`.

## 7. Images and the asset resolver

An image is a path in the model, never bytes. The HTML importer resolves every `src` through
`TypstHtmlImportOptions.AssetResolver` and stores the **resolved name** — the asset path the world
builder knows — so the exported `img` carries that name:

```csharp
var options = new TypstHtmlImportOptions
{
    AssetResolver = source => source == "logo.png" ? "assets/logo.png" : null,
};

var exported = TypstDocument.FromHtml("<img src=\"logo.png\">", options).ToHtml();
// <figure>
// <img src="assets/logo.png" alt="" />
// </figure>
```

Export needs no resolver: it writes what the model holds. A `data:` URL stored by the caller is
written unchanged, so a self-contained image survives the round trip without a file. A figure has
no alternative text in the model, so `alt` is always the empty string, which HTML defines as
decorative.

## 8. Round trip

`TypstDocument.FromHtml(html).ToHtml()` returns the same fragment for the constructs both sides
implement. The tests in `HtmlExportTests` pin one fixture per construct and check two properties:
the first export equals the fixture, and importing the export and exporting again is a fixed point.

| Construct | Round-trips |
| --- | --- |
| headings, paragraphs, inline text | yes, exactly |
| `strong`, `em`, inline `code` | yes |
| `ul`, `ol`, nested lists | yes |
| tables without alignment | yes |
| code blocks with `class="language-…"` | yes |
| links, including `&amp;` in the URL | yes |
| `blockquote` whose content is one paragraph | yes |
| figures (image path, resolved by the resolver on the way in) | yes |
| term lists (`dl`) | yes |
| cell `align`, `colspan`, `rowspan` | written, but the importer reads only the spans: alignment is lost on the way back |
| a figure caption | the caption becomes a paragraph, because the importer treats `figure` and `figcaption` as transparent |
| footnotes, citations, math, styled text | text survives, the construct does not |
| anything in section 6 | not at all: counted on export |

HTML export is a delivery format, like Markdown. The model is the lossless form; the round trip is
a convenience for pipelines that already speak HTML, not a storage format.

## 9. Fidelity contract

| Property | Guarantee |
| --- | --- |
| Text content | Preserved, in document order, including table cells and captions |
| Block structure | Preserved for headings, paragraphs, lists, tables, code, quotes, figures |
| Inline emphasis | Preserved for strong, emphasis, code, links, underline, strike, sub and sup |
| Attribute values | Escaped; never truncated |
| Page furniture | Absent by construction: the model has none |
| Layout | Absent: no positions, widths, colours or fonts |
| Losses | Reported in `Unrepresentable`, never guessed |
| Determinism | Byte-for-byte stable, `\n` line endings |
