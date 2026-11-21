using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Xunit;

namespace M3scalinum.Text.Charts.Tests;

/// <summary>
/// What every rendering owes a caller, whatever the kind: valid XML, no way out of the document,
/// the size it was asked for, and the same bytes every time.
/// </summary>
public sealed class ChartContractTests
{
    private static readonly ChartKind[] Kinds =
    [
        ChartKind.Line,
        ChartKind.Area,
        ChartKind.Bar,
        ChartKind.StackedBar,
        ChartKind.Scatter,
        ChartKind.Pie,
        ChartKind.Donut,
        ChartKind.Sparkline,
    ];

    private static ChartSpec SpecOf(ChartKind kind) => kind switch
    {
        ChartKind.Line => Fixtures.Revenue(),
        ChartKind.Area => Fixtures.Area(),
        ChartKind.Bar => Fixtures.Grouped(),
        ChartKind.StackedBar => Fixtures.Stacked(),
        ChartKind.Scatter => Fixtures.Scatter(),
        ChartKind.Pie => Fixtures.Pie(),
        ChartKind.Donut => Fixtures.Donut(),
        _ => Fixtures.Sparkline(),
    };

    [Theory]
    [InlineData(ChartKind.Line)]
    [InlineData(ChartKind.Area)]
    [InlineData(ChartKind.Bar)]
    [InlineData(ChartKind.StackedBar)]
    [InlineData(ChartKind.Scatter)]
    [InlineData(ChartKind.Pie)]
    [InlineData(ChartKind.Donut)]
    [InlineData(ChartKind.Sparkline)]
    public void Every_kind_renders_well_formed_svg(ChartKind kind)
    {
        var svg = Chart.RenderSvg(SpecOf(kind));

        // Parse throws on anything that is not XML; the helper then checks the SVG namespace.
        var document = Svg.Parse(svg);

        Assert.Equal("svg", document.Root!.Name.LocalName);
        Assert.NotEmpty(svg);
    }

    [Fact]
    public void An_empty_spec_and_an_empty_pie_are_well_formed_too()
    {
        Svg.Parse(Chart.RenderSvg(Fixtures.Empty()));
        Svg.Parse(Chart.RenderSvg(ChartSpec.Pie()));
        Svg.Parse(Chart.RenderSvg(ChartSpec.Sparkline()));
    }

    [Fact]
    public void The_same_spec_renders_the_same_bytes_twice()
    {
        var specs = Kinds.Select(SpecOf).Append(Fixtures.Empty()).Append(ChartSpec.Pie());

        foreach (var spec in specs)
        {
            var first = Chart.RenderSvg(spec, new ChartSize(500, 280));
            var second = Chart.RenderSvg(spec, new ChartSize(500, 280));

            Assert.Equal(first, second);
        }
    }

    [Fact]
    public void The_svg_refers_to_nothing_outside_itself()
    {
        foreach (var kind in Kinds)
        {
            var svg = Chart.RenderSvg(SpecOf(kind));

            foreach (var forbidden in new[] { "<script", "<image", "<use", "<foreignObject", "<style", "xlink:", "url(", "@import" })
            {
                Assert.DoesNotContain(forbidden, svg);
            }

            var document = Svg.Parse(svg);

            foreach (var element in document.Descendants())
            {
                Assert.DoesNotContain(
                    element.Name.LocalName,
                    new[] { "script", "image", "use", "foreignObject", "style" });
            }

            foreach (var attribute in document.Root!.DescendantsAndSelf().SelectMany(element => element.Attributes()))
            {
                Assert.DoesNotContain(attribute.Name.LocalName, new[] { "href", "src" });
            }
        }
    }

    [Fact]
    public void The_view_box_follows_the_requested_size()
    {
        var document = Svg.Parse(Chart.RenderSvg(Fixtures.Revenue(), new ChartSize(300, 150)));
        var root = document.Root!;

        Assert.Equal("300", root.Attribute("width")!.Value);
        Assert.Equal("150", root.Attribute("height")!.Value);
        Assert.Equal("0 0 300 150", root.Attribute("viewBox")!.Value);
        Assert.Equal("xMidYMid meet", root.Attribute("preserveAspectRatio")!.Value);
    }

    [Fact]
    public void The_text_names_the_font_family_the_caller_gave()
    {
        var plain = Svg.Parse(Chart.RenderSvg(Fixtures.Revenue())).Root!;
        Assert.Equal("sans-serif", plain.Attribute("font-family")!.Value);

        var named = Chart.RenderSvg(Fixtures.Revenue().WithFontFamily("Inter, system-ui, sans-serif"));
        Assert.Equal("Inter, system-ui, sans-serif", Svg.Parse(named).Root!.Attribute("font-family")!.Value);
    }

    [Fact]
    public void Labels_are_thinned_when_they_would_overlap()
    {
        var document = Svg.Parse(Chart.RenderSvg(Fixtures.Crowded(), new ChartSize(320, 180)));
        var labels = Svg.Labels(document, "middle");

        // Twenty-four categories, four labels: the rule drops the ones that would collide.
        Assert.Equal(4, labels.Count);
        Assert.Equal(["category 0", "category 7", "category 14", "category 21"], labels);

        var positions = document.Descendants(Svg.Ns + "text")
            .Where(element => (string?)element.Attribute("text-anchor") == "middle"
                && (string?)element.Attribute("font-size") == "11")
            .Select(element => Svg.Number(element, "x"))
            .ToArray();

        // The documented rule: every label clears the previous one's estimated box by four units.
        for (var index = 1; index < positions.Length; index++)
        {
            var gap = positions[index] - positions[index - 1];
            var needed = ((labels[index - 1].Length + labels[index].Length) * 11 * 0.6 / 2) + 4;

            Assert.True(gap >= needed - 0.01, $"{labels[index]} sits {gap} from {labels[index - 1]}, needs {needed}");
        }
    }

    [Fact]
    public void Text_and_attributes_are_escaped()
    {
        var spec = ChartSpec.Line()
            .WithTitle("Revenue & <margin>")
            .AddSeries("A \"quoted\" series", [new ChartPoint(0, 1), new ChartPoint(1, 2)])
            .AddSeries("B <plain>", [new ChartPoint(0, 2), new ChartPoint(1, 3)]);

        var svg = Chart.RenderSvg(spec);

        Assert.Contains("Revenue &amp; &lt;margin&gt;", svg);
        Assert.Contains("A &quot;quoted&quot; series", svg);
        Assert.Contains("B &lt;plain&gt;", svg);
        Assert.DoesNotContain("<margin>", svg);

        var document = Svg.Parse(svg);
        Assert.Equal("Revenue & <margin>", document.Root!.Attribute("aria-label")!.Value);
        Assert.Contains(document.Descendants(Svg.Ns + "title"), element => element.Value == "Revenue & <margin>");
    }

    [Fact]
    public void Rendering_ignores_the_ambient_culture()
    {
        var expected = Chart.RenderSvg(Fixtures.Revenue());
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;

        // The suite runs with InvariantGlobalization on, so a named culture cannot be created: a copy
        // of the invariant one with a comma for the decimal separator is the closest a test can get,
        // and it is enough to catch a format call that forgot its provider.
        var comma = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        comma.NumberFormat.NumberDecimalSeparator = ",";
        comma.NumberFormat.NumberGroupSeparator = ".";

        try
        {
            CultureInfo.CurrentCulture = comma;
            CultureInfo.CurrentUICulture = comma;

            Assert.Equal(expected, Chart.RenderSvg(Fixtures.Revenue()));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Fact]
    public void The_legend_lists_the_series_in_the_order_they_were_added()
    {
        var document = Svg.Parse(Chart.RenderSvg(Fixtures.Grouped()));

        var entries = document.Descendants(Svg.Ns + "text")
            .Where(element => (string?)element.Attribute("font-size") == "10")
            .Select(element => element.Value)
            .ToArray();

        Assert.Equal(["2025", "2026"], entries);
    }

    [Fact]
    public void A_series_is_drawn_in_the_order_its_points_were_given()
    {
        var document = Svg.Parse(Chart.RenderSvg(Fixtures.Revenue()));
        var numbers = Svg.Numbers(Svg.Attribute(Svg.Paths(document)[0], "d"));

        // Four points, so eight numbers, and the x coordinates rise with the points.
        Assert.Equal(8, numbers.Count);
        Assert.True(numbers[0] < numbers[2] && numbers[2] < numbers[4] && numbers[4] < numbers[6]);
    }

    [Fact]
    public void ToTypstImage_embeds_the_svg_bytes_and_escapes_them_once()
    {
        var spec = Fixtures.Revenue();
        var svg = Chart.RenderSvg(spec);
        var markup = Chart.ToTypstImage(spec);

        Assert.StartsWith("#image(bytes(\"<svg", markup, StringComparison.Ordinal);
        Assert.EndsWith("\"), format: \"svg\", width: 100%, alt: \"Revenue\")\n", markup, StringComparison.Ordinal);
        Assert.Equal(svg, EmbeddedSvg(markup));

        // One line: the chart's own line breaks are escapes, not newlines in the caller's source.
        Assert.Equal(1, markup.Split('\n').Length - 1);
    }

    [Fact]
    public void ToTypstImage_carries_the_title_as_alt_text_and_drops_it_when_there_is_none()
    {
        var untitled = ChartSpec.Line().AddSeries("s", [new ChartPoint(0, 1), new ChartPoint(1, 2)]);

        Assert.DoesNotContain("alt:", Chart.ToTypstImage(untitled));
        Assert.Contains("alt: \"Revenue\"", Chart.ToTypstImage(Fixtures.Revenue()));
    }

    [Theory]
    [InlineData("100%")]
    [InlineData("12cm")]
    [InlineData("240pt")]
    [InlineData("0.5in")]
    [InlineData("3em")]
    public void ToTypstImage_accepts_a_plain_length(string width)
    {
        var markup = Chart.ToTypstImage(Fixtures.Revenue(), ChartSize.Default, width);

        Assert.Contains($"width: {width}", markup);
    }

    [Theory]
    [InlineData("")]
    [InlineData("auto")]
    [InlineData("12 cm")]
    [InlineData("-3cm")]
    [InlineData("cm")]
    [InlineData("100%);#rect(")]
    [InlineData("1fr")]
    public void ToTypstImage_refuses_a_width_that_is_not_a_length(string width)
    {
        Assert.Throws<ArgumentException>(() => Chart.ToTypstImage(Fixtures.Revenue(), ChartSize.Default, width));
    }

    [Fact]
    public void The_legend_and_the_grid_can_be_switched_off()
    {
        var spec = Fixtures.Revenue();
        spec.ShowGrid = false;
        spec.ShowLegend = false;

        var document = Svg.Parse(Chart.RenderSvg(spec));

        Assert.DoesNotContain(
            Svg.Of(document, "line"),
            element => Svg.Attribute(element, "stroke") == "#e5e7eb");
        Assert.DoesNotContain(
            Svg.Of(document, "text"),
            element => Svg.Attribute(element, "font-size") == "10");
    }

    [Fact]
    public void A_pie_with_two_series_is_refused()
    {
        var spec = ChartSpec.Pie()
            .AddSeries("a", [new ChartPoint(0, 1, "x")])
            .AddSeries("b", [new ChartPoint(0, 1, "y")]);

        Assert.Throws<InvalidOperationException>(() => Chart.RenderSvg(spec));
    }

    [Fact]
    public void A_pie_with_a_negative_slice_is_refused()
    {
        var spec = ChartSpec.Pie().AddSeries("a", [new ChartPoint(0, 1, "x"), new ChartPoint(0, -1, "y")]);

        Assert.Throws<InvalidOperationException>(() => Chart.RenderSvg(spec));
    }

    [Fact]
    public void A_sparkline_with_two_series_is_refused()
    {
        var spec = ChartSpec.Sparkline()
            .AddSeries("a", [new ChartPoint(0, 1)])
            .AddSeries("b", [new ChartPoint(0, 2)]);

        Assert.Throws<InvalidOperationException>(() => Chart.RenderSvg(spec));
    }

    [Fact]
    public void A_point_that_is_not_finite_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChartPoint(double.NaN, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChartPoint(1, double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChartSize(0, 100));
    }

    /// <summary>Reads the SVG back out of the Typst string literal that carries it.</summary>
    private static string EmbeddedSvg(string markup)
    {
        var open = markup.IndexOf('"', markup.IndexOf("bytes(", StringComparison.Ordinal));
        var builder = new StringBuilder(markup.Length);

        for (var index = open + 1; index < markup.Length; index++)
        {
            if (markup[index] == '"')
            {
                return builder.ToString();
            }

            if (markup[index] != '\\')
            {
                builder.Append(markup[index]);
                continue;
            }

            index++;
            builder.Append(markup[index] switch
            {
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                var other => other,
            });
        }

        throw new InvalidOperationException("the markup holds no closed string literal");
    }
}
