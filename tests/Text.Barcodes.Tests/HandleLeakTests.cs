using System;
using M3scalinum.Text.Barcodes.Internal;
using Xunit;

namespace M3scalinum.Text.Barcodes.Tests;

/// <summary>
/// Holds the handle soaks apart from the rest of the suite: they count native handles, so another
/// class holding one would turn the assertion into noise.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HandleSoak
{
    public const string Name = "Handle soak";
}

/// <summary>
/// Every native object a call creates is owned by a handle the managed side disposes. The count the
/// native library keeps is exact, so a leak shows up as a monotonic rise rather than as a working
/// set that grew for allocator reasons.
/// </summary>
[Collection(HandleSoak.Name)]
public sealed class HandleLeakTests
{
    private const int Cycles = 5_000;

    /// <summary>
    /// A handle that was handed to the finalizer is released at some later point; this tolerance
    /// absorbs a pending one, and nothing else.
    /// </summary>
    private const int HandleTolerance = 4;

    [Fact]
    public void A_barcode_handle_is_counted_while_it_is_held()
    {
        // Barcode.Encode frees its handle before it returns — the image is a copy — so the count is
        // only observable at the interop layer. This is the guard for the soak below: a counter
        // stuck at zero would make it pass for nothing.
        var before = BarcodeNative.LiveBarcodeCount;

        using (var handle = BarcodeNative.Encode("QRCode", "GUARD", -1, -1, null))
        {
            Assert.Equal(before + 1, BarcodeNative.LiveBarcodeCount);
            Assert.True(handle.ModuleWidth > 0);
        }

        Assert.Equal(before, BarcodeNative.LiveBarcodeCount);
    }

    [Fact]
    public void A_decode_handle_is_counted_while_it_is_held()
    {
        var png = Barcode.Encode(BarcodeSymbology.QrCode, "GUARD").Png;
        var before = BarcodeNative.LiveDecodeCount;

        using (var decoded = BarcodeNative.Decode(png))
        {
            Assert.Equal(before + 1, BarcodeNative.LiveDecodeCount);
            Assert.True(BarcodeNative.Success(decoded));
        }

        Assert.Equal(before, BarcodeNative.LiveDecodeCount);
    }

    [Fact]
    public void Five_thousand_encode_and_decode_cycles_release_every_handle()
    {
        // Warm up: the first calls load the library, the engine and the encoders.
        for (var index = 0; index < 20; index++)
        {
            _ = Barcode.Decode(Barcode.Encode(BarcodeSymbology.QrCode, "WARM").Png);
        }

        var barcodesBefore = BarcodeNative.LiveBarcodeCount;
        var decodesBefore = BarcodeNative.LiveDecodeCount;

        for (var index = 0; index < Cycles; index++)
        {
            var image = Barcode.Encode(BarcodeSymbology.QrCode, "CYCLE");
            var decoded = Barcode.Decode(image.Png);

            Assert.True(decoded.Success);
        }

        Assert.InRange(
            Math.Abs(BarcodeNative.LiveBarcodeCount - barcodesBefore), 0, HandleTolerance);
        Assert.InRange(
            Math.Abs(BarcodeNative.LiveDecodeCount - decodesBefore), 0, HandleTolerance);
    }

    [Fact]
    public void A_failed_encode_releases_its_handles()
    {
        var before = BarcodeNative.LiveBarcodeCount;

        for (var index = 0; index < 500; index++)
        {
            Assert.Throws<BarcodeFormatException>(
                () => Barcode.Encode(BarcodeSymbology.Ean13, "123456789012"));
        }

        Assert.InRange(Math.Abs(BarcodeNative.LiveBarcodeCount - before), 0, HandleTolerance);
    }

    [Fact]
    public void A_blank_image_releases_its_decode_handle()
    {
        var png = BlankPng.Create(80, 40);
        var before = BarcodeNative.LiveDecodeCount;

        for (var index = 0; index < 500; index++)
        {
            Assert.False(Barcode.Decode(png).Success);
        }

        Assert.InRange(Math.Abs(BarcodeNative.LiveDecodeCount - before), 0, HandleTolerance);
    }

    [Fact]
    public void One_thousand_cycles_do_not_grow_managed_memory()
    {
        for (var index = 0; index < 20; index++)
        {
            _ = Barcode.Decode(Barcode.Encode(BarcodeSymbology.QrCode, "WARM").Png);
        }

        var before = ManagedBytes();

        for (var index = 0; index < 1_000; index++)
        {
            _ = Barcode.Decode(Barcode.Encode(BarcodeSymbology.QrCode, "MEMORY").Png);
        }

        var growth = ManagedBytes() - before;

        // Every buffer the native side hands over is copied and freed; anything above a few
        // megabytes over a thousand cycles means one of them is being retained.
        Assert.True(growth < 8 * 1024 * 1024, $"managed memory grew by {growth / 1024} KiB");
    }

    private static long ManagedBytes()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return GC.GetTotalMemory(forceFullCollection: true);
    }
}
