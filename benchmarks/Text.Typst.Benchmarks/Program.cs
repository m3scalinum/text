using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using M3scalinum.Text.Typst.Compilation;
using M3scalinum.Text.Documents;

namespace M3scalinum.Text.Typst.Benchmarks;

/// <summary>
/// A dependency-free benchmark harness. It measures the scenarios that matter for a host — compile,
/// export, template render — and stores a baseline so a regression fails the build.
/// </summary>
/// <remarks>
/// Usage:
///   dotnet run -c Release -- --write benchmarks/baseline.json
///   dotnet run -c Release -- --check benchmarks/baseline.json
///   dotnet run -c Release -- --samples artifacts/a.json --label main --min-iteration-ms 1
/// </remarks>
internal static class Program
{
    private const int Warmup = 10;
    private const int Iterations = 50;

    private const string DefaultBaselinePath = "benchmarks/baseline.json";

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
        ("compile-incremental", CompileIncremental),
        ("export-pdf", ExportPdf),
        ("export-png", ExportPng),
        ("export-html", ExportHtml),
        ("export-markdown", ExportMarkdown),
        ("document-model", DocumentModel),
    ];

    private static int Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "--write";
        string? path = null;
        var iterations = Iterations;
        var warmup = Warmup;
        var label = string.Empty;
        var minIterationMs = 0.0;

        for (var index = 1; index < args.Length; index++)
        {
            var argument = args[index];

            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                path ??= argument;
                continue;
            }

            if (index + 1 >= args.Length)
            {
                return Fail($"`{argument}` needs a value");
            }

            var value = args[++index];

            switch (argument)
            {
                case "--iterations":
                    if (!TryParsePositive(value, out iterations))
                    {
                        return Fail($"`--iterations` expects a positive integer, got `{value}`");
                    }

                    break;
                case "--warmup":
                    if (!TryParsePositive(value, out warmup))
                    {
                        return Fail($"`--warmup` expects a positive integer, got `{value}`");
                    }

                    break;
                case "--label":
                    label = value;
                    break;
                case "--min-iteration-ms":
                    if (!double.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out minIterationMs) || minIterationMs < 0)
                    {
                        return Fail($"`--min-iteration-ms` expects a non-negative number, got `{value}`");
                    }

                    break;
                default:
                    return Fail($"unknown option `{argument}`; use --write, --check or --samples");
            }
        }

        var settings = new Settings(warmup, iterations, minIterationMs);

        return mode switch
        {
            "--write" => Write(path ?? DefaultBaselinePath, settings),
            "--check" => Check(path ?? DefaultBaselinePath, settings),
            "--samples" => Samples(path, label, settings),
            _ => Fail($"unknown mode `{mode}`; use --write, --check or --samples"),
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static int Write(string path, Settings settings)
    {
        var results = new Dictionary<string, Result>(StringComparer.Ordinal);

        foreach (var (name, run) in Scenarios)
        {
            var result = Measure(run, settings, repeats: 1).Summary;
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

    /// <summary>
    /// Writes every raw sample instead of the three summary numbers, so an A/B on one machine can
    /// measure its own noise instead of assuming it. <c>tools/bench-compare.py</c> reads this file;
    /// the baseline format above is unrelated and stays byte-for-byte the same.
    /// </summary>
    private static int Samples(string? path, string label, Settings settings)
    {
        if (string.IsNullOrEmpty(path))
        {
            return Fail("--samples needs an output path");
        }

        var scenarios = new Dictionary<string, SampleScenario>(StringComparer.Ordinal);

        foreach (var (name, run) in Scenarios)
        {
            var repeats = settings.MinIterationMs > 0 ? Calibrate(run, settings) : 1;
            var measurement = Measure(run, settings, repeats, warmedUp: settings.MinIterationMs > 0);
            var samples = measurement.Samples;
            var mean = 0.0;

            foreach (var sample in samples)
            {
                mean += sample;
            }

            mean /= samples.Length;

            var variance = 0.0;

            foreach (var sample in samples)
            {
                variance += (sample - mean) * (sample - mean);
            }

            var deviation = samples.Length > 1 ? Math.Sqrt(variance / (samples.Length - 1)) : 0.0;

            scenarios[name] = new SampleScenario
            {
                MinMs = measurement.Summary.MinMs,
                MedianMs = measurement.Summary.MedianMs,
                MeanMs = Math.Round(mean, 3),
                StdDevMs = Math.Round(deviation, 3),
                P95Ms = Math.Round(samples[(int)((samples.Length - 1) * 0.95)], 3),
                Repeats = repeats,
                SamplesMs = Array.ConvertAll(samples, static sample => Math.Round(sample, 3)),
            };

            Console.WriteLine(
                $"{name,-18} {measurement.Summary.MedianMs,8:0.00} ms median  {deviation,7:0.00} ms sd  x{repeats}");
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(
            new SampleDocument
            {
                Label = label,
                RuntimeVersion = Environment.Version.ToString(),
                OperatingSystem = RuntimeInformation.OSDescription,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                ProcessorCount = Environment.ProcessorCount,
                Warmup = settings.Warmup,
                Iterations = settings.Iterations,
                Scenarios = scenarios,
            },
            JsonOptions));

        Console.WriteLine($"samples written to {path}");
        return 0;
    }

    private static int Check(string path, Settings settings)
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

            var actual = Measure(run, settings, repeats: 1).Summary;
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

    /// <summary>
    /// How many times the body has to run for one timed iteration to reach
    /// <see cref="Settings.MinIterationMs"/>. A 30 µs scenario is measured by the scheduler and the
    /// JIT as much as by the compiler, and a ratio computed on it is noise; the repeat count is
    /// recorded in the samples file so a comparison can see what was measured. Only `--samples`
    /// uses it: `--write` and `--check` keep the one-call-per-iteration semantics of the baseline.
    /// </summary>
    private static int Calibrate(Func<long> run, Settings settings)
    {
        for (var index = 0; index < settings.Warmup; index++)
        {
            _ = run();
        }

        var probe = new double[3];

        for (var index = 0; index < probe.Length; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            _ = run();
            stopwatch.Stop();
            probe[index] = stopwatch.Elapsed.TotalMilliseconds;
        }

        Array.Sort(probe);
        var observed = Math.Max(probe[probe.Length / 2], 0.001);
        var repeats = (int)Math.Ceiling(settings.MinIterationMs / observed);

        return Math.Clamp(repeats, 1, 1_000);
    }

    private static Measurement Measure(Func<long> run, Settings settings, int repeats, bool warmedUp = false)
    {
        for (var index = warmedUp ? settings.Warmup : 0; index < settings.Warmup; index++)
        {
            _ = run();
        }

        var samples = new double[settings.Iterations];

        for (var index = 0; index < settings.Iterations; index++)
        {
            var stopwatch = Stopwatch.StartNew();

            for (var repeat = 0; repeat < repeats; repeat++)
            {
                _ = run();
            }

            stopwatch.Stop();
            samples[index] = stopwatch.Elapsed.TotalMilliseconds;
        }

        // Sorted in place: everything downstream wants order statistics, and the caller gets the
        // sorted array, which is what `--samples` writes.
        Array.Sort(samples);

        // The minimum is the most stable estimator for CPU-bound work: it is the run that was
        // least disturbed by the scheduler, the GC and the rest of the machine.
        var min = samples[0];
        var median = samples[samples.Length / 2];

        return new Measurement(
            new Result
            {
                MinMs = Math.Round(min, 3),
                MedianMs = Math.Round(median, 3),
                OpsPerSecond = Math.Round(1000.0 / min * repeats, 1),
            },
            samples);
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

    /// <summary>
    /// The loop a preview server runs: one sentence of a six-page document is edited, the world is
    /// reused for the next compilation, and the compilation reports the pages it has to re-render.
    /// </summary>
    /// <remarks>
    /// The session lives for the whole benchmark, so every iteration after the first compiles an
    /// edited document against the state Typst memoized for the previous one. That is the case the
    /// scenario exists to keep honest: a regression that recreates the world per compile, or that
    /// stops asking the native library for fingerprints, shows up here as an iteration that costs
    /// what a cold compile costs.
    /// </remarks>
    private static long CompileIncremental() => Session.Value.Edit();

    private static readonly Lazy<IncrementalSession> Session = new(() => new IncrementalSession());

    /// <summary>A six-page document whose first page carries the sentence being edited.</summary>
    private static string IncrementalReport(int edit)
    {
        var markup = new System.Text.StringBuilder("#set page(paper: \"a4\")\n#set text(size: 10pt)\n");

        for (var page = 0; page < 6; page++)
        {
            if (page > 0)
            {
                markup.Append("#pagebreak(weak: true)\n");
            }

            markup.Append($"#heading(level: 1)[Section {page}]\n\n");

            for (var paragraph = 0; paragraph < 6; paragraph++)
            {
                markup.Append($"Paragraph {paragraph} of page {page}: #lorem({40 + paragraph}) ");

                if (page == 0 && paragraph == 2)
                {
                    markup.Append($"Revision {edit} of the sentence. ");
                }

                markup.Append("\n\n");
            }
        }

        return markup.ToString();
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

    private static bool TryParsePositive(string value, out int parsed) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) && parsed > 0;

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 2;
    }

    /// <summary>
    /// One editable document, kept for the whole run: the world is edited in place and the
    /// fingerprints of the previous compilation are the comparison base of the next one.
    /// </summary>
    private sealed class IncrementalSession : IDisposable
    {
        private readonly TypstWorld _world;
        private readonly TypstCompiler _compiler = new();
        private readonly TypstIncrementalState _state = new();
        private int _edit;

        public IncrementalSession()
        {
            _world = new TypstWorldBuilder().AddSource("main.typ", IncrementalReport(0)).Build();

            // The baseline: the first compilation has nothing to compare against, and excluding it
            // from the measurement keeps every iteration a "second compilation of an edited
            // document" rather than a mix of cold and warm.
            using (var baseline = _compiler.CompileChanged(_world, _state))
            {
            }
        }

        public long Edit()
        {
            _edit++;
            _world.SetSource("main.typ", IncrementalReport(_edit));

            using var changed = _compiler.CompileChanged(_world, _state);

            // Read both numbers so the work cannot be elided, and return a value that depends on
            // the comparison having happened.
            return changed.Document.PageCount + changed.ChangedPages.Count;
        }

        public void Dispose() => _world.Dispose();
    }

    private readonly record struct Settings(int Warmup, int Iterations, double MinIterationMs);

    private sealed record Measurement(Result Summary, double[] Samples);

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

    private sealed class SampleDocument
    {
        public string Tool { get; set; } = "Text.Typst.Benchmarks";

        public int Format { get; set; } = 1;

        public string? Label { get; set; }

        public string? RuntimeVersion { get; set; }

        public string? OperatingSystem { get; set; }

        public string? Architecture { get; set; }

        public int ProcessorCount { get; set; }

        public int Warmup { get; set; }

        public int Iterations { get; set; }

        public Dictionary<string, SampleScenario>? Scenarios { get; set; }
    }

    private sealed class SampleScenario
    {
        public double MinMs { get; set; }

        public double MedianMs { get; set; }

        public double MeanMs { get; set; }

        public double StdDevMs { get; set; }

        public double P95Ms { get; set; }

        public int Repeats { get; set; }

        public double[] SamplesMs { get; set; } = [];
    }
}
