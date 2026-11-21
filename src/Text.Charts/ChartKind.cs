namespace M3scalinum.Text.Charts;

/// <summary>The shape a <see cref="ChartSpec"/> is drawn as.</summary>
/// <remarks>
/// <para>
/// The kinds split into two families. <see cref="Line"/>, <see cref="Area"/>, <see cref="Scatter"/>
/// and <see cref="Sparkline"/> place every point by its <see cref="ChartPoint.X"/> and
/// <see cref="ChartPoint.Y"/> on a numeric x axis. <see cref="Bar"/>, <see cref="StackedBar"/>,
/// <see cref="Pie"/> and <see cref="Donut"/> are categorical: a point takes the slot of its ordinal
/// position in its series and its <see cref="ChartPoint.Label"/> names that slot, so
/// <see cref="ChartPoint.X"/> is ignored.
/// </para>
/// </remarks>
public enum ChartKind
{
    /// <summary>A line through the points of each series.</summary>
    Line = 0,

    /// <summary>A line through the points of each series, filled down to the zero baseline.</summary>
    Area = 1,

    /// <summary>One bar per point, one bar per series inside each category slot.</summary>
    Bar = 2,

    /// <summary>One bar per category, with the series stacked on top of one another.</summary>
    StackedBar = 3,

    /// <summary>An unconnected marker per point.</summary>
    Scatter = 4,

    /// <summary>One slice per point, sized by its value against the series total.</summary>
    Pie = 5,

    /// <summary>A pie with a hole in the middle.</summary>
    Donut = 6,

    /// <summary>A bare line with no axes, no title and no legend, sized for a table cell.</summary>
    Sparkline = 7,
}
