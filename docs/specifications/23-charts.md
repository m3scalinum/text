# 23 — Charts

`M3scalinum.Text.Charts` turns a typed description of a chart into standalone SVG, and into the
Typst markup that embeds it. It is the smallest package in the suite: it holds no font, no renderer,
no image codec and no third-party reference. It writes a string.

## 1. Scope

```csharp
using M3scalinum.Text.Charts;

var spec = ChartSpec.Line()
    .WithTitle("Revenue")
    .WithAxisTitles("Month", "EUR thousand")
    .AddSeries("2025", [new ChartPoint(1, 10), new ChartPoint(2, 14), new ChartPoint(3, 12)])
    .AddSeries("2026", [new ChartPoint(1, 12), new ChartPoint(2, 15), new ChartPoint(3, 19)]);

string svg = Chart.RenderSvg(spec, new ChartSize(640, 360));
string typst = Chart.ToTypstImage(spec, new ChartSize(640, 360));
```

**In scope.** A chart kind, a title, two axis titles, a font family name, a palette, a legend, grid
lines, and the series with their points. Eight kinds: `Line`, `Area`, `Bar` (grouped), `StackedBar`,
`Scatter`, `Pie`, `Donut`, `Sparkline`.

**Out of scope, deliberately.**

| Not done | Why |
| --- | --- |
| Animation, transition, interaction | An SVG in a report is a still image. Anything that moves belongs to the presentation layer that shows the report, not to the document that carries it. |
| Tooltips, hover, click targets | A tooltip is a script and a DOM; the output is one `<svg>` element with no script in it, and adding one would make every consumer a security decision. |
| 3-D, perspective, shadows, gradients | A third dimension on a flat page costs accuracy and gains nothing; the flat kinds are the ones that read. |
| A grammar of graphics | No scales, no facets, no aesthetics, no layers, no statistical transforms. The model covers the eight shapes a report actually embeds; anything else is a table, or code that emits its own SVG. |
| A second y axis, a date axis, annotations, error bars | Each one doubles the layout rule and serves a minority of reports. |
| A layout engine | The renderer measures nothing. It estimates a text width from a character count, and the specification says so instead of promising pixel accuracy. |

## 2. Why SVG, and why no dependency

**Decision — emit SVG rather than raster.** SVG is text: the package needs no image codec, no font
engine, no native library, and no compression. A chart is a `StringBuilder` and arithmetic, so the
package has zero package references, is `IsAotCompatible`, and adds nothing to a consumer's
transitive graph or to its published AOT image.

**Consequence — text is a request, not a promise.** Every label is a `<text>` element naming a font
family. Nothing is embedded, nothing is measured, and nothing is converted to outlines. A consumer
with the family draws it; a viewer without it substitutes. That is what the caller names
`WithFontFamily` for, and why the default is `sans-serif`.

**Decision — no `CultureInfo.GetCultureInfo`, invariant formatting everywhere.** The suite sets
`InvariantGlobalization=true`, so a named culture is not available at all. Every number is formatted
through `CultureInfo.InvariantCulture` explicitly: coordinates at most three decimals, tick labels
with the decimals their step needs.

## 3. The model

| Type | Holds | Notes |
| --- | --- | --- |
| `ChartSpec` | Kind, title, axis titles, font family, `ShowLegend`, `ShowGrid`, series | Mutable and not thread-safe, like `TypstDocument`. Static factories: `Line()`, `Area()`, `Bar()`, `StackedBar()`, `Scatter()`, `Pie()`, `Donut()`, `Sparkline()`. |
| `ChartSeries` | Name, points, optional CSS colour | Order is the order given: the renderer never sorts, merges or re-samples. |
| `ChartPoint` | `X`, `Y`, optional `Label` | Both coordinates must be finite; a NaN throws at construction, not at render time. |
| `ChartSize` | Width and height in SVG user units | Positive and finite. `Default` is 640 × 360; `Compact` is 160 × 32, the size a sparkline wants. |
| `ChartKind` | The shape | Line, Area, Bar, StackedBar, Scatter, Pie, Donut, Sparkline. |

The kinds split in two families.

- **Numeric** (`Line`, `Area`, `Scatter`, `Sparkline`): a point is placed by its `X` and `Y`. The x
  axis spans the data, padded by 2 % of its span so the outermost points are not drawn on the axis
  line, and labelled only at the nice ticks inside that span.
- **Categorical** (`Bar`, `StackedBar`, `Pie`, `Donut`): a point takes the slot of its ordinal
  position in its series, and `Label` names that slot. `X` is ignored. A slot the first series does
  not label falls back to the 1-based index.

## 4. The tick rule

Stated so that it is testable, and so a reader knows what a "sane algorithm" means here.

1. Divide the span by the target interval count — five for a value axis, six for an x axis.
2. Round the raw interval up to the first of 1, 2, 2.5, 5, 10 times a power of ten.
3. The ticks are the multiples of that interval on the axis. A value axis is widened outwards to the
   outermost tick, so it starts and ends on one; the x axis of a points chart is not, so it ends
   where the data does and the ticks sit inside it.
4. At most 200 ticks. A span wide enough to lose precision, or one whose span overflows a double,
   falls back to the two bounds.
5. A flat range is widened: one unit when the value is zero, half its magnitude otherwise.

Tick labels: `0` for zero, a plain decimal with the decimals the step needs (trailing zeros removed)
while the magnitude is between `1e-4` and `1e7`, and `0.###E+0` outside that. So a step of 2.5
labels `10`, `12.5`, `15`; a range of 1.2 to 1.9 tera labels `1.2E+12` … `2E+12`.

## 5. No overlapping labels

**The rule, documented rather than promised.** No font is measured, so every label's width is
estimated at 0.6 em per character. A label is drawn only if its estimated box, plus four units of
air, clears the label drawn before it; otherwise it is dropped and the tick mark stands alone. The
same rule thins the y axis when a chart is too short for five lines of text.

Consequences a caller can rely on: labels never collide under the estimate; the estimate is wrong
for a wide face or a script with wide glyphs; the first and last label of a categorical axis may
overhang the plot area into the margin.

## 6. Determinism

The same spec at the same size renders the same bytes, for every kind. That is asserted, not
aspired to:

- every number goes through one formatter, which builds its text with the invariant culture;
- every collection is iterated in the order it was given — series, points, palette, legend;
- nothing reads the clock, the culture, the file system, a font, or a hash-ordered container;
- the palette is a fixed array of eight colours, cycling past the eighth;
- a series that names no colour takes `series[index % 8]`.

## 7. What a rendering contains

One `<svg>` element with `width`, `height`, `viewBox="0 0 W H"`, `preserveAspectRatio="xMidYMid
meet"`, `role="img"`, an `aria-label` when there is a title, and the named `font-family`. Then, in
document order:

| Part | Emitted as |
| --- | --- |
| Title | `<title>` and a bold `<text>` |
| Grid | one `<line>` per tick, skipped where it would coincide with an axis |
| Axes | the y axis at the left edge, the x axis at zero when zero is on the scale and at the bottom otherwise |
| Ticks | a `<line>` mark per tick, a `<text>` label where the thinning rule allows |
| Series | `<path>` for line, area and sparkline; `<rect>` per bar; `<circle>` per scatter point; `<path>` per slice |
| Legend | a 10 × 10 `<rect>` and a `<text>` per series, drawn when there is more than one entry |

No `<image>`, no `href`, no `xlink:`, no `<style>`, no `<script>`, no `url(…)`, no `@import`: the
document refers to nothing outside itself. A test asserts each of those absences for every kind.

Degenerate cases, each one a test:

| Case | Result |
| --- | --- |
| No series, or a series with no points | Axes with the ticks of 0 to 1, and no series element at all |
| One point in a numeric series | A marker, because a one-command path draws nothing |
| A pie whose values are all zero | An outline circle; no division by zero |
| A negative value on a bar chart | Zero is included in the range, and the bar hangs below the baseline |
| Stacked bars with negative values | Positive and negative stacks grow away from the baseline independently |

## 8. Embedding in Typst

`Chart.ToTypstImage(spec, size, width)` returns one line of Typst source:

```typst
#image(bytes("<svg …>"), format: "svg", width: 100%, alt: "Revenue")
```

**Decision — embed the bytes rather than write an asset.** A `<path>` to an asset means the caller
must register it with the world builder, keep the path in step with the document, and explain the
pairing to every template. Bytes make the document self-contained: one chart, one source file, no
second artifact that can go missing.

**Decision — do the escaping once, here.** `TypstText` escapes the backslash, the double quote and
the whitespace out of the SVG, so the markup is one line and nothing else needs escaping. A caller
never concatenates an SVG into Typst markup. `image(…)` is the same node the document model's figure
emits, so the result drops into a template or a `Raw` block unchanged.

The width is validated as a plain Typst length (a number and an optional `pt`, `mm`, `cm`, `in`,
`em`, `rem` or `%`); anything else — `auto`, `1fr`, a value carrying a `)` — is refused with an
`ArgumentException`, because the markup is built by concatenation and a length is the one place a
caller could inject into it.

The `alt` text is the chart title, and is omitted when there is none.

## 9. Invariants

- **MUST** render the same bytes for the same spec and size.
- **MUST NOT** contain a reference to anything outside the document.
- **MUST** format every number through the invariant culture.
- **MUST NOT** add a package reference: `dotnet pack` produces empty dependency groups.
- **MUST** render axes and no series element for an empty series rather than throw.
- **MUST** throw `InvalidOperationException` for a pie or donut with more than one series, a
  negative slice, or a sparkline with more than one series.
- **MUST** produce well-formed XML for every kind, including the degenerate ones.

## 10. Tests

`tests/Text.Charts.Tests` holds a golden-shaped assertion per kind — structure, the tick labels the
rule produces, and a handful of coordinates — rather than a hash of the file, which would fail on a
rounding change and explain nothing. Beyond those: a pie whose slices do not sum to 100, a pie with
one slice, an empty pie, a single-point series, an empty series, a negative bar, a huge range, a tiny
range, XML well-formedness for every kind, the absence of any external reference, the label-thinning
rule, the tick rule and the number formatting on their own, and two renders compared byte for byte.
