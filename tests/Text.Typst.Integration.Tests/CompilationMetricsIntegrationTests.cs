using System;
using System.Linq;
using M3scalinum.Text.Typst.Compilation;
using M3scalinum.Text.Typst.Diagnostics;
using M3scalinum.Text.Typst.Export;
using Xunit;

namespace M3scalinum.Text.Typst.Integration.Tests;

/// <summary>
/// The metrics against the real native library: the numbers must come from a compilation that
/// actually ran, not from a decoder defaulting to zero.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CompilationMetricsIntegrationTests
{
    [Fact]
    public void A_compilation_reports_a_duration_pages_and_fonts()
    {
        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", "= Metrics\nOne page.")
            .Build();

        using var result = new TypstCompiler().Compile(world);
        var metrics = result.Metrics;

        Assert.True(metrics.TotalDuration > TimeSpan.Zero, "the call took measurable time");
        Assert.True(metrics.CompileDuration > TimeSpan.Zero, "Typst itself took measurable time");
        Assert.True(
            metrics.TotalDuration >= metrics.CompileDuration,
            "the total call cannot be shorter than the compile inside it");
        Assert.Equal(1, metrics.PageCount);
        Assert.Equal(result.PageCount, metrics.PageCount);
        Assert.True(metrics.FontCount > 0, "the embedded fonts are in the binary");
        Assert.Equal(metrics.FontCount, metrics.EmbeddedFontCount);
        Assert.Equal(0, metrics.HostFontCount);
        Assert.True(metrics.FontBytes > 0, "the fonts have bytes");
        Assert.Equal(result.Diagnostics.Count, metrics.DiagnosticCount);
    }

    [Fact]
    public void The_page_count_follows_the_layout()
    {
        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", "#pagebreak()\n#pagebreak()\n= Third page")
            .Build();

        using var result = new TypstCompiler().Compile(world);

        Assert.Equal(3, result.Metrics.PageCount);
        Assert.Equal(3, result.PageCount);
    }

    [Fact]
    public void An_html_document_has_no_pages_but_still_measures()
    {
        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", "= Heading\nText.")
            .Build();

        using var result = new TypstCompiler().Compile(
            world,
            new TypstCompilationOptions { DocumentKind = TypstDocumentKind.Html });

        Assert.Equal(0, result.Metrics.PageCount);
        Assert.True(result.Metrics.CompileDuration > TimeSpan.Zero);
        Assert.True(result.Metrics.FontBytes > 0);
    }

    [Fact]
    public void A_warning_is_counted_by_severity()
    {
        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", "#set text(font: \"No Such Family Installed\")\n= Warning")
            .Build();

        using var result = new TypstCompiler().Compile(world);

        Assert.True(result.Metrics.WarningCount >= 1, "an unknown font family is a warning");
        Assert.Equal(0, result.Metrics.ErrorCount);
        Assert.Equal(
            result.Diagnostics.Count(d => d.Severity == TypstSeverity.Warning),
            result.Metrics.WarningCount);
    }

    [Fact]
    public void An_export_reports_the_pdf_size_it_produced()
    {
        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", "= Metrics\nA paragraph that produces a real PDF.")
            .Build();

        using var result = new TypstCompiler().Compile(world);

        Assert.Null(result.LastExport);

        var pdf = result.ExportPdf();

        var exported = Assert.IsType<ExportMetrics>(result.LastExport);
        Assert.Equal(ExportFormat.Pdf, exported.Format);
        Assert.Equal(pdf.LongLength, exported.Bytes);
        Assert.True(exported.Bytes > 4);
        Assert.True(exported.Duration > TimeSpan.Zero);
    }

    [Fact]
    public void Two_compilations_of_different_documents_keep_their_own_metrics()
    {
        using var onePage = new TypstWorldBuilder()
            .AddSource("main.typ", "= One")
            .Build();

        using var twoPages = new TypstWorldBuilder()
            .AddSource("main.typ", "#pagebreak()\n= Two")
            .Build();

        using var first = new TypstCompiler().Compile(onePage);
        using var second = new TypstCompiler().Compile(twoPages);

        Assert.Equal(1, first.Metrics.PageCount);
        Assert.Equal(2, second.Metrics.PageCount);
        Assert.NotSame(first.Metrics, second.Metrics);
    }
}
