using System.Collections.Generic;

namespace M3scalinum.Text.Charts.Tests;

/// <summary>The charts the tests render. They are built here once so an assertion can name the shape.</summary>
internal static class Fixtures
{
    /// <summary>Two line series over four months: the tick labels and coordinates the tests pin.</summary>
    internal static ChartSpec Revenue() => ChartSpec.Line()
        .WithTitle("Revenue")
        .WithAxisTitles("Month", "EUR")
        .AddSeries(
            "2025",
            [new ChartPoint(1, 10), new ChartPoint(2, 14), new ChartPoint(3, 12), new ChartPoint(4, 18)])
        .AddSeries(
            "2026",
            [new ChartPoint(1, 12), new ChartPoint(2, 15), new ChartPoint(3, 19), new ChartPoint(4, 22)]);

    internal static ChartSpec Area() => ChartSpec.Area()
        .WithTitle("Traffic")
        .WithAxisTitles("Day", "Visits")
        .AddSeries("visits", [new ChartPoint(1, 4), new ChartPoint(2, 9), new ChartPoint(3, 7), new ChartPoint(4, 12)]);

    internal static ChartSpec Grouped() => ChartSpec.Bar()
        .WithTitle("Revenue by quarter")
        .WithAxisTitles("Quarter", "EUR")
        .AddSeries(
            "2025",
            [
                new ChartPoint(0, 4, "Q1"),
                new ChartPoint(0, 8, "Q2"),
                new ChartPoint(0, 6, "Q3"),
                new ChartPoint(0, 9, "Q4"),
            ])
        .AddSeries(
            "2026",
            [
                new ChartPoint(0, 5, "Q1"),
                new ChartPoint(0, 7, "Q2"),
                new ChartPoint(0, 11, "Q3"),
                new ChartPoint(0, 10, "Q4"),
            ]);

    internal static ChartSpec Stacked() => ChartSpec.StackedBar()
        .WithTitle("Costs")
        .WithAxisTitles("Quarter", "EUR")
        .AddSeries("hosting", [new ChartPoint(0, 4, "Q1"), new ChartPoint(0, 5, "Q2")])
        .AddSeries("support", [new ChartPoint(0, 3, "Q1"), new ChartPoint(0, 2, "Q2")]);

    internal static ChartSpec Scatter() => ChartSpec.Scatter()
        .WithTitle("Spread")
        .WithAxisTitles("x", "y")
        .AddSeries("sample", [new ChartPoint(1, 2), new ChartPoint(2, 5), new ChartPoint(2.5, 3), new ChartPoint(4, 8)]);

    internal static ChartSpec NegativeBar() => ChartSpec.Bar()
        .WithTitle("Change")
        .WithAxisTitles("Quarter", "EUR")
        .AddSeries(
            "delta",
            [new ChartPoint(0, 4, "Q1"), new ChartPoint(0, -6, "Q2"), new ChartPoint(0, 2, "Q3")]);

    /// <summary>Ten, twenty and thirty: sixty in total, so every share is a fraction of sixty.</summary>
    internal static ChartSpec Pie() => ChartSpec.Pie()
        .WithTitle("Share")
        .AddSeries(
            "share",
            [new ChartPoint(0, 10, "a"), new ChartPoint(0, 20, "b"), new ChartPoint(0, 30, "c")]);

    internal static ChartSpec Donut() => ChartSpec.Donut()
        .WithTitle("Share")
        .AddSeries(
            "share",
            [new ChartPoint(0, 30, "a"), new ChartPoint(0, 30, "b"), new ChartPoint(0, 40, "c")]);

    internal static ChartSpec Sparkline() => ChartSpec.Sparkline()
        .AddSeries(
            "walk",
            [
                new ChartPoint(1, 3),
                new ChartPoint(2, 5),
                new ChartPoint(3, 4),
                new ChartPoint(4, 7),
                new ChartPoint(5, 6),
            ]);

    internal static ChartSpec Empty() => ChartSpec.Line()
        .WithTitle("Nothing yet")
        .WithAxisTitles("Month", "EUR")
        .AddSeries("none", []);

    internal static ChartSpec Huge() => ChartSpec.Line()
        .WithAxisTitles("x", "y")
        .AddSeries("big", [new ChartPoint(1, 1.2e12), new ChartPoint(2, 1.9e12)]);

    internal static ChartSpec Tiny() => ChartSpec.Line()
        .WithAxisTitles("x", "y")
        .AddSeries("small", [new ChartPoint(1, 1.2e-6), new ChartPoint(2, 1.9e-6)]);

    /// <summary>Twenty-four categories, each with a long name, in a box too small to label them all.</summary>
    internal static ChartSpec Crowded()
    {
        var points = new List<ChartPoint>();

        for (var index = 0; index < 24; index++)
        {
            points.Add(new ChartPoint(0, index + 1, "category " + index));
        }

        return ChartSpec.Bar().WithTitle("Many categories").AddSeries("s", points);
    }

    /// <summary>One spec per kind, for the tests that sweep the whole surface.</summary>
    internal static IEnumerable<(string Name, ChartSpec Spec)> EveryKind()
    {
        yield return ("line", Revenue());
        yield return ("area", Area());
        yield return ("bar", Grouped());
        yield return ("stacked", Stacked());
        yield return ("scatter", Scatter());
        yield return ("pie", Pie());
        yield return ("donut", Donut());
        yield return ("sparkline", Sparkline());
        yield return ("empty", Empty());
    }
}
