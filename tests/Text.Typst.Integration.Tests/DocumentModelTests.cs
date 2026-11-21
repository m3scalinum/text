using M3scalinum.Text.Typst.Compilation;
using M3scalinum.Text.Documents;
using Xunit;

namespace M3scalinum.Text.Typst.Integration.Tests;

/// <summary>The document model, compiled by the real compiler.</summary>
[Trait("Category", "Integration")]
public sealed class DocumentModelTests
{
    private static TypstDocument Report() => TypstDocument.Create()
        .Metadata(m => m.Title("Q3 2026 revenue report").Author("Finance").Language("en"))
        .Page(p => p.Paper("a4").Margin(TypstLength.Cm(2)).Numbering("1 / 1"))
        .Heading(1, "Revenue")
        .Paragraph(c => c
            .Text("Revenue grew by ")
            .Bold("12%")
            .Text(" year over year, driven by ")
            .Emphasis("subscriptions")
            .Text("."))
        .Heading(2, "Breakdown")
        .Table(t => t
            .Columns(TypstLength.Fr(2), TypstLength.Fr(1))
            .HeaderRow("Item", "Amount")
            .Row("Hosting", "1 200 EUR")
            .Row("Support", "800 EUR"))
        .BulletList("Hosting", "Support")
        .Code("rust", "fn main() {}")
        .Math("sum_(k=1)^n k = (n(n+1))/2")
        .Divider()
        .Paragraph(c => c.Text("See ").Link("https://typst.app", "the reference").Text("."));

    [Fact]
    public void Compiles_a_model_built_document()
    {
        var pdf = TypstCompiler.CompileToPdf(Report());

        Assert.Equal("%PDF"u8.ToArray(), pdf[..4]);
        Assert.True(pdf.Length > 1000);
    }

    [Fact]
    public void Produces_a_single_page()
    {
        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", Report().ToMarkup())
            .Build();
        using var result = new TypstCompiler().Compile(world);

        Assert.Equal(1, result.PageCount);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Escapes_text_that_would_otherwise_be_markup()
    {
        var document = TypstDocument.Create()
            .Paragraph("Cost: 5 * 3 = 15 # not a heading @ not a reference");

        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", document.ToMarkup())
            .Build();
        using var result = new TypstCompiler().Compile(world);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Emits_markup_that_is_stable()
    {
        Assert.Equal(Report().ToMarkup(), Report().ToMarkup());
    }

    [Fact]
    public void Compiles_shapes_paints_and_decorations()
    {
        var document = TypstDocument.Create()
            .Page(p => p.Paper("a4").Margin(TypstLength.Cm(2)))
            .Heading(1, "Shapes")
            .Add(new TypstContent()
                .Rect(TypstLength.Cm(4), TypstLength.Cm(2), TypstColor.Named("navy"), TypstStroke.Solid(TypstLength.Pt(1)))
                .Line(TypstLength.Percent(100), stroke: TypstStroke.Solid(TypstLength.Pt(0.5), TypstColor.Named("gray")))
                .Circle(TypstLength.Cm(1), TypstGradient.Radial(TypstColor.Named("white"), TypstColor.Named("navy")))
                .Polygon(
                    TypstColor.Named("orange"),
                    TypstStroke.None,
                    (TypstLength.Pt(0), TypstLength.Pt(0)),
                    (TypstLength.Cm(2), TypstLength.Pt(0)),
                    (TypstLength.Cm(1), TypstLength.Cm(1))))
            .Paragraph(c => c
                .Upper("small caps")
                .Text(" ")
                .SmallCaps("and small caps")
                .Text(" ")
                .Highlight("highlighted")
                .Text(" ")
                .Subscript("2")
                .Superscript("3")
                .Footnote("a note"))
            .MetadataEntry("source", "erp", "meta:source");

        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", document.ToMarkup())
            .Build();
        using var result = new TypstCompiler().Compile(world);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, result.PageCount);
    }

    [Fact]
    public void Compiles_a_right_to_left_document()
    {
        var document = TypstDocument.Create()
            .TextDirection(TypstDirection.RightToLeft)
            .Paragraph("مرحبا بالعالم");

        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", document.ToMarkup())
            .Build();
        using var result = new TypstCompiler().Compile(world);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Compiles_styled_text_blocks_and_layout_wrappers()
    {
        var document = TypstDocument.Create()
            .Page(p => p.Paper("a4").Margin(TypstLength.Cm(2)))
            .Paragraph(c => c.Styled(
                "Warning",
                new TypstTextStyle()
                    .Font("Libertinus Serif")
                    .Size(TypstLength.Pt(14))
                    .Weight("bold")
                    .Fill(TypstColor.Named("red"))))
            .Block(
                "boxed",
                TypstLength.Percent(50),
                TypstLength.Cm(2),
                TypstColor.Named("silver"),
                TypstStroke.Solid(TypstLength.Pt(1)))
            .Stack("stacked", TypstDirection.LeftToRight)
            .Scale(50, 200, "stretched")
            .Place(TypstAlignment.Right, "stamp", TypstLength.Cm(1), TypstLength.Cm(2))
            .Quote("inline", block: false);

        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", document.ToMarkup())
            .Build();
        using var result = new TypstCompiler().Compile(world);

        Assert.Empty(result.Diagnostics);
    }
}
