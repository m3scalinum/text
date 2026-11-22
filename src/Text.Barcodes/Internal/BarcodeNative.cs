using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace M3scalinum.Text.Barcodes.Internal;

/// <summary>Default interop layer: thin, allocation-conscious marshalling.</summary>
internal sealed class BarcodeNative
{
    private static readonly Lazy<BarcodeNative> LazyInstance = new(() => new BarcodeNative());

    private readonly string _engineVersion;

    static BarcodeNative()
    {
        NativeLibraryResolver.Initialize();
    }

    private BarcodeNative()
    {
        var abi = NativeMethods.AbiVersion();
        if (abi != 1)
        {
            throw new BarcodeNativeException(
                $"the native barcode library reports ABI version {abi}, this build requires 1");
        }

        _engineVersion = NativeMethods.ReadUtf8(NativeMethods.EngineVersion());
    }

    public static BarcodeNative Instance => LazyInstance.Value;

    /// <summary>The engine and its version, for example <c>rxing 0.9.4</c>.</summary>
    public string EngineVersion => _engineVersion;

    /// <summary>Barcode handles the native library holds right now.</summary>
    /// <remarks>
    /// Diagnostic: a host that encodes and disposes can assert that the count returns to what it was.
    /// The process working set cannot answer that question — it is process-wide, and each platform
    /// retains freed native blocks differently.
    /// </remarks>
    public static int LiveBarcodeCount => (int)NativeMethods.BarcodeLiveCount();

    /// <summary>Decode handles the native library holds right now. See <see cref="LiveBarcodeCount"/>.</summary>
    public static int LiveDecodeCount => (int)NativeMethods.DecodeLiveCount();

    /// <summary>Decode-page handles the native library holds right now. See <see cref="LiveBarcodeCount"/>.</summary>
    public static int LiveDecodePageCount => (int)NativeMethods.DecodePageLiveCount();

    public static BarcodeHandle Encode(
        string symbology,
        string payload,
        int quietZone,
        int barHeight,
        string? errorCorrection)
    {
        var handle = NativeMethods.BarcodeEncode(
            symbology, payload, quietZone, barHeight, errorCorrection, out var error);

        if (handle == IntPtr.Zero)
        {
            NativeCallException.ThrowFromError(error, $"encode '{symbology}'");
        }

        return new BarcodeHandle(handle);
    }

    public static string Svg(BarcodeHandle barcode)
        => ReadString(barcode, "read the SVG", NativeMethods.BarcodeSvg);

    public static byte[] Png(BarcodeHandle barcode, int scale)
        => ReadBytes(barcode, scale, "read the PNG", NativeMethods.BarcodePng);

    /// <summary>The style scalars the styled entry points take, already packed for the ABI.</summary>
    /// <param name="Foreground">The module colour as <c>0xRRGGBB</c>, or a negative value for black.</param>
    /// <param name="Background">The background as <c>0xRRGGBB</c>, or a negative value for white.</param>
    /// <param name="LogoFraction">The share the reserved centre covers, or zero for none.</param>
    /// <param name="LogoFill">The fill as <c>0xRRGGBB</c>, or a negative value for the background.</param>
    public readonly record struct StyleArguments(
        int Foreground, int Background, double LogoFraction, int LogoFill)
    {
        /// <summary>Reads the options into the four scalars, taking the defaults where none is set.</summary>
        /// <param name="foreground">The module colour, or null.</param>
        /// <param name="background">The background colour, or null.</param>
        /// <param name="logo">The reserved centre, or null.</param>
        /// <returns>The four scalars.</returns>
        public static StyleArguments From(
            BarcodeColor? foreground, BarcodeColor? background, BarcodeLogo? logo)
            => new(
                foreground?.ToPacked() ?? -1,
                background?.ToPacked() ?? -1,
                logo?.Fraction ?? 0d,
                logo?.Fill?.ToPacked() ?? -1);
    }

    /// <summary>The styled SVG rendering of a symbol.</summary>
    public static string SvgStyled(BarcodeHandle barcode, StyleArguments style)
        => ReadStyledString(barcode, "read the styled SVG", style, NativeMethods.BarcodeSvgStyled);

    /// <summary>The styled raster rendering of a symbol.</summary>
    public static byte[] PngStyled(BarcodeHandle barcode, int scale, StyleArguments style)
    {
        var added = false;
        try
        {
            barcode.DangerousAddRef(ref added);
            var status = NativeMethods.BarcodePngStyled(
                barcode.DangerousGetHandle(),
                (uint)scale,
                style.Foreground,
                style.Background,
                style.LogoFraction,
                style.LogoFill,
                out var png,
                out var error);
            NativeCallException.Throw(status, error, "read the styled PNG");
            return NativeCallException.ReadAndFree(png);
        }
        finally
        {
            if (added)
            {
                barcode.DangerousRelease();
            }
        }
    }

    /// <summary>Encodes for paper and returns the raster with the geometry the native side computed.</summary>
    /// <param name="symbology">The ABI name of the symbology.</param>
    /// <param name="payload">The text to encode.</param>
    /// <param name="quietZone">Modules on each side, or a negative value for the normative minimum.</param>
    /// <param name="barHeight">Bar height in modules, or a negative value for the default.</param>
    /// <param name="errorCorrection">The error correction level, or null.</param>
    /// <param name="dpi">The output resolution, in dots per inch.</param>
    /// <param name="widthMillimetres">The whole-symbol width, or zero when the module size sizes it.</param>
    /// <param name="moduleMillimetres">The X-dimension, or zero when the width sizes it.</param>
    /// <param name="widthModules">The reserved width in modules, or zero to leave it unchecked.</param>
    /// <param name="style">The colours and the reserved centre, or null for the plain rendering.</param>
    /// <returns>The PNG and the geometry JSON.</returns>
    public static (byte[] Png, string Geometry) Print(
        string symbology,
        string payload,
        int quietZone,
        int barHeight,
        string? errorCorrection,
        int dpi,
        double widthMillimetres,
        double moduleMillimetres,
        int widthModules,
        BarcodeNative.StyleArguments? style = null)
    {
        int status;
        NativeBuffer png;
        NativeBuffer geometry;
        IntPtr error;

        if (style is { } arguments)
        {
            status = NativeMethods.BarcodePrintStyled(
                symbology,
                payload,
                quietZone,
                barHeight,
                errorCorrection,
                dpi,
                widthMillimetres,
                moduleMillimetres,
                (uint)widthModules,
                arguments.Foreground,
                arguments.Background,
                arguments.LogoFraction,
                arguments.LogoFill,
                out png,
                out geometry,
                out error);
        }
        else
        {
            status = NativeMethods.BarcodePrint(
                symbology,
                payload,
                quietZone,
                barHeight,
                errorCorrection,
                dpi,
                widthMillimetres,
                moduleMillimetres,
                (uint)widthModules,
                out png,
                out geometry,
                out error);
        }

        NativeCallException.Throw(status, error, $"print '{symbology}'");

        return (NativeCallException.ReadAndFree(png), NativeCallException.ReadAndFreeString(geometry));
    }

    public static DecodeHandle Decode(ReadOnlySpan<byte> png)
    {
        unsafe
        {
            fixed (byte* data = png)
            {
                var handle = NativeMethods.DecodePng(data, (nuint)png.Length, out var error);
                if (handle == IntPtr.Zero)
                {
                    NativeCallException.ThrowFromError(error, "decode the image");
                }

                return new DecodeHandle(handle);
            }
        }
    }

    /// <summary>Decodes every barcode in an image, under the caller's two ceilings.</summary>
    /// <param name="image">The encoded image. PNG is the only encoding the engine reads today.</param>
    /// <param name="maximumPixels">The largest number of pixels the image may hold.</param>
    /// <param name="maximumBytes">The largest number of bytes the image may occupy.</param>
    /// <returns>The page handle. A readable image without a symbol is a page with no symbols.</returns>
    public static DecodePageHandle DecodeImage(
        ReadOnlySpan<byte> image, long maximumPixels, long maximumBytes)
    {
        unsafe
        {
            fixed (byte* data = image)
            {
                var handle = NativeMethods.DecodeImage(
                    data,
                    (nuint)image.Length,
                    (ulong)maximumPixels,
                    (nuint)maximumBytes,
                    out var error);

                if (handle == IntPtr.Zero)
                {
                    NativeCallException.ThrowFromError(error, "decode the image");
                }

                return new DecodePageHandle(handle);
            }
        }
    }

    /// <summary>Why the page holds no symbol, empty when it holds at least one.</summary>
    public static string PageFailure(DecodePageHandle page)
        => ReadString(page, "read the decode failure", NativeMethods.DecodePageError);

    /// <summary>Takes one symbol out of the page as a handle of its own.</summary>
    public static DecodeHandle PageSymbol(DecodePageHandle page, int index)
    {
        var added = false;
        try
        {
            page.DangerousAddRef(ref added);
            var handle = NativeMethods.DecodePageSymbol(
                page.DangerousGetHandle(), (uint)index, out var error);

            if (handle == IntPtr.Zero)
            {
                NativeCallException.ThrowFromError(error, $"read symbol {index} of the page");
            }

            return new DecodeHandle(handle);
        }
        finally
        {
            if (added)
            {
                page.DangerousRelease();
            }
        }
    }

    /// <summary>The box around the detected points, or null when the detector reported none.</summary>
    public static BarcodeBounds? Bounds(DecodeHandle decoded)
    {
        var json = ReadString(decoded, "read the decode bounds", NativeMethods.DecodeBox);
        if (json.Length == 0)
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        var box = document.RootElement;
        return new BarcodeBounds(
            box.GetProperty("x").GetSingle(),
            box.GetProperty("y").GetSingle(),
            box.GetProperty("width").GetSingle(),
            box.GetProperty("height").GetSingle());
    }

    public static bool Success(DecodeHandle decoded)
        => NativeMethods.DecodeSuccess(decoded.DangerousGetHandle()) != 0;

    public static string Text(DecodeHandle decoded)
        => ReadString(decoded, "read the decoded text", NativeMethods.DecodeText);

    public static string Symbology(DecodeHandle decoded)
        => ReadString(decoded, "read the decoded symbology", NativeMethods.DecodeSymbology);

    public static string Format(DecodeHandle decoded)
        => ReadString(decoded, "read the decoded format", NativeMethods.DecodeFormat);

    public static string Failure(DecodeHandle decoded)
        => ReadString(decoded, "read the decode failure", NativeMethods.DecodeError);

    public static IReadOnlyList<(float X, float Y)> Points(DecodeHandle decoded)
    {
        var json = ReadString(decoded, "read the decode points", NativeMethods.DecodePoints);
        if (json.Length == 0)
        {
            return [];
        }

        var points = new List<(float X, float Y)>();
        using var document = JsonDocument.Parse(json);
        foreach (var element in document.RootElement.EnumerateArray())
        {
            points.Add((
                element.GetProperty("x").GetSingle(),
                element.GetProperty("y").GetSingle()));
        }

        return points;
    }

    /// <summary>The symbologies the native library reports, in its own order.</summary>
    public static IReadOnlyList<string> Symbologies()
    {
        var json = Encoding.UTF8.GetString(NativeCallException.ReadAndFree(NativeMethods.Symbologies()));
        if (json.Length == 0)
        {
            return [];
        }

        var names = new List<string>();
        using var document = JsonDocument.Parse(json);
        foreach (var element in document.RootElement.EnumerateArray())
        {
            names.Add(element.GetString() ?? string.Empty);
        }

        return names;
    }

    /// <summary>The symbologies the native library reports, with whether it can write each one.</summary>
    public static IReadOnlyList<SymbologyCapability> SymbologyCapabilities()
    {
        var json = Encoding.UTF8.GetString(
            NativeCallException.ReadAndFree(NativeMethods.SymbologyCapabilities()));
        if (json.Length == 0)
        {
            return [];
        }

        var capabilities = new List<SymbologyCapability>();
        using var document = JsonDocument.Parse(json);
        foreach (var element in document.RootElement.EnumerateArray())
        {
            capabilities.Add(new SymbologyCapability(
                element.GetProperty("name").GetString() ?? string.Empty,
                element.GetProperty("encode").GetBoolean(),
                element.GetProperty("quietZone").GetInt32(),
                element.GetProperty("minimumModuleMm").GetDouble()));
        }

        return capabilities;
    }

    /// <summary>Parses a GS1 element string, as the JSON the managed types read.</summary>
    public static string ParseGs1(string raw)
    {
        var status = NativeMethods.Gs1Parse(raw, out var json, out var error);
        NativeCallException.Throw(status, error, "parse the GS1 data");
        return NativeCallException.ReadAndFreeString(json);
    }

    /// <summary>Builds a Digital Link URL from a raw GS1 element string.</summary>
    public static string DigitalLink(string raw, string baseUrl)
    {
        var status = NativeMethods.Gs1DigitalLink(raw, baseUrl, out var url, out var error);
        NativeCallException.Throw(status, error, "build the Digital Link");
        return NativeCallException.ReadAndFreeString(url);
    }

    /// <summary>Parses a Digital Link URL, as the JSON the managed types read.</summary>
    public static string FromDigitalLink(string url)
    {
        var status = NativeMethods.Gs1FromDigitalLink(url, out var json, out var error);
        NativeCallException.Throw(status, error, "parse the Digital Link");
        return NativeCallException.ReadAndFreeString(json);
    }

    /// <summary>The Application Identifiers the native library understands, as JSON.</summary>
    public static string Gs1Identifiers()
        => Encoding.UTF8.GetString(NativeCallException.ReadAndFree(NativeMethods.Gs1Identifiers()));

    private delegate int BufferReader(IntPtr handle, out NativeBuffer buffer, out IntPtr error);

    private delegate int ScaledBufferReader(
        IntPtr handle, uint scale, out NativeBuffer buffer, out IntPtr error);

    private delegate int StyledBufferReader(
        IntPtr handle,
        int foreground,
        int background,
        double logoFraction,
        int logoFill,
        out NativeBuffer buffer,
        out IntPtr error);

    /// <summary>Holds the handle for one styled read, as <see cref="ReadString"/> does.</summary>
    private static string ReadStyledString(
        SafeHandle handle, string operation, StyleArguments style, StyledBufferReader read)
    {
        var added = false;
        try
        {
            handle.DangerousAddRef(ref added);
            var status = read(
                handle.DangerousGetHandle(),
                style.Foreground,
                style.Background,
                style.LogoFraction,
                style.LogoFill,
                out var buffer,
                out var error);
            NativeCallException.Throw(status, error, operation);
            return NativeCallException.ReadAndFreeString(buffer);
        }
        finally
        {
            if (added)
            {
                handle.DangerousRelease();
            }
        }
    }

    /// <summary>
    /// Holds the handle for the duration of one table read, so a finalizer cannot free the native
    /// object under us between two calls.
    /// </summary>
    private static string ReadString(SafeHandle handle, string operation, BufferReader read)
    {
        var added = false;
        try
        {
            handle.DangerousAddRef(ref added);
            var status = read(handle.DangerousGetHandle(), out var buffer, out var error);
            NativeCallException.Throw(status, error, operation);
            return NativeCallException.ReadAndFreeString(buffer);
        }
        finally
        {
            if (added)
            {
                handle.DangerousRelease();
            }
        }
    }

    private static byte[] ReadBytes(
        SafeHandle handle, int scale, string operation, ScaledBufferReader read)
    {
        var added = false;
        try
        {
            handle.DangerousAddRef(ref added);
            var status = read(handle.DangerousGetHandle(), (uint)scale, out var buffer, out var error);
            NativeCallException.Throw(status, error, operation);
            return NativeCallException.ReadAndFree(buffer);
        }
        finally
        {
            if (added)
            {
                handle.DangerousRelease();
            }
        }
    }
}
