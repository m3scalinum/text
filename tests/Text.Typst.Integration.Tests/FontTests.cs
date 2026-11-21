using System;
using System.IO;
using System.Linq;
using M3scalinum.Text.Typst.Compilation;
using Xunit;

namespace M3scalinum.Text.Typst.Integration.Tests;

/// <summary>Font discovery: an explicit directory, and the host's installed fonts.</summary>
[Trait("Category", "Integration")]
public sealed class FontTests
{
    private static string? FindSystemFont()
    {
        string[] candidates =
        [
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/TTF/DejaVuSans.ttf",
            "/System/Library/Fonts/Supplemental/Arial.ttf",
            @"C:\Windows\Fonts\segoeui.ttf",
            @"C:\Windows\Fonts\arial.ttf",
        ];

        return candidates.FirstOrDefault(File.Exists);
    }

    [Fact]
    public void A_font_directory_adds_faces()
    {
        var source = FindSystemFont();
        if (source is null)
        {
            return;
        }

        var directory = Path.Combine(Path.GetTempPath(), "sharp-typst-fonts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            File.Copy(source, Path.Combine(directory, Path.GetFileName(source)));

            using var embedded = new TypstWorldBuilder().AddSource("main.typ", "= x").Build();
            using var withDirectory = new TypstWorldBuilder()
                .AddFontDirectory(directory)
                .AddSource("main.typ", "= x")
                .Build();

            Assert.True(
                withDirectory.FontCount > embedded.FontCount,
                $"expected more fonts than {embedded.FontCount}, got {withDirectory.FontCount}");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void System_fonts_add_faces()
    {
        using var embedded = new TypstWorldBuilder().AddSource("main.typ", "= x").Build();
        using var withSystem = new TypstWorldBuilder()
            .AddSystemFonts()
            .AddSource("main.typ", "= x")
            .Build();

        Assert.True(
            withSystem.FontCount >= embedded.FontCount,
            $"expected at least {embedded.FontCount} fonts, got {withSystem.FontCount}");
    }

    [Fact]
    public void A_missing_directory_adds_nothing()
    {
        using var embedded = new TypstWorldBuilder().AddSource("main.typ", "= x").Build();
        using var withMissing = new TypstWorldBuilder()
            .AddFontDirectory(Path.Combine(Path.GetTempPath(), "sharp-typst-does-not-exist"))
            .AddSource("main.typ", "= x")
            .Build();

        Assert.Equal(embedded.FontCount, withMissing.FontCount);
    }

    [Fact]
    public void A_document_compiles_with_a_font_from_a_directory()
    {
        var source = FindSystemFont();
        if (source is null)
        {
            return;
        }

        var directory = Path.Combine(Path.GetTempPath(), "sharp-typst-fonts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            File.Copy(source, Path.Combine(directory, Path.GetFileName(source)));

            using var world = new TypstWorldBuilder()
                .AddFontDirectory(directory)
                .AddSource("main.typ", "#set text(font: \"DejaVu Sans\")\n\n= Title")
                .Build();

            using var result = new TypstCompiler().Compile(world);

            Assert.Equal(1, result.PageCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
