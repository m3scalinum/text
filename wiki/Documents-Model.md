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

A header row is emitted as `table.header(repeat: true, …)`, so it repeats on every page the table
spans; `RepeatHeader(false)` draws it on the first page only. A cell carries `Breakable = false` when
its row must not be cut across a page, and `Breakable = true` when a tall cell may be split — left
null, Typst's `auto` decides and the model emits nothing.

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

## A reusable style

A `TypstDocumentStyle` holds the page, the body text, heading levels 1 to 3, the language and a
running header and footer, and any document applies it with `Apply`:

```csharp
var style = TypstDocumentStyle.Create()
    .Page(p => p.Paper("a4").Margin(TypstLength.Cm(2)).Numbering("1 / 1"))
    .Body(b => b.Font("Inter").Size(TypstLength.Pt(10)).Leading(1.4))
    .Heading(1, h => h.Size(TypstLength.Pt(18)).Spacing(TypstLength.Cm(0.6), TypstLength.Cm(0.3)))
    .Language(TypstLanguage.French)
    .Header("{title} — {page} / {pages}")
    .Footer("Confidential")
    .Build();                                   // frozen; the builder can be reused or dropped

document.Apply(style);
```

`Build()` freezes the `#set` and `#show` lines the style will write, so mutating the builder
afterwards changes nothing. The header and footer text takes three placeholders — `{page}` for the
page number, `{pages}` for the page count and `{title}` for the document title — and every other
character is literal.

A style speaks only where it is configured: a document that already set a page fill, a font or a
header keeps them wherever the style is silent, and applying the same style twice writes its lines
once.

## References, labels and a table of contents

`Label` names the element it follows, and `Reference` points at a name:

```csharp
document
    .Table(t => t
        .HeaderRow("Region", "Revenue")
        .Row("North", "1.2M")
        .WithLabel("tbl:revenue"))
    .Paragraph(TypstContent.Empty.Text("Revenue by region: "))
    .Reference("tbl:revenue")
    .Outline(TypstContent.Empty.Text("Contents"), depth: 2);
```

`Reference` emits `#ref(<label>)`, with an optional `supplement` word printed before the number; the
model writes no number itself, because Typst resolves it against the laid-out document. A label may
be referenced from anywhere, including before the target appears.

`Label(TypstLabelKind kind)` generates deterministic names — `sec:1`, `fig:1`, `tbl:1`, then `:2` for
the next call of that kind — so a caller never has to invent one. Labelling a heading turns
`#set heading(numbering: "1.1")` on, because Typst refuses to reference a heading that is not
numbered.

`Outline(title, depth)` emits `#outline(…)`; Typst fills it at layout time with the final page number
of every heading up to `depth` (1 to 6). A caller that needs the same tree on the host side reads
`TypstEvaluation.Outline` from the compiled document.

## Figures

```csharp
document.Figure(
    f => f
        .Image("chart.png", TypstLength.Percent(80))
        .Alt("Revenue by region, north ahead of south by 0.3M")
        .WithCaption(TypstContent.Empty.Text("Revenue by region")),
    TypstNumberingScope.Section);
```

`Alt` is prose, quoted and escaped, never parsed: Typst turns it into a `/Figure` structure element
carrying `/Alt`, which is what `PdfConformance.CheckAccessibility` looks for. `Image` takes an
optional `fit` of `cover`, `contain` or `stretch`.

`TypstNumberingScope.Section` prints numbers as `section.figure` and restarts the count at every
level-1 heading; it turns heading numbering on unless the document already numbers its headings. A
figure that precedes the first section keeps a plain number. `Footnote(content, scope)` takes the same
scope, where `Section` resets the note counter at each level-1 heading instead of numbering one
sequence for the whole document.

## Formatting dates and amounts

`TypstFormatting` writes dates, numbers and amounts the way a language writes them, from a table in
the code rather than from a culture object — the suite ships with `InvariantGlobalization=true`, where
`CultureInfo.GetCultureInfo("fr-FR")` throws and a culture-taking `ToString` silently returns the
invariant result:

```csharp
TypstFormatting.FormatDate(new DateOnly(2026, 3, 1), TypstLanguage.French);      // 01/03/2026
TypstFormatting.FormatNumber(
    1234.56m, TypstNumberFormat.WithFractionDigits(TypstLanguage.French, 2));   // 1 234,56
TypstFormatting.FormatCurrency(1234.56m, "EUR", TypstLanguage.French);            // 1 234,56 €
```

The output is a pure function of the language, the pattern and the value: the same call produces the
same string whatever `CultureInfo.CurrentCulture` says. The French thousands separator is U+00A0, not
the narrow U+202F, because the compiler only special-cases the former when shaping.

## Writing it out

`ToMarkup()` is the Typst source, and the same document goes out as Markdown or as HTML without a
compilation:

```csharp
string markup = document.ToMarkup();

TypstMarkdownExportResult markdown = document.ToMarkdown();
TypstHtmlExportResult html = document.ToHtml();
```

Both exports report what they could not carry in `Unrepresentable` — constructs with their name and
count — rather than approximating it. See [Importing Markdown](Documents-Markdown-Import) and
[Importing HTML](Documents-Html-Import) for the mappings.

## Escaping

Every character that Typst treats as markup is escaped: `#`, `$`, `*`, `_`, `` ` ``, `<`, `>`, `@`,
`~`, `\`, and the line-start markers `-`, `+`, `/`, `=`. Comments (`//`, `/*`) are escaped too.

`Raw(markup)` is the only escape hatch: the string is emitted untouched. It is documented as "you are
writing Typst now" and is the one place where the model cannot protect you.
