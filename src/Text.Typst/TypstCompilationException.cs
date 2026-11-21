using System.Collections.Generic;
using M3scalinum.Text.Typst.Diagnostics;

namespace M3scalinum.Text.Typst;

/// <summary>The compiler rejected the document.</summary>
public sealed class TypstCompilationException : TypstException
{
    /// <summary>Initialises the exception.</summary>
    public TypstCompilationException(string message, IReadOnlyList<TypstDiagnostic> diagnostics)
        : base(message, TypstErrorCode.Compilation, diagnostics)
    {
    }
}