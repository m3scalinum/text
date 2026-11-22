using System;
using System.Runtime.InteropServices;

namespace M3scalinum.Text.Barcodes.Internal;

/// <summary>
/// Raw P/Invoke declarations. Nothing here is public: callers go through <see cref="BarcodeNative"/>.
/// </summary>
internal static partial class NativeMethods
{
    internal const string LibraryName = "sharp_text_barcodes";

    [LibraryImport(LibraryName, EntryPoint = "bt_abi_version")]
    internal static partial uint AbiVersion();

    [LibraryImport(LibraryName, EntryPoint = "bt_engine_version")]
    internal static partial IntPtr EngineVersion();

    [LibraryImport(LibraryName, EntryPoint = "bt_build_info")]
    internal static partial IntPtr BuildInfo();

    [LibraryImport(LibraryName, EntryPoint = "bt_error_free")]
    internal static partial void ErrorFree(IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "bt_error_code")]
    internal static partial int ErrorCode(IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "bt_error_json")]
    internal static partial NativeBuffer ErrorJson(IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "bt_error_traceback")]
    internal static partial NativeBuffer ErrorTraceback(IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "bt_buffer_free")]
    internal static partial void BufferFree(NativeBuffer buffer);

    [LibraryImport(LibraryName, EntryPoint = "bt_symbologies")]
    internal static partial NativeBuffer Symbologies();

    [LibraryImport(LibraryName, EntryPoint = "bt_symbology_capabilities")]
    internal static partial NativeBuffer SymbologyCapabilities();

    [LibraryImport(LibraryName, EntryPoint = "bt_gs1_parse", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int Gs1Parse(string raw, out NativeBuffer json, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "bt_gs1_digital_link", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int Gs1DigitalLink(
        string raw, string baseUrl, out NativeBuffer url, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "bt_gs1_from_digital_link", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int Gs1FromDigitalLink(string url, out NativeBuffer json, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "bt_gs1_identifiers")]
    internal static partial NativeBuffer Gs1Identifiers();

    [LibraryImport(LibraryName, EntryPoint = "bt_barcode_encode", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr BarcodeEncode(
        string symbology,
        string payload,
        int quietZone,
        int barHeight,
        string? errorCorrection,
        out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "bt_barcode_svg")]
    internal static partial int BarcodeSvg(IntPtr barcode, out NativeBuffer svg, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "bt_barcode_png")]
    internal static partial int BarcodePng(
        IntPtr barcode, uint scale, out NativeBuffer png, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "bt_barcode_svg_styled")]
    internal static partial int BarcodeSvgStyled(
        IntPtr barcode,
        int foreground,
        int background,
        double logoFraction,
        int logoFill,
        out NativeBuffer svg,
        out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "bt_barcode_png_styled")]
    internal static partial int BarcodePngStyled(
        IntPtr barcode,
        uint scale,
        int foreground,
        int background,
        double logoFraction,
        int logoFill,
        out NativeBuffer png,
        out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "bt_barcode_print_styled", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int BarcodePrintStyled(
        string symbology,
        string payload,
        int quietZone,
        int barHeight,
        string? errorCorrection,
        double dpi,
        double widthMillimetres,
        double moduleMillimetres,
        uint widthModules,
        int foreground,
        int background,
        double logoFraction,
        int logoFill,
        out NativeBuffer png,
        out NativeBuffer geometry,
        out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "bt_barcode_print", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int BarcodePrint(
        string symbology,
        string payload,
        int quietZone,
        int barHeight,
        string? errorCorrection,
        double dpi,
        double widthMillimetres,
        double moduleMillimetres,
        uint widthModules,
        out NativeBuffer png,
        out NativeBuffer geometry,
        out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "bt_barcode_width")]
    internal static partial uint BarcodeWidth(IntPtr barcode);

    [LibraryImport(LibraryName, EntryPoint = "bt_barcode_height")]
    internal static partial uint BarcodeHeight(IntPtr barcode);

    [LibraryImport(LibraryName, EntryPoint = "bt_barcode_free")]
    internal static partial void BarcodeFree(IntPtr barcode);

    [LibraryImport(LibraryName, EntryPoint = "bt_barcode_live_count")]
    internal static partial uint BarcodeLiveCount();

    [LibraryImport(LibraryName, EntryPoint = "bt_decode_png")]
    internal static unsafe partial IntPtr DecodePng(byte* data, nuint length, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "bt_decode_success")]
    internal static partial int DecodeSuccess(IntPtr decoded);

    [LibraryImport(LibraryName, EntryPoint = "bt_decode_text")]
    internal static partial int DecodeText(
        IntPtr decoded, out NativeBuffer text, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "bt_decode_symbology")]
    internal static partial int DecodeSymbology(
        IntPtr decoded, out NativeBuffer name, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "bt_decode_format")]
    internal static partial int DecodeFormat(
        IntPtr decoded, out NativeBuffer format, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "bt_decode_points")]
    internal static partial int DecodePoints(
        IntPtr decoded, out NativeBuffer points, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "bt_decode_error")]
    internal static partial int DecodeError(
        IntPtr decoded, out NativeBuffer message, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "bt_decode_free")]
    internal static partial void DecodeFree(IntPtr decoded);

    [LibraryImport(LibraryName, EntryPoint = "bt_decode_live_count")]
    internal static partial uint DecodeLiveCount();

    [LibraryImport(LibraryName, EntryPoint = "bt_decode_box")]
    internal static partial int DecodeBox(
        IntPtr decoded, out NativeBuffer box, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "bt_decode_image")]
    internal static unsafe partial IntPtr DecodeImage(
        byte* data, nuint length, ulong maximumPixels, nuint maximumBytes, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "bt_decode_page_count")]
    internal static partial uint DecodePageCount(IntPtr page);

    [LibraryImport(LibraryName, EntryPoint = "bt_decode_page_width")]
    internal static partial uint DecodePageWidth(IntPtr page);

    [LibraryImport(LibraryName, EntryPoint = "bt_decode_page_height")]
    internal static partial uint DecodePageHeight(IntPtr page);

    [LibraryImport(LibraryName, EntryPoint = "bt_decode_page_error")]
    internal static partial int DecodePageError(
        IntPtr page, out NativeBuffer message, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "bt_decode_page_symbol")]
    internal static partial IntPtr DecodePageSymbol(IntPtr page, uint index, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "bt_decode_page_free")]
    internal static partial void DecodePageFree(IntPtr page);

    [LibraryImport(LibraryName, EntryPoint = "bt_decode_page_live_count")]
    internal static partial uint DecodePageLiveCount();

    internal static string ReadUtf8(IntPtr pointer)
        => Marshal.PtrToStringUTF8(pointer) ?? string.Empty;
}
