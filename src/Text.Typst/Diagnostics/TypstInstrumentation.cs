using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using M3scalinum.Text.Typst.Compilation;

namespace M3scalinum.Text.Typst.Diagnostics;

/// <summary>
/// The instrumentation M3scalinum.Text.Typst publishes: one <see cref="System.Diagnostics.ActivitySource"/>
/// and one <see cref="Meter"/>, both named <c>M3scalinum.Text.Typst</c>.
/// </summary>
/// <remarks>
/// Nothing is collected unless the host listens. Wiring both to OpenTelemetry is two calls:
/// <c>AddSource("M3scalinum.Text.Typst")</c> and <c>AddMeter("M3scalinum.Text.Typst")</c>.
/// </remarks>
public static class TypstInstrumentation
{
    /// <summary>The name of the <see cref="System.Diagnostics.ActivitySource"/> and the <see cref="Meter"/>.</summary>
    public const string SourceName = "M3scalinum.Text.Typst";

    private static readonly string InstrumentationVersion =
        typeof(TypstInstrumentation).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(TypstInstrumentation).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    /// <summary>Spans for compilation and export.</summary>
    public static ActivitySource ActivitySource { get; } = new(SourceName, InstrumentationVersion);

    /// <summary>Counters and histograms for compilation and export.</summary>
    public static Meter Meter { get; } = new(SourceName, InstrumentationVersion);

    internal static Histogram<double> CompilationDuration { get; } = Meter.CreateHistogram<double>(
        "typst.compilation.duration",
        unit: "ms",
        description: "Wall-clock time a compilation takes.");

    internal static Histogram<double> ExportDuration { get; } = Meter.CreateHistogram<double>(
        "typst.export.duration",
        unit: "ms",
        description: "Wall-clock time an export takes.");

    internal static Histogram<long> PageCount { get; } = Meter.CreateHistogram<long>(
        "typst.document.pages",
        unit: "page",
        description: "Pages produced by one successful compilation.");

    internal static Counter<long> Compilations { get; } = Meter.CreateCounter<long>(
        "typst.compilations",
        unit: "{compilation}",
        description: "Compilations attempted, tagged by outcome.");

    internal static Counter<long> Exports { get; } = Meter.CreateCounter<long>(
        "typst.exports",
        unit: "{export}",
        description: "Exports attempted, tagged by format and outcome.");

    internal static Activity? StartCompilation(string root, TypstDocumentKind kind)
    {
        var activity = ActivitySource.StartActivity("Typst.Compile", ActivityKind.Internal);
        activity?.SetTag("typst.root", root);
        activity?.SetTag("typst.document.kind", DocumentKindName(kind));
        return activity;
    }

    internal static void RecordCompilation(
        Activity? activity,
        long startedTimestamp,
        int pages,
        int diagnostics,
        string? error)
    {
        var succeeded = error is null;
        var elapsed = Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds;

        CompilationDuration.Record(elapsed);
        Compilations.Add(1, new KeyValuePair<string, object?>("outcome", Outcome(succeeded)));

        if (activity is null)
        {
            return;
        }

        activity.SetTag("typst.pages", pages);
        activity.SetTag("typst.diagnostics", diagnostics);

        if (succeeded && pages > 0)
        {
            PageCount.Record(pages);
        }

        if (!succeeded)
        {
            activity.SetStatus(ActivityStatusCode.Error, error);
        }
    }

    internal static Activity? StartExport(string format)
    {
        var activity = ActivitySource.StartActivity("Typst.Export", ActivityKind.Internal);
        activity?.SetTag("typst.format", format);
        return activity;
    }

    internal static void RecordExport(
        Activity? activity,
        long startedTimestamp,
        string format,
        long? bytes,
        string? error)
    {
        var succeeded = error is null;
        var elapsed = Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds;
        var tags = new KeyValuePair<string, object?>[]
        {
            new("typst.format", format),
            new("outcome", Outcome(succeeded)),
        };

        ExportDuration.Record(elapsed, tags);
        Exports.Add(1, tags);

        if (activity is null)
        {
            return;
        }

        if (bytes is { } size)
        {
            activity.SetTag("typst.bytes", size);
        }

        if (!succeeded)
        {
            activity.SetStatus(ActivityStatusCode.Error, error);
        }
    }

    private static string Outcome(bool succeeded) => succeeded ? "success" : "error";

    private static string DocumentKindName(TypstDocumentKind kind) => kind switch
    {
        TypstDocumentKind.Html => "html",
        TypstDocumentKind.Bundle => "bundle",
        _ => "paged",
    };
}
