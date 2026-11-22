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
    public static IReadOnlyList<(string Name, bool CanEncode)> SymbologyCapabilities()
    {
        var json = Encoding.UTF8.GetString(
            NativeCallException.ReadAndFree(NativeMethods.SymbologyCapabilities()));
        if (json.Length == 0)
        {
            return [];
        }

        var capabilities = new List<(string Name, bool CanEncode)>();
        using var document = JsonDocument.Parse(json);
        foreach (var element in document.RootElement.EnumerateArray())
        {
            capabilities.Add((
                element.GetProperty("name").GetString() ?? string.Empty,
                element.GetProperty("encode").GetBoolean()));
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
