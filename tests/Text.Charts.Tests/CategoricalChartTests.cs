using System;
using System.Globalization;
using System.Linq;
using Xunit;

namespace M3scalinum.Text.Charts.Tests;

/// <summary>The kinds that are not drawn on an axis: pie, donut and sparkline.</summary>
public sealed class CategoricalChartTests
{
    [Fact]
    public void A_pie_chart_sizes_each_slice_in_proportion_to_its_share()
    {
        var document = Svg.Parse(Chart.RenderSvg(Fixtures.Pie()));
        var paths = Svg.Paths(document);

        Assert.Equal(3, paths.Count);

        // Ten, twenty and thirty out of sixty: the first slice turns through 60 degrees.
        var numbers = Svg.Numbers(Svg.Attribute(paths[0], "d"));
        var centerX = numbers[0];
        var centerY = numbers[1];
        var radius = centerY - numbers[3];
        var angle = -90 + (10d / 60d * 360d);

        Assert.Equal(320, centerX, 3);
        Assert.Equal(182, centerY, 3);
        Assert.Equal(centerX + (radius * Math.Cos(Radian(angle))), numbers[9], 2);
        Assert.Equal(centerY + (radius * Math.Sin(Radian(angle))), numbers[10], 2);

        // The second slice starts exactly where the first one stopped.
        var second = Svg.Numbers(Svg.Attribute(paths[1], "d"));
        Assert.Equal(numbers[9], second[2], 3);
        Assert.Equal(numbers[10], second[3], 3);
    }

    [Fact]
    public void A_pie_chart_normalises_slices_that_do_not_sum_to_one_hundred()
    {
        var shapes = ChartSpec.Pie()
            .AddSeries("a", [new ChartPoint(0, 1, "x"), new ChartPoint(0, 1, "y"), new ChartPoint(0, 2, "z")]);
        var percentages = ChartSpec.Pie()
            .AddSeries(
                "a",
                [new ChartPoint(0, 25, "x"), new ChartPoint(0, 25, "y"), new ChartPoint(0, 50, "z")]);

        var fromShapes = Svg.Paths(Svg.Parse(Chart.RenderSvg(shapes)))
            .Select(path => Svg.Attribute(path, "d")).ToArray();
        var fromPercentages = Svg.Paths(Svg.Parse(Chart.RenderSvg(percentages)))
            .Select(path => Svg.Attribute(path, "d")).ToArray();

        Assert.Equal(3, fromShapes.Length);
        Assert.Equal(fromPercentages, fromShapes);
    }

    [Fact]
    public void A_donut_chart_punches_a_hole_smaller_than_its_slices()
    {
        var document = Svg.Parse(Chart.RenderSvg(Fixtures.Donut()));
        var paths = Svg.Paths(document);

        Assert.Equal(3, paths.Count);

        // An annular sector: the outer arc, the radius in, the inner arc back, and closed. The hole
        // is part of every slice, so the neighbouring slices cannot show through the middle.
        var data = Svg.Attribute(paths[0], "d");
        Assert.Equal(2, data.Split('A').Length - 1);
        Assert.Equal(1, data.Split('L').Length - 1);

        var numbers = Svg.Numbers(data);
        var outer = numbers[2];
        var inner = numbers[11];

        Assert.Equal(138.7, outer, 3);
        Assert.Equal(76.285, inner, 3);
        Assert.True(inner < outer);

        // The slice starts at the top of the circle, on the outer radius.
        Assert.Equal(320, numbers[0], 3);
        Assert.Equal(43.3, numbers[1], 3);
    }

    [Fact]
    public void A_donut_with_one_slice_is_a_ring()
    {
        var spec = ChartSpec.Donut().AddSeries("only", [new ChartPoint(0, 5, "all")]);
        var path = Assert.Single(Svg.Paths(Svg.Parse(Chart.RenderSvg(spec))));

        // A whole circle with a whole circle subtracted: the even-odd rule leaves a ring.
        Assert.Equal("evenodd", Svg.Attribute(path, "fill-rule"));
        Assert.Equal(4, Svg.Attribute(path, "d").Split('A').Length - 1);
    }

    [Fact]
    public void A_pie_with_one_slice_is_a_whole_disc()
    {
        var spec = ChartSpec.Pie().AddSeries("only", [new ChartPoint(0, 5, "all")]);
        var document = Svg.Parse(Chart.RenderSvg(spec));

        var path = Assert.Single(Svg.Paths(document));
        var data = Svg.Attribute(path, "d");

        Assert.DoesNotContain("L", data);
        Assert.Equal(2, data.Split('A').Length - 1);
    }

    [Fact]
    public void A_pie_without_slices_draws_an_outline_and_does_not_divide_by_zero()
    {
        var document = Svg.Parse(Chart.RenderSvg(ChartSpec.Pie()));

        Assert.Empty(Svg.Paths(document));
        var outline = Assert.Single(Svg.Of(document, "circle"));
        Assert.Equal("none", Svg.Attribute(outline, "fill"));
        Assert.Equal("#e5e7eb", Svg.Attribute(outline, "stroke"));
    }

    [Fact]
    public void A_sparkline_draws_a_line_and_nothing_else()
    {
        var svg = Chart.RenderSvg(Fixtures.Sparkline(), ChartSize.Compact);
        var document = Svg.Parse(svg);

        Assert.Empty(Svg.Of(document, "text"));
        Assert.Empty(Svg.Of(document, "line"));
        Assert.Empty(Svg.Of(document, "rect"));

        var path = Assert.Single(Svg.Paths(document));
        Assert.Equal("M 1 31 L 40.5 16 L 80 23.5 L 119.5 1 L 159 8.5", Svg.Attribute(path, "d"));
        Assert.Equal("160", document.Root!.Attribute("width")!.Value);
        Assert.Equal("32", document.Root!.Attribute("height")!.Value);
    }

    [Fact]
    public void A_sparkline_of_one_point_is_a_single_marker()
    {
        var spec = ChartSpec.Sparkline().AddSeries("one", [new ChartPoint(1, 4)]);
        var document = Svg.Parse(Chart.RenderSvg(spec, ChartSize.Compact));

        Assert.Empty(Svg.Paths(document));
        Assert.Single(Svg.Of(document, "circle"));
    }

    private static double Radian(double degrees) => degrees * Math.PI / 180;
}
