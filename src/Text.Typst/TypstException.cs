using System;
using System.Collections.Generic;
using M3scalinum.Text.Typst.Diagnostics;

namespace M3scalinum.Text.Typst;

/// <summary>Base type for every failure raised by M3scalinum.Text.Typst.</summary>
public abstract class TypstException : Exception
{
    /// <summary>Initialises the exception.</summary>
    protected TypstException(
        string message,
        TypstErrorCode code,
        IReadOnlyList<TypstDiagnostic>? diagnostics = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        Diagnostics = diagnostics ?? [];
    }

    /// <summary>Machine-readable reason.</summary>
    public TypstErrorCode Code { get; }

    /// <summary>Compiler diagnostics, empty when the failure did not come from the compiler.</summary>
    public IReadOnlyList<TypstDiagnostic> Diagnostics { get; }
}