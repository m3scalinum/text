using System;
using System.IO;
using System.Runtime.InteropServices;
using M3scalinum.Text.Barcodes.Internal;
using Xunit;

namespace M3scalinum.Text.Barcodes.Tests;

/// <summary>
/// The probing order and the RID helpers, pinned with every input explicit.
/// </summary>
/// <remarks>
/// A development run always finds the library through <c>BARCODES_NATIVE_PATH</c>, so the package
/// layout, the musl fallback and the file names are the parts of resolution no other test reaches.
/// </remarks>
public sealed class NativeLibraryResolverTests
{
    [Theory]
    [InlineData("win-x64", "win")]
    [InlineData("osx-arm64", "osx")]
    [InlineData("linux-x64", "linux")]
    public void The_host_probe_order_is_rid_os_then_beside_the_assembly(string rid, string os)
    {
        // The file name follows the host, not the RID argument: what is pinned here is the order of
        // the four directories, which is the contract with NuGet's runtimes/<rid>/native/ layout.
        var file = NativeLibraryResolver.FileName();

        var candidates = NativeLibraryResolver.Candidates(rid, "portable", os, "/app");

        Assert.Equal(4, candidates.Length);
        Assert.Equal(Path.Combine("/app", "runtimes", rid, "native", file), candidates[0]);
        Assert.Equal(Path.Combine("/app", "runtimes", "portable", "native", file), candidates[1]);
        Assert.Equal(Path.Combine("/app", "runtimes", os, "native", file), candidates[2]);
        Assert.Equal(Path.Combine("/app", file), candidates[3]);
    }

    [Fact]
    public void The_host_candidates_start_with_the_musl_aware_rid_and_end_beside_the_assembly()
    {
        var candidates = NativeLibraryResolver.Candidates();
        var file = NativeLibraryResolver.FileName();

        Assert.Equal(4, candidates.Length);
        Assert.StartsWith(
            Path.Combine(AppContext.BaseDirectory, "runtimes", NativeLibraryResolver.RuntimeIdentifier()),
            candidates[0],
            StringComparison.Ordinal);
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, file), candidates[3]);
    }

    [Fact]
    public void The_portable_rid_names_the_os_and_the_process_architecture()
    {
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => "unknown",
        };

        Assert.Equal(
            $"{NativeLibraryResolver.OperatingSystemName()}-{architecture}",
            NativeLibraryResolver.PortableRuntimeIdentifier());
    }

    [Fact]
    public void The_musl_rid_is_always_a_linux_one()
    {
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => "unknown",
        };

        Assert.Equal($"linux-musl-{architecture}", NativeLibraryResolver.MuslRuntimeIdentifier());
    }

    [Fact]
    public void A_host_that_is_not_linux_is_not_musl_and_uses_the_portable_rid()
    {
        // On Linux this assertion is the other way round; on every other host the libc question does
        // not arise and the RID must be the portable one.
        if (OperatingSystem.IsLinux())
        {
            Assert.Equal(
                NativeLibraryResolver.IsMusl()
                    ? NativeLibraryResolver.MuslRuntimeIdentifier()
                    : NativeLibraryResolver.PortableRuntimeIdentifier(),
                NativeLibraryResolver.RuntimeIdentifier());

            return;
        }

        Assert.False(NativeLibraryResolver.IsMusl());
        Assert.Equal(
            NativeLibraryResolver.PortableRuntimeIdentifier(),
            NativeLibraryResolver.RuntimeIdentifier());
    }

    [Fact]
    public void The_operating_system_name_is_one_of_the_three_the_package_ships()
    {
        var name = NativeLibraryResolver.OperatingSystemName();

        Assert.Contains(name, new[] { "win", "osx", "linux" });
        Assert.Equal(
            OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux",
            name);
    }

    [Fact]
    public void The_file_name_is_the_one_that_host_loads()
    {
        var name = NativeLibraryResolver.FileName();

        var expected = OperatingSystem.IsWindows()
            ? "sharp_text_barcodes.dll"
            : OperatingSystem.IsMacOS() ? "libsharp_text_barcodes.dylib" : "libsharp_text_barcodes.so";

        Assert.Equal(expected, name);
    }
}
