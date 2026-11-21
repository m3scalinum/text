using System;
using System.Linq;
using M3scalinum.Text.SharpTypst.Compilation;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Integration.Tests;

/// <summary>The immutable world and its cheap clone.</summary>
[Trait("Category", "Integration")]
public sealed class WorldCloneTests
{
    [Fact]
    public void A_clone_compiles_independently_of_its_original()
    {
        using var template = new TypstWorldBuilder()
            .AddSource("main.typ", "#let who = \"original\"\n= Hello #who")
            .Build();

        using var specialised = template.WithSource(
            "main.typ", "#let who = \"clone\"\n= Hello #who");

        var compiler = new TypstCompiler();
        var html = new TypstCompilationOptions { DocumentKind = TypstDocumentKind.Html };
        using var original = compiler.Compile(template, html);
        using var clone = compiler.Compile(specialised, html);

        var originalText = original.ExportText();
        var cloneText = clone.ExportText();

        Assert.Contains("original", originalText, StringComparison.Ordinal);
        Assert.Contains("clone", cloneText, StringComparison.Ordinal);
        Assert.DoesNotContain("clone", originalText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_clone_keeps_the_fonts_of_its_original()
    {
        using var template = new TypstWorldBuilder().AddSource("main.typ", "= x").Build();
        using var clone = template.Clone();

        Assert.Equal(template.FontCount, clone.FontCount);
        Assert.Equal(template.FontFamilies, clone.FontFamilies);
    }

    [Fact]
    public void A_clone_can_be_compiled_from_several_threads_at_once()
    {
        using var template = new TypstWorldBuilder()
            .AddSource("main.typ", "= Shared\n#lorem(40)")
            .Build();

        var compiler = new TypstCompiler();

        var outputs = Enumerable.Range(0, 8)
            .AsParallel()
            .WithDegreeOfParallelism(8)
            .Select(_ =>
            {
                using var clone = template.Clone();
                using var result = compiler.Compile(clone);
                return result.ExportPdf();
            })
            .ToList();

        Assert.Equal(8, outputs.Count);
        Assert.All(outputs, pdf => Assert.True(pdf.Length > 1000));
        Assert.Single(outputs.Select(pdf => pdf.Length).Distinct());
    }
}
