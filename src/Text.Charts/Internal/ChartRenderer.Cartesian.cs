using System;
using System.Collections.Generic;
using System.Text;

namespace M3scalinum.Text.Charts.Internal;

/// <summary>The series drawing for the kinds that sit on a numerical frame.</summary>
internal sealed partial class ChartRenderer
{
    private void DrawLines(ChartPlot plot)
    {
        for (var index = 0; index < _spec.Series.Count; index++)
        {
            var series = _spec.Series[index];
            var color = ColorOf(series, index);

            if (series.Points.Count == 0)
            {
                continue;
            }

            if (series.Points.Count == 1)
            {
                DrawMarker(plot.X(series.Points[0].X), plot.Y(series.Points[0].Y), MarkerRadius, color);
                continue;
            }

            _svg.Append("<path d=\"").Append(PathData(plot, series.Points))
                .Append("\" fill=\"none\" stroke=\"").Append(color)
                .Append("\" stroke-width=\"").Append(SvgText.Number(StrokeWidth))
                .Append("\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/>\n");
        }
    }

    private void DrawAreas(ChartPlot plot)
    {
        var baseline = plot.Baseline;

        for (var index = 0; index < _spec.Series.Count; index++)
        {
            var series = _spec.Series[index];
            var color = ColorOf(series, index);

            if (series.Points.Count == 0)
            {
                continue;
            }

            if (series.Points.Count == 1)
            {
                DrawMarker(plot.X(series.Points[0].X), plot.Y(series.Points[0].Y), MarkerRadius, color);
                continue;
            }

            var path = new StringBuilder(PathData(plot, series.Points));

            // Down from the last point to the baseline, back to the first one, and closed: the area
            // between the line and zero, which is what an area chart shows.
            path.Append(" L ").Append(SvgText.Number(plot.X(series.Points[^1].X)))
                .Append(' ').Append(SvgText.Number(baseline))
                .Append(" L ").Append(SvgText.Number(plot.X(series.Points[0].X)))
                .Append(' ').Append(SvgText.Number(baseline)).Append(" Z");

            _svg.Append("<path d=\"").Append(path).Append("\" fill=\"").Append(color)
                .Append("\" fill-opacity=\"0.2\" stroke=\"").Append(color)
                .Append("\" stroke-width=\"").Append(SvgText.Number(StrokeWidth))
                .Append("\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/>\n");
        }
    }

    private void DrawScatter(ChartPlot plot)
    {
        for (var index = 0; index < _spec.Series.Count; index++)
        {
            var series = _spec.Series[index];
            var color = ColorOf(series, index);

            foreach (var point in series.Points)
            {
                DrawMarker(plot.X(point.X), plot.Y(point.Y), MarkerRadius, color);
            }
        }
    }

    private void DrawGroupedBars(ChartPlot plot)
    {
        var count = CategoryCount();

        if (count == 0)
        {
            return;
        }

        var slot = plot.Width / count;
        var group = slot * 0.8;
        var seriesCount = _spec.Series.Count;
        var cell = group / seriesCount;
        var barWidth = seriesCount > 1 ? cell * 0.9 : group;
        var inset = (cell - barWidth) / 2;
        var baseline = plot.Baseline;

        for (var seriesIndex = 0; seriesIndex < seriesCount; seriesIndex++)
        {
            var series = _spec.Series[seriesIndex];
            var color = ColorOf(series, seriesIndex);

            for (var pointIndex = 0; pointIndex < series.Points.Count; pointIndex++)
            {
                var value = series.Points[pointIndex].Y;
                var x = plot.Left + (slot * pointIndex) + (slot * 0.1) + (seriesIndex * cell) + inset;
                var top = Math.Min(plot.Y(value), baseline);
                var height = Math.Abs(plot.Y(value) - baseline);

                WriteBar(x, top, barWidth, height, color);
            }
        }
    }

    private void DrawStackedBars(ChartPlot plot)
    {
        var count = CategoryCount();

        if (count == 0)
        {
            return;
        }

        var slot = plot.Width / count;
        var barWidth = slot * 0.8;

        for (var category = 0; category < count; category++)
        {
            var up = 0d;
            var down = 0d;
            var x = plot.Left + (slot * category) + (slot * 0.1);

            for (var seriesIndex = 0; seriesIndex < _spec.Series.Count; seriesIndex++)
            {
                var series = _spec.Series[seriesIndex];

                if (category >= series.Points.Count)
                {
                    continue;
                }

                var value = series.Points[category].Y;

                if (value == 0)
                {
                    continue;
                }

                var from = value > 0 ? up : down;
                var to = from + value;
                var top = Math.Min(plot.Y(from), plot.Y(to));
                var height = Math.Abs(plot.Y(to) - plot.Y(from));

                WriteBar(x, top, barWidth, height, ColorOf(series, seriesIndex));

                if (value > 0)
                {
                    up = to;
                }
                else
                {
                    down = to;
                }
            }
        }
    }

    private void WriteBar(double x, double y, double width, double height, string color)
    {
        _svg.Append("<rect x=\"").Append(SvgText.Number(x))
            .Append("\" y=\"").Append(SvgText.Number(y))
            .Append("\" width=\"").Append(SvgText.Number(width))
            .Append("\" height=\"").Append(SvgText.Number(height))
            .Append("\" fill=\"").Append(color).Append("\"/>\n");
    }

    private void DrawMarker(double x, double y, double radius, string color)
    {
        _svg.Append("<circle cx=\"").Append(SvgText.Number(x))
            .Append("\" cy=\"").Append(SvgText.Number(y))
            .Append("\" r=\"").Append(SvgText.Number(radius))
            .Append("\" fill=\"").Append(color).Append("\"/>\n");
    }

    private static string PathData(ChartPlot plot, IReadOnlyList<ChartPoint> points)
    {
        var path = new StringBuilder(points.Count * 16);

        for (var index = 0; index < points.Count; index++)
        {
            path.Append(index == 0 ? "M " : " L ")
                .Append(SvgText.Number(plot.X(points[index].X))).Append(' ')
                .Append(SvgText.Number(plot.Y(points[index].Y)));
        }

        return path.ToString();
    }
}
