using System;
using System.Buffers.Binary;
using System.Globalization;
using System.Text.RegularExpressions;
using M3scalinum.Text.Typst.Compilation;
using Xunit;

namespace M3scalinum.Text.Typst.Integration.Tests;

/// <summary>
/// The merged exports must reach the native <c>svg-merged</c> and <c>png-merged</c> formats, not
/// the single-page ones with a null page index.
/// </summary>
[Trait("Category", "Integration")]
public sealed partial class MergedExportTests
{
    private const string TwoPages = """
        = First page

        #pagebreak()

        = Second page
        """;

    private static CompilationResult Compile(string markup)
    {
        var world = new TypstWorldBuilder().AddSource("main.typ", markup).Build();
        return new TypstCompiler().Compile(world);
    }

    private static (int Width, int Height) PngSize(byte[] png)
    {
        Assert.True(png.Length > 24);
        Assert.Equal(0x89, png[0]);
        Assert.Equal((byte)'P', png[1]);

        var width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));

        return (width, height);
    }

    private static double SvgHeight(string svg)
    {
        var match = HeightAttribute().Match(svg);
        Assert.True(match.Success, "the SVG has no height attribute");

        return double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    [GeneratedRegex("height=\"([0-9.]+)pt\"")]
    private static partial Regex HeightAttribute();

    [Fact]
    public void Merged_png_stacks_every_page()
    {
        using var result = Compile(TwoPages);

        Assert.Equal(2, result.PageCount);

        var single = PngSize(result.ExportPng(page: 0, ppi: 72f));
        var merged = PngSize(result.ExportPngMerged(ppi: 72f));

        Assert.Equal(single.Width, merged.Width);
        Assert.True(
            merged.Height > single.Height * 2,
            $"expected a stacked image taller than {single.Height * 2}px, got {merged.Height}px");
    }

    [Fact]
    public void Merged_svg_stacks_every_page()
    {
        using var result = Compile(TwoPages);

        var single = SvgHeight(System.Text.Encoding.UTF8.GetString(result.ExportSvg(page: 0)));
        var merged = SvgHeight(System.Text.Encoding.UTF8.GetString(result.ExportSvgMerged()));

        Assert.True(
            merged > single * 2,
            $"expected a merged SVG taller than {single * 2}pt, got {merged}pt");
    }

    [Fact]
    public void A_single_page_document_merges_to_one_page()
    {
        using var result = Compile("= Only page");

        var single = PngSize(result.ExportPng(page: 0, ppi: 72f));
        var merged = PngSize(result.ExportPngMerged(ppi: 72f));

        Assert.Equal(single.Width, merged.Width);
        Assert.Equal(single.Height, merged.Height);
    }
}
