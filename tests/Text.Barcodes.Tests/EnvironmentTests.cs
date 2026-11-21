using System;
using Xunit;

namespace M3scalinum.Text.Barcodes.Tests;

/// <summary>The handshake with the native library, and the promise that the two halves agree.</summary>
public sealed class EnvironmentTests
{
    [Fact]
    public void The_engine_reports_itself()
    {
        Assert.StartsWith("rxing 0.9", BarcodeEnvironment.EngineVersion, StringComparison.Ordinal);
    }

    [Fact]
    public void The_assembly_reports_a_version()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+", BarcodeEnvironment.BarcodesVersion);
    }

    [Fact]
    public void The_native_library_lists_every_symbology_the_enum_declares()
    {
        // The enum is the promise; the native list is the fact. A build whose native half is older
        // reports a short list here instead of failing at some caller's first EAN-13.
        var declared = Enum.GetValues<BarcodeSymbology>();
        var supported = Barcode.SupportedSymbologies;

        Assert.Equal(declared.Length, supported.Count);
        foreach (var symbology in declared)
        {
            Assert.Contains(symbology, supported);
        }
    }

    [Fact]
    public void The_ten_symbologies_are_the_ten_this_package_documents()
    {
        Assert.Equal(
            [
                BarcodeSymbology.QrCode,
                BarcodeSymbology.DataMatrix,
                BarcodeSymbology.Aztec,
                BarcodeSymbology.Pdf417,
                BarcodeSymbology.Code128,
                BarcodeSymbology.Code39,
                BarcodeSymbology.Ean13,
                BarcodeSymbology.Ean8,
                BarcodeSymbology.Itf,
                BarcodeSymbology.Codabar,
            ],
            Barcode.SupportedSymbologies);
    }

    [Fact]
    public void The_abi_names_round_trip_through_the_parser()
    {
        foreach (var symbology in Enum.GetValues<BarcodeSymbology>())
        {
            var name = BarcodeSymbologyNames.ToAbiName(symbology);

            Assert.Equal(symbology, BarcodeSymbologyNames.FromAbiName(name));
            Assert.True(
                BarcodeSymbologyNames.FromAbiName(name.ToUpperInvariant()) == symbology,
                "the native parser is case-insensitive, and so is this one");
        }

        Assert.Null(BarcodeSymbologyNames.FromAbiName("UPC-A"));
        Assert.Null(BarcodeSymbologyNames.FromAbiName(string.Empty));
    }

    [Fact]
    public void The_resolver_probes_the_package_layout_before_the_application_directory()
    {
        // The order is the contract with NuGet's runtimes/<rid>/native/ layout; pinning it here
        // means a future edit to the resolver has to say why.
        var candidates = Internal.NativeLibraryResolver.Candidates(
            "linux-musl-x64", "linux-x64", "linux", "/app");

        // The file name follows the host (this test runs on every platform), so it is taken from
        // the resolver rather than spelled out: what is pinned is the order of the four
        // directories, which is the contract with NuGet's runtimes/<rid>/native/ layout.
        var fileName = Internal.NativeLibraryResolver.FileName();

        Assert.Equal(4, candidates.Length);
        Assert.Equal($"/app/runtimes/linux-musl-x64/native/{fileName}", candidates[0].Replace('\\', '/'));
        Assert.Equal($"/app/runtimes/linux-x64/native/{fileName}", candidates[1].Replace('\\', '/'));
        Assert.Equal($"/app/runtimes/linux/native/{fileName}", candidates[2].Replace('\\', '/'));
        Assert.Equal($"/app/{fileName}", candidates[3].Replace('\\', '/'));
    }

    [Fact]
    public void The_override_variable_has_the_name_the_documentation_gives()
    {
        // The tests themselves rely on it: without the variable they would load whatever native
        // library the output directory happens to hold.
        var name = typeof(Internal.NativeLibraryResolver)
            .GetField("OverrideVariable", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?.GetRawConstantValue() as string;

        Assert.Equal("BARCODES_NATIVE_PATH", name);
    }
}
