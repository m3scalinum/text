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
| `Citation(key)`, `Bibliography(bibliography, style)` | a resolved citation and the model-built list |

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

## Language and direction

`.Language("en")` in the metadata writes the document language as a string.
`TypstDocument.Language` and `TypstDocumentStyleBuilder.Language` take a `TypstLanguageTag`
instead, and write the same `#set text(lang: …)` rule; the document's own call is written after
every style rule, so it wins over a style that set a language of its own.

The tag is what selects the hyphenation dictionary, the line-breaking rules and the language the
PDF declares. It is not `TypstLanguage`, the closed table `TypstFormatting` reads its number and
date patterns from: `TypstLanguageTag` carries a code for some sixty languages Typst hyphenates,
including the ones that have no date pattern.

```csharp
document
    .Language(TypstLanguageTag.Japanese)
    .Paragraph(c => c
        .Text("A Japanese quotation: ")
        .Language(TypstLanguageTag.Japanese, TypstContent.Empty.Text("東京"))
        .Text(" keeps its own line breaking."));
```

`Of` reads any well-formed BCP 47 tag — two or three letters, then one-to-eight-letter or digit
subtags separated by hyphens, as in `zh-Hans` or `de-CH` — and refuses anything else with
`ArgumentException`. It does not check the tag against a list of languages: a tag Typst has no
dictionary for hyphenates nothing. Equality ignores case, because Typst does.

Direction is set per block or per run. `TypstDocument.Direction` gives a block of paragraphs its
base direction, and `TypstContent.Direction` gives one run a direction that differs from its
paragraph's:

```csharp
document
    .Direction(TypstDirection.RightToLeft, TypstContent.Empty.Text("فقرة كاملة"))
    .Paragraph(c => c
        .Text("The heading reads ")
        .Direction(TypstDirection.RightToLeft, TypstContent.Empty.Text("العنوان"))
        .Text(" in Arabic."));
```

The block form emits `#block[#set text(dir: …) …]`, because a paragraph resolves `dir` from its own
style chain and an inline embedding would leave the base direction left to right. The run form emits
`#text(dir: …)[…]`, which the bidirectional algorithm reorders with its neighbours.

`TypstDirection` also names `TopToBottom` and `BottomToTop`, and both are refused with
`ArgumentOutOfRangeException`: `text(dir:)` is a horizontal property, and the compiler rejects `ttb`
and `btt` there. Vertical text is a rotated block, not a direction.

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

## Sections: a header, a footer and a watermark per part

A style describes one presentation from the first page to the last. `TypstSection` is the boundary
inside a document: a part with its own running header, footer, watermark and page numbering, and a
cover that shows no page number.

```csharp
var report = TypstDocument.Create()
    .Apply(style)
    .Section(
        section => section.NoPageNumber().NoHeader().Footer("COVER"),
        body => body.Heading(1, "Annual report").Paragraph("Cover page"))
    .Section(
        section => section
            .Header("Part I")
            .Footer("Part I — page {page}")
            .Watermark("DRAFT"),
        body => body.Heading(1, "Context").Paragraph("The contract starts here."));
```

`TypstSection.Create()` holds the section's content in `Body`, a whole `TypstDocument`, so every
method a document has works inside it. `TypstDocument.Section(TypstSection)` appends the section;
`TypstDocument.Section(configure, body)` builds it and fills it in one call. `Header` and `Footer`
take the same `{page}`, `{pages}` and `{title}` placeholders as a style; `NoHeader` and `NoFooter`
remove the inherited one for the section's pages only, and `NoWatermark` does the same for the mark.

`PageNumbering("i")` numbers the section's pages with another pattern, and `NoPageNumber` hides the
number without touching the counter: the page after an unnumbered cover is page two. `StartsOnNewPage`
puts the section on a fresh page, which matters only for a section that overrides nothing.

The whole mechanism is a `#set page(…)` inside a `#[ … ]` content block: Typst scopes the rule to
the block, so what the document and its style declared is in force again after it. A section only
overrides what it declares — one with a footer and no header keeps the document's header. A page
that holds the last lines of one part and the first lines of the next uses the earlier part's setup,
because Typst picks the setup of the content that starts the page; a caller that wants an exact
boundary calls `StartsOnNewPage`.

`Watermark` takes a word and an optional configuration delegate, or a prepared `TypstWatermark`:

```csharp
section.Watermark("CONFIDENTIAL", w => w.Angle(-45).Size(TypstLength.Pt(96)).Opacity(0.2));
```

The mark is the page's background, centred and rotated, not a run of content: it is drawn before the
body on every page the section covers and never pushes the text down. `TypstWatermark.Create(text)`
defaults to a −30° slant, 72 pt and a light grey at 15 %; `Angle`, `Size`, `Color` and `Opacity`
change them, and `Opacity` refuses a value outside 0 to 1 with `ArgumentOutOfRangeException`. It is
drawn as text, so a text extraction returns the mark's words on each covered page, before the body.

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

## Citations and a bibliography

A `.bib` file is parsed once into a `TypstBibliography`, and a document cites its keys:

```csharp
TypstBibliography bibliography = TypstBibliography.Parse(File.ReadAllText("refs.bib"));

var document = TypstDocument.Create().Heading(1, "Method");

document
    .Paragraph(c => c.Text("The layout pass is linear ").Cite(document, "chen2022").Text("."))
    .Paragraph(c => c.Text("Counters are page-scoped ").Cite(document, "dupo2024").Text("."))
    .Heading(1, "References")
    .Bibliography(bibliography, TypstBibliographyStyle.AuthorDate);
```

`Cite(document, key)` is an extension on `TypstContent`: it joins the citation to the paragraph in
progress, and the document records its position in the citation order as it builds. `Citation(key)`
appends the citation as a paragraph of its own and `CitationContent(key)` returns it as content for
elsewhere. A `prose: true` argument makes the citation a grammatical part of the sentence —
`Dupont (2024)` rather than `(Dupont, 2024)`, `Chen et al. [1]` rather than `[1]`.

The two styles differ in the flow and in the list, not in the entry text:

| Style | In the flow | The list |
| --- | --- | --- |
| `Numeric` | `[1]`, `[2]`, … | numbered in order of first citation, `[1]` before the entry |
| `AuthorDate` | `(Dupont, 2024)`, `(Dupont and Durand, 2024)`, `(Chen et al., 2022)` | sorted by first author's family name, then year, then key; unnumbered |

Only the cited entries are listed, in the order the style asks for; a document that cites nothing
lists every entry in file order. The list is emitted by the model as markup, one paragraph per
entry, because `#bibliography("refs.bib")` reads its file from the compilation world and resolves
its style through the embedded CSL catalogue. The model's list is the string your test can assert.

One document carries one bibliography: a second `Bibliography(…)` throws
`InvalidOperationException`. A citation naming a key the bibliography does not define raises
`TypstCitationException`, whose `Key` and `AvailableKeys` name the mistake and the keys that exist;
nothing is ever written as `[?]`. When `Bibliography(…)` was already called the key is checked at
the citation call; a citation built before one is attached fails when the document is written, with
`InvalidOperationException` naming the missing bibliography.

`TypstBibTexParser.Parse` reads `@article`, `@book` and `@inproceedings` specifically and any other
entry type structurally, then formats it generically. `@string` macros, a bare macro as a value and
`#` concatenation are refused with `TypstBibTexParseException`, whose `Position` is the offset of
the construct — a field silently dropped is worse than a file that has to be expanded first.
`TypstBibTexEntry` exposes `Key`, `EntryType`, `Fields`, `Title`, `Authors`, `Year`, `Venue`,
`Publisher`, `Volume`, `Number`, `Pages`, `Doi` and `Field(name)`, and a field it has no accessor
for is still in `Fields`.

`TypstBibliography` is immutable once parsed, so one instance can serve several documents, each
with its own citation order. `Entries` and `Keys` are in file order; `Contains`, `Resolve` and the
indexer answer by key, and `Resolve` throws `TypstCitationException` naming the alternatives.
`FromEntries` builds the same type from entries another reader produced, and refuses two entries
that share a key with `ArgumentException`.

`TypstDocument.Cite(key)` and `TypstContent.Cite(key)` are a different pair: they write Typst's
own `@key` and `#cite(<key>)`, which need a `#bibliography(path, style)` in the document and
hayagriva's CSL styles. The typed members are named `Citation` and `Cite(document, key)` so the two
machineries cannot be confused.

## An index

`IndexMark` records a term — and an optional subterm — at a point in the flow, and `Index` prints
the alphabetical list with the page each mark was laid out on:

```csharp
document
    .Paragraph("A cache evicts entries under pressure.")
    .IndexMark("cache", "eviction")
    .IndexMark("buffer")
    .PageBreak()
    .Index(TypstContent.Empty.Text("Index"));
```

The mark emits an invisible `#context metadata(…)` carrying the term, the subterm and
`counter(page).at(here()).first()`; the term and subterm are trimmed, and a blank one is an
`ArgumentException`. Nothing of the mark reaches the page. `Index(title)` emits one `context` block
that queries those dictionaries and nothing else of the document's metadata:

- terms are sorted by their lowercase form and de-duplicated, and each subterm the same way under
  its term;
- the page numbers are sorted numerically and de-duplicated, so a page marked twice prints once;
- a term marked only under a subterm prints no line of its own, and terms that differ only in case
  stay two adjacent lines.

The title, when given, is printed as a level-1 heading. The sort folds case with Typst's `lower`,
which consults no locale: it is case-insensitive and culture-invariant, but it is not a collation,
so `Å` sorts after `Z` and an accent makes a different term. A mark inside a floating element —
a figure with `placement: top`, say — reports the page the float lands on, which is the page the
reader sees. The index prints the value of `counter(page)`, the same counter the footer prints, so a
roman-numbered preface indexes its number rather than its glyph.

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
