using System.Diagnostics;

namespace M3scalinum.Text.Typst.Native;

/// <summary>
/// Where a deprecation notice about the native library goes.
/// </summary>
/// <remarks>
/// <para>
/// One warning, once per process, through a <see cref="TraceSource"/> named after this assembly.
/// A <see cref="TraceSource"/> rather than <c>Console</c>: a library that writes to the console
/// interferes with whatever the host prints, while a trace source is silent until the host attaches
/// a listener — and a host that wants the notice in its logs, or wants to fail on it, attaches one.
/// </para>
/// <para>
/// The listener a consumer needs:
/// <c>TraceSource("M3scalinum.Text.Typst.Native", SourceLevels.Warning).Listeners.Add(new ConsoleTraceListener())</c>,
/// or the equivalent in its logging framework. The text is self-contained: the revision found, the
/// revision expected, the accepted range, the loaded file, and what stops working when the library
/// is replaced.
/// </para>
/// </remarks>
internal static class AbiWarning
{
    /// <summary>The name a host filters on to receive ABI notices.</summary>
    internal const string SourceName = "M3scalinum.Text.Typst.Native";

    private static readonly TraceSource Instance = new(SourceName, SourceLevels.Warning);

    /// <summary>
    /// The source the warning is written to. Exposed so a test can attach a listener and read the
    /// text a host would see; nothing in the library writes to it except <see cref="Warn"/>.
    /// </summary>
    internal static TraceSource Source => Instance;

    /// <summary>Writes one warning event with <paramref name="message"/>.</summary>
    internal static void Warn(string message)
        // A format string with no placeholders still parses braces, and a Windows path or a
        // library name can carry one, so the message is passed as an argument.
        => Instance.TraceEvent(TraceEventType.Warning, 1, "{0}", message);
}
