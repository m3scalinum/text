using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using M3scalinum.Text.Typst.Native;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

/// <summary>
/// The load-time handshake: a native library older than this build is either accepted with a
/// warning or refused, and the refusal names enough to fix the situation without reading the code
/// (Spec 02 §11).
/// </summary>
/// <remarks>
/// The real library cannot be replaced per test, so the negotiation is driven from what a loaded
/// library reports — the same seam <see cref="FakeTypstNative"/> uses one level up, where the whole
/// native surface is a C# object. <c>AbiContract</c> takes the reported revision and the file path
/// and nothing else, which is exactly what <c>tn_abi_version</c> and the resolver provide.
/// </remarks>
public sealed class AbiNegotiationTests
{
    /// <summary>
    /// A build of <c>sharp_typst</c>: one ABI revision, loaded from one file. A revision is what a
    /// release of the crate reports; older builds are what a stale asset next to an application is.
    /// </summary>
    private sealed record FakeNativeLibrary(uint AbiRevision, string Path);

    private static FakeNativeLibrary Library(uint abiRevision)
        => new(abiRevision, Path.Combine(Path.GetTempPath(), "stale", "sharp_typst.dll"));

    [Fact]
    public void The_current_revision_is_accepted_silently()
    {
        var library = Library(AbiContract.Current);
        var warnings = new List<string>();

        var report = AbiContract.Inspect(library.AbiRevision, library.Path, warnings.Add);

        Assert.Equal(AbiVerdict.Compatible, report.Verdict);
        Assert.Empty(warnings);
        Assert.Equal(AbiContract.Current, report.NativeVersion);
        Assert.Contains(library.Path, report.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_earlier_revision_inside_the_window_is_accepted_with_a_warning()
    {
        // Revision 1 is the previous ABI: still served, and the case a user hits when a native
        // package was pinned while the managed package moved on.
        var library = Library(1);
        var warnings = new List<string>();

        var report = AbiContract.Inspect(library.AbiRevision, library.Path, warnings.Add);

        Assert.Equal(AbiVerdict.Deprecated, report.Verdict);
        Assert.Equal(1u, report.NativeVersion);

        var warning = Assert.Single(warnings);

        // The five facts a consumer needs, none of them requiring any other source.
        Assert.Contains("ABI revision 1", warning, StringComparison.Ordinal);
        Assert.Contains($"this build is at revision {AbiContract.Current}", warning, StringComparison.Ordinal);
        Assert.Contains(AbiContract.Range, warning, StringComparison.Ordinal);
        Assert.Contains(library.Path, warning, StringComparison.Ordinal);
        Assert.Contains("deprecated", warning, StringComparison.Ordinal);

        // What breaks, and when: the gap in symbols, and the revision where the library is refused.
        Assert.Contains($"refused from revision {AbiContract.Current + 1} on", warning, StringComparison.Ordinal);
        Assert.Contains("fails at its call, not at load", warning, StringComparison.Ordinal);
        Assert.Contains("Ship the native asset from the same release", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void A_library_older_than_the_window_is_refused_with_a_typed_exception_naming_both_revisions()
    {
        // One revision below the window: the shape of a library from before the range existed.
        var library = Library(AbiContract.Minimum - 1);

        var thrown = Assert.Throws<NativeCallException>(
            () => AbiContract.Ensure(library.AbiRevision, library.Path));

        Assert.Equal(NativeStatus.AbiMismatch, thrown.Status);
        Assert.Contains("ABI revision 0", thrown.Message, StringComparison.Ordinal);
        Assert.Contains($"at revision {AbiContract.Current}", thrown.Message, StringComparison.Ordinal);
        Assert.Contains(AbiContract.Range, thrown.Message, StringComparison.Ordinal);
        Assert.Contains(library.Path, thrown.Message, StringComparison.Ordinal);

        // The public taxonomy: what a caller catches is a TypstNativeException, not an interop
        // shape, and the code says why.
        var translated = Assert.IsType<TypstNativeException>(NativeErrorTranslator.Translate(thrown));

        Assert.Equal(TypstErrorCode.AbiMismatch, translated.Code);
        Assert.Contains("ABI revision 0", translated.Message, StringComparison.Ordinal);
        Assert.Contains($"revision {AbiContract.Current}", translated.Message, StringComparison.Ordinal);
        Assert.Contains(library.Path, translated.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_library_newer_than_this_build_is_refused_and_the_message_says_to_update_the_package()
    {
        var library = Library(AbiContract.Current + 1);

        var thrown = Assert.Throws<NativeCallException>(
            () => AbiContract.Ensure(library.AbiRevision, library.Path));

        Assert.Equal(NativeStatus.AbiMismatch, thrown.Status);
        Assert.Contains($"ABI revision {AbiContract.Current + 1}", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("newer than the revision", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("update M3scalinum.Text.Typst", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_negotiation_never_runs_without_an_outcome()
    {
        // Every revision a u32 can hold is either accepted or refused: a gap here would let a
        // library through and fail later, on the first symbol that changed.
        foreach (var revision in new uint[] { 0, AbiContract.Minimum, AbiContract.Current, AbiContract.Current + 1, uint.MaxValue })
        {
            var report = AbiContract.Inspect(revision, "sharp-typst", warn: null);

            Assert.Equal(
                revision >= AbiContract.Minimum && revision <= AbiContract.Current,
                report.Verdict != AbiVerdict.Refused);
            Assert.False(string.IsNullOrWhiteSpace(report.Message));
        }
    }

    [Fact]
    public void The_accepted_range_covers_the_current_revision_and_the_previous_one()
    {
        // A revision is served for two ABI revisions, which is what makes the window measurable
        // rather than a promise in prose.
        Assert.Equal(2u, AbiContract.AcceptedRevisions);
        Assert.Equal(AbiContract.Current - 1, AbiContract.Minimum);
        Assert.Equal($"{AbiContract.Minimum}..{AbiContract.Current}", AbiContract.Range);
    }

    [Fact]
    public void The_managed_range_matches_the_constants_the_crate_exports()
    {
        // Two languages, one contract: a bump on one side alone would make every installed pair
        // either deprecated or refused for no reason.
        var lib = File.ReadAllText(Path.Combine(RepositoryRoot(), "native", "sharp-typst", "src", "lib.rs"));

        Assert.Equal(AbiContract.Current, RustConstant(lib, "TN_ABI_VERSION"));
        Assert.Equal(AbiContract.Minimum, RustConstant(lib, "TN_ABI_MIN_VERSION"));
    }

    [Fact]
    public void The_deprecation_warning_reaches_a_trace_listener()
    {
        // The warning is only useful if a host can read it: a TraceSource is silent until a
        // listener is attached, and attaching one is the documented consumption path.
        using var listener = new Collector();
        AbiWarning.Source.Listeners.Add(listener);

        try
        {
            AbiWarning.Warn("the native library at 'x' reports ABI revision 1");
        }
        finally
        {
            AbiWarning.Source.Listeners.Remove(listener);
        }

        var message = Assert.Single(listener.Messages);

        Assert.Equal(TraceEventType.Warning, message.Type);
        Assert.Contains("ABI revision 1", message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_library_path_is_reported_as_the_runtime_probe()
    {
        var report = AbiContract.Inspect(0, libraryPath: null);

        Assert.Contains("default probe", report.Message, StringComparison.Ordinal);
    }

    private static uint RustConstant(string source, string name)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            source, $@"pub const {name}: u32 = (\d+);");

        Assert.True(match.Success, $"{name} is not declared in lib.rs");
        return uint.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "docs", "specifications")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"could not find docs/specifications above {AppContext.BaseDirectory}");
    }

    /// <summary>Collects the trace events a host would receive.</summary>
    private sealed class Collector : TraceListener
    {
        public List<(TraceEventType Type, string Text)> Messages { get; } = [];

        // TraceSource routes to the formatting overload, so overriding only the plain one would
        // collect nothing and the test would pass on silence.
        public override void TraceEvent(
            TraceEventCache? eventCache,
            string source,
            TraceEventType eventType,
            int id,
            string? format,
            params object?[]? args)
            => Messages.Add((
                eventType,
                format is null ? string.Empty
                    : args is { Length: > 0 } ? string.Format(CultureInfo.InvariantCulture, format, args)
                    : format));

        public override void TraceEvent(
            TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message)
            => Messages.Add((eventType, message ?? string.Empty));

        public override void Write(string? message)
        {
        }

        public override void WriteLine(string? message)
        {
        }
    }
}
