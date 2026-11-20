using System;
using M3scalinum.Text.SharpTypst.Documents;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Tests;

public sealed class ShapeTests
{
    private static string Render(Action<TypstContent> build)
    {
        var content = new TypstContent();
        build(content);
        return TypstDocument.Create().Add(content).ToMarkup();
    }

    [Fact]
    public void Emits_a_line()
    {
        var markup = Render(c => c.Line(TypstLength.Percent(100), stroke: TypstStroke.Solid(TypstLength.Pt(1))));

        Assert.Contains("#line(length: 100%, stroke: 1pt)", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_a_rectangle_with_a_fill_and_a_radius()
    {
        var markup = Render(c => c.Rect(
            TypstLength.Cm(2),
            TypstLength.Cm(1),
            TypstColor.Named("red"),
            TypstStroke.Solid(TypstLength.Pt(1)),
            TypstLength.Mm(2)));

        Assert.Contains(
            "#rect(width: 2cm, height: 1cm, fill: red, stroke: 1pt, radius: 2mm)",
            markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_a_square_a_circle_and_an_ellipse()
    {
        var markup = Render(c => c
            .Square(TypstLength.Cm(1), TypstColor.Named("blue"))
            .Circle(TypstLength.Cm(1))
            .Ellipse(TypstLength.Cm(2), TypstLength.Cm(1)));

        Assert.Contains("#square(size: 1cm, fill: blue)", markup, StringComparison.Ordinal);
        Assert.Contains("#circle(radius: 1cm)", markup, StringComparison.Ordinal);
        Assert.Contains("#ellipse(width: 2cm, height: 1cm)", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_a_polygon_and_a_curve()
    {
        var markup = Render(c => c
            .Polygon(
                TypstColor.Named("red"),
                TypstStroke.None,
                (TypstLength.Pt(0), TypstLength.Pt(0)),
                (TypstLength.Cm(1), TypstLength.Pt(0)),
                (TypstLength.Pt(0), TypstLength.Cm(1)))
            .Curve(
                TypstStroke.Solid(TypstLength.Pt(1)),
                (TypstLength.Pt(0), TypstLength.Pt(0)),
                (TypstLength.Cm(1), TypstLength.Cm(1))));

        Assert.Contains(
            "#polygon(fill: red, stroke: none, (0pt, 0pt), (1cm, 0pt), (0pt, 1cm))",
            markup,
            StringComparison.Ordinal);
        Assert.Contains(
            "#curve(stroke: 1pt, curve.move((0pt, 0pt)), curve.line((1cm, 1cm)))",
            markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_polygon_with_fewer_than_three_points()
    {
        var content = new TypstContent();

        Assert.Throws<ArgumentException>(() => content.Polygon(
            null, null, (TypstLength.Pt(0), TypstLength.Pt(0)), (TypstLength.Pt(1), TypstLength.Pt(1))));
    }

    [Fact]
    public void Rejects_a_curve_with_fewer_than_two_points()
    {
        var content = new TypstContent();

        Assert.Throws<ArgumentException>(() => content.Curve(
            null, (TypstLength.Pt(0), TypstLength.Pt(0))));
    }

    [Fact]
    public void Accepts_a_gradient_as_a_fill()
    {
        var markup = Render(c => c.Rect(
            TypstLength.Cm(2),
            TypstLength.Cm(1),
            TypstGradient.Linear(90, TypstColor.Named("red"), TypstColor.Named("blue"))));

        Assert.Contains("fill: gradient.linear(red, blue, angle: 90deg)", markup, StringComparison.Ordinal);
    }
}