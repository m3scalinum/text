using System;

namespace M3scalinum.Text.Typst;

/// <summary>
/// The compilation did not finish within the time the caller allowed.
/// </summary>
/// <remarks>
/// Typst 0.15 has no interrupt hook, so the work is not stopped: it is abandoned, and the thread
/// that runs it releases its handles when it ends. The timeout bounds how long the caller waits,
/// not how much CPU the host spends — a document from an untrusted source still needs process-level
/// isolation.
/// </remarks>
public sealed class TypstTimeoutException : TypstException
{
    /// <summary>Initialises the exception.</summary>
    public TypstTimeoutException(TimeSpan timeout)
        : base(
            $"the compilation did not finish within {timeout.TotalMilliseconds:0} ms; it keeps running in the background and its handles are released when it ends",
            TypstErrorCode.Timeout)
    {
        Timeout = timeout;
    }

    /// <summary>The time the caller allowed.</summary>
    public TimeSpan Timeout { get; }
}