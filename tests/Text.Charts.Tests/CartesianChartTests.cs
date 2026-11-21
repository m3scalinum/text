using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace M3scalinum.Text.Charts.Tests;

/// <summary>
/// The kinds that sit on a numerical frame. Each test pins the structure, the tick labels the axis
/// rule produces, and a handful of coordinates — not a hash of the file, which would fail on a
/// rounding change and say nothing about why.
/// </summary>
public sealed class CartesianChartTests
{
    private static XElement[] Bars(XDocument document)
        => Svg.Of(document, "rect").Where(rect => Svg.Number(rect, "width") != 10).ToArray();

    [Fact]
    public void A_line_chart_draws_one_path_per_series_in_the_order_they_were_added()
    {
        var document = Svg.Parse(Chart.RenderSvg(Fixtures.Revenue()));
        var paths = Svg.Paths(document);

        Assert.Equal(2, paths.Count);
        Assert.All(paths, path => Assert.Equal("none", Svg.Attribute(path, "fill")));
        Assert.Equal("#2563eb", Svg.Attribute(paths[0], "stroke"));
        Assert.Equal("#dc2626", Svg.Attribute(paths[1], "stroke"));

        // Four points, one move and three lines, at the coordinates the layout produced.
        Assert.Equal(
            "M 71.238 294 L 251.879 211.44 L 432.521 252.72 L 613.162 128.88",
            Svg.Attribute(paths[0], "d"));
    }

    [Fact]
    public void The_value_axis_of_a_line_chart_ends_on_a_tick_and_pins_the_ticks_it_chose()
    {
        var document = Svg.Parse(Chart.RenderSvg(Fixtures.Revenue()));

        // 10 to 22 in five intervals: the raw step is 2.4, so the axis takes 2.5 and stretches to
        // 22.5. These are the labels a reader sees, and they are asserted rather than promised.
        Assert.Equal(["10", "12.5", "15", "17.5", "20", "22.5"], Svg.Labels(document, "end"));

        // The x axis covers the data: 1 to 4, padded, with the ticks that fall inside.
        Assert.Equal(["1", "2", "3", "4"], Svg.Labels(document, "middle"));
    }

    [Fact]
    public void The_title_and_the_axis_titles_are_written_as_text()
    {
        var document = Svg.Parse(Chart.RenderSvg(Fixtures.Revenue()));
        var texts = Svg.Of(document, "text");

        var title = Assert.Single(texts, element => Svg.Attribute(element, "font-size") == "15");
        Assert.Equal("Revenue", title.Value);
        Assert.Equal("bold", Svg.Attribute(title, "font-weight"));

        Assert.Contains(texts, element => element.Value == "Month");
        Assert.Contains(texts, element => element.Value == "EUR"
            && Svg.Attribute(element, "transform").StartsWith("rotate(-90 ", System.StringComparison.Ordinal));
    }

    [Fact]
    public void An_area_chart_closes_each_path_down_to_the_baseline()
    {
        var document = Svg.Parse(Chart.RenderSvg(Fixtures.Area()));
        var path = Assert.Single(Svg.Paths(document));

        Assert.Equal("0.2", Svg.Attribute(path, "fill-opacity"));

        // The line, then down to the zero line, then back to where it started and closed.
        Assert.Equal(
            "M 71.238 225.04 L 251.879 113.84 L 432.521 158.32 L 613.162 47.12 L 613.162 314 L 71.238 314 Z",
            Svg.Attribute(path, "d"));
    }

    [Fact]
    public void A_grouped_bar_chart_draws_one_bar_per_point_and_puts_them_side_by_side()
    {
        var document = Svg.Parse(Chart.RenderSvg(Fixtures.Grouped()));
        var bars = Bars(document);

        Assert.Equal(8, bars.Length);

        Assert.Equal(77.308, Svg.Number(bars[0], "x"), 3);
        Assert.Equal(211.44, Svg.Number(bars[0], "y"), 3);
        Assert.Equal(50.724, Svg.Number(bars[0], "width"), 3);
        Assert.Equal(82.56, Svg.Number(bars[0], "height"), 3);

        // The second series sits beside the first inside the same category slot, never on top of it.
        var firstRight = Svg.Number(bars[0], "x") + Svg.Number(bars[0], "width");
        Assert.True(Svg.Number(bars[4], "x") >= firstRight, "the two series of a category overlap");

        Assert.All(bars.Take(4), bar => Assert.Equal("#2563eb", Svg.Attribute(bar, "fill")));
        Assert.All(bars.Skip(4), bar => Assert.Equal("#dc2626", Svg.Attribute(bar, "fill")));

        // The y axis includes zero, because a bar measures its height from it.
        Assert.Equal(["0", "2.5", "5", "7.5", "10", "12.5"], Svg.Labels(document, "end"));
        Assert.Equal(["Q1", "Q2", "Q3", "Q4"], Svg.Labels(document, "middle"));
    }

    [Fact]
    public void A_stacked_bar_chart_stacks_each_series_on_the_one_below()
    {
        var document = Svg.Parse(Chart.RenderSvg(Fixtures.Stacked()));
        var bars = Bars(document);

        Assert.Equal(4, bars.Length);

        // The first category: hosting at the bottom, support on top of it, sharing an x and a width.
        Assert.Equal(Svg.Number(bars[0], "x"), Svg.Number(bars[1], "x"), 3);
        Assert.Equal(Svg.Number(bars[0], "width"), Svg.Number(bars[1], "width"), 3);
        Assert.Equal(
            Svg.Number(bars[0], "y"),
            Svg.Number(bars[1], "y") + Svg.Number(bars[1], "height"),
            3);

        // 4 + 3 and 5 + 2: every bar reaches the same total, which is what stacking means.
        var totals = new[]
        {
            Svg.Number(bars[0], "height") + Svg.Number(bars[1], "height"),
            Svg.Number(bars[2], "height") + Svg.Number(bars[3], "height"),
        };
        Assert.Equal(totals[0], totals[1], 3);
    }

    [Fact]
    public void A_scatter_chart_draws_a_marker_per_point_and_no_line()
    {
        var document = Svg.Parse(Chart.RenderSvg(Fixtures.Scatter()));
        var circles = Svg.Of(document, "circle");

        Assert.Empty(Svg.Paths(document));
        Assert.Equal(4, circles.Count);
        Assert.Equal(51.819, Svg.Number(circles[0], "cx"), 3);
        Assert.Equal(314, Svg.Number(circles[0], "cy"), 3);
        Assert.All(circles, circle => Assert.Equal(3, Svg.Number(circle, "r"), 3));
    }

    [Fact]
    public void A_negative_bar_hangs_below_the_zero_line_and_the_axis_spans_it()
    {
        var document = Svg.Parse(Chart.RenderSvg(Fixtures.NegativeBar()));
        var bars = Bars(document);

        Assert.Equal(3, bars.Length);

        var baseline = Svg.Number(bars[0], "y") + Svg.Number(bars[0], "height");
        Assert.Equal(147.2, baseline, 3);

        // The negative bar starts on the baseline and ends below it.
        Assert.Equal(baseline, Svg.Number(bars[1], "y"), 3);
        Assert.True(Svg.Number(bars[1], "y") + Svg.Number(bars[1], "height") > baseline);

        var labels = Svg.Labels(document, "end");
        Assert.Contains("0", labels);
        Assert.Contains("-6", labels);
    }

    [Fact]
    public void A_single_point_series_draws_a_marker_rather_than_an_empty_path()
    {
        var spec = ChartSpec.Line().AddSeries("one", [new ChartPoint(3, 7)]);
        var document = Svg.Parse(Chart.RenderSvg(spec));

        Assert.Empty(Svg.Paths(document));
        var marker = Assert.Single(Svg.Of(document, "circle"));
        Assert.Equal("#2563eb", Svg.Attribute(marker, "fill"));
    }

    [Fact]
    public void An_empty_series_renders_the_axes_and_no_path()
    {
        var document = Svg.Parse(Chart.RenderSvg(Fixtures.Empty()));

        Assert.Empty(Svg.Paths(document));
        Assert.Empty(Svg.Of(document, "circle"));

        // Axes are still there: a y axis of six ticks and an x axis of six.
        Assert.Equal(6, Svg.Labels(document, "end").Count);
        Assert.Equal(6, Svg.Labels(document, "middle").Count);
        Assert.Equal(["0", "0.2", "0.4", "0.6", "0.8", "1"], Svg.Labels(document, "end"));
    }

    [Fact]
    public void A_huge_range_is_labelled_in_scientific_notation()
    {
        var labels = Svg.Labels(Svg.Parse(Chart.RenderSvg(Fixtures.Huge())), "end");

        Assert.Equal(["1.2E+12", "1.4E+12", "1.6E+12", "1.8E+12", "2E+12"], labels);
    }

    [Fact]
    public void A_tiny_range_keeps_its_precision()
    {
        var document = Svg.Parse(Chart.RenderSvg(Fixtures.Tiny()));

        Assert.Equal(["1.2E-6", "1.4E-6", "1.6E-6", "1.8E-6", "2E-6"], Svg.Labels(document, "end"));

        // The line is drawn, and every coordinate is a number a consumer can read.
        var path = Assert.Single(Svg.Paths(document));
        Assert.All(Svg.Numbers(Svg.Attribute(path, "d")), value => Assert.True(double.IsFinite(value)));
    }
}
