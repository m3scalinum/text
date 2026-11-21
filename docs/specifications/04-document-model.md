# 04 — Document model

A fluent C# API that emits Typst source. It exists so that application code can build a document
without string concatenation and without knowing Typst's escaping rules.

## 1. Contract

- The model is **write-only for Typst**: `TypstDocument.ToMarkup()` returns a `string` of Typst
  source, and no node ever parses Typst back. The only input it accepts is HTML, through
  `TypstDocument.FromHtml` (§12): a one-way import, not a round trip.
- The emitted source is **self-contained**: it declares its own page setup and metadata and
  imports nothing.
- The emitted source is **stable**: the same object graph always produces the same string, byte for
  byte. Golden-file tests depend on this.
- The model never validates Typst semantics (a font name, a colour, a label). It validates only
  what it can know: null arguments, ranges, and structural rules.

## 2. Shape

```csharp
using M3scalinum.Text.SharpTypst.Documents;

var doc = TypstDocument.Create()
    .Metadata(m => m
        .Title("Q3 2026 revenue report")
        .Author("Finance")
        .Keywords("q3", "revenue")
        .Language("en"))
    .Page(p => p
        .Paper("a4")
        .Margin(TypstLength.Cm(2))
        .Numbering("1 / 1"))
    .Heading(1, "Revenue")
    .Paragraph(c => c
        .Text("Revenue grew by ")
        .Bold("12%")
        .Text(" year over year, driven by ")
        .Emphasis("subscriptions")
        .Text("."))
    .Table(t => t
        .Columns(TypstLength.Fr(2), TypstLength.Fr(1))
        .HeaderRow("Item", "Amount")
        .Row("Hosting", "1 200 EUR")
        .Row("Support", "800 EUR"))
    .Figure(f => f
        .Image("charts/revenue.png", TypstLength.Percent(80))
        .WithCaption("Revenue by month")
        .WithLabel("fig:revenue"))
    .Paragraph(c => c.Text("See ").Reference("fig:revenue").Text(" for the monthly split."));

string markup = doc.ToMarkup();
```

`TypstDocument` is mutable and not thread-safe. `ToMarkup()` may be called repeatedly; it does not
consume the document.

## 3. Nodes

The left column names the node. The public entry point is the matching method on `TypstDocument`
(block nodes) or `TypstContent` (inline nodes); where the two names differ, the notes give the
public one.

| Node | Emitted Typst | Notes |
| --- | --- | --- |
| `Heading(level, content)` | `=` × level + space + content | `level` 1–6; outside that range throws `ArgumentOutOfRangeException` |
| `Paragraph(content)` | content + blank line | Empty content emits nothing |
| `BulletList(items)` | `- item` per line | Nesting via `BulletList` inside an item, indented 2 spaces per level |
| `NumberedList(items)` | `+ item` per line | Same nesting rule |
| `TermList(terms)` | `/ term: description` | |
| `Table` | `#table(columns: (…), [..], …)` | Cells are content blocks; the header row is `table.header(…)`; `TypstTableCell` adds `colspan`/`rowspan` |
| `Figure` | `#figure(image(…), caption: [..]) <label>` | Caption and label are optional |
| `Image(path, width?, height?, fit?)` | `#image("path", width: …)` | `path` is a virtual asset path; the method lives on `TypstFigure` |
| `CodeBlock(language, code)` | fenced block with language tag | Public: `TypstDocument.Code(language, code)`. Code is emitted verbatim, never escaped |
| `InlineCode(code)` | `` `code` `` | Public: `TypstContent.Code(code)`. Backticks inside are handled by using a longer fence |
| `Math(expression, block)` | `$ expression $` | `block: true` adds surrounding spaces and display mode |
| `Quote(content, block)` | `#quote(block: true)[…]` | |
| `Link(url, content?)` | `#link("url")[…]` | Without content, the URL is the text |
| `Reference(label)` | `@label` | |
| `Label(name)` | `<name>` | Attached to the preceding element |
| `PageBreak()` | `#pagebreak()` | |
| `LineBreak()` | `\` + newline | |
| `Divider()` | `#divider()` | Thematic break, styleable by templates |
| `Columns(count, content)` | `#columns(count)[…]` | |
| `Align(alignment, content)` | `#align(center)[…]` | |
| `Box(content)` | `#box[…]` | |
| `Text(content, style)` | `#text(weight: "bold")[…]` | `TypstContent.Styled(text, TypstTextStyle)`; style: font, size, weight, slant, fill, tracking, leading |
| `Footnote(content)` | `#footnote[…]` | |
| `Cite(key, form?)` | `@key` or `#cite(<key>, form: "prose")` | `key` is a bibliography key |
| `Bibliography(path, style?)` | `#bibliography("refs.bib", style: "ieee")` | `path` is a virtual asset path |
| `Outline(title?, depth?)` | `#outline(title: […], depth: 2)` | |
| `TermList(terms)` | `/ term: description` | |
| `Highlight(content)` | `#highlight[…]` | |
| `Subscript(content)` / `Superscript(content)` | `#sub[…]` / `#super[…]` | |
| `Overline(content)` / `Strikethrough(content)` | `#overline[…]` / `#strike[…]` | |
| `SmallCaps(content)` | `#smallcaps[…]` | |
| `Upper(content)` / `Lower(content)` | `#upper[…]` / `#lower[…]` | |
| `Lorem(words?)` | `#lorem(40)` | |
| `ParagraphBreak()` | `#parbreak()` | |
| `ColumnBreak()` | `#colbreak()` | |
| `Grid(columns, cells…)` | `#grid(columns: 2, […], …)` | Cells are content blocks |
| `Stack(direction)` | `#stack(dir: ttb, …)` | |
| `Pad(length, content)` | `#pad(x: 1em)[…]` | |
| `Place(alignment, dx?, dy?, content)` | `#place(center + horizon)[…]` | |
| `Block(content, width?, height?, fill?, stroke?)` | `#block(width: 100%)[…]` | |
| `Hide(content)` | `#hide[…]` | |
| `Rotate(angle, content)` / `Scale(x, y, content)` / `Move(dx, dy, content)` / `Skew(ax, ay, content)` | `#rotate(…)`, `#scale(…)`, `#move(…)`, `#skew(…)` | |
| `HSpace(length)` / `VSpace(length)` | `#h(1em)` / `#v(1em)` | |
| `Line`, `Rect`, `Square`, `Circle`, `Ellipse`, `Polygon`, `Curve` | `#line(…)`, `#rect(…)`, … | Shapes take `TypstLength`, `TypstColor`, `Stroke` |
| `MetadataNode(key, value)` | `#metadata((key: value)) <label>` | Public: `TypstDocument.MetadataEntry(key, value, label)`. Readable by `query` from Typst code |
| `Raw(markup)` | verbatim | Escape hatch: the caller's string is emitted untouched |

`Raw` is the only node that bypasses escaping. It is documented as "you are writing Typst now" and
is the supported way to use a Typst feature the model does not cover.

The complete mapping between the Typst reference and this node set — including what is
deliberately left to `Raw` — is in [11-reference-coverage.md](11-reference-coverage.md).

## 4. Inline content

`TypstContent` is a builder for a run of inline material:

```csharp
.Paragraph(c => c
    .Text("Total: ")
    .Bold("1 234 EUR")
    .Text(" (")
    .Link("https://example.com/invoice/42", "invoice")
    .Text(")"))
```

- `Text(string)` escapes.
- `Bold`, `Emphasis`, `Underline`, `Strikethrough`, `SmallCaps` wrap in `*…*`, `_…_`,
  `#underline[…]`, `#strike[…]`, `#smallcaps[…]`.
- `Math(string)` inserts inline math.
- `Node(TypstNode)` inserts a block node inline (rare; used for `InlineCode`).
- Implicit conversion from `string` to `TypstContent` is provided so `Heading(1, "Revenue")` works.

## 5. Escaping

The writer escapes markup-mode special characters. This table is the specification; the
implementation is a single `switch` over `char`.

| Character | Emitted | Why |
| --- | --- | --- |
| `\` | `\\` | Escape character |
| `#` | `\#` | Starts code |
| `$` | `\$` | Starts math |
| `*` | `\*` | Strong emphasis |
| `_` | `\_` | Emphasis |
| `` ` `` | `` \` `` | Raw text |
| `<` | `\<` | Label |
| `>` | `\>` | Label terminator |
| `@` | `\@` | Reference |
| `~` | `\~` | Non-breaking space |
| `-` at line start | `\-` | Bullet list |
| `+` at line start | `\+` | Numbered list |
| `1.` at line start | `1\.` | Numbered list, digit form |
| `/` at line start | `\/` | Term list |
| `=` at line start | `\=` | Heading |
| `//` anywhere | `\/\/` | Line comment |
| `/*` anywhere | `\/*` | Block comment |
| `]` inside a content block | `\]` | Closes the block |
| `[` inside a content block | `\[` | Opens a nested block |

Not escaped, because they are not special in markup mode: `"`, `'`, `(`, `)`, `{`, `}`, `%`, `&`,
`|`, `!`, `?`, `:`, `;`, `,`, `.`, `^`, `°`, `€`.

Rules:

1. Escaping is applied per character, in one pass, with no lookahead beyond the two-character
   sequences above and the digit run of a `1.` list marker.
2. Line-start rules apply to the first non-space character of a line produced by the writer, not to
   the first character of the input string. A paragraph whose text begins with `-` is escaped.
3. A digit run followed by `.` is escaped only when the period is followed by a space or ends the
   text: `1. item` is a list marker, `1.5` is a number.
4. Newlines inside `Text` are preserved as line breaks in the source. Typst collapses them into
   spaces, which is the intended behaviour for wrapped text.
5. `CodeBlock`, `InlineCode`, `Math` and `Raw` content is never escaped. `InlineCode` chooses a
   backtick fence longer than the longest run of backticks in the code.

## 6. Writer

```csharp
internal sealed class TypstMarkupWriter
{
    public void WriteText(string text);              // escapes
    public void WriteVerbatim(string text);          // no escaping
    public void WriteLine();                         // newline
    public void WriteBlankLine();                    // exactly one blank line, collapsed
    public IDisposable ContentBlock();               // "[" … "]" with bracket escaping
    public IDisposable Indent();                     // +2 spaces for nested lists
}
```

Invariants enforced by the writer:

- No more than one consecutive blank line in the output.
- No trailing whitespace on any line.
- The output ends with exactly one newline.
- Indentation is spaces only, 2 per level, never tabs.

These invariants are asserted by unit tests over every node type, because they are what makes
golden files readable in review.

## 7. Page setup

```csharp
.Page(p => p
    .Paper("a4")                                  // a0–a6, letter, legal, or .Size(w, h)
    .Margin(TypstLength.Cm(2))                    // every side
    .Margin(TypstLength.Cm(2), TypstLength.Cm(2), // top, right, bottom, left (CSS order)
            TypstLength.Cm(3), TypstLength.Cm(3))
    .MarginX(TypstLength.Cm(3))                   // left and right
    .MarginY(TypstLength.Cm(2))                   // top and bottom
    .Landscape()                                  // swaps width and height
    .Fill(TypstColor.Rgb(250, 250, 240))          // page background
    .Columns(2)                                   // body columns
    .Numbering("1 / 1")                           // null disables numbering
    .WithHeader(TypstContent.Empty.Text("Confidential"))
    .WithFooter(TypstContent.Empty.Text("Page ").PageNumber().Text(" of ").PageCount()))
```

`Margin` is exclusive: the last call wins, so a uniform margin clears the per-side ones and the
per-side overload clears the uniform one. `MarginX` and `MarginY` set only their axis.

`PageNumber()` emits `#context counter(page).display()` and `PageCount()` emits
`#context counter(page).final().first()`. Both need `context` because the value is only known after
layout — Typst rejects the bare form inside a header or footer. Header and footer content is emitted
inside `#set page(header: […], footer: […])`.

## 8. Metadata

`Metadata` maps to `#set document(...)`:

| Member | Typst |
| --- | --- |
| `Title` | `title` |
| `Author` | `author` (string or array) |
| `Keywords` | `keywords` |
| `Date` | `date` — defaults to the world's fixed clock, not to `now` |
| `Language` | `#set text(lang: "en")` |

`Date` is emitted only when set explicitly; otherwise Typst uses the world clock, which is already
deterministic.

## 9. Units

```csharp
public readonly record struct TypstLength
{
    public static TypstLength Cm(double v);
    public static TypstLength Mm(double v);
    public static TypstLength Pt(double v);
    public static TypstLength Em(double v);
    public static TypstLength Percent(double v);
    public static TypstLength Fr(double v);
    public static TypstLength Zero { get; }
}
```

Formatting uses the invariant culture and up to three decimals, trailing zeros trimmed
(`2cm`, `1.5cm`, `12.5pt`, `80%`, `1fr`). A `double` that is `NaN` or infinite throws.

## 10. Compiling a model

```csharp
using var compiler = new TypstCompiler();
byte[] pdf = compiler.CompileToPdf(doc);          // convenience: ToMarkup() + compile
```

`CompileToPdf(TypstDocument)` is sugar over `CompileToPdf(doc.ToMarkup())`. The model has no
knowledge of the compiler; the dependency points the other way.

## 11. What the model deliberately does not do

| Not provided | Reason |
| --- | --- |
| A node for every Typst function | The model covers the document-shaped 90 %; `Raw` covers the rest |
| Automatic table of contents | `#outline()` is one `Raw` call; a typed wrapper adds nothing |
| A theme/styling system | Templates own styling (see [05](05-templates.md)); the model emits structure |
| Round-trip parsing of Typst | Out of scope by design; the HTML importer only goes HTML → model |
| HTML-specific nodes | The model targets paged documents; HTML export uses the same source |

## 12. Importing HTML

`TypstDocument.FromHtml(html, options)` builds the node graph from an HTML page or fragment, so an
application that already holds HTML does not have to go through Typst source by hand. Call
`TypstHtmlImporter` directly when the elements that were dropped matter:

```csharp
using M3scalinum.Text.SharpTypst.Documents;

var importer = new TypstHtmlImporter(new TypstHtmlImportOptions
{
    AssetResolver = source => source == "charts/revenue.png" ? "charts/revenue.png" : null,
    UnknownElements = TypstHtmlUnknownElementPolicy.Skip,
    HonourInlineStyles = false,
});

TypstHtmlImportResult result = importer.Import(html);

foreach (var dropped in result.DroppedElements)
{
    Console.WriteLine(dropped.ElementName + ": " + dropped.Count);
}

string markup = result.Document.ToMarkup();
```

The parser is hand-written and has no dependency of its own: a single-pass tokenizer over the
string, a tree repair pass, and a converter into the nodes of §3. It is not a conforming HTML
parser, and it does not try to be one. It does handle the parts real pages rely on:

- void elements (`br`, `hr`, `img`, `meta`, `link`, …) and XHTML-style `<br/>`;
- unclosed `p`, `li`, `dt`/`dd`, `tr`, `td`/`th`, `thead`/`tbody`/`tfoot`, closed at the point a
  browser would close them;
- attributes with double quotes, single quotes or no quotes, and valueless attributes;
- comments, a doctype, and CDATA sections;
- character references: the named set listed in `HtmlEntities`, plus decimal and hexadecimal
  numeric references (`&#233;`, `&#xE9;`). An unknown reference is kept verbatim.

Whitespace is collapsed as HTML collapses it, except inside `pre`, where it is kept byte for byte.
A non-breaking space is content, not whitespace, and survives.

### 12.1 Fidelity contract

The table is the contract. "Dropped" means the element contributes no output and is counted in
`TypstHtmlImportResult.DroppedElements`; "transparent" means it contributes no output but its
children are imported and it is not counted.

| HTML | Node emitted | Notes |
| --- | --- | --- |
| `p` | `ParagraphNode` | An empty paragraph emits nothing |
| `h1`–`h6` | `HeadingNode` | Levels map to `=` … `======` |
| `ul`, `ol`, `li` | `ListNode` | Nested lists are emitted two spaces deeper per level |
| `table`, `thead`, `tbody`, `tfoot`, `tr`, `th`, `td` | `TableNode` | `colspan` and `rowspan` become `table.cell(…)`; column count is the widest row |
| `strong`, `b` | `BoldNode` | |
| `em`, `i` | `EmphasisNode` | |
| `code` | `InlineCodeNode` | The fence grows past any backtick run in the code |
| `pre` | `CodeBlockNode` | The language comes from `code class="language-…"`; one leading newline is removed; the fence grows past any backtick run in the code |
| `a href` | `LinkNode` | Without an `href`, its text is kept without a link |
| `img src` | `FigureNode` | `src` is resolved by `AssetResolver`; see below |
| `blockquote` | `QuoteNode` | Its blocks become paragraphs inside the quote |
| `br` | `LineBreakNode` | |
| `hr` | `#divider()` | |
| `dl`, `dt`, `dd` | `TermListNode` | A `dd` with no preceding `dt` gets an empty term |
| `sup`, `sub` | `#super[…]`, `#sub[…]` | |
| `html`, `body`, `div`, `span`, `section`, `article`, `header`, `footer`, `main`, `nav`, `aside`, `figure`, `figcaption`, `form`, `fieldset`, `details`, `summary`, `address`, `center`, `hgroup`, `menu`, `picture` | none | Transparent: children imported, element not counted |
| anything else | none | Unwrapped if it has text: the children are imported, the element is counted; `UnknownElements = Throw` fails instead |

Table headers: only the first `thead` row becomes `table.header(…)`. Further `thead` rows are
imported as body rows, and a `<caption>` is dropped and counted, because `TypstTable` has one header
row and no caption.

Elements whose content is invisible in a paged document — `head`, `title`, `meta`, `link`, `base`,
`script`, `style`, `noscript`, `template`, form controls, `source`, `track`, `wbr` — are ignored
silently, not counted. Elements whose text is not prose — `iframe`, `object`, `embed`, `video`,
`audio`, `canvas`, `svg`, `math`, `map` — are dropped with their subtree and counted.

### 12.2 Images

The importer **never fetches a network resource and never reads a file**. Every `img` source is
passed to `TypstHtmlImportOptions.AssetResolver`; the returned name must be an asset registered on
the world builder. When the resolver is null or returns null or white space, the import fails with
`TypstHtmlImportException` whose `ElementName` is `img`, rather than emitting an image that would
only fail later, at compile time. An inline `img` (inside a paragraph) is reduced to its `alt` text,
but it still has to resolve. `alt` and `title` are otherwise not emitted.

### 12.3 Inline styles

`HonourInlineStyles` defaults to `false`, and the importer does not do CSS layout. When it is
`true`, exactly three declarations are honoured:

| Declaration | Effect |
| --- | --- |
| `font-weight: bold`, `bolder`, or a numeric weight of 600 or more | bold |
| `font-style: italic` or `oblique` | emphasis |
| `text-decoration` containing `underline` / `line-through` | underline / strike |

Every other declaration — colour, size, font family, spacing, `display`, margin, width, position —
is ignored. A page whose fidelity depends on CSS must be rendered by a browser; importing it here
would silently produce a different document.

### 12.4 Escaping

HTML text and attribute values are never concatenated into Typst source. They enter the model as
`TextNode` and `LinkNode` values, so the writer of §5 escapes them: `*`, `_`, `#`, `$`, `<`, `>`,
`@`, `~` and brackets in text, and a quote or a backslash in an `href`, cannot break the emitted
markup. This is the one rule the importer shares with the rest of the model, and it is covered by a
fixture in both the managed and the compile-level test suites.

### 12.5 What a real page loses

Deliberately not imported, and not pretended otherwise: CSS layout (columns, floats, sizing,
positioning); classes and ids, except `class="language-…"` on `code`; `colspan`/`rowspan` are kept
but row groups, `colgroup` widths and `caption` are not; `alt` and `title`; inline `style` beyond
the three declarations above; MathML and SVG; `figure` captions (a `figcaption` becomes an ordinary
paragraph); definition lists with several `dd` per `dt` (the second `dd` gets an empty term);
footnotes, citations and any semantic markup not listed in §12.1. Anything outside the table is
either unwrapped and counted, or dropped with its subtree and counted, so the loss is visible in
`DroppedElements` instead of being silent.

## 13. Importing Markdown

`TypstDocument.FromMarkdown(markdown, options)` builds the node graph from a Markdown document or
fragment, so a README, a changelog or a specification written in Markdown can be typeset without a
conversion step outside the library. Call `MarkdownImporter` directly when the constructs that were
dropped matter:

```csharp
using M3scalinum.Text.SharpTypst.Documents;

var importer = new MarkdownImporter(new TypstMarkdownImportOptions
{
    AssetResolver = source => source == "charts/revenue.png" ? "charts/revenue.png" : null,
    RawHtml = TypstMarkdownRawHtmlPolicy.Drop,
});

TypstMarkdownImportResult result = importer.Import(markdown);

foreach (var dropped in result.DroppedConstructs)
{
    Console.WriteLine(dropped.ConstructName + ": " + dropped.Count);
}

string markup = result.Document.ToMarkup();
```

The parser is hand-written and has no dependency of its own: a line-based block pass, a definition
pass that collects reference links and footnotes before any block is converted, and a recursive
inline pass. It is not a conforming CommonMark implementation and does not pretend to be one. It
does handle the parts real documents rely on:

- ATX headings with a closing hash run, and setext headings underlined with `=` or `-`;
- fenced code blocks opened with three or more backticks or tildes, closed by a run at least as long
  as the opener, with the first word of the info string as the language; an unclosed fence runs to
  the end of the document, as CommonMark specifies;
- indented code blocks, taken four columns in;
- bullet lists with `-`, `+` or `*`, ordered lists with `.` or `)` delimiters, nesting by
  indentation, and tight versus loose lists;
- block quotes with `>`, including lazy continuation lines and blank quoted lines;
- thematic breaks of `*`, `-` or `_`;
- GFM tables with a header row, a delimiter row carrying the alignment, and body rows;
- reference link definitions, which may appear before or after the reference that uses them;
- footnote definitions, which may span several indented lines;
- backslash escapes and the character references `HtmlEntities` decodes.

Line endings are normalised (CRLF and lone CR become LF) and a byte-order mark is ignored. Tabs are
expanded to four-column tab stops before anything else, so a tab-indented code block is still a code
block; a tab inside a code block's content becomes spaces.

### 13.1 Fidelity contract

The table is the contract. "Dropped" means the construct contributes no output and is counted in
`TypstMarkdownImportResult.DroppedConstructs`; "transparent" means it contributes no output but its
content is imported and it is not counted.

| Markdown | Node emitted | Notes |
| --- | --- | --- |
| paragraph | `ParagraphNode` | Soft line breaks become one space |
| ATX `#`…`######` | `HeadingNode` | Levels map to `=` … `======`; a closing `##` run is stripped |
| setext `===` / `---` underline | `HeadingNode` | Level 1 for `=`, level 2 for `-` |
| fenced code block | `CodeBlockNode` | The first word of the info string is the language; the fence grows past any backtick run in the code |
| indented code block | `CodeBlockNode` | No language |
| `-`, `+`, `*` bullet list | `ListNode` | Nested lists are emitted two spaces deeper per level |
| `1.`, `1)` ordered list | `ListNode` | Emitted as `+`; the start number and the delimiter are lost |
| tight list | `ListNode` | Items are written without a blank line between them |
| loose list | `ListNode` | A blank line separates items, which is what makes a Typst list loose |
| `>` block quote | `QuoteNode` | Its blocks become paragraphs inside the quote |
| `***`, `---`, `___` | `#divider()` | Three or more markers, with optional spaces between them |
| GFM table | `TableNode` | Column count is the header's; `:---`, `---:` and `:---:` become `table.cell(align: …)` |
| `**strong**`, `__strong__` | `BoldNode` | |
| `*em*`, `_em_` | `EmphasisNode` | |
| `~~strike~~` | `#strike[…]` | |
| `code span` | `InlineCodeNode` | The fence grows past any backtick run in the code |
| `[text](destination "title")` | `LinkNode` | The title is parsed and dropped; there is no title node |
| `[text][label]`, `[label][]`, `[label]` | `LinkNode` | Resolved from the definitions; an undefined label stays literal text |
| `<https://…>`, `<mail@example.com>` | `LinkNode` | Bare URLs are not autolinked |
| `[^label]` + `[^label]: …` | `FootnoteNode` | The first reference emits `#footnote[…]`; a second reference to the same label stays literal text and is counted |
| two trailing spaces or `\` before a line break | `LineBreakNode` | |
| `![alt](destination)` alone in a paragraph | `FigureNode` | The destination is resolved by `AssetResolver`; `alt` becomes the caption, through the same escaper as text |
| `![alt](destination)` inside a paragraph | the alt text | Still has to resolve |
| raw HTML block | none | Dropped and counted as `html-block`; never passed through |
| an inline HTML tag | none | Dropped and counted as `html-inline`; the text around it survives |
| a reference link definition that is never used | none | Counted as `reference-definition` |
| a footnote definition that is never referenced | none | Counted as `footnote-definition` |
| a non-paragraph block inside a footnote definition | none | Counted as `footnote-block`; a footnote is inline content |
| a table cell beyond the header's column count | none | Counted as `table-cell`; a short row is padded with empty cells |

`RawHtml = TypstMarkdownRawHtmlPolicy.Throw` turns the first raw HTML block into a
`TypstMarkdownImportException` instead of dropping it. Inline HTML is always dropped and counted;
the policy governs blocks.

### 13.2 Images

The importer **never fetches a network resource and never reads a file**. Every image destination —
inline or reference — is passed to `TypstMarkdownImportOptions.AssetResolver`; the returned name
must be an asset registered on the world builder. When the resolver is null or returns null or
white space, the import fails with `TypstMarkdownImportException` whose `ConstructName` is `image`,
rather than emitting an image that would only fail later, at compile time. An image alone in its
paragraph becomes a figure whose caption is the alt text; the same image inside running text is
reduced to its alt text, so the sentence still reads.

### 13.3 Escaping

Markdown text is never concatenated into Typst source. Every piece of text — paragraph text, a link
label, an alt text used as a caption, a table cell, a footnote body — enters the model as a
`TextNode`, so the writer of §5 escapes it. `*`, `_`, `#`, `$`, `<`, `>`, `@`, `~`, backslashes and
brackets inside a content block cannot break the emitted markup, and a quote or a backslash in a
link destination is escaped by `LinkNode`. The fixture that pins this puts `*_#$<>@[]` in text, in a
link title and in an alt text, and is run again through the real compiler in the integration suite.

### 13.4 What a real README loses

Deliberately not imported, and not pretended otherwise: raw HTML, block-level or inline (dropped and
counted, never passed through); HTML comments, `<details>` and custom containers such as
`::: warning` (both are just paragraphs or HTML blocks); GFM extensions other than tables,
strikethrough and footnotes — task list checkboxes, bare-URL autolinking, heading ids of the
`{#id}` form; footnote numbering beyond document order (Typst numbers footnotes in the order they
appear, a second reference to one label is not the same footnote); the start number and delimiter
of an ordered list; link titles; image dimensions and any text following the destination inside an
image's parentheses; table alignment beyond per-column left, centre and right; hard-wrapped lines
that a Markdown renderer would keep as one paragraph are joined with a space, which is what
CommonMark does; and emphasis that spans a link boundary, which this parser does not resolve the way
CommonMark's delimiter stack would. Anything outside the table is either dropped and counted or
turned into its text, so the loss is visible in `DroppedConstructs` instead of being silent.
