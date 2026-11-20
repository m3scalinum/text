using System;
using System.IO;
using System.Linq;
using M3scalinum.Text.SharpTypst.Compilation;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Integration.Tests;

/// <summary>Compilation options and world introspection.</summary>
[Trait("Category", "Integration")]
public sealed class CompilationOptionsTests
{
    private const string WithWarning = """
        #set text(font: "A Font That Does Not Exist")

        = Still compiles
        """;

    private const string Clean = "= Fine";

    [Fact]
    public void A_warning_does_not_fail_by_default()
    {
        using var world = new TypstWorldBuilder().AddSource("main.typ", WithWarning).Build();
        using var result = new TypstCompiler().Compile(world);

        Assert.NotEmpty(result.Diagnostics);
        Assert.Equal(1, result.PageCount);
    }

    [Fact]
    public void WarningsAsErrors_turns_a_warning_into_a_failure()
    {
        using var world = new TypstWorldBuilder().AddSource("main.typ", WithWarning).Build();

        var exception = Assert.Throws<TypstCompilationException>(
            () => new TypstCompiler().Compile(
                world,
                new TypstCompilationOptions { WarningsAsErrors = true }));

        Assert.NotEmpty(exception.Diagnostics);
    }

    [Fact]
    public void WarningsAsErrors_leaves_a_clean_document_alone()
    {
        using var world = new TypstWorldBuilder().AddSource("main.typ", Clean).Build();
        using var result = new TypstCompiler().Compile(
            world,
            new TypstCompilationOptions { WarningsAsErrors = true });

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void The_result_reports_its_document_kind()
    {
        using var world = new TypstWorldBuilder().AddSource("main.typ", Clean).Build();
        var compiler = new TypstCompiler();

        using var paged = compiler.Compile(world);
        using var html = compiler.Compile(world, new TypstCompilationOptions
        {
            DocumentKind = TypstDocumentKind.Html,
        });

        Assert.Equal(TypstDocumentKind.Paged, paged.Kind);
        Assert.Equal(TypstDocumentKind.Html, html.Kind);
    }

    [Fact]
    public void The_world_lists_its_font_families()
    {
        using var world = new TypstWorldBuilder().AddSource("main.typ", Clean).Build();

        var families = world.FontFamilies;

        Assert.NotEmpty(families);
        Assert.Contains(families, family => family.Contains("Libertinus", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(families.OrderBy(f => f, StringComparer.Ordinal), families);
    }

    [Fact]
    public void A_font_directory_shows_up_in_the_family_list()
    {
        var source = new[]
        {
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/TTF/DejaVuSans.ttf",
            @"C:\Windows\Fonts\segoeui.ttf",
        }.FirstOrDefault(File.Exists);

        if (source is null)
        {
            return;
        }

        var directory = Path.Combine(Path.GetTempPath(), "sharp-typst-fam-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            File.Copy(source, Path.Combine(directory, Path.GetFileName(source)));

            using var world = new TypstWorldBuilder()
                .AddFontDirectory(directory)
                .AddSource("main.typ", Clean)
                .Build();

            Assert.Contains(world.FontFamilies, family => family.Contains("DejaVu Sans", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
