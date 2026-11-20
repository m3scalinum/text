using System;
using M3scalinum.Text.SharpTypst.Documents;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Tests;

public sealed class TypstLengthTests
{
    [Fact]
    public void Formats_with_the_invariant_culture_and_trims_zeros()
    {
        Assert.Equal("2cm", TypstLength.Cm(2).ToString());
        Assert.Equal("1.5cm", TypstLength.Cm(1.5).ToString());
        Assert.Equal("12.5pt", TypstLength.Pt(12.5).ToString());
        Assert.Equal("80%", TypstLength.Percent(80).ToString());
        Assert.Equal("1fr", TypstLength.Fr(1).ToString());
        Assert.Equal("0pt", TypstLength.Zero.ToString());
    }

    [Fact]
    public void Rejects_non_finite_values()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TypstLength.Cm(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => TypstLength.Cm(double.PositiveInfinity));
    }
}