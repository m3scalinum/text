using System;
using System.Text;
using M3scalinum.Text.Typst.Compilation;
using M3scalinum.Text.Typst.Diagnostics;
using M3scalinum.Text.Documents;
using Xunit;

namespace M3scalinum.Text.Typst.Integration.Tests;

/// <summary>The advanced surface — expressions, introspection, data, math, formats — compiled for real.</summary>
[Trait("Category", "Integration")]
public sealed class AdvancedSurfaceTests
{
    private static TypstWorldBuilder World(string markup) => new TypstWorldBuilder().AddSource("main.typ", markup);

    [Fact]
    public void Compiles_let_set_and_show_rules()
    {
        var document = TypstDocument.Create()
            .Let("total", TypstExpression.Number(1200))
            .Set("text(size: 11pt)")
            .Show("heading", "it => text(fill: navy)[#it]")
            .Heading(1, "Report")
            .Paragraph(c => c.Text("Total: ").Text("1200"));

        using var world = World(document.ToMarkup()).Build();
        using var result = new TypstCompiler().Compile(world);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Compiles_a_context_block_with_counters_and_states()
    {
        var document = TypstDocument.Create()
            .Page(p => p.Paper("a4").Margin(TypstLength.Cm(2)))
            .Heading(1, "First")
            .Context(context => context
                .Let("pages", TypstCounter.Page.Final())
                .Statement(TypstCounter.Page.Display("1 / 1")))
            .Heading(1, "Second")
            .Context(context => context.Statement(TypstQuery.Headings(1)));

        using var world = World(document.ToMarkup()).Build();
        using var result = new TypstCompiler().Compile(world);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Compiles_a_state_round_trip()
    {
        var state = TypstState.Of("seen", TypstExpression.Bool(false));

        var document = TypstDocument.Create()
            .Context(context => context.Statement(state.Update(TypstExpression.Bool(true))))
            .Context(context => context.Statement(state.Get()));

        using var world = World(document.ToMarkup()).Build();
        using var result = new TypstCompiler().Compile(world);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Compiles_math_functions()
    {
        var document = TypstDocument.Create()
            .Math(TypstMath.Frac("a^2 + b^2", "c"))
            .Math(TypstMath.Mat(["1", "2"], ["3", "4"]))
            .Math(TypstMath.Sqrt("x + 1"))
            .Math(TypstMath.Binom("n", "k"))
            .Math(TypstMath.Op("lim", limits: true))
            .Math(TypstMath.Underbrace("1 + 2 + 3", "6"))
            .Math(TypstMath.Bb("N"))
            .Math(TypstMath.Cal("P"))
            .Math(TypstMath.Cases("x & \"if\" x > 0", "-x & \"otherwise\""));

        using var world = World(document.ToMarkup()).Build();
        using var result = new TypstCompiler().Compile(world);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Compiles_data_loading_from_an_asset()
    {
        const string json = """{"items": [{"name": "hosting", "amount": 1200}]}""";

        var document = TypstDocument.Create()
            .Let("data", TypstData.Json("data.json"))
            .Context(context => context.Raw("data.items.len()"));

        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", document.ToMarkup())
            .AddAsset("data.json", Encoding.UTF8.GetBytes(json))
            .Build();

        using var result = new TypstCompiler().Compile(world);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Compiles_pdf_artifacts()
    {
        var document = TypstDocument.Create()
            .Page(p => p.Paper("a4").Margin(TypstLength.Cm(2)))
            .Expression(TypstPdf.Artifact(new TypstContent().Text("draft")))
            .Paragraph("body");

        using var world = World(document.ToMarkup()).Build();
        using var result = new TypstCompiler().Compile(world);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Compiles_html_elements_in_html_mode()
    {
        var document = TypstDocument.Create()
            .Expression(TypstHtml.Elem("div", new TypstContent().Text("hello"), ("class", "box")));

        using var world = World(document.ToMarkup()).Build();
        using var result = new TypstCompiler().CompileBoth(world);

        // `html.elem` is ignored by the paged exporter, which warns; the HTML export carries it.
        Assert.DoesNotContain(result.Paged.Diagnostics, diagnostic => diagnostic.Severity == TypstSeverity.Error);
        Assert.Contains("hello", result.Html.ExportHtmlString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Compiles_an_expression_block()
    {
        var document = TypstDocument.Create()
            .Let("x", TypstExpression.Number(6))
            .Expression(TypstExpression.Raw("x * 7"));

        using var world = World(document.ToMarkup()).Build();
        using var result = new TypstCompiler().Compile(world);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Compiles_an_include()
    {
        var document = TypstDocument.Create()
            .Include("part.typ")
            .Paragraph("after");

        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", document.ToMarkup())
            .AddSource("part.typ", "= Included\n\nFrom another file.")
            .Build();

        using var result = new TypstCompiler().Compile(world);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Compiles_an_import()
    {
        var document = TypstDocument.Create()
            .Import("lib.typ", "greet")
            .Expression(TypstExpression.Raw("greet(\"world\")"));

        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", document.ToMarkup())
            .AddSource("lib.typ", "#let greet(name) = [Hello, #name!]")
            .Build();

        using var result = new TypstCompiler().Compile(world);

        Assert.Empty(result.Diagnostics);
    }
}
