using System;
using M3scalinum.Text.Documents;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

public sealed class TypstPaintTests
{
    [Fact]
    public void Formats_colours()
    {
        Assert.Equal("red", TypstColor.Named("red").ToTypst());
        Assert.Equal("rgb(255, 0, 0)", TypstColor.Rgb(255, 0, 0).ToTypst());
        Assert.Equal("rgb(255, 0, 0, 0.5)", TypstColor.Rgba(255, 0, 0, 0.5).ToTypst());
        Assert.Equal("rgb(\"#ff0000\")", TypstColor.Hex("#ff0000").ToTypst());
        Assert.Equal("rgb(\"#f00\")", TypstColor.Hex("f00").ToTypst());
        Assert.Equal("luma(0.5)", TypstColor.Luma(0.5).ToTypst());
    }

    [Theory]
    [InlineData("zzz")]
    [InlineData("#12345")]
    [InlineData("")]
    public void Rejects_an_invalid_hex_colour(string hex)
    {
        Assert.ThrowsAny<ArgumentException>(() => TypstColor.Hex(hex));
    }

    [Fact]
    public void Rejects_an_alpha_outside_zero_to_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TypstColor.Rgba(0, 0, 0, 1.5));
    }

    [Fact]
    public void Formats_strokes()
    {
        Assert.Equal("none", TypstStroke.None.ToTypst());
        Assert.Equal("1pt", TypstStroke.Solid(TypstLength.Pt(1)).ToTypst());
        Assert.Equal("1pt + red", TypstStroke.Solid(TypstLength.Pt(1), TypstColor.Named("red")).ToTypst());
        Assert.Equal("2pt + rgb(0, 0, 0)", TypstStroke.Of(TypstLength.Pt(2), TypstColor.Rgb(0, 0, 0)).ToTypst());
    }

    [Fact]
    public void Formats_gradients()
    {
        var linear = TypstGradient.Linear(90, TypstColor.Named("red"), TypstColor.Named("blue"));
        var radial = TypstGradient.Radial(TypstColor.Named("white"), TypstColor.Named("black"));

        Assert.Equal("gradient.linear(red, blue, angle: 90deg)", linear.ToTypst());
        Assert.Equal("gradient.radial(white, black)", radial.ToTypst());
    }

    [Fact]
    public void Rejects_a_gradient_with_fewer_than_two_stops()
    {
        Assert.Throws<ArgumentException>(() => TypstGradient.Linear(0, TypstColor.Named("red")));
        Assert.Throws<ArgumentException>(() => TypstGradient.Radial(TypstColor.Named("red")));
    }

    [Fact]
    public void Formats_a_tiling()
    {
        var tiling = TypstTiling.Of(TypstLength.Pt(10), TypstLength.Pt(10), "#rect(fill: red)");

        Assert.Equal("tiling(size: (10pt, 10pt))[#rect(fill: red)]", tiling.ToTypst());
    }

    [Fact]
    public void Formats_angles()
    {
        Assert.Equal("45deg", TypstAngle.Degrees(45).ToString());
        Assert.Equal("1.5rad", TypstAngle.Radians(1.5).ToString());
        Assert.Equal("0.5turn", TypstAngle.Turns(0.5).ToString());
        Assert.Equal("0deg", TypstAngle.Zero.ToString());
    }
}