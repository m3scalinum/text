# Document Model

`TypstDocument` builds a document in C# and emits Typst markup. It does not parse Typst: a document
you build is always valid Typst, because the escaping is done for you. It can also be built from
HTML — see [Importing HTML](Documents-Html-Import).

```csharp
using M3scalinum.Text.Documents;

var document = TypstDocument.Create()
    .Metadata(m => m.Title("Q3 report").Author("Finance"))
    .Page(p => p.Paper("a4").Margin(TypstLength.Cm(2)))
    .Heading(1, "Q3 report")
    .Paragraph(TypstContent.Empty.Text("Revenue grew by ").Bold("12%").Text(" this quarter."))
    .Table(t => t
        .Columns(TypstLength.Fr(1), TypstLength.Fr(1))
        .HeaderRow("Region", "Revenue")
        .Row("North", "1.2M")
        .Row("South", "0.9M"));

var pdf = TypstCompiler.CompileToPdf(document);
```

## Blocks

| Method | Emits |
| --- | --- |
| `Heading(level, content)` | `=` × level |
| `Paragraph(content)` | content + blank line |
| `BulletList(items)` / `NumberedList(items)` | `- item` / `+ item` |
| `TermList((term, description)…)` | `/ term: description` |
| `Table(t => …)` | `#table(…)` |
| `Figure(f => …)` | `#figure(…)` |
| `Code(language, code)` | a fenced block |
| `Math(expression)` | `$ … $` |
| `Quote(content)` | `#quote(…)` |
| `Outline(title?, depth)` | `#outline(…)` |
| `Divider()`, `PageBreak()`, `ColumnBreak()`, `ParagraphBreak()` | the matching call |
| `Footnote(content)`, `Cite(key)`, `Bibliography(path, style?)` | the matching call |

## Tables

`TypstTable` takes either content cells or `TypstTableCell`s, and a cell can cover several columns
or rows the way HTML's `colspan`/`rowspan` do:

```csharp
.Table(t => t
    .Columns(TypstLength.Fr(1), TypstLength.Fr(1), TypstLength.Fr(1))
    .Row(new TypstTableCell("Merged", colspan: 2, rowspan: 2), new TypstTableCell("Third"))
    .Row()                                  // the row the span above fills
    .Row("Bottom a", "Bottom b", "Bottom c"));
```

A row below a row span lists only the cells the span leaves it, and a fully covered row is `Row()`
with no cell. `Columns(…)` states the widths; without it the table measures its column count from
the cells and emits that many equal columns. `HeaderRow(…)` marks the header row, and
`HeaderScope = TypstHeaderScope.Column | Row | Both` names what a header cell labels — a merged
header cell keeps both its span and its scope.

## Layout

```csharp
document
    .Columns(2, TypstContent.Empty.Text("Two columns"))
    .Align(TypstAlignment.Center, TypstContent.Empty.Text("Centred"))
    .Box(TypstContent.Empty.Text("Boxed"))
    .Block(TypstContent.Empty.Text("Blocked"), TypstLength.Cm(4))
    .Grid(2, TypstContent.Empty.Text("a"), TypstContent.Empty.Text("b"))
    .Stack(TypstContent.Empty.Text("stacked"))
    .Pad(TypstLength.Cm(1), TypstContent.Empty.Text("padded"))
    .Place(TypstAlignment.Right, TypstContent.Empty.Text("placed"))
    .Hide(TypstContent.Empty.Text("hidden"))
    .Rotate(15, TypstContent.Empty.Text("rotated"))
    .Scale(80, TypstContent.Empty.Text("scaled"))
    .Move(TypstLength.Cm(1), TypstLength.Cm(1), TypstContent.Empty.Text("moved"))
    .Skew(5, 5, TypstContent.Empty.Text("skewed"))
    .HSpace(TypstLength.Cm(1))
    .VSpace(TypstLength.Cm(1));
```

## Inline content

`TypstContent` is a chainable inline builder:

```csharp
var line = TypstContent.Empty
    .Text("plain ")
    .Bold("bold ")
    .Emphasis("italic ")
    .Code("code ")
    .Math("x^2 ")
    .Link("https://example.com", "link ")
    .LineBreak()
    .Text("after the break");
```

It also carries the shapes — `Line`, `Rect`, `Square`, `Circle`, `Ellipse`, `Polygon`, `Curve` — and
the decorations — `Upper`, `Lower`, `SmallCaps`, `Highlight`, `Underline`, `Overline`,
`Strikethrough`, `Subscript`, `Superscript`, `Lorem`.

## Page setup

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
per-side overload clears the uniform one.

`PageNumber()` and `PageCount()` emit `#context counter(page)…` — the `context` is required, Typst
rejects the bare form inside a header or footer.

## Metadata

```csharp
.Metadata(m => m
    .Title("Q3 report")
    .Author("Finance")
    .Keywords("revenue", "q3")
    .Language("en")
    .Date(new DateOnly(2026, 9, 30)))
```

Maps to `#set document(…)`. The date is what PDF/A needs; the title is what PDF/UA needs.

## Escaping

Every character that Typst treats as markup is escaped: `#`, `$`, `*`, `_`, `` ` ``, `<`, `>`, `@`,
`~`, `\`, and the line-start markers `-`, `+`, `/`, `=`. Comments (`//`, `/*`) are escaped too.

`Raw(markup)` is the only escape hatch: the string is emitted untouched. It is documented as "you are
writing Typst now" and is the one place where the model cannot protect you.
