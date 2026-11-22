using System.Text.Json;
using M3scalinum.Text.Typst.Compilation;
using M3scalinum.Text.Typst.Export;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

/// <summary>
/// The managed mapping of the <c>pdf-fonts</c> report: the format string, the options forwarded and
/// the decoding of what the native side sends back.
/// </summary>
public sealed class PdfFontInventoryTests
{
    private const string Report = """
        {"bytes":1000,"fontBytes":400,"fonts":[
          {"name":"QKJXZB+NewCM10-Regular","family":"NewCM10-Regular","subsetTag":"QKJXZB","subset":true,"format":"truetype","bytes":300},
          {"name":"ABCDEF+NewCM10-Italic","family":"NewCM10-Italic","subsetTag":"ABCDEF","subset":true,"format":"cff","bytes":100}
        ]}
        """;

    private static (FakeTypstNative Native, CompilationResult Result) NewResult()
    {
        var native = new FakeTypstNative();
        native.ExportResults["pdf-fonts"] = System.Text.Encoding.UTF8.GetBytes(Report);
        var world = new TypstWorldBuilder(native).AddSource("main.typ", "= Title").Build();
        var result = new TypstCompiler(native).Compile(world);
        world.Dispose();
        return (native, result);
    }

    [Fact]
    public void The_report_asks_the_native_side_for_the_pdf_fonts_format()
    {
        var (native, result) = NewResult();
        using var _ = result;

        result.MeasurePdfFonts();

        Assert.Equal("pdf-fonts", native.Exports[0].Format);
    }

    [Fact]
    public void The_pdf_options_travel_with_the_measurement()
    {
        var (native, result) = NewResult();
        using var _ = result;

        result.MeasurePdfFonts(new PdfExportOptions { PageRanges = "2", Tagged = false });

        using var json = JsonDocument.Parse(native.Exports[0].Options!);
        var pdf = json.RootElement.GetProperty("pdf");
        Assert.Equal("2", pdf.GetProperty("pageRanges").GetString());
        Assert.False(pdf.GetProperty("tagged").GetBoolean());
    }

    [Fact]
    public void Every_embedded_font_is_decoded_with_its_bytes_and_format()
    {
        var (_, result) = NewResult();
        using var _ = result;

        var inventory = result.MeasurePdfFonts();

        Assert.Equal(1000, inventory.Bytes);
        Assert.Equal(400, inventory.FontBytes);
        Assert.Equal(0.4, inventory.FontShare, 3);
        Assert.Equal(2, inventory.Fonts.Count);

        var first = inventory.Fonts[0];
        Assert.Equal("QKJXZB+NewCM10-Regular", first.Name);
        Assert.Equal("NewCM10-Regular", first.Family);
        Assert.Equal("QKJXZB", first.SubsetTag);
        Assert.True(first.IsSubset);
        Assert.Equal(PdfFontFormat.TrueType, first.Format);
        Assert.Equal(300, first.Bytes);

        Assert.Equal(PdfFontFormat.Cff, inventory.Fonts[1].Format);
    }

    [Fact]
    public void A_measurement_is_not_an_export_of_the_caller()
    {
        var (_, result) = NewResult();
        using var _ = result;

        result.MeasurePdfFonts();

        // The caller received no bytes, so the result's own export reading is unchanged.
        Assert.Null(result.LastExport);
    }

    [Fact]
    public void A_font_without_a_subset_tag_is_reported_as_a_whole_font()
    {
        var (native, result) = NewResult();
        using var _ = result;

        native.ExportResults["pdf-fonts"] = System.Text.Encoding.UTF8.GetBytes(
            """{"bytes":10,"fontBytes":10,"fonts":[{"name":"Plain","family":"Plain","subset":false,"format":"type1","bytes":10}]}""");

        var font = Assert.Single(result.MeasurePdfFonts().Fonts);

        Assert.False(font.IsSubset);
        Assert.Null(font.SubsetTag);
        Assert.Equal(PdfFontFormat.Type1, font.Format);
    }

    [Fact]
    public void An_unknown_format_does_not_throw()
    {
        var (native, result) = NewResult();
        using var _ = result;

        native.ExportResults["pdf-fonts"] = System.Text.Encoding.UTF8.GetBytes(
            """{"bytes":5,"fontBytes":5,"fonts":[{"name":"X","family":"X","format":"woff9000","bytes":5}]}""");

        Assert.Equal(PdfFontFormat.Unknown, Assert.Single(result.MeasurePdfFonts().Fonts).Format);
    }
}
