using System;

namespace M3scalinum.Text.Typst.Compilation;

/// <summary>Options for a single compilation.</summary>
public sealed class TypstCompilationOptions
{
    /// <summary>Virtual path of the source to compile. Default <c>main.typ</c>.</summary>
    public string Root { get; init; } = "main.typ";

    /// <summary>Layout pass to run. Default <see cref="TypstDocumentKind.Paged"/>.</summary>
    public TypstDocumentKind DocumentKind { get; init; } = TypstDocumentKind.Paged;

    /// <summary>
    /// Whether a warning fails the compilation. Default <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// When set, a document that compiles with warnings throws
    /// <see cref="TypstCompilationException"/> carrying those warnings, and the result is disposed.
    /// Use it in a build or a CI gate where a warning is a defect.
    /// </remarks>
    public bool WarningsAsErrors { get; init; }

    /// <summary>
    /// How long the caller is willing to wait for the compilation. Default: no bound.
    /// </summary>
    /// <remarks>
    /// Typst 0.15 has no interrupt hook, so this does not stop the work: the compilation is
    /// abandoned on its own thread and its handles are released when it ends. It bounds the caller's
    /// wait, which is what a request has to bound, and it makes a template from an untrusted source
    /// survivable — pair it with <see cref="TypstLimits"/> so the abandoned work cannot eat the
    /// machine either.
    /// </remarks>
    public TimeSpan? Timeout { get; init; }
}
