# 11 — Reference coverage

The Typst reference (`https://typst.app/docs/reference`) is the specification of the language and
its standard library. This document states, item by item, how each part of it is reachable from
M3scalinum.Text.SharpTypst, and what is deliberately not given a C# surface.

## 1. Two different questions

| Question | Answer |
| --- | --- |
| Is the whole Typst language available? | **Yes, by construction.** M3scalinum.Text.SharpTypst embeds the real compiler (0.15.1). No function is reimplemented, filtered, sandboxed or re-exported. Any valid Typst source compiles. |
| Does every function have a typed C# API? | **Yes.** All 151 reference entries are reachable from C#: 145 as first-class types, 6 as typed properties on options objects. |

Confusing the two is the usual failure mode of a binding: either it re-implements the language
badly, or it exposes 150 functions as 150 C# methods and becomes a second, worse Typst. This
library takes the first road — the compiler is the language — and then types the surface on top of
it, so that the typed API is a convenience over the real thing rather than a replacement for it.

## 2. Coverage levels

| Level | Meaning | Where it lives |
| --- | --- | --- |
| **L0** | Available in any source; no API needed | the compiler |
| **L1** | First-class C# type in `M3scalinum.Text.SharpTypst.Documents` | `M3scalinum.Text.SharpTypst` |
| **L2** | Typed property on an options object (page, text, table, metadata) | `M3scalinum.Text.SharpTypst` |
| **L3** | Reachable only by writing Typst: `Raw` node, template, or hand-written source | the caller |
| **L4** | Not reachable in v1, with a stated reason | — |

`L3` is the escape hatch, not a gap: a `Raw` node emits its string untouched, so any function is
one line away. No entry currently needs it.

## 3. Inventory

Counts as of Typst 0.15.1, taken from the reference sidebar.

| Category | Items | L1 | L2 | L3 | L4 |
| --- | --- | --- | --- | --- | --- |
| Foundations | 32 | 31 | 1 | 0 | 0 |
| Model | 22 | 19 | 3 | 0 | 0 |
| Text | 14 | 13 | 1 | 0 | 0 |
| Math | 20 | 20 | 0 | 0 | 0 |
| Layout | 27 | 26 | 1 | 0 | 0 |
| Visualize | 12 | 12 | 0 | 0 | 0 |
| Introspection | 7 | 7 | 0 | 0 | 0 |
| Symbols | 2 | 2 | 0 | 0 | 0 |
| Data loading | 7 | 7 | 0 | 0 | 0 |
| HTML | 3 | 3 | 0 | 0 | 0 |
| PDF | 5 | 5 | 0 | 0 | 0 |
| Bundle | 0 | 0 | 0 | 0 | 0 |
| **Total** | **151** | **145** | **6** | **0** | **0** |

Every entry of the reference now has a typed C# surface. The six L2 entries are typed properties on
options objects rather than standalone types, which is the right shape for them: `document`,
`numbering` and `title` are fields of the document metadata, `text` and `page` are set rules, and
`datetime` is a metadata value.

## 4. Per-category matrix

### 4.1 Foundations (32)

| Item | Level | C# surface |
| --- | --- | --- |
| `str` | L1 | `string`, `TypstContent.Text` |
| `int`, `float`, `decimal` | L1 | `int`, `double`, `decimal` in template parameters and lengths |
| `bool` | L1 | `bool` |
| `array` | L1 | `IEnumerable<T>` in template parameters |
| `dictionary` | L1 | `IReadOnlyDictionary<string, object?>` in template parameters |
| `content` | L1 | `TypstContent` |
| `label` | L1 | `Label(name)` node, `Reference(label)` |
| `datetime` | L2 | `Metadata.Date`, template `date` parameter |
| `duration` | L1 | `TypstExpression.Duration(seconds, minutes, hours, days, weeks)` |
| `bytes` | L1 | `TypstExpression.Bytes(text)`; bytes are also registered as assets |
| `path` | L1 | `TypstExpression.Path(path)`; virtual paths are strings in the API |
| `regex` | L1 | `TypstExpression.Regex(pattern)` |
| `selector` | L1 | `TypstExpression.Selector(target)` |
| `type`, `version` | L1 | `TypstExpression.TypeOf`, `TypstExpression.Version` |
| `module`, `function`, `arguments` | L1 | `TypstDocument.ImportAs(path, alias)` + `TypstExpression.Member(module, name)`; `TypstFunction.Define(name).Parameter(…).Rest(…).Body(…)`; `TypstExpression.Function(parameters, body)` and `TypstExpression.Arguments` |
| `auto`, `none` | L1 | `TypstExpression.Auto`, `TypstExpression.None`; `null` maps to `none` in template parameters |
| `symbol` | L1 | `TypstExpression.Symbol(name)` |
| `calc` | L1 | `TypstExpression.Calc(call)` |
| `eval` | L1 | `TypstExpression.Eval(code)` |
| `plugin` | L1 | `TypstPlugin.Load(path).Call(function, args…)` |
| `panic`, `assert` | L1 | `TypstExpression.Panic`, `TypstExpression.Assert` |
| `repr` | L1 | `TypstExpression.Repr` |
| `std`, `sys`, `target` | L1 | `TypstExpression.Std`, `TypstExpression.Sys`, `TypstExpression.Target` |

### 4.2 Model (22)

| Item | Level | C# surface |
| --- | --- | --- |
| `document` | L2 | `Metadata(...)` |
| `par` | L1 | `Paragraph` |
| `heading` | L1 | `Heading(level, content)` |
| `list` | L1 | `BulletList` |
| `enum` | L1 | `NumberedList` |
| `terms` | L1 | `TermList` |
| `table` | L1 | `Table` |
| `figure` | L1 | `Figure` |
| `quote` | L1 | `Quote` |
| `footnote` | L1 | `Footnote(content)` |
| `cite` | L1 | `Cite(key, form)` |
| `bibliography` | L1 | `Bibliography(path, style)` |
| `outline` | L1 | `Outline(title, depth)` |
| `link` | L1 | `Link(url, content)` |
| `ref` | L1 | `Reference(label)` |
| `parbreak` | L1 | `ParagraphBreak` |
| `divider` | L1 | `Divider` |
| `strong` | L1 | `Bold` |
| `emph` | L1 | `Emphasis` |
| `numbering` | L2 | `Page.Numbering`, `Heading.Numbering` |
| `title` | L2 | `Metadata.Title` |
| `asset` | L1 | `TypstDocument.Asset(path, data)`; valid only in the bundle target, which `CompileBundle` provides |

### 4.3 Text (14)

| Item | Level | C# surface |
| --- | --- | --- |
| `text` | L2 | `TextStyle` on `Text` nodes and `Page` |
| `upper`, `lower` | L1 | `Upper`, `Lower` inline |
| `smallcaps` | L1 | `SmallCaps` |
| `highlight` | L1 | `Highlight` |
| `underline`, `overline`, `strike` | L1 | `Underline`, `Overline`, `Strikethrough` |
| `sub`, `super` | L1 | `Subscript`, `Superscript` |
| `raw` | L1 | `InlineCode`, `CodeBlock` |
| `linebreak` | L1 | `LineBreak` |
| `lorem` | L1 | `Lorem(words)` |
| `smartquote` | L1 | `TypstDocument.SmartQuotes(enabled)` |

### 4.4 Math (20)

| Item | Level | C# surface |
| --- | --- | --- |
| `math` | L1 | `Math(expression)` — the expression is Typst math syntax, verbatim |
| `equation` | L1 | `Math(..., block: true)` with optional numbering |
| `frac`, `roots`, `binom`, `vec`, `mat`, `cases`, `lr`, `accent`, `attach`, `op`, `class`, `cancel`, `primes`, `stretch`, `sizes`, `styles`, `underover`, `variants` | L1 | `TypstMath` — `Frac`, `Sqrt`/`Root`, `Binom`, `Vec`, `Mat`, `Cases`, `Lr`, `Accent`, `Attach`, `Op`, `Class`, `Cancel`, `Primes`, `Stretch`, `Display`/`Inline`/`Script`/`Sscript`, `Upright`/`Italic`/`Bold`, `Underbrace`…`Overshell` |

`TypstMath` returns a `TypstExpression`, so a formula can be built and passed to `Math(...)`. The
arguments are math source, not C# strings to be escaped: `TypstMath.Frac("a^2", "2")` emits
`frac(a^2, 2)`. The compiler validates the result with a real position on error.

### 4.5 Layout (27)

| Item | Level | C# surface |
| --- | --- | --- |
| `page` | L2 | `Page(...)` |
| `pagebreak` | L1 | `PageBreak` |
| `block` | L1 | `Block` |
| `box` | L1 | `Box` |
| `place` | L1 | `Place(alignment, dx, dy)` |
| `align` | L1 | `Align(alignment, content)` |
| `pad` | L1 | `Pad(length, content)` |
| `grid` | L1 | `Grid(columns, rows)` |
| `stack` | L1 | `Stack(direction)` |
| `columns` | L1 | `Columns(count, content)` |
| `colbreak` | L1 | `ColumnBreak` |
| `h`, `v` | L1 | `HSpace`, `VSpace` |
| `hide` | L1 | `Hide(content)` |
| `rotate`, `scale`, `move`, `skew` | L1 | `Rotate(degrees or TypstAngle)`, `Scale`, `Move`, `Skew` |
| `length`, `ratio`, `relative`, `fraction` | L1 | `TypstLength` — `Cm`, `Mm`, `Pt`, `Em`, `Percent` (ratio), `Fr` (fraction); a `TypstLength` is accepted wherever Typst takes a relative |
| `angle` | L1 | `TypstAngle.Degrees`, `Radians`, `Turns` |
| `alignment` | L1 | `TypstAlignment` |
| `direction` | L1 | `TypstDirection`, applied with `TypstDocument.TextDirection` |
| `measure` | L1 | `TypstContext.Measure(content)` |
| `repeat` | L1 | `TypstDocument.Repeat(content, gap, justify)` |

### 4.6 Visualize (12)

| Item | Level | C# surface |
| --- | --- | --- |
| `image` | L1 | `Figure.Image(path, width)` |
| `line` | L1 | `TypstContent.Line(length, angle, stroke)` |
| `rect` | L1 | `TypstContent.Rect(width, height, fill, stroke, radius)` |
| `square` | L1 | `TypstContent.Square(size, fill, stroke, radius)` |
| `circle` | L1 | `TypstContent.Circle(radius, fill, stroke)` |
| `ellipse` | L1 | `TypstContent.Ellipse(width, height, fill, stroke)` |
| `polygon` | L1 | `TypstContent.Polygon(fill, stroke, points…)` |
| `curve` | L1 | `TypstContent.Curve(stroke, points…)` |
| `color` | L1 | `TypstColor` — `Named`, `Rgb`, `Rgba`, `Hex`, `Luma` |
| `gradient` | L1 | `TypstGradient.Linear(angle, stops…)`, `Radial(stops…)` |
| `stroke` | L1 | `TypstStroke.None`, `Solid(thickness, paint)`, `Of(thickness, paint)` |
| `tiling` | L1 | `TypstTiling.Of(width, height, body)` |

### 4.7 Introspection (7)

| Item | Level | C# surface |
| --- | --- | --- |
| `metadata` | L1 | `Metadata` node, `MetadataEntry(key, value, label)` |
| `counter` | L1 | `TypstCounter` — `Page`, `Heading`, `Figure`, `Footnote`, `Equation`, `Of(name)`; `Display`, `Get`, `At`, `Step`, `Update`, `Final` |
| `state` | L1 | `TypstState.Of(name, initial)`; `Get`, `At`, `Update`, `Final` |
| `query` | L1 | `TypstQuery` — `Of(selector)`, `Headings(level)`, `Figures()`, `Labels(label)` |
| `locate`, `here`, `location` | L1 | `TypstLocations` — `Here`, `Page`, `Position`, `Locate(label)` |

Introspection is contextual by nature: it reasons about the document being laid out. The typed
surface therefore emits code that runs *inside* the compiler, in a `#context { … }` block built by
`TypstDocument.Context(...)`. No callback crosses the ABI: the C# side writes the expression, the
compiler evaluates it.

### 4.8 Symbols (2)

| Item | Level | C# surface |
| --- | --- | --- |
| `sym` | L1 | `TypstExpression.Symbol(name)` |
| `emoji` | L1 | `TypstExpression.Emoji(name)` |

### 4.9 Data loading (7)

| Item | Level | C# surface |
| --- | --- | --- |
| `read` | L1 | `TypstData.Read(path)` |
| `json`, `yaml`, `toml`, `csv`, `cbor`, `xml` | L1 | `TypstData.Json`, `Yaml`, `Toml`, `Csv`, `Cbor`, `Xml` |

The host supplies bytes, Typst parses them: the file is registered with `AddAsset` and the typed
call emits `json("data.json")`. A C# JSON reader would be a second, divergent implementation.

### 4.10 HTML (3)

| Item | Level | C# surface |
| --- | --- | --- |
| `html.elem`, `html.frame`, `html.typed` | L1 | `TypstHtml.Elem(tag, body, attrs…)`, `Frame(content)`, `Typed(tag, body, attrs…)` — the typed layer is one constructor per element, so `Typed("p", …)` emits `html.p(…)` |

These only take effect in an HTML export; the paged exporter warns and ignores them.

### 4.11 PDF (5)

| Item | Level | C# surface |
| --- | --- | --- |
| `pdf.attach`, `pdf.artifact` | L1 | `TypstPdf.Attach(path, description)`, `TypstPdf.Artifact(content)` |
| `pdf.data-cell`, `pdf.header-cell`, `pdf.table-summary` | L1 | `TypstPdf.DataCell`, `HeaderCell`, `TableSummary(summary, table)` |

These three require the `A11yExtras` feature, which the world enables along with `Html` and
`Bundle`: every feature only adds definitions, so enabling all of them costs nothing and makes the
whole reference reachable from one world.

### 4.12 Bundle (0)

The `bundle` module has no items of its own in 0.15.1: it is a compilation target, not a function.
That target is supported — `TypstDocumentKind.Bundle` and `TypstCompiler.CompileBundle` — and it is
the only one in which `asset` and `document` are valid. The `typst-bundle` crate supplies the
`Output` implementation and the exporter; the native layer adapts them to a manifest plus per-file
bytes.

## 5. Conformance

The matrix above is only worth something if it is enforced. Three mechanisms:

### 5.1 Inventory drift check

`tests/fixtures/reference-inventory.json` lists every item per category, generated from the
reference. A test compares it with the matrix in this document and fails when:

- an item exists in the inventory but not in the matrix (a Typst upgrade added something),
- the matrix names an item that no longer exists,
- a level changed without the document being updated.

Upgrading Typst therefore starts with a failing test that lists exactly what to review.

### 5.2 Stdlib smoke documents

`tests/fixtures/conformance/<category>.typ` — one file per category, each invoking **every item of
that category at least once** with minimal valid arguments. They are compiled by
`ConformanceTests`, in `Paged` mode for eleven categories and in `Html` mode for the twelfth.

This is the real proof that "the whole reference is available": 151 items, 12 documents, compiled
by the shipped native library. A function that the compiler cannot reach fails the build. The
bundle category is compiled in bundle mode, which is the only target where `asset` and `document`
are valid.

### 5.3 Typed surface tests

Every typed entry has a unit test asserting the emitted Typst for a representative call, and the
conformance documents compile the same constructs for real. The inventory test
(`ReferenceInventoryTests`) checks the totals and the L3 list against this document, so a level
cannot change without the matrix being updated.

## 6. What this means for the API

| Consequence | Detail |
| --- | --- |
| The model is a convenience, not a gate | Anything not modelled is one `Raw` away |
| No "unsupported feature" errors | The library never refuses valid Typst |
| Templates are the pressure valve | Complex layout belongs in a `.typ` file, not in C# |
| The matrix is versioned | A Typst upgrade is a review of this document, not a silent behaviour change |

## 7. Known gaps worth naming

| Gap | Impact | Mitigation |
| --- | --- | --- |
| Introspection is source-only | Page counters, `here()`, `locate()` are emitted as Typst code, not evaluated in C# | `TypstDocument.Context(...)`; the compiler evaluates them |
| `plugin` needs a host loader | `TypstPlugin.Load` emits the call, but the world must be able to serve the `.wasm` asset | Register the module with `AddAsset` |
