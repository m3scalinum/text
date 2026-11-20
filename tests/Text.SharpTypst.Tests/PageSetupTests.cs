using M3scalinum.Text.SharpTypst.Documents;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Tests;

/// <summary>Page setup: margins, orientation, fill and columns.</summary>
public sealed class PageSetupTests
{
    private static string Markup(System.Action<TypstPageSetup> configure)
        => TypstDocument.Create().Page(configure).Paragraph("x").ToMarkup();

    [Fact]
    public void A_uniform_margin_emits_a_single_length()
    {
        var markup = Markup(page => page.Margin(TypstLength.Cm(2)));

        Assert.Contains("margin: 2cm", markup, System.StringComparison.Ordinal);
        Assert.DoesNotContain("margin: (", markup, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Per_side_margins_emit_a_dictionary()
    {
        // CSS order: top, right, bottom, left.
        var markup = Markup(page => page.Margin(
            TypstLength.Cm(2),
            TypstLength.Cm(1.5),
            TypstLength.Cm(3),
            TypstLength.Cm(1)));

        Assert.Contains(
            "margin: (top: 2cm, bottom: 3cm, left: 1cm, right: 1.5cm)",
            markup,
            System.StringComparison.Ordinal);
    }

    [Fact]
    public void MarginX_sets_only_the_horizontal_sides()
    {
        var markup = Markup(page => page.MarginX(TypstLength.Cm(4)));

        Assert.Contains("margin: (left: 4cm, right: 4cm)", markup, System.StringComparison.Ordinal);
    }

    [Fact]
    public void MarginY_sets_only_the_vertical_sides()
    {
        var markup = Markup(page => page.MarginY(TypstLength.Mm(15)));

        Assert.Contains("margin: (top: 15mm, bottom: 15mm)", markup, System.StringComparison.Ordinal);
    }

    [Fact]
    public void A_uniform_margin_clears_the_per_side_ones()
    {
        var markup = Markup(page => page
            .Margin(TypstLength.Cm(1), TypstLength.Cm(4), TypstLength.Cm(2), TypstLength.Cm(3))
            .Margin(TypstLength.Cm(5)));

        Assert.Contains("margin: 5cm", markup, System.StringComparison.Ordinal);
        Assert.DoesNotContain("top:", markup, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Per_side_margins_clear_the_uniform_one()
    {
        var markup = Markup(page => page
            .Margin(TypstLength.Cm(5))
            .MarginX(TypstLength.Cm(2)));

        Assert.Contains("margin: (left: 2cm, right: 2cm)", markup, System.StringComparison.Ordinal);
        Assert.DoesNotContain("margin: 5cm", markup, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Landscape_emits_flipped()
    {
        var markup = Markup(page => page.Paper("a4").Landscape());

        Assert.Contains("flipped: true", markup, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Landscape_can_be_turned_back_off()
    {
        var markup = Markup(page => page.Landscape().Landscape(false));

        Assert.DoesNotContain("flipped", markup, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Fill_emits_the_paint()
    {
        var markup = Markup(page => page.Fill(TypstColor.Rgb(250, 250, 240)));

        Assert.Contains("fill: rgb(250, 250, 240)", markup, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Columns_emits_the_count()
    {
        var markup = Markup(page => page.Columns(2));

        Assert.Contains("columns: 2", markup, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Columns_rejects_a_non_positive_count()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() => Markup(page => page.Columns(0)));
    }

    [Fact]
    public void Fill_rejects_null()
    {
        Assert.Throws<System.ArgumentNullException>(() => Markup(page => page.Fill(null!)));
    }

    [Fact]
    public void Everything_combines_into_one_set_page()
    {
        var markup = Markup(page => page
            .Paper("a4")
            .Landscape()
            .Margin(TypstLength.Cm(2), TypstLength.Cm(3), TypstLength.Cm(2), TypstLength.Cm(3))
            .Fill(TypstColor.White)
            .Columns(2)
            .Numbering("1 / 1"));

        Assert.Contains("paper: \"a4\"", markup, System.StringComparison.Ordinal);
        Assert.Contains("flipped: true", markup, System.StringComparison.Ordinal);
        Assert.Contains("margin: (top: 2cm, bottom: 2cm, left: 3cm, right: 3cm)", markup, System.StringComparison.Ordinal);
        Assert.Contains("fill: white", markup, System.StringComparison.Ordinal);
        Assert.Contains("columns: 2", markup, System.StringComparison.Ordinal);
        Assert.Contains("numbering: \"1 / 1\"", markup, System.StringComparison.Ordinal);
    }
}
