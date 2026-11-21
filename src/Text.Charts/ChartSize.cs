using System;

namespace M3scalinum.Text.Charts;

/// <summary>The drawing surface of a chart, in SVG user units (one unit is one pixel).</summary>
/// <remarks>
/// The size fixes the <c>viewBox</c>, so the drawing scales with the box the SVG is placed in; it
/// does not fix the size on a page. <see cref="Chart.RenderSvg(ChartSpec)"/> uses
/// <see cref="Default"/>.
/// </remarks>
public readonly record struct ChartSize
{
    /// <summary>Creates a size.</summary>
    /// <param name="width">The width in user units.</param>
    /// <param name="height">The height in user units.</param>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is not a positive, finite number.</exception>
    public ChartSize(double width, double height)
    {
        if (!double.IsFinite(width) || width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "a chart width must be a positive, finite number");
        }

        if (!double.IsFinite(height) || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "a chart height must be a positive, finite number");
        }

        Width = width;
        Height = height;
    }

    /// <summary>The width in user units.</summary>
    public double Width { get; }

    /// <summary>The height in user units.</summary>
    public double Height { get; }

    /// <summary>640 × 360, the size a report figure usually wants.</summary>
    public static ChartSize Default => new(640, 360);

    /// <summary>160 × 32, the size of a sparkline that sits in a table cell.</summary>
    public static ChartSize Compact => new(160, 32);
}
