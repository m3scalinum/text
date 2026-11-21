using System;
using System.Buffers.Binary;
using System.Text;
using M3scalinum.Text.Typst.Compilation;
using M3scalinum.Text.Typst.Export;
using Xunit;

namespace M3scalinum.Text.Typst.Integration.Tests;

/// <summary>
/// Export formats against the real compiler. Filtered out on machines without the native
/// library: <c>-- -filter "/*/*/*[Category!=Integration]"</c>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class FormatTests
{
    private const string Markup = """
        #set page(paper: "a4", margin: 2cm)
        = Formats
        A paragraph with *strong* text.
        """;

    private static CompilationResult Compile(string markup = Markup, TypstDocumentKind kind = TypstDocumentKind.Paged)
    {
        var world = new TypstWorldBuilder().AddSource("main.typ", markup).Build();
        var result = new TypstCompiler().Compile(world, new TypstCompilationOptions { DocumentKind = kind });
        world.Dispose();
        return result;
    }

    [Fact]
    public void Exports_svg_for_one_page()
    {
        using var result = Compile();

        var svg = Encoding.UTF8.GetString(result.ExportSvg());

        Assert.Contains("<svg", svg, StringComparison.Ordinal);
        Assert.Contains("</svg>", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void Exports_a_merged_svg_with_one_root()
    {
        using var result = Compile();

        var svg = Encoding.UTF8.GetString(result.ExportSvgMerged());

        Assert.Equal(1, CountOccurrences(svg, "<svg"));
    }

    [Fact]
    public void Exports_png_with_the_png_signature()
    {
        using var result = Compile();

        var png = result.ExportPng();

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
    }

    [Fact]
    public void Png_resolution_scales_with_ppi()
    {
        using var result = Compile();

        var at72 = PngWidth(result.ExportPng(ppi: 72f));
        var at144 = PngWidth(result.ExportPng(ppi: 144f));

        // Page width in points is not an integer, so each resolution rounds independently.
        Assert.InRange(at144, (at72 * 2) - 1, (at72 * 2) + 1);
    }

    [Fact]
    public void Exports_html_from_an_html_document()
    {
        using var result = Compile(kind: TypstDocumentKind.Html);

        var html = result.ExportHtmlString();

        Assert.Contains("<html", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Formats", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Html_document_has_no_pages()
    {
        using var result = Compile(kind: TypstDocumentKind.Html);

        Assert.Equal(0, result.PageCount);
    }

    [Fact]
    public void Compile_both_produces_pdf_and_html_from_one_source()
    {
        using var world = new TypstWorldBuilder().AddSource("main.typ", Markup).Build();
        using var both = new TypstCompiler().CompileBoth(world);

        var pdf = both.Paged.ExportPdf();
        var html = both.Html.ExportHtmlString();

        Assert.Equal("%PDF"u8.ToArray(), pdf[..4]);
        Assert.Contains("<html", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_a_page_out_of_range()
    {
        using var result = Compile();

        var exception = Assert.Throws<TypstNativeException>(() => result.ExportSvg(page: 99));

        Assert.Equal(TypstErrorCode.InvalidArgument, exception.Code);
        Assert.Contains("out of range", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_html_export_from_a_paged_document()
    {
        using var result = Compile();

        var exception = Assert.Throws<TypstNativeException>(() => result.Export(ExportFormat.Html));

        Assert.Equal(TypstErrorCode.InvalidArgument, exception.Code);
    }

    [Fact]
    public void Svg_output_is_deterministic()
    {
        using var first = Compile();
        using var second = Compile();

        Assert.Equal(first.ExportSvg(), second.ExportSvg());
    }

    private static int PngWidth(byte[] png) => (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16, 4));

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
