using System;

namespace M3scalinum.Text.Typst.Native;

/// <summary>What the managed layer concludes from the revision a native library reports.</summary>
internal enum AbiVerdict
{
    /// <summary>Same revision as this build: nothing to say.</summary>
    Compatible,

    /// <summary>An earlier revision inside the accepted range: usable, but deprecated.</summary>
    Deprecated,

    /// <summary>Outside the accepted range: the library must not be used.</summary>
    Refused,
}

/// <summary>The outcome of reading one native library's ABI revision (Spec 02 §11).</summary>
/// <remarks>
/// Carries the numbers as well as the sentence, so a caller can log or branch on the facts without
/// parsing the message, and a test can assert on both.
/// </remarks>
internal sealed class AbiReport
{
    /// <summary>Initialises the report.</summary>
    /// <param name="verdict">What to do with the library.</param>
    /// <param name="nativeVersion">The revision the library reported.</param>
    /// <param name="libraryPath">The file that was loaded, when the resolver knows it.</param>
    /// <param name="message">A sentence that diagnoses the situation on its own.</param>
    public AbiReport(AbiVerdict verdict, uint nativeVersion, string libraryPath, string message)
    {
        Verdict = verdict;
        NativeVersion = nativeVersion;
        LibraryPath = libraryPath;
        Message = message;
    }

    /// <summary>What to do with the library.</summary>
    public AbiVerdict Verdict { get; }

    /// <summary>The revision the library reported.</summary>
    public uint NativeVersion { get; }

    /// <summary>The file the resolver loaded, or a placeholder when the runtime probed for it.</summary>
    public string LibraryPath { get; }

    /// <summary>What was reported, what this build expects, the accepted range and the path.</summary>
    public string Message { get; }
}

/// <summary>
/// The ABI revisions this managed assembly can work against, and how a native library is judged
/// against them (Spec 02 §11).
/// </summary>
/// <remarks>
/// <para>
/// The check replaces the exact-equality handshake: a native library is accepted when its revision
/// falls in <c>[Minimum, Current]</c>. A revision below <see cref="Current"/> is deprecated and
/// produces a warning; outside the range the load is refused with a typed failure that names both
/// versions.
/// </para>
/// <para>
/// There is no switch to turn this off, and there is no environment variable: the check runs on
/// every load, because a mismatch that reaches the first call fails later and less clearly.
/// </para>
/// </remarks>
internal static class AbiContract
{
    /// <summary>The ABI revision this assembly is built against.</summary>
    internal const uint Current = 2;

    /// <summary>The oldest ABI revision this assembly still works against.</summary>
    /// <remarks>
    /// A revision is served for two ABI revisions — its own and the next one — so revision 1 is
    /// accepted here and will be refused when <see cref="Current"/> reaches 3.
    /// </remarks>
    internal const uint Minimum = 1;

    /// <summary>How many revisions the accepted range spans: the current one and the previous one.</summary>
    internal const uint AcceptedRevisions = Current - Minimum + 1;

    /// <summary>The accepted range, as written in messages: <c>1..2</c>.</summary>
    internal static string Range => $"{Minimum}..{Current}";

    /// <summary>
    /// Judges <paramref name="nativeVersion"/> without throwing, and announces a deprecation
    /// through <paramref name="warn"/> when the library is still usable.
    /// </summary>
    /// <param name="nativeVersion">The revision the library reported.</param>
    /// <param name="libraryPath">The file that was loaded, when it is known.</param>
    /// <param name="warn">Where a deprecation warning goes; <see langword="null"/> discards it.</param>
    internal static AbiReport Inspect(uint nativeVersion, string? libraryPath, Action<string>? warn = null)
    {
        var path = Describe(libraryPath);

        if (nativeVersion < Minimum)
        {
            return new AbiReport(
                AbiVerdict.Refused,
                nativeVersion,
                path,
                $"the native library at '{path}' reports ABI revision {nativeVersion}, which this build does not support: "
                + $"M3scalinum.Text.Typst is at revision {Current} and accepts revisions {Range}. "
                + $"Revision {nativeVersion} predates the layout this layer calls, so the first call would fail instead of the load. "
                + "Replace it with the library shipped in the package's runtimes/<rid>/native folder; a stale file next to the "
                + "application, or a native package pinned to an older version, is what usually puts an old library there.");
        }

        if (nativeVersion > Current)
        {
            return new AbiReport(
                AbiVerdict.Refused,
                nativeVersion,
                path,
                $"the native library at '{path}' reports ABI revision {nativeVersion}, which is newer than the revision {Current} "
                + $"this build understands: M3scalinum.Text.Typst is at revision {Current} and accepts revisions {Range}. "
                + "The native asset belongs to a newer release than the managed package; update M3scalinum.Text.Typst and "
                + "M3scalinum.Text.Typst.Native together so both halves of the same release are loaded.");
        }

        if (nativeVersion < Current)
        {
            var message =
                $"the native library at '{path}' reports ABI revision {nativeVersion}; this build is at revision {Current} "
                + $"and accepts revisions {Range}. Revision {nativeVersion} is deprecated: it is still served, and it is refused "
                + $"from revision {Current + 1} on. Symbols added to the ABI after revision {nativeVersion} are absent from this "
                + $"library, so a feature that needs one fails at its call, not at load. Ship the native asset from the same release "
                + $"as the managed package before the next ABI revision: a managed layer at revision {Current + 1} will refuse this "
                + "library outright.";

            warn?.Invoke(message);
            return new AbiReport(AbiVerdict.Deprecated, nativeVersion, path, message);
        }

        return new AbiReport(
            AbiVerdict.Compatible,
            nativeVersion,
            path,
            $"the native library at '{path}' reports ABI revision {nativeVersion}, which this build accepts.");
    }

    /// <summary>
    /// Judges <paramref name="nativeVersion"/> and refuses it when it is out of range.
    /// </summary>
    /// <exception cref="NativeCallException">
    /// <see cref="NativeStatus.AbiMismatch"/> when the library is outside the accepted range.
    /// </exception>
    internal static AbiReport Ensure(uint nativeVersion, string? libraryPath, Action<string>? warn = null)
    {
        var report = Inspect(nativeVersion, libraryPath, warn);

        if (report.Verdict == AbiVerdict.Refused)
        {
            throw new NativeCallException(NativeStatus.AbiMismatch, string.Empty, report.Message);
        }

        return report;
    }

    /// <summary>
    /// The path a message quotes. The runtime probes for the library when the resolver has no
    /// candidate, and saying so is more useful than an empty pair of quotes.
    /// </summary>
    private static string Describe(string? libraryPath)
        => string.IsNullOrEmpty(libraryPath) ? "(unknown: found by the runtime's default probe)" : libraryPath;
}
