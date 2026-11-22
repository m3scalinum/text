using System;

namespace M3scalinum.Text.Typst.Compilation;

/// <summary>
/// What one compilation cost: the time it took, what it produced, and what it had to load to
/// produce it.
/// </summary>
/// <remarks>
/// <para>
/// The numbers are attached to the result they describe, not published to a process-wide place.
/// Compilations run in parallel — a pool compiles one document per request — so anything shared
/// would have every caller overwriting another one's duration and page count. A return value
/// travels with the document, which is the only scope in which it is true.
/// </para>
/// <para>
/// <see cref="CompileDuration"/> is the part Typst itself spends; <see cref="TotalDuration"/> is the
/// whole call, marshalling and diagnostics included. The difference is what the managed layer costs
/// around the compiler, and it is the number that grows when the layer, not the layout, regresses.
/// </para>
/// <para>
/// Nothing here is collected unless a caller reads it: there is no listener to enable, no counter
/// to reset, and no overhead when the result is exported and discarded.
/// </para>
/// </remarks>
public sealed record CompilationMetrics
{
    internal CompilationMetrics(
        TimeSpan totalDuration,
        TimeSpan compileDuration,
        int pageCount,
        int fontCount,
        int embeddedFontCount,
        long fontBytes,
        int errorCount,
        int warningCount,
        int hintCount)
    {
        TotalDuration = totalDuration;
        CompileDuration = compileDuration;
        PageCount = pageCount;
        FontCount = fontCount;
        EmbeddedFontCount = embeddedFontCount;
        FontBytes = fontBytes;
        ErrorCount = errorCount;
        WarningCount = warningCount;
        HintCount = hintCount;
    }

    /// <summary>
    /// How long the whole <c>Compile</c> call took, from the end of the queue wait to the result
    /// being ready: the native call, the diagnostics and the metrics read included.
    /// </summary>
    /// <remarks>
    /// The wait for a slot in <see cref="TypstCompiler"/>'s gate is excluded. A caller measuring its
    /// own request adds that wait back, which is what makes the two numbers tell apart "the machine
    /// is saturated" from "this document is expensive".
    /// </remarks>
    public TimeSpan TotalDuration { get; }

    /// <summary>
    /// How long Typst itself ran: the compile, plus the bundle export when the target is a bundle.
    /// </summary>
    /// <remarks>
    /// Measured natively, so it does not include marshalling, option encoding or reading the
    /// diagnostics back. It is the number to compare between two versions of a template; the
    /// difference with <see cref="TotalDuration"/> is the cost of the layer around it.
    /// </remarks>
    public TimeSpan CompileDuration { get; }

    /// <summary>Pages the layout produced. Zero for an HTML or bundle document, which has none.</summary>
    public int PageCount { get; }

    /// <summary>Fonts the world could resolve: embedded in the binary and loaded from the host.</summary>
    public int FontCount { get; }

    /// <summary>How many of <see cref="FontCount"/> are the fonts embedded in the native library.</summary>
    public int EmbeddedFontCount { get; }

    /// <summary>How many of <see cref="FontCount"/> came from the host: system or uploaded faces.</summary>
    public int HostFontCount => FontCount - EmbeddedFontCount;

    /// <summary>
    /// Bytes of font data the world held, each underlying file counted once even when it carries
    /// several faces.
    /// </summary>
    /// <remarks>
    /// This is what a pool has to budget per concurrent compilation: the layout copies glyph data
    /// out of these buffers, and a host that loads a hundred megabytes of its own fonts pays it
    /// once per world that loads them.
    /// </remarks>
    public long FontBytes { get; }

    /// <summary>
    /// Errors the compile emitted. Always zero on a result: a compilation that fails throws
    /// <see cref="TypstCompilationException"/> and produces no <see cref="CompilationResult"/>.
    /// </summary>
    public int ErrorCount { get; }

    /// <summary>Warnings the compile emitted.</summary>
    public int WarningCount { get; }

    /// <summary>Hints the compile emitted.</summary>
    public int HintCount { get; }

    /// <summary>Diagnostics of every severity.</summary>
    public int DiagnosticCount => ErrorCount + WarningCount + HintCount;
}
