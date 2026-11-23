using System;
using M3scalinum.Text.Documents;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

public sealed class TextWeightTests
{
    private static string Styled(TypstTextStyle style) =>
        TypstDocument.Create()
            .Paragraph(c => c.Styled("x", style))
            .ToMarkup();

    [Fact]
    public void Emits_a_numeric_weight_unquoted()
    {
        // A variable font interpolates the axis: 350 is not one of the nine names and Typst only
        // accepts it as a number. Quoting it made the compiler reject the whole document.
        var markup = Styled(new TypstTextStyle().Weight(350));

        Assert.Contains("#text(weight: 350)[x]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_a_named_weight_quoted()
    {
        var markup = Styled(new TypstTextStyle().Weight("semibold"));

        Assert.Contains("#text(weight: \"semibold\")[x]", markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("350")]
    [InlineData("400")]
    [InlineData("1000")]
    public void Refuses_a_numeric_string_and_points_at_the_numeric_overload(string weight)
    {
        var error = Assert.Throws<ArgumentException>(() => new TypstTextStyle().Weight(weight));

        Assert.Contains("Weight(int)", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public void Refuses_a_weight_outside_the_axis_range(int weight)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TypstTextStyle().Weight(weight));
    }
}
