using System;
using System.Linq;
using M3scalinum.Text.SharpTypst.Compilation;
using M3scalinum.Text.SharpTypst.Diagnostics;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Integration.Tests;

/// <summary>The source snippet, populated by the real compiler.</summary>
[Trait("Category", "Integration")]
public sealed class DiagnosticSnippetIntegrationTests
{
    [Fact]
    public void A_compile_error_carries_the_source_line()
    {
        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", "= Title\n\n#let x = totl")
            .Build();

        var exception = Assert.Throws<TypstCompilationException>(
            () => new TypstCompiler().Compile(world));

        var diagnostic = exception.Diagnostics.First(d => d.Severity == TypstSeverity.Error);

        Assert.Equal("#let x = totl", diagnostic.SourceLine);
        Assert.Equal(3, diagnostic.Location?.Line);
    }

    [Fact]
    public void The_rendered_snippet_points_at_the_offending_token()
    {
        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", "= Title\n\n#let x = totl")
            .Build();

        var exception = Assert.Throws<TypstCompilationException>(
            () => new TypstCompiler().Compile(world));

        var text = exception.Diagnostics
            .First(d => d.Severity == TypstSeverity.Error)
            .ToDisplayString();

        Assert.Contains("3 | #let x = totl", text, StringComparison.Ordinal);
        Assert.Contains("^", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_warning_also_carries_its_source_line()
    {
        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", "#set text(font: \"No Such Font\")\n\n= Still fine")
            .Build();

        using var result = new TypstCompiler().Compile(world);

        var warning = result.Diagnostics.First(d => d.Severity == TypstSeverity.Warning);

        Assert.Equal("#set text(font: \"No Such Font\")", warning.SourceLine);
        Assert.Equal(1, warning.Location?.Line);
    }

    [Fact]
    public void A_diagnostic_in_an_included_file_names_that_file()
    {
        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", "#include \"partials/broken.typ\"")
            .AddSource("partials/broken.typ", "#let y = nope")
            .Build();

        var exception = Assert.Throws<TypstCompilationException>(
            () => new TypstCompiler().Compile(world));

        var diagnostic = exception.Diagnostics.First(d => d.Severity == TypstSeverity.Error);

        Assert.Equal("partials/broken.typ", diagnostic.Location?.File);
        Assert.Equal("#let y = nope", diagnostic.SourceLine);
    }
}
