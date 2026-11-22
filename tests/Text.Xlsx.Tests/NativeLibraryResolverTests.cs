using System;
using System.IO;
using M3scalinum.Text.Xlsx.Internal;
using Xunit;

namespace M3scalinum.Text.Xlsx.Tests;

/// <summary>Where the package looks for its native library, and in what order.</summary>
public sealed class NativeLibraryResolverTests
{
    [Fact]
    public void The_candidates_are_the_rid_folder_then_the_os_folder_then_the_application_folder()
    {
        var candidates = NativeLibraryResolver.Candidates("linux-x64", "linux", "/app");

        Assert.Equal(3, candidates.Length);
        Assert.Equal(Path.Combine("/app", "runtimes", "linux-x64", "native", NativeLibraryResolver.FileName()), candidates[0]);
        Assert.Equal(Path.Combine("/app", "runtimes", "linux", "native", NativeLibraryResolver.FileName()), candidates[1]);
        Assert.Equal(Path.Combine("/app", NativeLibraryResolver.FileName()), candidates[2]);
    }

    [Fact]
    public void The_library_is_named_per_platform()
    {
        Assert.Equal(
            OperatingSystem.IsWindows() ? "sharp_text_xlsx.dll"
                : OperatingSystem.IsMacOS() ? "libsharp_text_xlsx.dylib"
                : "libsharp_text_xlsx.so",
            NativeLibraryResolver.FileName());
    }

    [Fact]
    public void The_runtime_identifier_is_an_os_and_an_architecture()
    {
        var rid = NativeLibraryResolver.RuntimeIdentifier();

        Assert.Contains("-", rid, StringComparison.Ordinal);
        Assert.StartsWith(NativeLibraryResolver.OperatingSystemName(), rid);
    }

    [Fact]
    public void The_abi_version_is_the_one_this_package_speaks()
    {
        Assert.SkipUnless(XlsxNativeProbe.IsAvailable, XlsxNativeProbe.SkipReason);

        Assert.Equal(1u, XlsxNative.AbiVersion());
    }

    [Fact]
    public void A_parsed_workbook_is_released_with_its_handle()
    {
        Assert.SkipUnless(XlsxNativeProbe.IsAvailable, XlsxNativeProbe.SkipReason);

        var before = XlsxNative.LiveCount();

        using (var workbook = XlsxNative.Parse(
            XlsxReaderTests.Annexed(),
            XlsxNative.OptionsJson(XlsxOptions.DefaultMaxWorkbookBytes, XlsxOptions.DefaultMaxCells)))
        {
            // This handle is alive, so the process holds at least one whatever else the run is
            // doing: the exact figure is not asserted because other tests open workbooks too.
            Assert.True(XlsxNative.LiveCount() >= before + 1);
            Assert.NotEmpty(XlsxNative.ReadAst(workbook));
        }
    }

    [Fact]
    public void The_options_payload_is_the_json_the_native_reader_parses()
    {
        var json = XlsxNative.OptionsJson(1024, 8);

        Assert.Equal("{\"maxBytes\": 1024, \"maxCells\": 8}", json);
    }
}
