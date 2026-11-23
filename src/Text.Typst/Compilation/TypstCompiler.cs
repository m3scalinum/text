using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using M3scalinum.Text.Typst.Diagnostics;
using M3scalinum.Text.Documents;
using M3scalinum.Text.Typst.Internal;
using M3scalinum.Text.Typst.Native;

namespace M3scalinum.Text.Typst.Compilation;

/// <summary>
/// Compiles a <see cref="TypstWorld"/> into a document. Stateless apart from its native
/// binding: one instance per process is the intended usage.
/// </summary>
public sealed class TypstCompiler
{
    private readonly ITypstNative _native;
    private readonly SemaphoreSlim? _gate;

    /// <summary>
    /// Creates a compiler that runs at most one compilation per processor at a time.
    /// </summary>
    /// <remarks>
    /// A compilation is CPU-bound: letting a hundred requests start a hundred of them at once does
    /// not make them faster, it makes every one of them slower and the process unresponsive.
    /// </remarks>
    public TypstCompiler()
        : this(TypstNative.Instance, Environment.ProcessorCount)
    {
    }

    /// <summary>
    /// Creates a compiler that runs at most <paramref name="maxConcurrency"/> compilations at once.
    /// </summary>
    /// <param name="maxConcurrency">
    /// The ceiling. <c>0</c> or a negative value removes it, which is what a caller that already
    /// bounds the work itself wants.
    /// </param>
    public TypstCompiler(int maxConcurrency)
        : this(TypstNative.Instance, maxConcurrency)
    {
    }

    internal TypstCompiler(ITypstNative native)
        : this(native, Environment.ProcessorCount)
    {
    }

    internal TypstCompiler(ITypstNative native, int maxConcurrency)
    {
        _native = native;
        _gate = maxConcurrency > 0 ? new SemaphoreSlim(maxConcurrency, maxConcurrency) : null;
    }

    /// <summary>Compiles the world's root source.</summary>
    /// <exception cref="TypstCompilationException">The compiler rejected the document.</exception>
    /// <exception cref="TypstNativeException">The native layer failed.</exception>
    /// <exception cref="TypstTimeoutException">
    /// <see cref="TypstCompilationOptions.Timeout"/> elapsed before the compilation finished. The
    /// work is abandoned, not stopped, and its handles are released when it ends.
    /// </exception>
    public CompilationResult Compile(TypstWorld world, TypstCompilationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        options ??= new TypstCompilationOptions();

        // The clock starts before the queue: a caller that allows five seconds allows five seconds
        // of waiting, not five seconds of compiling after an unbounded wait.
        var clock = options.Timeout is null ? null : Stopwatch.StartNew();

        if (!EnterGate(options.Timeout, clock))
        {
            throw new TypstTimeoutException(options.Timeout!.Value);
        }

        try
        {
            return CompileHoldingGate(world, options, clock);
        }
        finally
        {
            _gate?.Release();
        }
    }

    /// <summary>Waits for a slot, or gives up when the caller's time is up.</summary>
    private bool EnterGate(TimeSpan? timeout, Stopwatch? clock)
    {
        if (_gate is null)
        {
            return true;
        }

        if (timeout is not { } limit)
        {
            _gate.Wait();
            return true;
        }

        var remaining = limit - (clock?.Elapsed ?? TimeSpan.Zero);

        return remaining > TimeSpan.Zero && _gate.Wait(remaining);
    }

    /// <summary>Compiles with a slot already held.</summary>
    private CompilationResult CompileHoldingGate(TypstWorld world, TypstCompilationOptions options, Stopwatch? clock)
    {
        if (options.Timeout is not { } timeout)
        {
            return CompileCore(world, options);
        }

        var remaining = timeout - (clock?.Elapsed ?? TimeSpan.Zero);

        if (remaining <= TimeSpan.Zero)
        {
            throw new TypstTimeoutException(timeout);
        }

        return CompileTimed(world, options, remaining, timeout);
    }

    /// <summary>
    /// Compiles on its own thread and gives up waiting after <paramref name="remaining"/>.
    /// </summary>
    private CompilationResult CompileTimed(
        TypstWorld world,
        TypstCompilationOptions options,
        TimeSpan remaining,
        TimeSpan timeout)
    {
        // Typst cannot be interrupted, so a timed compile runs on its own thread over a clone: the
        // caller may dispose its world the moment the timeout fires, and the abandoned compile still
        // reads a live one. The clone and the result belong to that thread and are released by it.
        var clone = world.Clone();

        var task = Task.Factory.StartNew(
            () =>
            {
                try
                {
                    return CompileCore(clone, options);
                }
                finally
                {
                    clone.Dispose();
                }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        bool finished;

        try
        {
            finished = task.Wait(remaining);
        }
        catch (AggregateException)
        {
            // The compile failed rather than ran late; GetResult below rethrows the real exception
            // instead of the wrapper.
            finished = true;
        }

        if (!finished)
        {
            // Observe the outcome either way: a finished result is disposed, and a faulted one is
            // read so it does not surface as an unobserved task exception much later.
            _ = task.ContinueWith(
                abandoned =>
                {
                    if (abandoned.Status == TaskStatus.RanToCompletion)
                    {
                        abandoned.Result.Dispose();
                    }
                    else
                    {
                        _ = abandoned.Exception;
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            throw new TypstTimeoutException(timeout);
        }

        return task.GetAwaiter().GetResult();
    }

    private CompilationResult CompileCore(TypstWorld world, TypstCompilationOptions options)
    {
        var dto = new CompileOptionsDto
        {
            Root = options.Root,
            Document = options.DocumentKind switch
            {
                TypstDocumentKind.Html => "html",
                TypstDocumentKind.Bundle => "bundle",
                _ => "paged",
            },
        };

        var json = JsonSerializer.Serialize(dto, TypstJsonContext.Default.CompileOptionsDto);

        var started = Stopwatch.GetTimestamp();
        using var activity = TypstInstrumentation.StartCompilation(options.Root, options.DocumentKind);
        var pages = 0;
        var diagnosticCount = 0;
        string? failure = null;

        try
        {
            var handle = _native.Compile(world.Handle, json, out var diagnosticsJson);
            var diagnostics = NativeErrorTranslator.ParseDiagnostics(diagnosticsJson);
            diagnosticCount = diagnostics.Count;
            var metrics = ReadMetrics(handle, diagnostics, started);
            var result = new CompilationResult(_native, handle, diagnostics, options.DocumentKind, metrics);

            if (options.WarningsAsErrors && diagnostics.Any(d => d.Severity == TypstSeverity.Warning))
            {
                result.Dispose();
                throw new TypstCompilationException(
                    $"{diagnostics.Count(d => d.Severity == TypstSeverity.Warning)} warning(s) treated as errors",
                    diagnostics);
            }

            pages = result.PageCount;
            return result;
        }
        catch (TypstCompilationException exception)
        {
            failure = exception.Message;
            throw;
        }
        catch (NativeCallException exception)
        {
            failure = exception.Message;
            throw NativeErrorTranslator.Translate(exception);
        }
        catch (DllNotFoundException exception)
        {
            failure = exception.Message;
            throw new TypstNativeException(
                "the native Typst library could not be loaded; see Spec 02 §9 for the probing rules",
                TypstErrorCode.AbiMismatch,
                innerException: exception);
        }
        finally
        {
            TypstInstrumentation.RecordCompilation(activity, started, pages, diagnosticCount, failure);
        }
    }

    /// <summary>
    /// Builds the metrics of a finished compilation: the numbers only the library can measure, plus
    /// the two the managed layer owns — the whole call and the diagnostic split.
    /// </summary>
    private CompilationMetrics ReadMetrics(
        TypstDocumentHandle handle,
        IReadOnlyList<TypstDiagnostic> diagnostics,
        long startedTimestamp)
    {
        var json = _native.Metrics(handle);
        var dto = json.Length == 0
            ? new CompileMetricsDto()
            : JsonSerializer.Deserialize(json, TypstJsonContext.Default.CompileMetricsDto)
                ?? new CompileMetricsDto();

        return new CompilationMetrics(
            totalDuration: Stopwatch.GetElapsedTime(startedTimestamp),
            // A tick is 100 ns; Typst reports nanoseconds, and the extra two decimal places are
            // noise next to the cost of a compile.
            compileDuration: TimeSpan.FromTicks(dto.CompileNanos / 100),
            pageCount: dto.Pages,
            fontCount: dto.FontsTotal,
            embeddedFontCount: dto.FontsEmbedded,
            fontBytes: dto.FontBytes,
            errorCount: diagnostics.Count(d => d.Severity == TypstSeverity.Error),
            warningCount: diagnostics.Count(d => d.Severity == TypstSeverity.Warning),
            hintCount: diagnostics.Count(d => d.Severity == TypstSeverity.Hint));
    }

    /// <summary>
    /// Compiles the world on the thread pool, waiting for a slot without holding a thread.
    /// </summary>
    /// <remarks>
    /// The wait for a slot is asynchronous: a request that arrives while the compiler is saturated
    /// parks on the semaphore instead of occupying a thread pool thread to stand in line. The
    /// compilation itself is CPU-bound and has nothing to await, so it still occupies a thread once
    /// it starts.
    ///
    /// Typst 0.15 has no interrupt hook, so cancellation cannot abort a compilation that has begun:
    /// the task throws <see cref="OperationCanceledException"/> once the compilation finishes, and
    /// the result is disposed rather than returned. A cancelled request that is still queued leaves
    /// without compiling at all.
    /// </remarks>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    /// <exception cref="TypstTimeoutException"><see cref="TypstCompilationOptions.Timeout"/> elapsed.</exception>
    public async Task<CompilationResult> CompileAsync(
        TypstWorld world,
        TypstCompilationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(world);
        options ??= new TypstCompilationOptions();
        cancellationToken.ThrowIfCancellationRequested();

        var clock = options.Timeout is null ? null : Stopwatch.StartNew();
        var entered = false;

        try
        {
            if (_gate is not null)
            {
                if (options.Timeout is { } limit)
                {
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    linked.CancelAfter(limit);

                    try
                    {
                        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new TypstTimeoutException(limit);
                    }
                }
                else
                {
                    await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                }

                entered = true;
            }

            var result = await Task
                .Run(() => CompileHoldingGate(world, options, clock), CancellationToken.None)
                .ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                result.Dispose();
                throw new OperationCanceledException(cancellationToken);
            }

            return result;
        }
        finally
        {
            if (entered)
            {
                _gate!.Release();
            }
        }
    }

    /// <summary>
    /// Compiles the world and reports which pages differ from the compilation recorded in
    /// <paramref name="state"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is <see cref="Compile"/> plus one comparison: the document is complete, every page is
    /// laid out, and the extra answer is the list of page indices whose content is not the same as
    /// in the previous compilation. Nothing is skipped — the saving of an incremental edit comes
    /// from Typst's own memoization of an unchanged world, not from this method, and a caller that
    /// wants both edits the world in place with <see cref="TypstWorld.SetSource"/> and keeps it
    /// between calls.
    /// </para>
    /// <para>
    /// The comparison is against what <paramref name="state"/> recorded last, so the first call on
    /// a fresh state reports every page as changed. A page that the previous compilation had and
    /// this one does not is reported in <see cref="CompileChangedResult.RemovedPages"/>.
    /// </para>
    /// <para>
    /// What it does not say is <em>what</em> changed inside a page: a page is either the same
    /// digest or not (Spec 03 §15).
    /// </para>
    /// </remarks>
    /// <exception cref="TypstCompilationException">The compiler rejected the document.</exception>
    /// <exception cref="TypstNativeException">The native layer failed.</exception>
    /// <exception cref="TypstTimeoutException"><see cref="TypstCompilationOptions.Timeout"/> elapsed.</exception>
    public CompileChangedResult CompileChanged(
        TypstWorld world,
        TypstIncrementalState state,
        TypstCompilationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(state);

        var document = Compile(world, options);

        try
        {
            var fingerprints = document.PageFingerprints();
            var previous = state.Fingerprints;
            var first = !state.HasBaseline;

            var changed = new List<int>();

            for (var page = 0; page < fingerprints.Length; page++)
            {
                if (first || page >= previous.Length || previous[page] != fingerprints[page])
                {
                    changed.Add(page);
                }
            }

            var removed = new List<int>();

            for (var page = fingerprints.Length; page < previous.Length; page++)
            {
                removed.Add(page);
            }

            state.Record(fingerprints);

            return new CompileChangedResult(document, changed, removed, first);
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Drops every compilation result Typst memoized in this process.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Typst keeps each stage of a compilation — parsing, evaluation, layout — in a process-wide
    /// table keyed by that stage's inputs, and never evicts. That is what makes a second
    /// compilation of an edited document cheaper than a cold one; it is also why a host that
    /// compiles thousands of <em>different</em> documents keeps all of their intermediate results.
    /// This is the valve.
    /// </para>
    /// <para>
    /// Process-wide, not per compiler: a second <see cref="TypstCompiler"/> instance does not have
    /// its own table, because there is only one. The next compilation of any document — from any
    /// thread — is cold again. A compilation already running keeps what it has looked up and
    /// recomputes the rest.
    /// </para>
    /// </remarks>
    public void ClearCache() => _native.ClearCache();

    /// <summary>Compiles a single source to PDF with default settings.</summary>
    public static byte[] CompileToPdf(string markup)
    {
        ArgumentNullException.ThrowIfNull(markup);

        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", markup)
            .Build();

        using var result = new TypstCompiler().Compile(world);
        return result.ExportPdf();
    }

    /// <summary>Compiles a document model to PDF.</summary>
    public static byte[] CompileToPdf(TypstDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return CompileToPdf(document.ToMarkup());
    }

    /// <summary>
    /// Compiles a single source and writes the PDF into <paramref name="destination"/>, without
    /// materialising it in a managed array first.
    /// </summary>
    public static void CompileToPdf(Stream destination, string markup)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(markup);

        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", markup)
            .Build();

        using var result = new TypstCompiler().Compile(world);
        result.ExportPdf(destination);
    }

    /// <summary>
    /// Compiles a document model and writes the PDF into <paramref name="destination"/>.
    /// </summary>
    public static void CompileToPdf(Stream destination, TypstDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        CompileToPdf(destination, document.ToMarkup());
    }

    /// <summary>Compiles a single source to SVG, one page.</summary>
    public static byte[] CompileToSvg(string markup, int page = 0)
    {
        ArgumentNullException.ThrowIfNull(markup);

        using var world = new TypstWorldBuilder().AddSource("main.typ", markup).Build();
        using var result = new TypstCompiler().Compile(world);
        return result.ExportSvg(page);
    }

    /// <summary>Compiles a single source to PNG, one page.</summary>
    public static byte[] CompileToPng(string markup, int page = 0, float ppi = 144f)
    {
        ArgumentNullException.ThrowIfNull(markup);

        using var world = new TypstWorldBuilder().AddSource("main.typ", markup).Build();
        using var result = new TypstCompiler().Compile(world);
        return result.ExportPng(page, ppi);
    }

    /// <summary>Compiles a single source to an HTML string.</summary>
    public static string CompileToHtml(string markup)
    {
        ArgumentNullException.ThrowIfNull(markup);

        using var world = new TypstWorldBuilder().AddSource("main.typ", markup).Build();
        using var result = new TypstCompiler().Compile(
            world,
            new TypstCompilationOptions { DocumentKind = TypstDocumentKind.Html });

        return result.ExportHtmlString();
    }

    /// <summary>Compiles a single source to Markdown, derived from the HTML document.</summary>
    public static string CompileToMarkdown(string markup)
    {
        ArgumentNullException.ThrowIfNull(markup);

        using var world = new TypstWorldBuilder().AddSource("main.typ", markup).Build();
        using var result = new TypstCompiler().Compile(
            world,
            new TypstCompilationOptions { DocumentKind = TypstDocumentKind.Html });

        return result.ExportMarkdown();
    }

    /// <summary>Compiles a single source to plain text, derived from the HTML document.</summary>
    public static string CompileToText(string markup)
    {
        ArgumentNullException.ThrowIfNull(markup);

        using var world = new TypstWorldBuilder().AddSource("main.typ", markup).Build();
        using var result = new TypstCompiler().Compile(
            world,
            new TypstCompilationOptions { DocumentKind = TypstDocumentKind.Html });

        return result.ExportText();
    }

    /// <summary>
    /// Compiles the world as a bundle and returns the HTML document plus the files declared with
    /// <c>#asset</c>. This is the only target in which <c>#asset</c> is valid.
    /// </summary>
    public BundleResult CompileBundle(TypstWorld world, TypstCompilationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(world);

        using var document = Compile(world, new TypstCompilationOptions
        {
            Root = options?.Root ?? "main.typ",
            DocumentKind = TypstDocumentKind.Bundle,
        });

        return new BundleResult(document.ExportHtmlString(), document.ExportBundleFiles());
    }

    /// <summary>
    /// Compiles the same world twice: once as a paged document, once as an HTML document.
    /// PDF and HTML are different layout passes in Typst, so this is the only way to get
    /// both from one source.
    /// </summary>
    /// <remarks>
    /// The paged pass runs first. If it fails, the HTML pass is not attempted and the
    /// exception carries the paged diagnostics, which are the authoritative ones.
    /// </remarks>
    public CompileBothResult CompileBoth(TypstWorld world, TypstCompilationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        var root = options?.Root ?? "main.typ";

        var paged = Compile(world, new TypstCompilationOptions
        {
            Root = root,
            DocumentKind = TypstDocumentKind.Paged,
        });

        try
        {
            var html = Compile(world, new TypstCompilationOptions
            {
                Root = root,
                DocumentKind = TypstDocumentKind.Html,
            });

            return new CompileBothResult(paged, html);
        }
        catch
        {
            paged.Dispose();
            throw;
        }
    }
}
