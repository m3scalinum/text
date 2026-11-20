using System;

namespace M3scalinum.Text.SharpTypst.Compilation;

/// <summary>
/// The two layout passes of the same source: a paged document for PDF/SVG/PNG and an HTML
/// document for HTML, Markdown and text.
/// </summary>
public sealed class CompileBothResult : IDisposable
{
    private bool _disposed;

    internal CompileBothResult(CompilationResult paged, CompilationResult html)
    {
        Paged = paged;
        Html = html;
    }

    /// <summary>The paged layout, used for PDF, SVG and PNG.</summary>
    public CompilationResult Paged { get; }

    /// <summary>The HTML layout, used for HTML, Markdown and text.</summary>
    public CompilationResult Html { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Paged.Dispose();
        Html.Dispose();
    }
}
