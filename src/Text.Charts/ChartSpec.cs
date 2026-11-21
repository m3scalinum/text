using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Charts;

/// <summary>What a chart is: its kind, its labels, its series and the handful of switches the renderer honours.</summary>
/// <remarks>
/// <para>
/// The spec is mutable and not thread-safe, like the document model it feeds. Series and points are
/// drawn in the order they were added: the model never sorts, merges or re-samples.
/// </para>
/// <para>
/// What a spec cannot express is deliberate. There is no animation, no tooltip, no interaction, no
/// second y axis, no date axis and no per-point styling: see <c>docs/specifications/23-charts.md</c>
/// for the boundary and why it sits there.
/// </para>
/// </remarks>
public sealed class ChartSpec
{
    private readonly List<ChartSeries> _series = [];

    /// <summary>Creates an empty spec of the given kind.</summary>
    /// <param name="kind">The shape to draw.</param>
    public ChartSpec(ChartKind kind) => Kind = kind;

    /// <summary>The shape to draw.</summary>
    public ChartKind Kind { get; }

    /// <summary>The title, drawn above the plot; null for none.</summary>
    public string? Title { get; private set; }

    /// <summary>The x axis title, drawn below the tick labels; null for none.</summary>
    public string? XAxisTitle { get; private set; }

    /// <summary>The y axis title, drawn rotated beside the tick labels; null for none.</summary>
    public string? YAxisTitle { get; private set; }

    /// <summary>The font family the text elements name; null means <see cref="Chart.DefaultFontFamily"/>.</summary>
    public string? FontFamily { get; private set; }

    /// <summary>Whether the legend is drawn when there is more than one entry. Defaults to true.</summary>
    public bool ShowLegend { get; set; } = true;

    /// <summary>Whether the grid lines are drawn. Defaults to true; a sparkline never draws them.</summary>
    public bool ShowGrid { get; set; } = true;

    /// <summary>The series, in the order they were added.</summary>
    public IReadOnlyList<ChartSeries> Series => _series;

    /// <summary>Creates a line chart.</summary>
    public static ChartSpec Line() => new(ChartKind.Line);

    /// <summary>Creates an area chart.</summary>
    public static ChartSpec Area() => new(ChartKind.Area);

    /// <summary>Creates a chart of grouped bars.</summary>
    public static ChartSpec Bar() => new(ChartKind.Bar);

    /// <summary>Creates a chart of stacked bars.</summary>
    public static ChartSpec StackedBar() => new(ChartKind.StackedBar);

    /// <summary>Creates a scatter chart.</summary>
    public static ChartSpec Scatter() => new(ChartKind.Scatter);

    /// <summary>Creates a pie chart.</summary>
    public static ChartSpec Pie() => new(ChartKind.Pie);

    /// <summary>Creates a donut chart.</summary>
    public static ChartSpec Donut() => new(ChartKind.Donut);

    /// <summary>Creates a sparkline.</summary>
    public static ChartSpec Sparkline() => new(ChartKind.Sparkline);

    /// <summary>Sets the title.</summary>
    /// <param name="title">The title text; null or empty for none.</param>
    public ChartSpec WithTitle(string? title)
    {
        Title = string.IsNullOrEmpty(title) ? null : title;
        return this;
    }

    /// <summary>Sets the axis titles.</summary>
    /// <param name="x">The x axis title; null or empty for none.</param>
    /// <param name="y">The y axis title; null or empty for none.</param>
    public ChartSpec WithAxisTitles(string? x, string? y)
    {
        XAxisTitle = string.IsNullOrEmpty(x) ? null : x;
        YAxisTitle = string.IsNullOrEmpty(y) ? null : y;
        return this;
    }

    /// <summary>Sets the font family every <c>&lt;text&gt;</c> names.</summary>
    /// <param name="fontFamily">A CSS font family list; null restores the default.</param>
    /// <remarks>
    /// The renderer embeds no font: the SVG asks for a family by name and the viewer supplies it.
    /// </remarks>
    public ChartSpec WithFontFamily(string? fontFamily)
    {
        FontFamily = string.IsNullOrWhiteSpace(fontFamily) ? null : fontFamily;
        return this;
    }

    /// <summary>Adds a series, drawn after the ones already added.</summary>
    /// <param name="series">The series.</param>
    /// <exception cref="ArgumentNullException"><paramref name="series"/> is null.</exception>
    public ChartSpec AddSeries(ChartSeries series)
    {
        ArgumentNullException.ThrowIfNull(series);
        _series.Add(series);
        return this;
    }

    /// <summary>Adds a series from points.</summary>
    /// <param name="name">The name the legend shows.</param>
    /// <param name="points">The points, in the order they are drawn.</param>
    /// <param name="color">A CSS colour for the series; the palette is used when null.</param>
    public ChartSpec AddSeries(string name, IEnumerable<ChartPoint> points, string? color = null)
        => AddSeries(new ChartSeries(name, points, color));

    /// <summary>Checks what only the renderer can know: that a single-slice chart got one series, and no negative slice.</summary>
    internal void Validate()
    {
        if (Kind is ChartKind.Pie or ChartKind.Donut)
        {
            if (_series.Count > 1)
            {
                throw new InvalidOperationException(
                    $"a {Kind} chart draws one series of slices; {_series.Count} were added");
            }

            foreach (var series in _series)
            {
                foreach (var point in series.Points)
                {
                    if (point.Y < 0)
                    {
                        throw new InvalidOperationException(
                            $"a {Kind} chart cannot size a negative slice; series '{series.Name}' holds {point.Y}");
                    }
                }
            }
        }

        if (Kind is ChartKind.Sparkline && _series.Count > 1)
        {
            throw new InvalidOperationException(
                $"a sparkline draws one series; {_series.Count} were added");
        }
    }
}
