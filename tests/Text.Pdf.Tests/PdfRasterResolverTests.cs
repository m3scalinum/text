using System;
using System.IO;
using System.Runtime.InteropServices;
using M3scalinum.Text.Pdf.Internal;
using Xunit;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// Where the raster library is looked for: the override variable, the
/// <c>runtimes/&lt;rid&gt;/native/</c> layout, and the host-specific names.
/// </summary>
/// <remarks>
/// Nothing here renders anything, so these run on a machine without the library; the two tests that
/// need a loadable file skip when the machine has none, and say so.
/// </remarks>
public sealed class PdfRasterResolverTests
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
            ? "sharp_text_pdf.dll"
            : OperatingSystem.IsMacOS() ? "libsharp_text_pdf.dylib" : "libsharp_text_pdf.so";

        Assert.Equal(expected, NativeLibraryResolver.FileName());
    }

    [Fact]
    public void The_os_name_is_the_short_name_the_layout_uses()
        => Assert.Equal(
            OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux",
            NativeLibraryResolver.OperatingSystemName());

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
    public void The_override_variable_is_the_one_this_package_names()
    {
        Assert.Equal("SHARPTEXTPDF_NATIVE_PATH", NativeLibraryResolver.OverrideVariable);

        var overrides = NativeLibraryResolver.OverridePaths();

        Assert.All(overrides, path => Assert.False(string.IsNullOrEmpty(path)));
        Assert.True(overrides.Count <= 1);
    }

    [Fact]
    public void A_library_this_resolver_does_not_own_is_left_to_the_runtime()
        => Assert.Equal(
            IntPtr.Zero,
            NativeLibraryResolver.Resolve("sharp_typst", typeof(NativeLibraryResolver).Assembly, null));

    [Fact]
    public void A_probe_with_nothing_to_load_returns_zero()
        => Assert.Equal(
            IntPtr.Zero,
            NativeLibraryResolver.Resolve(
                PdfRasterNativeMethods.LibraryName,
                ["/no/such/override"],
                ["/no/such/candidate"]));

    [Fact]
    public void An_override_that_exists_is_loaded_before_any_candidate()
    {
        var library = LoadableLibrary();
        if (library is null)
        {
            Assert.Skip(
                $"no loadable native library on this machine: set {NativeLibraryResolver.OverrideVariable} to one.");
        }

        var handle = NativeLibraryResolver.Resolve(
            PdfRasterNativeMethods.LibraryName,
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
                $"no loadable native library on this machine: set {NativeLibraryResolver.OverrideVariable} to one.");
        }

        var handle = NativeLibraryResolver.Resolve(PdfRasterNativeMethods.LibraryName, [], [library!]);

        Assert.NotEqual(IntPtr.Zero, handle);
        NativeLibrary.Free(handle);
    }

    /// <summary>
    /// A file this platform can load as a library: the raster library the suite is pointed at, or a
    /// system library on Windows, whose path is always there.
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
            return File.Exists(system) ? system : null;
        }

        return null;
    }
}
