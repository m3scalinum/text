using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Charts.Internal;

/// <summary>A numeric axis: its bounds, its step, and the ticks the renderer draws.</summary>
/// <remarks>
/// The rule is the usual "nice numbers" one, stated so it can be tested rather than promised:
/// the span is divided into <c>target</c> intervals, the raw interval is rounded up to the first of
/// 1, 2, 2.5, 5, 10 times a power of ten, and the bounds are widened outwards to the nearest
/// multiples of that interval. The axis therefore starts and ends on a tick, and every tick label is
/// an exact multiple of the step instead of an accumulated sum.
/// </remarks>
internal sealed class TickScale
{
    private TickScale(double min, double max, double step, IReadOnlyList<double> ticks)
    {
        Min = min;
        Max = max;
        Step = step;
        Ticks = ticks;
    }

    /// <summary>The lower bound of the axis.</summary>
    internal double Min { get; }

    /// <summary>The upper bound of the axis.</summary>
    internal double Max { get; }

    /// <summary>The distance between two ticks.</summary>
    internal double Step { get; }

    /// <summary>The tick values, ascending.</summary>
    internal IReadOnlyList<double> Ticks { get; }

    /// <summary>Builds the scale that covers <paramref name="min"/>..<paramref name="max"/> in about <paramref name="target"/> intervals.</summary>
    /// <param name="min">The smallest value the axis must show.</param>
    /// <param name="max">The largest value the axis must show.</param>
    /// <param name="target">The number of intervals to aim for.</param>
    /// <param name="extend">Whether the bounds are widened outwards to the outermost tick.</param>
    internal static TickScale Nice(double min, double max, int target, bool extend = true)
    {
        if (!(max > min))
        {
            max = min + 1;
        }

        var span = max - min;

        if (!double.IsFinite(span) || span <= 0)
        {
            return TwoTicks(min, max);
        }

        var raw = span / target;
        var exponent = Math.Floor(Math.Log10(raw));

        if (!double.IsFinite(exponent) || exponent is < -300 or > 300)
        {
            return TwoTicks(min, max);
        }

        var magnitude = Math.Pow(10, exponent);
        var normalized = raw / magnitude;

        var step = normalized <= 1 ? magnitude
            : normalized <= 2 ? 2 * magnitude
            : normalized <= 2.5 ? 2.5 * magnitude
            : normalized <= 5 ? 5 * magnitude
            : 10 * magnitude;

        var first = extend ? Math.Floor(min / step) * step : Math.Ceiling(min / step) * step;
        var last = extend ? Math.Ceiling(max / step) * step : Math.Floor(max / step) * step;

        if (!double.IsFinite(first) || !double.IsFinite(last))
        {
            return TwoTicks(min, max);
        }

        var count = Math.Round((last - first) / step) + 1;

        // A step that rounds badly, or a span wide enough to lose precision, is not worth hundreds of
        // labels: fall back to the two bounds rather than draw an unreadable axis. Fewer than two
        // ticks inside the range is just as unreadable, so the axis is widened instead.
        if (!double.IsFinite(count) || count > 200)
        {
            return TwoTicks(min, max);
        }

        if (count < 2)
        {
            return extend ? TwoTicks(min, max) : Nice(min, max, target, extend: true);
        }

        if (extend)
        {
            (min, max) = (first, last);
        }

        var ticks = new double[(int)count];

        for (var index = 0; index < ticks.Length; index++)
        {
            var value = first + (index * step);

            // 0.30000000000000004 and -0 are artefacts of the arithmetic, not of the data.
            ticks[index] = Math.Abs(value) < step * 1e-9 ? 0 : value;
        }

        return new TickScale(min, max, step, ticks);
    }

    private static TickScale TwoTicks(double min, double max)
        => new(min, max, max - min, [min, max]);
}
