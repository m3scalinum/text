using System;
using System.IO;
using M3scalinum.Text.Typst.Native;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

/// <summary>
/// The native library resolver decides whether the package loads at all. A wrong path here is a
/// <c>DllNotFoundException</c> on a platform nobody tested, so the search order is pinned.
/// </summary>
public sealed class NativeLibraryResolverTests
{
    private static string Separator => Path.DirectorySeparatorChar.ToString();

    [Fact]
    public void The_candidates_are_ordered_from_most_to_least_specific()
    {
        var candidates = NativeLibraryResolver.Candidates();
        var fileName = NativeLibraryResolver.FileName();

        Assert.Equal(4, candidates.Length);

        // 1. runtimes/<rid>/native/<file> — linux-musl-<arch> on a musl host.
        Assert.Contains(
            $"{Separator}runtimes{Separator}{NativeLibraryResolver.RuntimeIdentifier()}{Separator}native{Separator}",
            candidates[0],
            StringComparison.Ordinal);
        Assert.EndsWith(fileName, candidates[0], StringComparison.Ordinal);

        // 2. runtimes/<portable-rid>/native/<file> — the glibc build, tried second on musl.
        Assert.Contains(
            $"{Separator}runtimes{Separator}{NativeLibraryResolver.PortableRuntimeIdentifier()}{Separator}native{Separator}",
            candidates[1],
            StringComparison.Ordinal);
        Assert.EndsWith(fileName, candidates[1], StringComparison.Ordinal);

        // 3. runtimes/<os>/native/<file> — the universal macOS dylib lives here.
        Assert.Contains(
            $"{Separator}runtimes{Separator}{NativeLibraryResolver.OperatingSystemName()}{Separator}native{Separator}",
            candidates[2],
            StringComparison.Ordinal);
        Assert.EndsWith(fileName, candidates[2], StringComparison.Ordinal);

        // 4. next to the application.
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, fileName), candidates[3]);
    }

    [Fact]
    public void The_musl_candidate_is_probed_before_the_portable_one()
    {
        // NuGet's RID graph resolves linux-musl-x64 to the glibc asset, which then fails to load.
        // The musl artifact must therefore be tried first, and the portable one kept as a fallback.
        var candidates = NativeLibraryResolver.Candidates(
            "linux-musl-x64", "linux-x64", "linux", "/app");

        Assert.Equal(
            [
                $"/app{Separator}runtimes{Separator}linux-musl-x64{Separator}native{Separator}{NativeLibraryResolver.FileName()}",
                $"/app{Separator}runtimes{Separator}linux-x64{Separator}native{Separator}{NativeLibraryResolver.FileName()}",
                $"/app{Separator}runtimes{Separator}linux{Separator}native{Separator}{NativeLibraryResolver.FileName()}",
                $"/app{Separator}{NativeLibraryResolver.FileName()}",
            ],
            candidates);
    }

    [Fact]
    public void Only_a_linux_host_can_be_musl()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.False(NativeLibraryResolver.IsMusl());
            Assert.Equal(
                NativeLibraryResolver.PortableRuntimeIdentifier(),
                NativeLibraryResolver.RuntimeIdentifier());
        }
        else
        {
            // On Linux the two possible answers are the portable RID and the musl one.
            Assert.Contains(
                NativeLibraryResolver.RuntimeIdentifier(),
                new[] { NativeLibraryResolver.PortableRuntimeIdentifier(), NativeLibraryResolver.MuslRuntimeIdentifier() });
        }
    }

    [Fact]
    public void The_second_candidate_is_the_universal_osx_fallback()
    {
        // .NET maps osx-arm64 to ['osx', 'unix-arm64'], so a universal dylib ships under `osx`.
        // Dropping this candidate is a silent DllNotFoundException on macOS.
        var candidates = NativeLibraryResolver.Candidates();
        var os = NativeLibraryResolver.OperatingSystemName();

        Assert.Contains($"{Separator}runtimes{Separator}{os}{Separator}native{Separator}", candidates[2], StringComparison.Ordinal);
    }

    [Fact]
    public void The_file_name_matches_the_platform()
    {
        var name = NativeLibraryResolver.FileName();

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("sharp_typst.dll", name);
        }
        else if (OperatingSystem.IsMacOS())
        {
            Assert.Equal("libsharp_typst.dylib", name);
        }
        else
        {
            Assert.Equal("libsharp_typst.so", name);
        }
    }

    [Fact]
    public void The_runtime_identifier_matches_the_platform()
    {
        var rid = NativeLibraryResolver.RuntimeIdentifier();

        Assert.StartsWith(NativeLibraryResolver.OperatingSystemName() + "-", rid, StringComparison.Ordinal);
        Assert.Contains(
            rid,
            new[]
            {
                "win-x64", "win-arm64", "win-x86",
                "linux-x64", "linux-arm64", "linux-x86",
                "linux-musl-x64", "linux-musl-arm64",
                "osx-x64", "osx-arm64", "osx-x86",
            });
    }

    [Fact]
    public void The_operating_system_name_is_one_of_the_three_rid_prefixes()
    {
        Assert.Contains(
            NativeLibraryResolver.OperatingSystemName(),
            new[] { "win", "linux", "osx" });
    }
}
