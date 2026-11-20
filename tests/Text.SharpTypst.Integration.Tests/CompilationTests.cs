using System;
using M3scalinum.Text.SharpTypst.Compilation;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Integration.Tests;

/// <summary>
/// Tests that need the native library. They are filtered out on machines without it:
/// <c>dotnet test --filter Category!=Integration</c>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CompilationTests
{
    [Fact]
    public void Compiles_a_minimal_document_to_pdf()
    {
        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", "= Hello\nThis is *Typst*.")
            .Build();

        using var result = new TypstCompiler().Compile(world);
        var pdf = result.ExportPdf();

        Assert.True(result.PageCount >= 1);
        Assert.True(pdf.Length > 4);
        Assert.Equal("%PDF"u8.ToArray(), pdf[..4]);
    }

    [Fact]
    public void Reports_an_error_with_a_position()
    {
        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", "= Hello\n#totl")
            .Build();

        var exception = Assert.Throws<TypstCompilationException>(
            () => new TypstCompiler().Compile(world));

        Assert.NotEmpty(exception.Diagnostics);
        Assert.NotNull(exception.Diagnostics[0].Location);
        Assert.Equal("main.typ", exception.Diagnostics[0].Location!.File);
    }

    [Fact]
    public void Is_deterministic_for_the_same_inputs()
    {
        static byte[] Render()
        {
            using var world = new TypstWorldBuilder()
                .AddSource("main.typ", "= Deterministic\nSame bytes every time.")
                .Build();
            using var result = new TypstCompiler().Compile(world);
            return result.ExportPdf();
        }

        Assert.Equal(Render(), Render());
    }

    [Fact]
    public void Exposes_the_bundled_typst_version()
    {
        Assert.StartsWith("0.15", TypstEnvironment.TypstVersion, StringComparison.Ordinal);
    }
}
