# 04 — Document model

A fluent C# API that emits Typst source. It exists so that application code can build a document
without string concatenation and without knowing Typst's escaping rules.

## 1. Contract

- The model is **write-only**: `TypstDocument.ToMarkup()` returns a `string` of Typst source.
  There is no parser.
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
| `Table` | `#table(columns: (…), [..], …)` | Cells are content blocks; the header row is `table.header(…)` |
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
| Round-trip parsing | Out of scope by design |
| HTML-specific nodes | The model targets paged documents; HTML export uses the same source |
