using System;
using System.IO;
using System.Runtime.InteropServices;
using M3scalinum.Text.Docx.Internal;
using Xunit;

namespace M3scalinum.Text.Docx.Tests;

/// <summary>
/// The probing order the resolver uses when the override variable is not set.
/// </summary>
/// <remarks>
/// The reader never sees these paths on a machine where <c>SHARPTEXTDOCX_NATIVE_PATH</c> is set, and
/// the RID folders a package ships are the one thing a test cannot observe from a development
/// checkout. <see cref="NativeLibraryResolver.Candidates(string, string, string)"/> takes every input
/// explicitly for that reason: the order is pinned here rather than inferred from the host.
/// </remarks>
public sealed class NativeLibraryResolverTests
{
    [Fact]
    public void The_probe_order_is_rid_then_os_then_beside_the_assembly()
    {
        var root = Path.Combine("somewhere", "app");
        var file = NativeLibraryResolver.FileName();

        var candidates = NativeLibraryResolver.Candidates("win-x64", "win", root);

        Assert.Equal(3, candidates.Length);
        Assert.Equal(Path.Combine(root, "runtimes", "win-x64", "native", file), candidates[0]);
        Assert.Equal(Path.Combine(root, "runtimes", "win", "native", file), candidates[1]);
        Assert.Equal(Path.Combine(root, file), candidates[2]);
    }

    [Fact]
    public void A_universal_macos_library_is_probed_under_osx_as_well_as_the_rid()
    {
        var candidates = NativeLibraryResolver.Candidates("osx-arm64", "osx", "app");

        Assert.Contains(Path.Combine("app", "runtimes", "osx-arm64", "native"), candidates[0], StringComparison.Ordinal);
        Assert.Contains(Path.Combine("app", "runtimes", "osx", "native"), candidates[1], StringComparison.Ordinal);
    }

    [Fact]
    public void The_host_probe_ends_beside_the_assembly()
    {
        var file = NativeLibraryResolver.FileName();
        var candidates = NativeLibraryResolver.Candidates();

        Assert.Equal(3, candidates.Length);
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, file), candidates[2]);

        // The first two are under the package layout, whichever host this runs on.
        Assert.StartsWith(
            Path.Combine(AppContext.BaseDirectory, "runtimes"),
            candidates[0],
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_operating_system_name_is_one_of_the_three_the_package_ships()
    {
        var name = NativeLibraryResolver.OperatingSystemName();

        Assert.Contains(name, new[] { "win", "osx", "linux" });

        // The same predicate the file-name and RID helpers use, so the three cannot disagree.
        var expected = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        Assert.Equal(expected, name);
    }

    [Fact]
    public void The_file_name_is_the_one_that_host_loads()
    {
        var name = NativeLibraryResolver.FileName();

        var expected = OperatingSystem.IsWindows()
            ? "sharp_text_docx.dll"
            : OperatingSystem.IsMacOS() ? "libsharp_text_docx.dylib" : "libsharp_text_docx.so";

        Assert.Equal(expected, name);
    }

    [Fact]
    public void The_runtime_identifier_is_the_os_and_the_process_architecture()
    {
        var rid = NativeLibraryResolver.RuntimeIdentifier();

        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => "unknown",
        };

        Assert.Equal($"{NativeLibraryResolver.OperatingSystemName()}-{architecture}", rid);
    }
}
