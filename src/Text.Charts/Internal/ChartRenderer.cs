using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace M3scalinum.Text.Charts.Internal;

/// <summary>
/// Turns a <see cref="ChartSpec"/> into standalone SVG: layout, ticks, axes, series and legend, all
/// written straight into one string buffer in a fixed order.
/// </summary>
/// <remarks>
/// The renderer holds no state between calls, formats every number through
/// <see cref="SvgText"/>, and iterates the spec's collections in the order they were given, so the
/// same spec always produces the same bytes. Nothing here reads the clock, the culture, the file
/// system or a font.
/// </remarks>
internal sealed partial class ChartRenderer
{
    private const double FontSize = 11;
    private const double AxisTitleFontSize = 12;
    private const double TitleFontSize = 15;
    private const double LegendFontSize = 10;
    private const double LabelGap = 6;
    private const double TickLength = 4;
    private const double AxisTitleSpace = 16;
    private const double LabelRowHeight = 18;
    private const double LegendRowHeight = 20;
    private const double Padding = 12;
    private const double MarkerRadius = 3;
    private const double StrokeWidth = 2;

    private const string Ink = "#111827";
    private const string MutedInk = "#4b5563";
    private const string AxisInk = "#374151";
    private const string GridInk = "#e5e7eb";

    /// <summary>The colours a series takes when it names none, in order, cycling past the eighth.</summary>
    private static readonly string[] Palette =
    [
        "#2563eb",
        "#dc2626",
        "#16a34a",
        "#d97706",
        "#7c3aed",
        "#0891b2",
        "#db2777",
        "#4b5563",
    ];

    private readonly ChartSpec _spec;
    private readonly ChartSize _size;
    private readonly StringBuilder _svg = new(4096);

    private ChartRenderer(ChartSpec spec, ChartSize size)
    {
        _spec = spec;
        _size = size;
    }

    private ChartKind Kind => _spec.Kind;

    /// <summary>Renders the spec at the size, after checking what only the renderer knows.</summary>
    internal static string Render(ChartSpec spec, ChartSize size)
    {
        ArgumentNullException.ThrowIfNull(spec);
        spec.Validate();

        return new ChartRenderer(spec, size).Build();
    }

    private string Build()
    {
        WriteOpen();

        switch (Kind)
        {
            case ChartKind.Sparkline:
                DrawSparkline();
                break;
            case ChartKind.Pie:
            case ChartKind.Donut:
                DrawCategorical();
                break;
            default:
                DrawCartesian();
                break;
        }

        _svg.Append("</svg>\n");

        return _svg.ToString();
    }

    // ---------------------------------------------------------------- document and frame

    private void WriteOpen()
    {
        var width = SvgText.Number(_size.Width);
        var height = SvgText.Number(_size.Height);
        var family = _spec.FontFamily ?? Chart.DefaultFontFamily;

        _svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(width)
            .Append("\" height=\"").Append(height)
            .Append("\" viewBox=\"0 0 ").Append(width).Append(' ').Append(height)
            .Append("\" preserveAspectRatio=\"xMidYMid meet\" role=\"img\"");

        if (_spec.Title is not null)
        {
            _svg.Append(" aria-label=\"").Append(SvgText.Escape(_spec.Title)).Append('"');
        }

        _svg.Append(" font-family=\"").Append(SvgText.Escape(family)).Append("\">\n");

        if (_spec.Title is not null)
        {
            _svg.Append("<title>").Append(SvgText.Escape(_spec.Title)).Append("</title>\n");
            WriteText(
                _spec.Title,
                _size.Width / 2,
                Padding + TitleFontSize,
                TitleFontSize,
                Ink,
                "middle",
                bold: true);
        }
    }

    private void DrawCartesian()
    {
        var entries = LegendEntries();
        var legendHeight = _spec.ShowLegend && entries.Count > 1 ? LegendRowHeight : 0;
        var titleHeight = _spec.Title is null ? 0 : TitleFontSize + 9;

        var (xMin, xMax) = Kind is ChartKind.Bar or ChartKind.StackedBar
            ? (0d, 1d)
            : PaddedXRange();

        var yRange = ValueRange();
        var yScale = TickScale.Nice(yRange.Min, yRange.Max, 5);
        var xScale = Kind is ChartKind.Bar or ChartKind.StackedBar
            ? TickScale.Nice(xMin, xMax, 5)
            : TickScale.Nice(xMin, xMax, 6, extend: false);

        var yLabels = yScale.Ticks.Select(tick => SvgText.TickLabel(tick, yScale.Step)).ToArray();
        var labelWidth = yLabels.Length == 0 ? 0 : yLabels.Max(text => SvgText.EstimateWidth(text, FontSize));

        // No font is measured, so the margin a label needs cannot be known: it is estimated, and
        // capped, so a long label squeezes the plot instead of erasing it.
        var left = Math.Min(Padding + labelWidth + LabelGap + (_spec.YAxisTitle is null ? 0 : AxisTitleSpace), _size.Width * 0.45);
        var bottom = Math.Min(
            Padding + LabelRowHeight + (_spec.XAxisTitle is null ? 0 : AxisTitleSpace) + legendHeight,
            _size.Height * 0.6);

        var plot = new ChartPlot(
            left,
            Padding + titleHeight,
            _size.Width - left - Padding - 4,
            _size.Height - Padding - titleHeight - bottom,
            xScale,
            yScale);

        if (_spec.ShowGrid)
        {
            DrawGrid(plot);
        }

        DrawAxes(plot);

        switch (Kind)
        {
            case ChartKind.Line:
                DrawLines(plot);
                break;
            case ChartKind.Area:
                DrawAreas(plot);
                break;
            case ChartKind.Scatter:
                DrawScatter(plot);
                break;
            case ChartKind.StackedBar:
                DrawStackedBars(plot);
                break;
            default:
                DrawGroupedBars(plot);
                break;
        }

        DrawLegend(entries, plot.Left);
    }

    // ---------------------------------------------------------------- scales

    /// <summary>The y values the axis must cover, with zero included for the kinds that sit on a baseline.</summary>
    private (double Min, double Max) ValueRange()
    {
        if (Kind == ChartKind.StackedBar)
        {
            var count = CategoryCount();
            var minimum = 0d;
            var maximum = 0d;

            for (var category = 0; category < count; category++)
            {
                var up = 0d;
                var down = 0d;

                foreach (var series in _spec.Series)
                {
                    if (category >= series.Points.Count)
                    {
                        continue;
                    }

                    var value = series.Points[category].Y;

                    if (value >= 0)
                    {
                        up += value;
                    }
                    else
                    {
                        down += value;
                    }
                }

                minimum = Math.Min(minimum, down);
                maximum = Math.Max(maximum, up);
            }

            return Widen(minimum, maximum);
        }

        var includesZero = Kind is ChartKind.Area or ChartKind.Bar;
        var (min, max) = DataRange(point => point.Y, (0d, 1d));

        if (includesZero)
        {
            min = Math.Min(min, 0);
            max = Math.Max(max, 0);
        }

        return Widen(min, max);
    }

    /// <summary>The x range, padded so that the outermost points are not drawn on the axis line.</summary>
    private (double Min, double Max) PaddedXRange()
    {
        var xs = _spec.Series.SelectMany(series => series.Points).Select(point => point.X).ToArray();

        if (xs.Length == 0)
        {
            // Nothing to show: the axis is 0 to 1, the same one an empty y axis gets.
            return (0, 1);
        }

        var min = xs.Min();
        var max = xs.Max();
        var span = max - min;
        var pad = span > 0 ? span * 0.02 : Math.Abs(min) > 0 ? Math.Abs(min) * 0.05 : 1;

        return (min - pad, max + pad);
    }

    private (double Min, double Max) DataRange(Func<ChartPoint, double> project, (double Min, double Max) empty)
    {
        var values = _spec.Series.SelectMany(series => series.Points).Select(project).ToArray();

        return values.Length == 0 ? empty : (values.Min(), values.Max());
    }

    /// <summary>Widens a flat range so that a single value still has a scale to sit on.</summary>
    private static (double Min, double Max) Widen(double min, double max)
    {
        if (max > min)
        {
            return (min, max);
        }

        var pad = Math.Abs(min) > 0 ? Math.Abs(min) / 2 : 1;

        return double.IsFinite(pad) ? (min - pad, max + pad) : (min, max);
    }

    private int CategoryCount()
        => _spec.Series.Count == 0 ? 0 : _spec.Series.Max(series => series.Points.Count);

    private static double SlotCenter(ChartPlot plot, int index, int count)
        => plot.Left + (plot.Width * (index + 0.5) / count);

    // ---------------------------------------------------------------- axes

    private void DrawGrid(ChartPlot plot)
    {
        foreach (var tick in plot.YScale.Ticks)
        {
            var y = plot.Y(tick);

            // The grid line on the baseline would be drawn twice, once as a grid line and once as
            // the axis; the axis wins.
            if (Math.Abs(y - plot.Baseline) < 0.5)
            {
                continue;
            }

            _svg.Append("<line x1=\"").Append(SvgText.Number(plot.Left))
                .Append("\" y1=\"").Append(SvgText.Number(y))
                .Append("\" x2=\"").Append(SvgText.Number(plot.Right))
                .Append("\" y2=\"").Append(SvgText.Number(y))
                .Append("\" stroke=\"").Append(GridInk).Append("\" stroke-width=\"1\"/>\n");
        }

        if (Kind is ChartKind.Bar or ChartKind.StackedBar)
        {
            return;
        }

        foreach (var tick in plot.XScale.Ticks)
        {
            var x = plot.X(tick);

            if (Math.Abs(x - plot.Left) < 0.5)
            {
                continue;
            }

            _svg.Append("<line x1=\"").Append(SvgText.Number(x))
                .Append("\" y1=\"").Append(SvgText.Number(plot.Top))
                .Append("\" x2=\"").Append(SvgText.Number(x))
                .Append("\" y2=\"").Append(SvgText.Number(plot.Bottom))
                .Append("\" stroke=\"").Append(GridInk).Append("\" stroke-width=\"1\"/>\n");
        }
    }

    private void DrawAxes(ChartPlot plot)
    {
        // The y axis is drawn at the left edge; the x axis at zero when zero is on the scale, at the
        // bottom otherwise, so a negative range still has a visible baseline.
        _svg.Append("<line x1=\"").Append(SvgText.Number(plot.Left))
            .Append("\" y1=\"").Append(SvgText.Number(plot.Top))
            .Append("\" x2=\"").Append(SvgText.Number(plot.Left))
            .Append("\" y2=\"").Append(SvgText.Number(plot.Bottom))
            .Append("\" stroke=\"").Append(AxisInk).Append("\" stroke-width=\"1\"/>\n");
        _svg.Append("<line x1=\"").Append(SvgText.Number(plot.Left))
            .Append("\" y1=\"").Append(SvgText.Number(plot.Baseline))
            .Append("\" x2=\"").Append(SvgText.Number(plot.Right))
            .Append("\" y2=\"").Append(SvgText.Number(plot.Baseline))
            .Append("\" stroke=\"").Append(AxisInk).Append("\" stroke-width=\"1\"/>\n");

        DrawValueTicks(plot);

        if (Kind is ChartKind.Bar or ChartKind.StackedBar)
        {
            DrawCategoryTicks(plot);
        }
        else
        {
            DrawNumericTicks(plot);
        }

        DrawAxisTitles(plot);
    }

    private void DrawValueTicks(ChartPlot plot)
    {
        var previous = double.PositiveInfinity;

        foreach (var tick in plot.YScale.Ticks)
        {
            var y = plot.Y(tick);
            var text = SvgText.TickLabel(tick, plot.YScale.Step);

            _svg.Append("<line x1=\"").Append(SvgText.Number(plot.Left - TickLength))
                .Append("\" y1=\"").Append(SvgText.Number(y))
                .Append("\" x2=\"").Append(SvgText.Number(plot.Left))
                .Append("\" y2=\"").Append(SvgText.Number(y))
                .Append("\" stroke=\"").Append(AxisInk).Append("\" stroke-width=\"1\"/>\n");

            // Ticks ascend in value and therefore descend on the screen: a label is drawn only when
            // the one below it is at least a line of text away, the same rule the x axis uses.
            if (previous - y < FontSize + 2)
            {
                continue;
            }

            previous = y;
            WriteText(text, plot.Left - LabelGap, y + 4, FontSize, MutedInk, "end");
        }
    }

    private void DrawNumericTicks(ChartPlot plot)
    {
        var labels = plot.XScale.Ticks
            .Select(tick => (Position: plot.X(tick), Text: SvgText.TickLabel(tick, plot.XScale.Step)))
            .ToArray();

        DrawCategoryAxis(labels, plot.Bottom, plot);
    }

    private void DrawCategoryTicks(ChartPlot plot)
    {
        var count = CategoryCount();

        if (count == 0)
        {
            return;
        }

        var labels = new (double Position, string Text)[count];

        for (var index = 0; index < count; index++)
        {
            labels[index] = (SlotCenter(plot, index, count), CategoryLabel(index));
        }

        DrawCategoryAxis(labels, plot.Bottom, plot);
    }

    /// <summary>Draws tick marks for every label and thins the labels themselves so none overlaps.</summary>
    private void DrawCategoryAxis((double Position, string Text)[] labels, double y, ChartPlot plot)
    {
        var lastRight = double.NegativeInfinity;

        foreach (var (position, text) in labels)
        {
            _svg.Append("<line x1=\"").Append(SvgText.Number(position))
                .Append("\" y1=\"").Append(SvgText.Number(y))
                .Append("\" x2=\"").Append(SvgText.Number(position))
                .Append("\" y2=\"").Append(SvgText.Number(y + TickLength))
                .Append("\" stroke=\"").Append(AxisInk).Append("\" stroke-width=\"1\"/>\n");

            // The documented rule: a label is drawn only when its estimated box, plus four units of
            // air, clears the previous one; otherwise it is dropped and the tick mark stands alone.
            var width = SvgText.EstimateWidth(text, FontSize);

            if (position - (width / 2) < lastRight + 4)
            {
                continue;
            }

            lastRight = position + (width / 2);
            WriteText(text, position, plot.Bottom + FontSize + 4, FontSize, MutedInk, "middle");
        }
    }

    private string CategoryLabel(int index)
    {
        foreach (var series in _spec.Series)
        {
            if (index < series.Points.Count && series.Points[index].Label is { } label)
            {
                return label;
            }
        }

        return (index + 1).ToString(CultureInfo.InvariantCulture);
    }

    private void DrawAxisTitles(ChartPlot plot)
    {
        if (_spec.XAxisTitle is not null)
        {
            var baseline = plot.Bottom + FontSize + 4 + AxisTitleSpace;

            WriteText(_spec.XAxisTitle, plot.CenterX, baseline, AxisTitleFontSize, MutedInk, "middle");
        }

        if (_spec.YAxisTitle is null)
        {
            return;
        }

        var x = Padding + 5;
        var y = plot.CenterY;

        _svg.Append("<text x=\"").Append(SvgText.Number(x))
            .Append("\" y=\"").Append(SvgText.Number(y))
            .Append("\" transform=\"rotate(-90 ").Append(SvgText.Number(x)).Append(' ').Append(SvgText.Number(y))
            .Append(")\" font-size=\"").Append(SvgText.Number(AxisTitleFontSize))
            .Append("\" fill=\"").Append(MutedInk).Append("\" text-anchor=\"middle\">")
            .Append(SvgText.Escape(_spec.YAxisTitle)).Append("</text>\n");
    }

    // ---------------------------------------------------------------- legend and text

    private List<(string Name, string Color)> LegendEntries()
    {
        var entries = new List<(string Name, string Color)>();

        if (Kind is ChartKind.Pie or ChartKind.Donut)
        {
            if (_spec.Series.Count == 0)
            {
                return entries;
            }

            var points = _spec.Series[0].Points;

            for (var index = 0; index < points.Count; index++)
            {
                entries.Add((points[index].Label ?? (index + 1).ToString(CultureInfo.InvariantCulture), Palette[index % Palette.Length]));
            }

            return entries;
        }

        for (var index = 0; index < _spec.Series.Count; index++)
        {
            if (_spec.Series[index].Points.Count > 0)
            {
                entries.Add((_spec.Series[index].Name, ColorOf(_spec.Series[index], index)));
            }
        }

        return entries;
    }

    private void DrawLegend(List<(string Name, string Color)> entries, double left)
    {
        if (!_spec.ShowLegend || entries.Count <= 1)
        {
            return;
        }

        var baseline = _size.Height - 6;
        var x = left;

        foreach (var (name, color) in entries)
        {
            _svg.Append("<rect x=\"").Append(SvgText.Number(x))
                .Append("\" y=\"").Append(SvgText.Number(baseline - 9))
                .Append("\" width=\"10\" height=\"10\" fill=\"").Append(color).Append("\"/>\n");
            WriteText(name, x + 14, baseline, LegendFontSize, MutedInk, "start");
            x += 14 + SvgText.EstimateWidth(name, LegendFontSize) + 16;
        }
    }

    private static string ColorOf(ChartSeries series, int index)
        => series.Color ?? Palette[index % Palette.Length];

    private void WriteText(
        string text,
        double x,
        double y,
        double fontSize,
        string fill,
        string anchor,
        bool bold = false)
    {
        _svg.Append("<text x=\"").Append(SvgText.Number(x))
            .Append("\" y=\"").Append(SvgText.Number(y))
            .Append("\" font-size=\"").Append(SvgText.Number(fontSize))
            .Append('"');

        if (bold)
        {
            _svg.Append(" font-weight=\"bold\"");
        }

        _svg.Append(" fill=\"").Append(fill)
            .Append("\" text-anchor=\"").Append(anchor).Append("\">")
            .Append(SvgText.Escape(text)).Append("</text>\n");
    }
}
