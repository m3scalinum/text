using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Charts;

/// <summary>A named series of points, drawn in one colour.</summary>
public sealed class ChartSeries
{
    private readonly List<ChartPoint> _points;

    /// <summary>Creates a series.</summary>
    /// <param name="name">The name the legend shows.</param>
    /// <param name="points">The points, in the order they are drawn.</param>
    /// <param name="color">A CSS colour for the series; the palette is used when null.</param>
    /// <exception cref="ArgumentNullException">A parameter is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty, or <paramref name="color"/> is empty.</exception>
    public ChartSeries(string name, IEnumerable<ChartPoint> points, string? color = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(points);

        if (color is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(color);
        }

        Name = name;
        Color = color;
        _points = [.. points];
    }

    /// <summary>The name the legend shows.</summary>
    public string Name { get; }

    /// <summary>The points, in the order they were given.</summary>
    public IReadOnlyList<ChartPoint> Points => _points;

    /// <summary>The CSS colour of the series, or null to take the next palette entry.</summary>
    public string? Color { get; }

    /// <summary>Returns the same series in another colour.</summary>
    /// <param name="color">A CSS colour.</param>
    public ChartSeries WithColor(string color)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(color);
        return new ChartSeries(Name, _points, color);
    }
}
