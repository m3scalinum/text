using System;
using System.IO;
using System.Runtime.InteropServices;
using M3scalinum.Text.Ocr.Internal;
using Xunit;

namespace M3scalinum.Text.Ocr.Tests;

/// <summary>
/// Where the native library is looked for: the override variables, the
/// <c>runtimes/&lt;rid&gt;/native/</c> layout, and the host-specific names.
/// </summary>
/// <remarks>
/// No model and no recognition is involved, so these run everywhere; the one test that
/// needs a loadable file skips when the machine has none, and says so.
/// </remarks>
public sealed class NativeLibraryResolverTests
{
    [Fact]
    public void The_candidates_are_the_two_runtime_layouts_the_os_layout_and_the_application_directory()
    {
        var fileName = NativeLibraryResolver.FileName();

        var candidates = NativeLibraryResolver.Candidates("win-x64", "linux-musl-x64", "win", "/app");

        Assert.Equal(
        [
            Path.Combine("/app", "runtimes", "win-x64", "native", fileName),
            Path.Combine("/app", "runtimes", "linux-musl-x64", "native", fileName),
            Path.Combine("/app", "runtimes", "win", "native", fileName),
            Path.Combine("/app", fileName),
        ], candidates);
    }

    [Fact]
    public void The_candidates_are_probed_under_the_directory_the_assembly_runs_from()
    {
        var candidates = NativeLibraryResolver.Candidates();

        Assert.Equal(4, candidates.Length);
        Assert.All(candidates, candidate =>
            Assert.StartsWith(AppContext.BaseDirectory, candidate, StringComparison.Ordinal));
    }

    [Fact]
    public void The_file_name_is_the_one_this_platform_loads()
    {
        var expected = OperatingSystem.IsWindows()
            ? "sharp_text_ocr.dll"
            : OperatingSystem.IsMacOS() ? "libsharp_text_ocr.dylib" : "libsharp_text_ocr.so";

        Assert.Equal(expected, NativeLibraryResolver.FileName());
    }

    [Fact]
    public void The_os_name_is_the_short_name_the_layout_uses()
    {
        var os = NativeLibraryResolver.OperatingSystemName();

        Assert.Equal(
            OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux",
            os);
    }

    [Fact]
    public void The_portable_identifier_names_the_os_and_an_architecture()
        => Assert.Matches(
            "^(win|osx|linux)-(x64|arm64|x86|unknown)$",
            NativeLibraryResolver.PortableRuntimeIdentifier());

    [Fact]
    public void The_musl_identifier_is_the_linux_one()
        => Assert.StartsWith("linux-musl-", NativeLibraryResolver.MuslRuntimeIdentifier());

    [Fact]
    public void The_runtime_identifier_follows_the_musl_decision()
        => Assert.Equal(
            NativeLibraryResolver.IsMusl()
                ? NativeLibraryResolver.MuslRuntimeIdentifier()
                : NativeLibraryResolver.PortableRuntimeIdentifier(),
            NativeLibraryResolver.RuntimeIdentifier());

    [Fact]
    public void Musl_is_only_ever_true_on_linux()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.False(NativeLibraryResolver.IsMusl());
        }
    }

    [Fact]
    public void The_overrides_are_the_two_variables_the_host_may_set()
    {
        var overrides = NativeLibraryResolver.OverridePaths();

        Assert.All(overrides, path => Assert.False(string.IsNullOrEmpty(path)));
        Assert.True(overrides.Count <= 2);
    }

    [Fact]
    public void A_library_this_resolver_does_not_own_is_left_to_the_runtime()
    {
        var handle = NativeLibraryResolver.Resolve(
            "sharp_typst", typeof(NativeLibraryResolver).Assembly, null);

        Assert.Equal(IntPtr.Zero, handle);
    }

    [Fact]
    public void A_probe_with_nothing_to_load_returns_zero()
    {
        var handle = NativeLibraryResolver.Resolve(
            NativeMethods.LibraryName,
            ["/no/such/override"],
            ["/no/such/candidate"]);

        Assert.Equal(IntPtr.Zero, handle);
    }

    [Fact]
    public void An_override_that_exists_is_loaded_before_any_candidate()
    {
        var library = LoadableLibrary();
        if (library is null)
        {
            Assert.Skip(
                "no loadable native library on this machine: set " +
                $"{NativeLibraryResolver.OverrideVariable} to one to run this test.");
        }

        var handle = NativeLibraryResolver.Resolve(
            NativeMethods.LibraryName,
            [library!],
            ["/no/such/candidate"]);

        Assert.NotEqual(IntPtr.Zero, handle);
        NativeLibrary.Free(handle);
    }

    [Fact]
    public void A_candidate_that_exists_is_loaded_when_no_override_is_supplied()
    {
        var library = LoadableLibrary();
        if (library is null)
        {
            Assert.Skip(
                "no loadable native library on this machine: set " +
                $"{NativeLibraryResolver.OverrideVariable} to one to run this test.");
        }

        var handle = NativeLibraryResolver.Resolve(NativeMethods.LibraryName, [], [library!]);

        Assert.NotEqual(IntPtr.Zero, handle);
        NativeLibrary.Free(handle);
    }

    [Fact]
    public void The_default_probe_finds_the_library_the_suite_is_pointed_at()
    {
        var library = Environment.GetEnvironmentVariable(NativeLibraryResolver.OverrideVariable);
        if (string.IsNullOrEmpty(library) || !File.Exists(library))
        {
            Assert.Skip(
                "the default probe has nothing to find: set " +
                $"{NativeLibraryResolver.OverrideVariable} to a native library to run this test.");
        }

        var handle = NativeLibraryResolver.Resolve(
            NativeMethods.LibraryName, typeof(NativeLibraryResolver).Assembly, null);

        Assert.NotEqual(IntPtr.Zero, handle);
        NativeLibrary.Free(handle);
    }

    /// <summary>
    /// A file this platform can load as a library: the OCR library the suite is pointed
    /// at, or a system library on Windows, whose path is always there.
    /// </summary>
    private static string? LoadableLibrary()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(NativeLibraryResolver.OverrideVariable);
        if (!string.IsNullOrEmpty(fromEnvironment) && File.Exists(fromEnvironment))
        {
            return fromEnvironment;
        }

        if (OperatingSystem.IsWindows())
        {
            var system = Path.Combine(Environment.SystemDirectory, "kernel32.dll");
            if (File.Exists(system))
            {
                return system;
            }
        }

        return null;
    }
}
