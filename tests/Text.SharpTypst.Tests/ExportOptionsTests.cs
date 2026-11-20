using System;
using System.Text;
using System.Text.Json;
using M3scalinum.Text.SharpTypst.Compilation;
using M3scalinum.Text.SharpTypst.Export;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Tests;

public sealed class ExportOptionsTests
{
    private static (FakeTypstNative Native, CompilationResult Result) NewResult()
    {
        var native = new FakeTypstNative();
        var world = new TypstWorldBuilder(native).AddSource("main.typ", "= Title").Build();
        var result = new TypstCompiler(native).Compile(world);
        world.Dispose();
        return (native, result);
    }

    [Fact]
    public void Svg_export_sends_the_page_and_pretty_flag()
    {
        var (native, result) = NewResult();
        using var _ = result;

        result.ExportSvg(page: 2);

        var (format, options) = native.Exports[0];
        Assert.Equal("svg", format);
        using var json = JsonDocument.Parse(options!);
        Assert.Equal(2, json.RootElement.GetProperty("page").GetInt32());
        Assert.False(json.RootElement.GetProperty("pretty").GetBoolean());
    }

    [Fact]
    public void Merged_svg_export_sends_no_page()
    {
        var (native, result) = NewResult();
        using var _ = result;

        result.ExportSvgMerged();

        using var json = JsonDocument.Parse(native.Exports[0].Options!);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("page").ValueKind);
    }

    [Fact]
    public void Png_export_sends_the_ppi()
    {
        var (native, result) = NewResult();
        using var _ = result;

        result.ExportPng(page: 1, ppi: 300f);

        var (format, options) = native.Exports[0];
        Assert.Equal("png", format);
        using var json = JsonDocument.Parse(options!);
        Assert.Equal(1, json.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(300, json.RootElement.GetProperty("ppi").GetSingle());
    }

    [Fact]
    public void Merged_png_export_keeps_the_ppi_and_drops_the_page()
    {
        var (native, result) = NewResult();
        using var _ = result;

        result.ExportPngMerged(ppi: 72f);

        using var json = JsonDocument.Parse(native.Exports[0].Options!);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("page").ValueKind);
        Assert.Equal(72, json.RootElement.GetProperty("ppi").GetSingle());
    }

    [Fact]
    public void Html_export_is_pretty_by_default_and_decodes_as_utf8()
    {
        var (native, result) = NewResult();
        using var _ = result;
        native.ExportResults["html"] = Encoding.UTF8.GetBytes("<h1>Été</h1>");

        var html = result.ExportHtmlString();

        Assert.Equal("<h1>Été</h1>", html);
        Assert.Equal("html", native.Exports[0].Format);
        using var json = JsonDocument.Parse(native.Exports[0].Options!);
        Assert.True(json.RootElement.GetProperty("pretty").GetBoolean());
    }

    [Fact]
    public void Markdown_and_text_are_forwarded_to_the_native_layer()
    {
        var (native, result) = NewResult();
        using var _ = result;

        result.Export(ExportFormat.Markdown);
        result.Export(ExportFormat.Text);

        Assert.Equal(["markdown", "text"], [native.Exports[0].Format, native.Exports[1].Format]);
    }

    [Fact]
    public void Exporting_after_dispose_throws()
    {
        var (_, result) = NewResult();
        result.Dispose();

        Assert.Throws<ObjectDisposedException>(() => result.ExportSvg());
    }
}
