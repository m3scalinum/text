using System;
using System.IO;
using M3scalinum.Text.Typst.Compilation;
using M3scalinum.Text.Typst.Export;
using M3scalinum.Text.Typst.Native;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

/// <summary>
/// The metrics of a compilation: the native numbers are decoded without loss, the totals are the
/// managed layer's own, and each result carries its own reading. A process-wide collector would
/// pass the first two and fail the third, which is why it is tested explicitly.
/// </summary>
public sealed class CompilationMetricsTests
{
    private const string NativePayload = """
        {
          "compileNanos": 1500000,
          "pages": 3,
          "fontsTotal": 12,
          "fontsEmbedded": 10,
          "fontBytes": 2048576
        }
        """;

    private static CompilationResult Compile(FakeTypstNative native)
    {
        using var world = new TypstWorldBuilder(native).AddSource("main.typ", "= Title").Build();
        return new TypstCompiler(native).Compile(world);
    }

    [Fact]
    public void Decodes_what_the_native_library_measured()
    {
        var native = new FakeTypstNative { MetricsJson = NativePayload };

        using var result = Compile(native);

        var metrics = result.Metrics;
        Assert.Equal(TimeSpan.FromMilliseconds(1.5), metrics.CompileDuration);
        Assert.Equal(3, metrics.PageCount);
        Assert.Equal(12, metrics.FontCount);
        Assert.Equal(10, metrics.EmbeddedFontCount);
        Assert.Equal(2, metrics.HostFontCount);
        Assert.Equal(2048576, metrics.FontBytes);
    }

    [Fact]
    public void Counts_the_diagnostics_of_the_document_by_severity()
    {
        var native = new FakeTypstNative
        {
            DiagnosticsJson = """
                {
                  "code": "ok",
                  "message": "",
                  "diagnostics": [
                    { "severity": "warning", "message": "unknown font family: Inter" },
                    { "severity": "warning", "message": "unused import" },
                    { "severity": "hint", "message": "try `total`" }
                  ]
                }
                """,
        };

        using var result = Compile(native);

        Assert.Equal(result.Diagnostics.Count, result.Metrics.DiagnosticCount);
        Assert.Equal(2, result.Metrics.WarningCount);
        Assert.Equal(1, result.Metrics.HintCount);
        Assert.Equal(0, result.Metrics.ErrorCount);
    }

    /// <summary>
    /// The total is the whole call and the compile is the part inside Typst, so a slow native call
    /// shows up as a difference between them rather than being attributed to the compiler.
    /// </summary>
    [Fact]
    public void The_total_covers_more_than_the_compile()
    {
        var native = new FakeTypstNative
        {
            MetricsJson = NativePayload,
            CompileDelay = TimeSpan.FromMilliseconds(50),
        };

        using var result = Compile(native);

        Assert.True(
            result.Metrics.TotalDuration >= TimeSpan.FromMilliseconds(40),
            $"the total must include the native call, got {result.Metrics.TotalDuration}");
        Assert.Equal(TimeSpan.FromMilliseconds(1.5), result.Metrics.CompileDuration);
    }

    /// <summary>
    /// Two compilations running one after the other keep their own numbers. This is the property a
    /// static collector loses as soon as a pool compiles in parallel: the second result would
    /// overwrite the first one's page count.
    /// </summary>
    [Fact]
    public void Each_result_keeps_the_metrics_of_its_own_compilation()
    {
        var native = new FakeTypstNative
        {
            MetricsJson = """{"compileNanos":1000000,"pages":3,"fontsTotal":12,"fontsEmbedded":12,"fontBytes":1024}""",
        };

        using var first = Compile(native);

        native.MetricsJson =
            """{"compileNanos":2000000,"pages":7,"fontsTotal":13,"fontsEmbedded":12,"fontBytes":2048}""";

        using var second = Compile(native);

        Assert.Equal(3, first.Metrics.PageCount);
        Assert.Equal(12, first.Metrics.FontCount);
        Assert.Equal(7, second.Metrics.PageCount);
        Assert.Equal(1, second.Metrics.HostFontCount);
        Assert.Equal(TimeSpan.FromMilliseconds(1), first.Metrics.CompileDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(2), second.Metrics.CompileDuration);
    }

    /// <summary>
    /// A library that measured nothing still produces a usable result: the metrics are zeros, not
    /// a failure. The ABI is additive, so a payload that is shorter than expected must decode.
    /// </summary>
    [Fact]
    public void An_empty_payload_decodes_to_defaults()
    {
        var native = new FakeTypstNative { MetricsJson = string.Empty };

        using var result = Compile(native);

        Assert.Equal(TimeSpan.Zero, result.Metrics.CompileDuration);
        Assert.Equal(0, result.Metrics.PageCount);
        Assert.Equal(0, result.Metrics.FontCount);
        Assert.Equal(0, result.Metrics.EmbeddedFontCount);
        Assert.Equal(0, result.Metrics.FontBytes);
    }

    [Fact]
    public void An_export_reports_the_bytes_it_produced()
    {
        var native = new FakeTypstNative
        {
            ExportResults = { ["pdf"] = new byte[1234] },
        };

        using var result = Compile(native);
        Assert.Null(result.LastExport);

        var pdf = result.ExportPdf();

        var exported = Assert.IsType<ExportMetrics>(result.LastExport);
        Assert.Equal(ExportFormat.Pdf, exported.Format);
        Assert.Equal(pdf.LongLength, exported.Bytes);
        Assert.Equal(1234, exported.Bytes);
    }

    [Fact]
    public void A_streamed_export_reports_what_reached_the_stream()
    {
        var native = new FakeTypstNative
        {
            ExportResults = { ["pdf"] = new byte[9000] },
            StreamChunkSize = 1000,
        };

        using var result = Compile(native);
        using var destination = new MemoryStream();

        result.ExportPdf(destination);

        var exported = Assert.IsType<ExportMetrics>(result.LastExport);
        Assert.Equal(9000, exported.Bytes);
        Assert.Equal(destination.Length, exported.Bytes);
    }

    /// <summary>
    /// A failed export writes nothing, so leaving the reading alone is honest: it still describes
    /// the last export that succeeded rather than reporting a zero-byte document.
    /// </summary>
    [Fact]
    public void A_failed_export_does_not_overwrite_the_last_reading()
    {
        var native = new FakeTypstNative
        {
            ExportResults = { ["pdf"] = new byte[1234] },
        };

        using var result = Compile(native);
        result.ExportPdf();
        var before = result.LastExport;

        native.ExportFailure = new NativeCallException(
            NativeStatus.Export,
            """{"code":"export","message":"the PDF writer refused the document"}""",
            "export 'pdf'");

        Assert.Throws<TypstExportException>(() => result.ExportPdf());
        Assert.Same(before, result.LastExport);
    }

    /// <summary>
    /// The metrics are a value of the result, not a mutable view: two reads of the same result are
    /// the same object, and nothing a caller does to the result changes the numbers.
    /// </summary>
    [Fact]
    public void The_metrics_of_a_result_do_not_change_when_it_is_exported()
    {
        var native = new FakeTypstNative
        {
            MetricsJson = NativePayload,
            ExportResults = { ["pdf"] = new byte[42] },
        };

        using var result = Compile(native);
        var metrics = result.Metrics;

        result.ExportPdf();

        Assert.Same(metrics, result.Metrics);
        Assert.Equal(3, result.Metrics.PageCount);
        Assert.Equal(TimeSpan.FromMilliseconds(1.5), result.Metrics.CompileDuration);
    }
}
