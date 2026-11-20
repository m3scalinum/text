using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using M3scalinum.Text.SharpTypst.Compilation;
using M3scalinum.Text.SharpTypst.Documents;

namespace M3scalinum.Text.SharpTypst.Benchmarks;

/// <summary>
/// A dependency-free benchmark harness. It measures the scenarios that matter for a host — compile,
/// export, template render — and stores a baseline so a regression fails the build.
/// </summary>
/// <remarks>
/// Usage:
///   dotnet run -c Release -- --write benchmarks/baseline.json
///   dotnet run -c Release -- --check benchmarks/baseline.json
/// </remarks>
internal static class Program
{
    private const int Warmup = 10;
    private const int Iterations = 50;

    /// <summary>
    /// The gate, as a ratio over the baseline. Generous on purpose: a shared CI runner varies by
    /// tens of percent between runs, and the point is to catch a real regression (a new copy, a
    /// lost cache), not to police noise.
    /// </summary>
    private const double RegressionGate = 1.5;

    /// <summary>A scenario is a name and a body that returns a byte count, so the work cannot be
    /// optimised away.</summary>
    private static readonly (string Name, Func<long> Run)[] Scenarios =
    [
        ("compile-small", CompileSmall),
        ("compile-report", CompileReport),
        ("export-pdf", ExportPdf),
        ("export-png", ExportPng),
        ("export-html", ExportHtml),
        ("export-markdown", ExportMarkdown),
        ("document-model", DocumentModel),
    ];

    private static int Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "--write";
        var path = args.Length > 1 ? args[1] : "benchmarks/baseline.json";

        return mode switch
        {
            "--write" => Write(path),
            "--check" => Check(path),
            _ => Fail($"unknown mode `{mode}`; use --write or --check"),
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static int Write(string path)
    {
        var results = new Dictionary<string, Result>(StringComparer.Ordinal);

        foreach (var (name, run) in Scenarios)
        {
            var result = Measure(name, run);
            results[name] = result;
            Console.WriteLine($"{name,-18} {result.MinMs,8:0.00} ms  {result.OpsPerSecond,8:0.0} op/s");
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(
            new Baseline { Scenarios = results },
            JsonOptions));

        Console.WriteLine($"baseline written to {path}");
        return 0;
    }

    private static int Check(string path)
    {
        if (!File.Exists(path))
        {
            return Fail($"baseline `{path}` not found; run with --write first");
        }

        var baseline = JsonSerializer.Deserialize<Baseline>(File.ReadAllText(path));

        if (baseline?.Scenarios is null)
        {
            return Fail($"baseline `{path}` is empty");
        }

        var failures = new List<string>();

        foreach (var (name, run) in Scenarios)
        {
            if (!baseline.Scenarios.TryGetValue(name, out var expected))
            {
                Console.WriteLine($"{name,-18} no baseline, skipped");
                continue;
            }

            var actual = Measure(name, run);
            var ratio = actual.MinMs / expected.MinMs;

            Console.WriteLine(
                $"{name,-18} {actual.MinMs,8:0.00} ms  baseline {expected.MinMs,8:0.00} ms  x{ratio:0.00}");

            if (ratio > RegressionGate)
            {
                failures.Add($"{name} regressed by {(ratio - 1) * 100:0}%");
            }
        }

        if (failures.Count == 0)
        {
            Console.WriteLine("no regression");
            return 0;
        }

        foreach (var failure in failures)
        {
            Console.Error.WriteLine(failure);
        }

        return 1;
    }

    private static Result Measure(string name, Func<long> run)
    {
        for (var index = 0; index < Warmup; index++)
        {
            _ = run();
        }

        var samples = new double[Iterations];

        for (var index = 0; index < Iterations; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            _ = run();
            stopwatch.Stop();
            samples[index] = stopwatch.Elapsed.TotalMilliseconds;
        }

        Array.Sort(samples);

        // The minimum is the most stable estimator for CPU-bound work: it is the run that was
        // least disturbed by the scheduler, the GC and the rest of the machine.
        var min = samples[0];
        var median = samples[samples.Length / 2];

        return new Result
        {
            MinMs = Math.Round(min, 3),
            MedianMs = Math.Round(median, 3),
            OpsPerSecond = Math.Round(1000.0 / min, 1),
        };
    }

    private const string SmallSource = """
        = Title

        A paragraph with *bold* and _emphasis_.
        """;

    private static long CompileSmall()
    {
        using var world = new TypstWorldBuilder().AddSource("main.typ", SmallSource).Build();
        using var result = new TypstCompiler().Compile(world);
        return result.PageCount;
    }

    private static long CompileReport()
    {
        using var world = new TypstWorldBuilder().AddSource("main.typ", Report().ToMarkup()).Build();
        using var result = new TypstCompiler().Compile(world);
        return result.PageCount;
    }

    private static long ExportPdf()
    {
        using var world = new TypstWorldBuilder().AddSource("main.typ", Report().ToMarkup()).Build();
        using var result = new TypstCompiler().Compile(world);
        return result.ExportPdf().Length;
    }

    private static long ExportPng()
    {
        using var world = new TypstWorldBuilder().AddSource("main.typ", Report().ToMarkup()).Build();
        using var result = new TypstCompiler().Compile(world);
        return result.ExportPng(ppi: 96f).Length;
    }

    private static long ExportHtml()
    {
        using var world = new TypstWorldBuilder().AddSource("main.typ", Report().ToMarkup()).Build();
        using var result = new TypstCompiler().Compile(
            world,
            new TypstCompilationOptions { DocumentKind = TypstDocumentKind.Html });

        return result.ExportHtmlString().Length;
    }

    private static long ExportMarkdown()
    {
        using var world = new TypstWorldBuilder().AddSource("main.typ", Report().ToMarkup()).Build();
        using var result = new TypstCompiler().Compile(
            world,
            new TypstCompilationOptions { DocumentKind = TypstDocumentKind.Html });

        return result.ExportMarkdown().Length;
    }

    private static long DocumentModel()
    {
        // 100 builds per iteration: a single build is ~10 µs, below the timer's useful resolution.
        long total = 0;

        for (var index = 0; index < 100; index++)
        {
            total += Report().ToMarkup().Length;
        }

        return total;
    }

    private static TypstDocument Report() => TypstDocument.Create()
        .Metadata(metadata => metadata.Title("Q3 2026 revenue report").Author("Finance").Language("en"))
        .Page(page => page.Paper("a4").Margin(TypstLength.Cm(2)).Numbering("1 / 1"))
        .Heading(1, "Revenue")
        .Paragraph(content => content
            .Text("Revenue grew by ")
            .Bold("12%")
            .Text(" year over year, driven by ")
            .Emphasis("subscriptions")
            .Text("."))
        .Heading(2, "Breakdown")
        .Table(table => table
            .Columns(TypstLength.Fr(2), TypstLength.Fr(1))
            .HeaderRow("Item", "Amount")
            .Row("Hosting", "1 200 EUR")
            .Row("Support", "800 EUR"))
        .BulletList("Hosting", "Support")
        .Math("sum_(k=1)^n k = (n(n+1))/2")
        .Divider()
        .Paragraph(content => content.Text("See ").Link("https://typst.app", "the reference").Text("."));

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 2;
    }

    private sealed class Baseline
    {
        public Dictionary<string, Result>? Scenarios { get; set; }
    }

    private sealed class Result
    {
        public double MinMs { get; set; }

        public double MedianMs { get; set; }

        public double OpsPerSecond { get; set; }
    }
}
