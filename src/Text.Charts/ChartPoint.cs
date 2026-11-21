using System;

namespace M3scalinum.Text.Charts;

/// <summary>One point of a <see cref="ChartSeries"/>: a position and, for a categorical chart, a name.</summary>
/// <remarks>
/// Both coordinates must be finite: a NaN or an infinity has no place on a scale, and rendering it
/// would put a deterministic but meaningless number in the SVG.
/// </remarks>
public readonly record struct ChartPoint
{
    /// <summary>Creates a point.</summary>
    /// <param name="x">The position on a numeric x axis; ignored by the categorical kinds.</param>
    /// <param name="y">The value the point is drawn at.</param>
    /// <param name="label">The category name a <see cref="ChartKind.Bar"/>, <see cref="ChartKind.StackedBar"/>, <see cref="ChartKind.Pie"/> or <see cref="ChartKind.Donut"/> point is labelled with.</param>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is not finite.</exception>
    public ChartPoint(double x, double y, string? label = null)
    {
        if (!double.IsFinite(x))
        {
            throw new ArgumentOutOfRangeException(nameof(x), x, "a point coordinate must be finite");
        }

        if (!double.IsFinite(y))
        {
            throw new ArgumentOutOfRangeException(nameof(y), y, "a point value must be finite");
        }

        X = x;
        Y = y;
        Label = label;
    }

    /// <summary>The position on a numeric x axis.</summary>
    public double X { get; }

    /// <summary>The value the point is drawn at.</summary>
    public double Y { get; }

    /// <summary>The category name, for the categorical kinds.</summary>
    public string? Label { get; }

    /// <summary>Creates a point of a numeric series.</summary>
    public static ChartPoint At(double x, double y) => new(x, y);

    /// <summary>Creates a point of a categorical series, named by <paramref name="label"/>.</summary>
    public static ChartPoint Named(string label, double value) => new(0, value, label);
}
