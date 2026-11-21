using System;
using System.Runtime.InteropServices;

namespace M3scalinum.Text.Typst.Native;

/// <summary>
/// Raw P/Invoke declarations. Nothing here is public: callers go through
/// <see cref="ITypstNative"/>.
/// </summary>
internal static partial class NativeMethods
{
    internal const string LibraryName = "sharp_typst";

    [LibraryImport(LibraryName, EntryPoint = "tn_abi_version")]
    internal static partial uint AbiVersion();

    [LibraryImport(LibraryName, EntryPoint = "tn_typst_version")]
    internal static partial IntPtr TypstVersion();

    [LibraryImport(LibraryName, EntryPoint = "tn_build_info")]
    internal static partial IntPtr BuildInfo();

    [LibraryImport(LibraryName, EntryPoint = "tn_error_free")]
    internal static partial void ErrorFree(IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "tn_error_code")]
    internal static partial int ErrorCode(IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "tn_error_json")]
    internal static partial NativeBuffer ErrorJson(IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "tn_error_traceback")]
    internal static partial NativeBuffer ErrorTraceback(IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "tn_buffer_free")]
    internal static partial void BufferFree(NativeBuffer buffer);

    [LibraryImport(LibraryName, EntryPoint = "tn_world_create", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr WorldCreate(string? configJson, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "tn_world_free")]
    internal static partial void WorldFree(IntPtr world);

    [LibraryImport(LibraryName, EntryPoint = "tn_world_add_source", StringMarshalling = StringMarshalling.Utf8)]
    internal static unsafe partial int WorldAddSource(
        IntPtr world, string path, byte* data, nuint length, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "tn_world_add_asset", StringMarshalling = StringMarshalling.Utf8)]
    internal static unsafe partial int WorldAddAsset(
        IntPtr world, string path, byte* data, nuint length, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "tn_world_add_font")]
    internal static unsafe partial int WorldAddFont(
        IntPtr world, byte* data, nuint length, uint index, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "tn_world_clear_sources")]
    internal static partial int WorldClearSources(IntPtr world, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "tn_world_font_count")]
    internal static partial uint WorldFontCount(IntPtr world);

    [LibraryImport(LibraryName, EntryPoint = "tn_world_source_count")]
    internal static partial uint WorldSourceCount(IntPtr world);

    [LibraryImport(LibraryName, EntryPoint = "tn_world_live_count")]
    internal static partial uint WorldLiveCount();

    [LibraryImport(LibraryName, EntryPoint = "tn_document_live_count")]
    internal static partial uint DocumentLiveCount();

    [LibraryImport(LibraryName, EntryPoint = "tn_compile", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr Compile(IntPtr world, string? optionsJson, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "tn_document_free")]
    internal static partial void DocumentFree(IntPtr document);

    [LibraryImport(LibraryName, EntryPoint = "tn_document_page_count")]
    internal static partial uint DocumentPageCount(IntPtr document);

    [LibraryImport(LibraryName, EntryPoint = "tn_document_diagnostics")]
    internal static partial int DocumentDiagnostics(IntPtr document, out NativeBuffer json, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "tn_document_export", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int DocumentExport(
        IntPtr document, string format, string? optionsJson, out NativeBuffer output, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "tn_document_export_reader", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int DocumentExportReader(
        IntPtr document, string format, string? optionsJson, out IntPtr reader, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "tn_export_reader_read")]
    internal static unsafe partial int ExportReaderRead(
        IntPtr reader, byte* buffer, nuint capacity, out nuint written, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "tn_export_reader_free")]
    internal static partial void ExportReaderFree(IntPtr reader);

    [LibraryImport(LibraryName, EntryPoint = "tn_document_bundle_asset")]
    internal static partial int DocumentBundleAsset(
        IntPtr document, uint index, out NativeBuffer output, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "tn_document_evaluate")]
    internal static partial int DocumentEvaluate(
        IntPtr document, out NativeBuffer output, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "tn_document_page_text")]
    internal static partial int DocumentPageText(
        IntPtr document, uint page, out NativeBuffer output, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "tn_world_font_families")]
    internal static partial int WorldFontFamilies(
        IntPtr world, out NativeBuffer output, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "tn_world_clone")]
    internal static partial int WorldClone(
        IntPtr world, out IntPtr clone, out IntPtr error);

    internal static string ReadUtf8(IntPtr pointer)
        => Marshal.PtrToStringUTF8(pointer) ?? string.Empty;
}