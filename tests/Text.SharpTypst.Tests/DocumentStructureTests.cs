using System;
using M3scalinum.Text.SharpTypst.Documents;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Tests;

public sealed class DocumentStructureTests
{
    [Fact]
    public void Emits_styled_text()
    {
        var markup = TypstDocument.Create()
            .Paragraph(c => c.Styled(
                "Warning",
                new TypstTextStyle()
                    .Font("DejaVu Sans")
                    .Size(TypstLength.Pt(14))
                    .Weight("bold")
                    .Slant("italic")
                    .Fill(TypstColor.Named("red"))
                    .Tracking(TypstLength.Em(0.1))
                    .Leading(1.4)))
            .ToMarkup();

        Assert.Contains(
            "#text(font: \"DejaVu Sans\", size: 14pt, weight: \"bold\", style: \"italic\", fill: red, tracking: 0.1em, leading: 1.4em)[Warning]",
            markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_an_unknown_slant()
    {
        Assert.Throws<ArgumentException>(() => new TypstTextStyle().Slant("slanted"));
    }

    [Fact]
    public void Emits_a_block_with_a_fill_and_a_stroke()
    {
        var markup = TypstDocument.Create()
            .Block(
                "boxed",
                TypstLength.Percent(50),
                TypstLength.Cm(2),
                TypstColor.Named("silver"),
                TypstStroke.Solid(TypstLength.Pt(1)))
            .ToMarkup();

        Assert.Contains(
            "#block(width: 50%, height: 2cm, fill: silver, stroke: 1pt)[boxed]",
            markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_a_placed_offset()
    {
        var markup = TypstDocument.Create()
            .Place(TypstAlignment.Right, "stamp", TypstLength.Cm(1), TypstLength.Cm(2))
            .ToMarkup();

        Assert.Contains("#place(right, dx: 1cm, dy: 2cm)[stamp]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_an_anisotropic_scale()
    {
        var markup = TypstDocument.Create().Scale(50, 200, "stretched").ToMarkup();

        Assert.Contains("#scale(x: 50%, y: 200%)[stretched]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_a_stack_in_a_direction()
    {
        var markup = TypstDocument.Create()
            .Stack("stacked", TypstDirection.LeftToRight)
            .ToMarkup();

        Assert.Contains("#stack(dir: ltr)[stacked]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_an_inline_quote()
    {
        var markup = TypstDocument.Create().Quote("inline", block: false).ToMarkup();

        Assert.Contains("#quote[inline]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_an_image_with_a_height_and_a_fit()
    {
        var markup = TypstDocument.Create()
            .Figure(f => f.Image("logo.png", TypstLength.Cm(3), TypstLength.Cm(2), "contain"))
            .ToMarkup();

        Assert.Contains(
            "image(\"logo.png\", width: 3cm, height: 2cm, fit: \"contain\")",
            markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_an_unknown_image_fit()
    {
        var figure = new TypstFigure();

        Assert.Throws<ArgumentException>(() => figure.Image("logo.png", fit: "squash"));
    }

    [Fact]
    public void Emits_a_context_block()
    {
        var markup = TypstDocument.Create()
            .Context("counter(page).display()")
            .ToMarkup();

        Assert.Contains("#context {counter(page).display()}", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_breaks_and_direction()
    {
        var markup = TypstDocument.Create()
            .TextDirection(TypstDirection.RightToLeft)
            .Paragraph("first")
            .ColumnBreak()
            .ParagraphBreak()
            .ToMarkup();

        Assert.Contains("#set text(dir: rtl)", markup, StringComparison.Ordinal);
        Assert.Contains("#colbreak()", markup, StringComparison.Ordinal);
        Assert.Contains("#parbreak()", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_a_rotation_from_an_angle()
    {
        var markup = TypstDocument.Create()
            .Rotate(TypstAngle.Degrees(45), "tilted")
            .ToMarkup();

        Assert.Contains("#rotate(45deg)[tilted]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_document_level_footnote_cite_and_metadata()
    {
        var markup = TypstDocument.Create()
            .Footnote("a note")
            .Cite("doe2026")
            .MetadataEntry("source", "erp")
            .Lorem(10)
            .ToMarkup();

        Assert.Contains("#footnote[a note]", markup, StringComparison.Ordinal);
        Assert.Contains("@doe2026", markup, StringComparison.Ordinal);
        Assert.Contains("#metadata((source: \"erp\"))", markup, StringComparison.Ordinal);
        Assert.Contains("#lorem(10)", markup, StringComparison.Ordinal);
    }
}