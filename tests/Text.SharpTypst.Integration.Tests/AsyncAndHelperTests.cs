using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using M3scalinum.Text.SharpTypst.Compilation;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Integration.Tests;

/// <summary>Asynchronous compilation and the one-line static helpers.</summary>
[Trait("Category", "Integration")]
public sealed class AsyncAndHelperTests
{
    private const string Markup = """
        = Title

        A paragraph with *bold* text.
        """;

    [Fact]
    public async Task CompileAsync_returns_a_usable_result()
    {
        using var world = new TypstWorldBuilder().AddSource("main.typ", Markup).Build();

        using var result = await new TypstCompiler().CompileAsync(
            world,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.PageCount);
        Assert.NotEmpty(result.ExportPdf());
    }

    [Fact]
    public async Task CompileAsync_honours_an_already_cancelled_token()
    {
        using var world = new TypstWorldBuilder().AddSource("main.typ", Markup).Build();
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new TypstCompiler().CompileAsync(world, cancellationToken: source.Token));
    }

    [Fact]
    public async Task CompileAsync_surfaces_a_compilation_error()
    {
        using var world = new TypstWorldBuilder().AddSource("main.typ", "#panic(\"boom\")").Build();

        await Assert.ThrowsAsync<TypstCompilationException>(
            () => new TypstCompiler().CompileAsync(
                world,
                cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void CompileToSvg_returns_an_svg()
    {
        var svg = Encoding.UTF8.GetString(TypstCompiler.CompileToSvg(Markup));

        Assert.StartsWith("<svg", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileToPng_returns_a_png()
    {
        var png = TypstCompiler.CompileToPng(Markup, ppi: 72f);

        Assert.Equal(0x89, png[0]);
        Assert.Equal((byte)'P', png[1]);
    }

    [Fact]
    public void CompileToHtml_returns_html()
    {
        var html = TypstCompiler.CompileToHtml(Markup);

        Assert.Contains("Title", html, StringComparison.Ordinal);
        Assert.Contains("<", html, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileToMarkdown_returns_markdown()
    {
        var markdown = TypstCompiler.CompileToMarkdown(Markup);

        Assert.Contains("# Title", markdown, StringComparison.Ordinal);
        Assert.Contains("**bold**", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileToText_returns_plain_text()
    {
        var text = TypstCompiler.CompileToText(Markup);

        Assert.Contains("Title", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<", text, StringComparison.Ordinal);
    }
}
