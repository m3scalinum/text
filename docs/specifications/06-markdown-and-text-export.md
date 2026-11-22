# 06 — Markdown and plain-text export

Typst has no Markdown exporter. This document specifies the one M3scalinum.Text.Typst provides, and — more
importantly — what it guarantees and what it does not.

## 1. Why not walk the paged document

The obvious implementation walks the laid-out pages and reconstructs text from glyph runs. It is
rejected:

- Layout has already destroyed semantics. A heading is a bold run at a larger size; a list item is
  a bullet glyph plus indented text; a table is a set of lines. Recovering structure from geometry
  is guesswork.
- Page furniture (headers, footers, page numbers, running titles) is interleaved with content and
  cannot be distinguished reliably.
- Hyphenation and justification insert breaks that do not exist in the source.

## 2. The chosen path

Markdown and text are derived from the **HTML document**, which Typst produces from the content
tree before layout:

```
source ──► typst::compile::<HtmlDocument> ──► typst_html::html_document()
                                              │
                                              ▼
                                    HtmlNode tree (semantic)
                                              │
                        ┌─────────────────────┴─────────────────────┐
                        ▼                                           ▼
                 Markdown writer                              Text writer
```

`HtmlNode` is a tree of `Text`, `Element` (tag, attributes, children, styles) and `Frame`
(a layouted island embedded as SVG). Tags carry the semantics: `h1`–`h6`, `p`, `ul`, `ol`, `li`,
`table`, `thead`, `tbody`, `tr`, `th`, `td`, `pre`, `code`, `blockquote`, `a`, `strong`, `em`,
`img`, `math` (MathML), `figure`, `figcaption`, `hr`, `br`.

Consequence: **Markdown and text export require a compilation with `DocumentKind.Html`.** Asking
for them on a paged document throws `InvalidOperationException` with that instruction in the
message.

## 3. Markdown mapping

| HTML | Markdown |
| --- | --- |
| `h1`…`h6` | `#` × level + space + inline content |
| `p` | inline content + blank line |
| `strong` | `**…**` |
| `em` | `*…*` |
| `code` (inline) | `` `…` `` with a fence longer than the longest backtick run |
| `pre > code` | fenced block, language from `data-lang` (Typst's own attribute), falling back to `class="language-…"` |
| `ul` / `ol` | `- ` / `1. `, nested with 2 spaces per level |
| `blockquote` | `> ` prefix on every line |
| `table` | GitHub pipe table; `th` row becomes the header, `align` attributes become `:---:` |
| `a[href]` | `[text](href)`; bare URL when text equals href |
| `img[src]` | `![alt](src)`; `src` is a data URL when the image was registered as bytes |
| `hr` | `---` |
| `br` | two trailing spaces + newline |
| `figure` / `figcaption` | image, then `*caption*` on its own line |
| `math` (MathML) | `$…$` reconstructed from the MathML tree; `$$…$$` when display |
| `Frame` (layouted island) | `<!-- typst frame: N pages -->` comment, content dropped |

### 3.1 Math reconstruction

MathML is walked back to Typst math syntax for the constructs that round-trip cleanly: identifiers,
numbers, operators, fractions (`mfrac` → `a / b`), sub/superscripts, roots, sums and integrals with
limits, matrices, and delimiters. Anything else is emitted as its linear text form inside `$…$`,
which is valid Typst but may not re-render identically.

This is a **best-effort** mapping and is documented as such: Markdown is a delivery format, not a
lossless intermediate.

### 3.2 Escaping

Markdown special characters in text nodes are escaped: `\`, `` ` ``, `*`, `_`, `[`, `]`, `<`, `>`,
`#` at line start, `-`/`+`/`1.` at line start, `|` inside tables. Escaping is applied to text
nodes only, never to generated syntax.

### 3.3 Two facts about Typst's HTML output

Both were found by running the exporter against the real compiler, and both are properties of
Typst, not of this library:

- **Headings are shifted by one level.** Typst reserves `h1` for the document title, so a `= Title`
  in the source becomes `<h2>`. Markdown therefore starts at `##`. A document that wants `#` must
  set `#set document(title: …)` and use `==` for its first section.
- **The code language lives in `data-lang`, not in a class.** A fenced block becomes
  `<pre><code data-lang="rust">`, with syntax highlighting expressed as inline `style` attributes
  on `<span>` children. The exporter reads `data-lang` first and falls back to
  `class="language-…"` for hand-written HTML; the spans contribute their text only.

## 4. Plain-text mapping

| HTML | Text |
| --- | --- |
| `h1`…`h6` | content, then a line of `=` (h1) or `-` (h2), then a blank line |
| `p` | content, wrapped at 100 columns, blank line after |
| `ul` / `ol` | `- ` / `1. `, nested with 2 spaces |
| `table` | cells joined with ` | `, one row per line, header underlined with `-` |
| `pre` | content verbatim, indented 4 spaces |
| `blockquote` | `> ` prefix |
| `a` | `text (href)` |
| `img` | `[image: alt]` |
| `math` | linear form, no delimiters |
| `hr` | a line of `─` × 40 |
| `Frame` | omitted |

Wrapping is done on word boundaries with a greedy algorithm; it never breaks inside a word longer
than the limit. Line endings are `\n` on every platform.

## 5. Fidelity contract

| Property | Guarantee |
| --- | --- |
| Text content | Preserved, in document order, including text inside tables and captions |
| Block structure | Preserved for headings, paragraphs, lists, tables, code, quotes, figures |
| Inline emphasis | Preserved for strong/emphasis/code/link |
| Page furniture | Absent (headers, footers, page numbers, running titles do not exist in HTML export) |
| Floats and placement | Absent (no pagination) |
| Layouted islands | Dropped, with a comment in Markdown and nothing in text |
| Exact whitespace | Not preserved (wrapping, collapsing) |
| Math | Best effort; simple formulas round-trip, complex ones degrade to linear text |
| Round-trip to Typst | Not guaranteed |

The contract is enforced by golden-file tests over a fixture corpus that covers every row of the
mapping tables.

## 6. API

```csharp
using var result = compiler.Compile(world, new TypstCompilationOptions
{
    DocumentKind = TypstDocumentKind.Html,
});

string markdown = result.ExportMarkdown();
string text     = result.ExportText();
```

Both are also reachable through `Export(ExportFormat.Markdown)` / `Export(ExportFormat.Text)`,
which return `byte[]` (UTF-8, no BOM).

## 7. Where the code lives

The walker is implemented in Rust (`export/markdown.rs`, `export/text.rs`) over `HtmlNode`, not in
C#. Reasons: the tree is a Rust type, crossing it to managed code would mean a second serialization
of the whole document, and the mapping is a pure function of the tree with no .NET dependency.

The managed side only receives the final UTF-8 bytes.

## 8. Writing Markdown from the document model

Everything above walks the **HTML document**, which exists only after a compilation with
`DocumentKind.Html`. That is the right path for Typst source, but it is the wrong path for a
document built in C#: `TypstDocument` already holds the nodes, so paying for layout to recover them
would be absurd. `TypstDocument.ToMarkdown()` therefore walks the model directly.

The mapping is the same one **§3** gives, applied to nodes instead of HTML tags, with the escaping of
**§3.2** applied to text nodes only:

| Model node | Markdown |
| --- | --- |
| `HeadingNode` (level 1…6) | `#` × level + space + inline content |
| `ParagraphNode`, `ContentNode` | inline content |
| `BoldNode` | `**…**` |
| `EmphasisNode` | `*…*` |
| `InlineCodeNode` | `` `…` `` with a fence longer than the longest backtick run |
| `CodeBlockNode` | fenced block, language from the node |
| `ListNode` (ordered or not) | `- ` / `1. `, nested with 2 spaces per level, blank line between loose items |
| `QuoteNode` | `> ` prefix on every line |
| `TableNode` | GitHub pipe table; the header row becomes the header, `TypstTableCell.Align` becomes `:---:` |
| `LinkNode` | `[text](href)`; bare URL when the text is the destination |
| `FigureNode` | `![caption](image)` |
| `MathNode` | `$…$`, `$$…$$` when display |
| `LineBreakNode` | two trailing spaces + newline |
| `#divider()` | `---` |

Three facts about the model, as opposed to the HTML tree:

- **Headings are not shifted.** The `h1` → `h2` shift of §3.3 is a property of Typst's HTML output,
  not of the model: a `HeadingNode` of level 1 is `#`, and reading it back gives the same level.
- **A figure has no alt text of its own.** Its caption is the only text it carries, so it becomes
  the alt text: `![caption](image)`. Markup inside the caption is not written — alt text is literal —
  which is what makes the round trip of an image stable.
- **Math is written as the model spells it.** The node holds Typst math syntax, not MathML, so
  `$…$` re-renders exactly; there is no §3.1 reconstruction to do.

### 8.1 What the model can express and Markdown cannot

A construct Markdown cannot spell is **dropped and counted**, never rendered as broken syntax and
never thrown. The count is keyed by construct, in `TypstMarkdownExportResult.Unrepresentable`:

| Construct | Source |
| --- | --- |
| `raw` | `RawNode` — verbatim Typst, and every shape (`#rect`, `#line`, `#circle`, …) |
| `call` | `CallNode`, `SimpleCallNode`, `BareCallNode` — align, block, box, grid, place, columns, spacing, page and column breaks, assets, includes |
| the decoration's own name (`strike`, `underline`, `highlight`, `smallcaps`, `sub`, `super`, `upper`, `lower`) | `WrapperNode` — the construct is lost, its text is kept |
| `outline` | `#outline()` |
| `footnote` | `FootnoteNode` |
| `cite` | `CiteNode` |
| `metadata` | `MetadataNode` |
| `label`, `reference` | `LabelNode`, `ReferenceNode` |
| `lorem` | `LoremNode` |
| `bibliography` | `BibliographyNode` |
| `term-list` | a term list, which GFM has no syntax for |
| `table-span` | a cell with `colspan` or `rowspan`, flattened into plain cells |
| `preamble` | each `#let`, `#import`, `#set`, `#show` line |
| `page-setup` | `#set page(…)` |
| `document-metadata` | `#set document(…)` |

### 8.2 Fidelity contract of the model writer

| Property | Guarantee |
| --- | --- |
| Text content | Preserved in document order, including the text of a construct that is dropped (except a footnote, a citation and a raw block, whose text is Typst source or a key) |
| Block structure | Preserved for headings, paragraphs, lists, tables, code, quotes, figures |
| Inline emphasis | Preserved for strong, emphasis, inline code and links |
| Heading levels | Preserved exactly; no shift |
| Table alignment | Preserved through the GFM delimiter row; spans are flattened |
| Escaping | Every special character of §3.2 escaped in text nodes, and only there |
| Exact whitespace | Not preserved: blocks are separated by one blank line, output ends with one newline |
| Math | Preserved as written; the model stores Typst math syntax |
| Round trip | `FromMarkdown(ToMarkdown())` is stable on canonical Markdown, and the export is idempotent: exporting its own output returns it unchanged |

"Canonical" means what the writer itself emits: `1. ` for every ordered item, a blank line between
blocks, no escape that is not needed. A document outside that form is normalised by the first pass;
the second pass is a fixed point.

### 8.3 API

```csharp
var result = document.ToMarkdown();

Console.WriteLine(result.Markdown);

foreach (var construct in result.Unrepresentable)
{
    // "call: 1", "outline: 1", "raw: 3", …
}
```

`TypstMarkdownExportResult` mirrors `TypstMarkdownImportResult`: `Markdown`, `Unrepresentable`,
`UnrepresentableCount` and `CountOf(construct)`. A round trip therefore reports both ends of the
loss — what the import could not hold, and what the export could not write.

### 8.4 Where the code lives

In C#, unlike §7: `Markdown/TypstMarkdownWriter.cs` walks the nodes, `Markdown/TypstDocument.Markdown.cs`
carries `ToMarkdown`. The model is write-only — its nodes keep their state in private fields so
nothing outside the builders can mutate them — so the walker reads those fields through a table it
builds once per node type, rather than adding a reader to every node.
