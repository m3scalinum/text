using System;
using System.Collections.Generic;
using System.Linq;

namespace M3scalinum.Text.Charts.Internal;

/// <summary>The series drawing for the kinds that do not sit on a frame: pie, donut and sparkline.</summary>
internal sealed partial class ChartRenderer
{
    /// <summary>The share of the radius a donut leaves as a hole.</summary>
    private const double DonutHole = 0.55;

    private void DrawCategorical()
    {
        var entries = LegendEntries();
        var legendHeight = _spec.ShowLegend && entries.Count > 1 ? LegendRowHeight : 0;
        var titleHeight = _spec.Title is null ? 0 : TitleFontSize + 9;

        var plot = new ChartPlot(
            Padding,
            Padding + titleHeight,
            _size.Width - (2 * Padding),
            _size.Height - Padding - titleHeight - Padding - legendHeight,
            TickScale.Nice(0, 1, 2),
            TickScale.Nice(0, 1, 2));

        var radius = Math.Min(plot.Width, plot.Height) / 2 * 0.95;
        var centerX = plot.CenterX;
        var centerY = plot.CenterY;
        var points = _spec.Series.Count == 0 ? [] : _spec.Series[0].Points;
        var total = points.Sum(point => point.Y);
        var donut = Kind == ChartKind.Donut;

        // A pie whose values are all zero has no angle to give any slice: an outline stands in for it,
        // rather than a division by zero or an exception a caller would have to special-case.
        if (total <= 0)
        {
            _svg.Append("<circle cx=\"").Append(SvgText.Number(centerX))
                .Append("\" cy=\"").Append(SvgText.Number(centerY))
                .Append("\" r=\"").Append(SvgText.Number(radius))
                .Append("\" fill=\"none\" stroke=\"").Append(GridInk)
                .Append("\" stroke-width=\"").Append(SvgText.Number(StrokeWidth)).Append("\"/>\n");
            DrawLegend(entries, plot.Left);

            return;
        }

        var inner = radius * DonutHole;
        var angle = -90d;

        for (var index = 0; index < points.Count; index++)
        {
            var fraction = points[index].Y / total;

            if (fraction <= 0)
            {
                continue;
            }

            var end = angle + (fraction * 360);
            var color = Palette[index % Palette.Length];

            if (fraction >= 0.9999)
            {
                WriteDisc(centerX, centerY, radius, donut ? inner : null, color);
            }
            else
            {
                WriteSlice(centerX, centerY, radius, donut ? inner : null, angle, end, color);
            }

            angle = end;
        }

        DrawLegend(entries, plot.Left);
    }

    private void WriteSlice(
        double centerX,
        double centerY,
        double radius,
        double? inner,
        double start,
        double end,
        string color)
    {
        var path = new System.Text.StringBuilder(128);
        var outerStart = PointOnCircle(centerX, centerY, radius, start);
        var outerEnd = PointOnCircle(centerX, centerY, radius, end);
        var sweep = end - start;

        if (inner is not { } hole)
        {
            // A wedge: from the centre out, along the arc, and back to the centre.
            path.Append("M ").Append(SvgText.Number(centerX)).Append(' ').Append(SvgText.Number(centerY));
            AppendLine(path, outerStart);
            AppendArc(path, radius, outerEnd, sweep, clockwise: true);
            path.Append(" Z");

            WritePath(path, color, evenOdd: false);
            return;
        }

        // An annular sector: out along the outer arc, in along the radius that ends it, back along
        // the inner arc, and out again to close. The hole is part of the slice rather than a second
        // subpath — a circle subtracted per slice would let the neighbouring slices show through it.
        var innerEnd = PointOnCircle(centerX, centerY, hole, end);
        var innerStart = PointOnCircle(centerX, centerY, hole, start);

        path.Append("M ").Append(SvgText.Number(outerStart.X)).Append(' ').Append(SvgText.Number(outerStart.Y));
        AppendArc(path, radius, outerEnd, sweep, clockwise: true);
        AppendLine(path, innerEnd);
        AppendArc(path, hole, innerStart, sweep, clockwise: false);
        path.Append(" Z");

        WritePath(path, color, evenOdd: false);
    }

    private void WriteDisc(double centerX, double centerY, double radius, double? inner, string color)
    {
        var path = new System.Text.StringBuilder(128);

        AppendCircle(path, centerX, centerY, radius);

        if (inner is { } hole)
        {
            // A whole circle inside a whole circle: the even-odd rule turns the smaller one into a hole.
            AppendCircle(path, centerX, centerY, hole);
        }

        WritePath(path, color, evenOdd: inner is not null);
    }

    private void WritePath(System.Text.StringBuilder path, string color, bool evenOdd)
    {
        _svg.Append("<path d=\"").Append(path).Append("\" fill=\"").Append(color).Append('"');

        if (evenOdd)
        {
            _svg.Append(" fill-rule=\"evenodd\"");
        }

        _svg.Append("/>\n");
    }

    private static (double X, double Y) PointOnCircle(double centerX, double centerY, double radius, double degrees)
        => (centerX + (radius * Math.Cos(Radians(degrees))), centerY + (radius * Math.Sin(Radians(degrees))));

    private static void AppendLine(System.Text.StringBuilder path, (double X, double Y) to)
        => path.Append(" L ").Append(SvgText.Number(to.X)).Append(' ').Append(SvgText.Number(to.Y));

    private static void AppendArc(
        System.Text.StringBuilder path,
        double radius,
        (double X, double Y) to,
        double sweep,
        bool clockwise)
    {
        path.Append(" A ").Append(SvgText.Number(radius)).Append(' ').Append(SvgText.Number(radius))
            .Append(" 0 ").Append(sweep > 180 ? "1" : "0").Append(clockwise ? " 1 " : " 0 ")
            .Append(SvgText.Number(to.X)).Append(' ').Append(SvgText.Number(to.Y));
    }

    private static void AppendCircle(System.Text.StringBuilder path, double centerX, double centerY, double radius)
    {
        path.Append("M ").Append(SvgText.Number(centerX - radius)).Append(' ').Append(SvgText.Number(centerY))
            .Append(" A ").Append(SvgText.Number(radius)).Append(' ').Append(SvgText.Number(radius))
            .Append(" 0 1 0 ").Append(SvgText.Number(centerX + radius)).Append(' ').Append(SvgText.Number(centerY))
            .Append(" A ").Append(SvgText.Number(radius)).Append(' ').Append(SvgText.Number(radius))
            .Append(" 0 1 0 ").Append(SvgText.Number(centerX - radius)).Append(' ').Append(SvgText.Number(centerY))
            .Append(" Z");
    }

    /// <summary>Degrees to radians, for the circle geometry of a slice.</summary>
    private static double Radians(double degrees) => degrees * Math.PI / 180;

    private void DrawSparkline()
    {
        var series = _spec.Series.Count == 0 ? null : _spec.Series[0];

        if (series is null || series.Points.Count == 0)
        {
            return;
        }

        var (xMin, xMax) = Widen(series.Points.Min(point => point.X), series.Points.Max(point => point.X));
        var (yMin, yMax) = Widen(series.Points.Min(point => point.Y), series.Points.Max(point => point.Y));

        // One unit of padding, so the stroke of the outermost point is not clipped by the viewBox.
        var plot = new ChartPlot(
            1,
            1,
            _size.Width - 2,
            _size.Height - 2,
            TickScale.Nice(xMin, xMax, 4),
            TickScale.Nice(yMin, yMax, 4));

        var color = ColorOf(series, 0);

        if (series.Points.Count == 1)
        {
            DrawMarker(plot.X(series.Points[0].X), plot.Y(series.Points[0].Y), 1.5, color);
            return;
        }

        _svg.Append("<path d=\"").Append(PathData(plot, series.Points))
            .Append("\" fill=\"none\" stroke=\"").Append(color)
            .Append("\" stroke-width=\"1.5\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/>\n");
    }
}
