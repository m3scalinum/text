# Charts

`M3scalinum.Text.Charts` describes a chart as a typed model and renders it as SVG a report can
embed. No dependency, no native code, no font: the output is a string.

```csharp
using M3scalinum.Text.Charts;

var spec = ChartSpec.Bar()
    .WithTitle("Revenue by quarter")
    .WithAxisTitles("Quarter", "EUR thousand")
    .AddSeries("2025", [new ChartPoint(0, 40, "Q1"), new ChartPoint(0, 80, "Q2"), new ChartPoint(0, 60, "Q3")])
    .AddSeries("2026", [new ChartPoint(0, 50, "Q1"), new ChartPoint(0, 70, "Q2"), new ChartPoint(0, 110, "Q3")]);

string svg = Chart.RenderSvg(spec, new ChartSize(640, 360));
```

`RenderSvg(spec)` uses 640 × 360. The SVG carries a `viewBox`, so it scales to whatever box it is
placed in.

## The kinds

| Kind | Factory | Point placement |
| --- | --- | --- |
| `Line` | `ChartSpec.Line()` | `X` and `Y`, on a numeric x axis |
| `Area` | `ChartSpec.Area()` | `X` and `Y`, filled down to zero |
| `Bar` | `ChartSpec.Bar()` | ordinal slot, `Label` names the category; series sit side by side |
| `StackedBar` | `ChartSpec.StackedBar()` | ordinal slot, series stack from the baseline |
| `Scatter` | `ChartSpec.Scatter()` | `X` and `Y`, unconnected markers |
| `Pie` | `ChartSpec.Pie()` | one slice per point, sized against the series total |
| `Donut` | `ChartSpec.Donut()` | the same, with a hole |
| `Sparkline` | `ChartSpec.Sparkline()` | `X` and `Y`, no axes, no labels — sized for a table cell |

```csharp
ChartSpec.Sparkline()
    .AddSeries("walk", [new ChartPoint(1, 3), new ChartPoint(2, 5), new ChartPoint(3, 4)]);

Chart.RenderSvg(spec, ChartSize.Compact);   // 160 × 32
```

Series and points are drawn in the order they are added; nothing is sorted, merged or re-sampled. A
series takes the next colour of a fixed eight-colour palette unless it names one:
`.AddSeries("net", points, "#0f766e")` or `.WithColor("#0f766e")`.

## In a Typst report

`ToTypstImage` returns the Typst call that draws the chart, with the SVG already escaped:

```csharp
string image = Chart.ToTypstImage(spec, new ChartSize(640, 360));
```

```typst
#image(bytes("<svg …>"), format: "svg", width: 100%, alt: "Revenue by quarter")
```

The width is a plain Typst length — `100%`, `12cm`, `240pt` — or left out for `100%`. Give it the
document model's escape hatch and it is in the report:

```csharp
var document = TypstDocument.Create()
    .Heading(1, "Revenue")
    .Raw(Chart.ToTypstImage(spec, new ChartSize(640, 360)));

File.WriteAllBytes("report.pdf", TypstCompiler.CompileToPdf(document));
```

The chart travels inside the source: there is no asset to register, no path to keep in step.

## What you get

- A value axis whose ticks are nice numbers (1, 2, 2.5, 5, 10 × a power of ten), starting and ending
  on a tick, with `0` included for bars and areas.
- Labels thinned rather than overlapped, using an estimated 0.6 em per character.
- Numbers formatted through the invariant culture, at most three decimals for a coordinate.
- Byte-identical output for the same spec, so a report can be diffed and a golden test can mean
  something.
- Text as `<text>` naming the font family you asked for — `WithFontFamily("Inter, sans-serif")`.

## What you do not get

- **No animation, no interaction, no tooltips.** The output is a still image with no script in it.
- **No 3-D, no gradients, no shadows.** Flat kinds only.
- **No grammar of graphics.** No facets, no multiple scales, no statistical transforms. Eight shapes,
  one axis each.
- **No measurement.** Nothing is laid out against the real font metrics; a very wide face may clip a
  long label.
- **No font embedding.** The SVG asks for a family by name. Whatever draws it — a browser, Typst, a
  rasteriser — supplies the face.

## Degenerate input

An empty series renders the axes and no series at all; a single point renders a marker, because a
line through one point is invisible; a pie whose values are all zero renders an outline. A pie with
a negative slice, a pie with two series and a sparkline with two series are refused with an
`InvalidOperationException`, because none of the three has a meaning worth guessing at.

The full contract — the tick rule, the label rule, what the document contains, the invariants — is
in [docs/specifications/23-charts.md](https://github.com/m3scalinum/text/blob/main/docs/specifications/23-charts.md).
