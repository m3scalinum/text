using System;
using System.Linq;
using M3scalinum.Text.Charts.Internal;
using Xunit;

namespace M3scalinum.Text.Charts.Tests;

/// <summary>The tick rule and the number formatting, asserted directly rather than through a chart.</summary>
public sealed class TickScaleTests
{
    [Fact]
    public void A_round_span_takes_the_step_it_implies()
    {
        var scale = TickScale.Nice(0, 100, 5);

        Assert.Equal(20, scale.Step);
        Assert.Equal(new double[] { 0, 20, 40, 60, 80, 100 }, scale.Ticks);
        Assert.Equal(0, scale.Min);
        Assert.Equal(100, scale.Max);
    }

    [Fact]
    public void A_span_that_is_not_round_is_rounded_up_to_the_nice_step()
    {
        // 22 over five intervals wants 4.4, which is not a nice number; 5 is, and the axis runs to 25.
        var scale = TickScale.Nice(0, 22, 5);

        Assert.Equal(5, scale.Step);
        Assert.Equal(0, scale.Min);
        Assert.Equal(25, scale.Max);
        Assert.Equal(new double[] { 0, 5, 10, 15, 20, 25 }, scale.Ticks);
    }

    [Fact]
    public void A_flat_range_is_widened_by_one_unit()
    {
        var scale = TickScale.Nice(3, 3, 5);

        Assert.Equal(3, scale.Min);
        Assert.Equal(4, scale.Max);
        Assert.Equal(0.2, scale.Step, 6);
        Assert.Equal(new[] { 3, 3.2, 3.4, 3.6, 3.8, 4 }, scale.Ticks.Select(tick => Math.Round(tick, 6)));
    }

    [Fact]
    public void A_huge_range_takes_a_huge_step_without_losing_its_labels()
    {
        var scale = TickScale.Nice(1.2e12, 1.9e12, 5);

        Assert.Equal(2e11, scale.Step);
        Assert.Equal(5, scale.Ticks.Count);
        Assert.Equal(1.2e12, scale.Ticks[0]);
        Assert.Equal(2e12, scale.Ticks[^1]);
    }

    [Fact]
    public void A_range_whose_span_overflows_falls_back_to_its_two_bounds()
    {
        // 2e308 is not a double: the span is infinite, and no step can be derived from it.
        var scale = TickScale.Nice(-1e308, 1e308, 5);

        Assert.Equal(new[] { -1e308, 1e308 }, scale.Ticks);
    }

    [Fact]
    public void A_kept_inside_range_does_not_widen_the_axis_but_keeps_its_bounds()
    {
        // The x axis of a line chart: 0.94 to 4.06, labelled at the whole numbers inside it.
        var scale = TickScale.Nice(0.94, 4.06, 6, extend: false);

        Assert.Equal(0.94, scale.Min, 6);
        Assert.Equal(4.06, scale.Max, 6);
        Assert.Equal(new double[] { 1, 2, 3, 4 }, scale.Ticks);
    }

    [Fact]
    public void A_negative_range_keeps_zero_as_a_tick()
    {
        // A bar chart that dips below zero: the axis must show the zero line it measures from.
        var scale = TickScale.Nice(-6, 4, 5);

        Assert.Equal(2, scale.Step);
        Assert.Equal(new double[] { -6, -4, -2, 0, 2, 4 }, scale.Ticks);
    }
}

/// <summary>The formatting every number in the document goes through.</summary>
public sealed class NumberTextTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(640, "640")]
    [InlineData(51.819, "51.819")]
    [InlineData(1.23456, "1.235")]
    public void A_coordinate_is_written_with_at_most_three_decimals(double value, string expected)
        => Assert.Equal(expected, SvgText.Number(value));

    [Fact]
    public void A_negative_zero_is_written_as_zero()
        => Assert.Equal("0", SvgText.Number(-0d));

    [Theory]
    [InlineData(0, 2, "0")]
    [InlineData(10, 2.5, "10")]
    [InlineData(12.5, 2.5, "12.5")]
    [InlineData(-6, 2, "-6")]
    [InlineData(0.30000000000000004, 0.1, "0.3")]
    [InlineData(1e12, 2e11, "1E+12")]
    [InlineData(1.2e-6, 2e-7, "1.2E-6")]
    public void A_tick_label_shows_the_decimals_its_step_needs(double value, double step, string expected)
        => Assert.Equal(expected, SvgText.TickLabel(value, step));

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a<b", "a&lt;b")]
    [InlineData("a&b", "a&amp;b")]
    [InlineData("say \"hi\"", "say &quot;hi&quot;")]
    [InlineData("it's", "it&#39;s")]
    [InlineData("a>b", "a&gt;b")]
    public void Text_is_escaped_for_XML(string value, string expected)
        => Assert.Equal(expected, SvgText.Escape(value));
}
