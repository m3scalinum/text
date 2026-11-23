using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Typst.Compilation;

/// <summary>
/// A compilation and the pages that differ from the previous one.
/// </summary>
/// <remarks>
/// <para>
/// The document is the whole compilation: every page is laid out and every export works on all of
/// it. What this type adds is the answer to "which pages do I have to re-render" — the list of
/// page indices whose content is not the same as in the compilation the state was holding.
/// </para>
/// <para>
/// It does not say <em>what</em> changed inside a page, and it does not say why: a page is either
/// different or it is not. Turning an index into pixels is the caller's job, and the document it
/// gets here is what renders them.
/// </para>
/// </remarks>
public sealed class CompileChangedResult : IDisposable
{
    private bool _disposed;

    internal CompileChangedResult(
        CompilationResult document,
        IReadOnlyList<int> changedPages,
        IReadOnlyList<int> removedPages,
        bool isFirstCompile)
    {
        Document = document;
        ChangedPages = changedPages;
        RemovedPages = removedPages;
        IsFirstCompile = isFirstCompile;
    }

    /// <summary>The compilation, complete: every page is present, changed or not.</summary>
    public CompilationResult Document { get; }

    /// <summary>
    /// Zero-based indices of the pages whose content differs from the previous compilation, in
    /// ascending order.
    /// </summary>
    /// <remarks>
    /// <see cref="IsFirstCompile"/> means "all of them". A document without pages — an HTML or
    /// bundle compilation — has an empty list: there is nothing to compare.
    /// </remarks>
    public IReadOnlyList<int> ChangedPages { get; }

    /// <summary>
    /// Zero-based indices the previous compilation had that this one no longer has, in ascending
    /// order. Empty when the document grew or kept its length.
    /// </summary>
    public IReadOnlyList<int> RemovedPages { get; }

    /// <summary>
    /// Whether there was no baseline to compare against, so <see cref="ChangedPages"/> lists every
    /// page of the document.
    /// </summary>
    public bool IsFirstCompile { get; }

    /// <summary>Releases the compiled document.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Document.Dispose();
    }
}
