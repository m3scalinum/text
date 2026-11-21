using System;

namespace M3scalinum.Text.Typst;

/// <summary>
/// A failure at the native boundary: a panic, a version mismatch, a missing library or an
/// invalid handle. The process is always still usable.
/// </summary>
public sealed class TypstNativeException : TypstException
{
    /// <summary>Initialises the exception.</summary>
    public TypstNativeException(
        string message,
        TypstErrorCode code,
        string? payload = null,
        Exception? innerException = null)
        : base(message, code, diagnostics: null, innerException)
    {
        Payload = payload;
    }

    /// <summary>Raw JSON error payload, useful for bug reports.</summary>
    public string? Payload { get; }
}