using System;
using System.Linq;
using M3scalinum.Text.Typst.Compilation;
using M3scalinum.Text.Typst.Export;
using Xunit;

namespace M3scalinum.Text.Typst.Integration.Tests;

/// <summary>
/// The font inventory against the real native library: the numbers are read back out of a PDF that
/// an actual compilation encoded, so a decoder defaulting to zero, or a walker that loses the
/// stream bodies, fails here.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PdfFontInventoryIntegrationTests
{
    private const string Document = """
        = Font inventory

        A paragraph of body text, in the font the template chose, long enough that the layout has to
        set more than a handful of glyphs and the subset is worth measuring.
        """;

    private static CompilationResult Compile()
    {
        using var world = new TypstWorldBuilder().AddSource("main.typ", Document).Build();
        return new TypstCompiler().Compile(world);
    }

    [Fact]
    public void A_real_export_embeds_fonts_and_the_inventory_finds_them()
    {
        using var result = Compile();

        var inventory = result.MeasurePdfFonts();

        Assert.NotEmpty(inventory.Fonts);
        Assert.All(inventory.Fonts, font => Assert.True(font.Bytes > 0, $"{font.Name} has bytes"));
        Assert.Equal(inventory.Fonts.Sum(font => font.Bytes), inventory.FontBytes);
        Assert.True(inventory.FontBytes > 0, "a text document embeds at least one font");
        Assert.True(
            inventory.FontBytes < inventory.Bytes,
            $"fonts ({inventory.FontBytes}) cannot exceed the file ({inventory.Bytes})");
        Assert.InRange(inventory.FontShare, 0.01, 1.0);
    }

    [Fact]
    public void The_inventory_describes_the_bytes_the_export_produces()
    {
        using var result = Compile();

        using var pdf = new System.IO.MemoryStream();
        result.ExportPdf(pdf);
        var inventory = result.MeasurePdfFonts();

        Assert.Equal(pdf.Length, inventory.Bytes);
    }

    [Fact]
    public void Every_embedded_face_carries_a_subset_tag()
    {
        using var result = Compile();

        var inventory = result.MeasurePdfFonts();

        // Typst's writer subsets unconditionally: there is no whole-font mode to contrast with, and
        // the tag is how the file says so.
        Assert.All(inventory.Fonts, font =>
        {
            Assert.True(font.IsSubset, $"{font.Name} is a subset");
            Assert.NotNull(font.SubsetTag);
            Assert.Equal(6, font.SubsetTag!.Length);
            Assert.DoesNotContain("+", font.Family);
        });
    }

    [Fact]
    public void The_report_names_the_font_the_document_used()
    {
        using var result = Compile();

        var inventory = result.MeasurePdfFonts();

        Assert.Contains(inventory.Fonts, font =>
            font.Family.Contains("New Computer Modern", StringComparison.OrdinalIgnoreCase)
            || font.Family.Contains("NewCM", StringComparison.OrdinalIgnoreCase)
            || font.Family.Contains("Libertinus", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_document_that_uses_no_glyphs_embeds_no_font()
    {
        using var world = new TypstWorldBuilder().AddSource("main.typ", "#pagebreak()").Build();
        using var result = new TypstCompiler().Compile(world);

        var inventory = result.MeasurePdfFonts();

        Assert.Empty(inventory.Fonts);
        Assert.Equal(0, inventory.FontBytes);
        Assert.True(inventory.Bytes > 0);
    }
}
