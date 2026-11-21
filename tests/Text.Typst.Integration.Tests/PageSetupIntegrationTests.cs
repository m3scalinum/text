using System;
using System.Buffers.Binary;
using M3scalinum.Text.Typst.Compilation;
using M3scalinum.Text.Documents;
using Xunit;

namespace M3scalinum.Text.Typst.Integration.Tests;

/// <summary>Page setup, compiled by the real compiler.</summary>
[Trait("Category", "Integration")]
public sealed class PageSetupIntegrationTests
{
    private static (int Width, int Height) PngSize(byte[] png)
        => (BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)),
            BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));

    [Fact]
    public void A_landscape_page_is_wider_than_it_is_tall()
    {
        var document = TypstDocument.Create()
            .Page(page => page.Paper("a4").Landscape())
            .Paragraph("Landscape");

        var (width, height) = PngSize(TypstCompiler.CompileToPng(document.ToMarkup()));

        Assert.True(width > height, $"expected a landscape page, got {width}x{height}");
    }

    [Fact]
    public void A_portrait_page_is_taller_than_it_is_wide()
    {
        var document = TypstDocument.Create()
            .Page(page => page.Paper("a4"))
            .Paragraph("Portrait");

        var (width, height) = PngSize(TypstCompiler.CompileToPng(document.ToMarkup()));

        Assert.True(height > width, $"expected a portrait page, got {width}x{height}");
    }

    [Fact]
    public void Per_side_margins_columns_and_fill_compile_cleanly()
    {
        var document = TypstDocument.Create()
            .Page(page => page
                .Paper("a4")
                .Margin(
                    TypstLength.Cm(2),
                    TypstLength.Cm(2.5),
                    TypstLength.Cm(3),
                    TypstLength.Cm(1.5))
                .Fill(TypstColor.Rgb(250, 250, 240))
                .Columns(2))
            .Heading(1, "Two columns")
            .Paragraph("Body text in a two-column layout.");

        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", document.ToMarkup())
            .Build();

        using var result = new TypstCompiler().Compile(world);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, result.PageCount);
    }

    [Fact]
    public void A_footer_with_page_numbers_compiles()
    {
        var document = TypstDocument.Create()
            .Page(page => page
                .Paper("a4")
                .WithFooter(TypstContent.Empty.Text("Page ").PageNumber().Text(" of ").PageCount()))
            .Paragraph("Body");

        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", document.ToMarkup())
            .Build();

        using var result = new TypstCompiler().Compile(world);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, result.PageCount);
    }

    [Fact]
    public void A_narrower_margin_leaves_more_room_for_text()
    {
        static int Lines(string margin) => TypstCompiler.CompileToPng(
            TypstDocument.Create()
                .Page(page => page.Paper("a4").Margin(TypstLength.Cm(double.Parse(margin, System.Globalization.CultureInfo.InvariantCulture))))
                .Paragraph(string.Concat(System.Linq.Enumerable.Repeat("word ", 400)))
                .ToMarkup()).Length;

        // A wider margin pushes the same text onto more pages, so the merged image is taller.
        var narrow = Lines("1");
        var wide = Lines("5");

        Assert.True(wide > narrow, $"narrow={narrow} wide={wide}");
    }
}
