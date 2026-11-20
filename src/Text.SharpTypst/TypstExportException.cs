using System.Collections.Generic;
using M3scalinum.Text.SharpTypst.Diagnostics;

namespace M3scalinum.Text.SharpTypst;

/// <summary>Layout succeeded but the export failed.</summary>
public sealed class TypstExportException : TypstException
{
    /// <summary>Initialises the exception.</summary>
    public TypstExportException(string message, IReadOnlyList<TypstDiagnostic> diagnostics)
        : base(message, TypstErrorCode.Export, diagnostics)
    {
    }
}