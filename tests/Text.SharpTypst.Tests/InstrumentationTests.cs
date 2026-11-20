using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using M3scalinum.Text.SharpTypst.Compilation;
using M3scalinum.Text.SharpTypst.Diagnostics;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Tests;

/// <summary>
/// Instrumentation is only useful if it is actually emitted: a span with the root and the document
/// kind, a counter per compilation, a duration histogram. Each test uses a unique root path so a
/// parallel test cannot satisfy the assertion by accident.
/// </summary>
public sealed class InstrumentationTests
{
    private static string UniqueRoot() => $"{Guid.NewGuid():N}.typ";

    private static ActivityListener Listen(ConcurrentBag<Activity> stopped)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TypstInstrumentation.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Add,
        };

        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    [Fact]
    public void A_compilation_emits_a_span_naming_the_root_and_the_document_kind()
    {
        var stopped = new ConcurrentBag<Activity>();
        using var listener = Listen(stopped);

        var root = UniqueRoot();
        var native = new FakeTypstNative();
        using var world = new TypstWorldBuilder(native).AddSource(root, "= Title").Build();
        using var result = new TypstCompiler(native).Compile(world, new TypstCompilationOptions { Root = root });

        var span = Assert.Single(
            stopped,
            activity => activity.OperationName == "Typst.Compile"
                && (string?)activity.GetTagItem("typst.root") == root);

        Assert.Equal("paged", span.GetTagItem("typst.document.kind"));
        Assert.Equal(0, span.GetTagItem("typst.diagnostics"));
    }

    [Fact]
    public void A_failed_compilation_marks_the_span_as_an_error()
    {
        var stopped = new ConcurrentBag<Activity>();
        using var listener = Listen(stopped);

        var root = UniqueRoot();
        var native = new FakeTypstNative
        {
            CompileFailure = new M3scalinum.Text.SharpTypst.Native.NativeCallException(
                M3scalinum.Text.SharpTypst.Native.NativeStatus.Compilation,
                string.Empty,
                "unknown variable: `totl`"),
        };

        using var world = new TypstWorldBuilder(native).AddSource(root, "= Title").Build();
        Assert.Throws<TypstCompilationException>(
            () => new TypstCompiler(native).Compile(world, new TypstCompilationOptions { Root = root }));

        var span = Assert.Single(
            stopped,
            activity => activity.OperationName == "Typst.Compile"
                && (string?)activity.GetTagItem("typst.root") == root);

        Assert.Equal(ActivityStatusCode.Error, span.Status);
    }

    [Fact]
    public void An_export_emits_a_span_naming_the_format_and_the_size()
    {
        var stopped = new ConcurrentBag<Activity>();
        using var listener = Listen(stopped);

        var native = new FakeTypstNative();
        // A distinctive size, so a parallel test's export cannot satisfy the assertion.
        native.ExportResults["pdf"] = new byte[4321];
        using var world = new TypstWorldBuilder(native).AddSource("main.typ", "= Title").Build();
        using var result = new TypstCompiler(native).Compile(world);
        result.ExportPdf();

        Assert.Contains(
            stopped,
            activity => activity.OperationName == "Typst.Export"
                && (string?)activity.GetTagItem("typst.format") == "pdf"
                && (long?)activity.GetTagItem("typst.bytes") == 4321L);
    }

    [Fact]
    public void A_compilation_records_the_counter_and_the_duration()
    {
        var instruments = new ConcurrentBag<string>();

        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == TypstInstrumentation.SourceName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };

        meterListener.SetMeasurementEventCallback<long>((instrument, _, _, _) => instruments.Add(instrument.Name));
        meterListener.SetMeasurementEventCallback<double>((instrument, _, _, _) => instruments.Add(instrument.Name));
        meterListener.Start();

        var native = new FakeTypstNative();
        using var world = new TypstWorldBuilder(native).AddSource("main.typ", "= Title").Build();
        using var result = new TypstCompiler(native).Compile(world);
        result.ExportPdf();

        Assert.Contains("typst.compilations", instruments);
        Assert.Contains("typst.compilation.duration", instruments);
        Assert.Contains("typst.exports", instruments);
        Assert.Contains("typst.export.duration", instruments);
    }

    [Fact]
    public void The_source_and_the_meter_share_one_name()
    {
        Assert.Equal("M3scalinum.Text.SharpTypst", TypstInstrumentation.SourceName);
        Assert.Equal(TypstInstrumentation.SourceName, TypstInstrumentation.ActivitySource.Name);
        Assert.Equal(TypstInstrumentation.SourceName, TypstInstrumentation.Meter.Name);
    }
}
