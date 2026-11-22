using System;
using System.Runtime.InteropServices;

namespace M3scalinum.Text.Xlsx.Internal;

/// <summary>Mirror of the native <c>TxBuffer</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeBuffer
{
    public IntPtr Pointer;

    public nuint Length;
}

/// <summary>
/// Raw P/Invoke declarations. Nothing here is public: callers go through <see cref="XlsxNative"/>.
/// </summary>
internal static partial class NativeMethods
{
    internal const string LibraryName = "sharp_text_xlsx";

    [LibraryImport(LibraryName, EntryPoint = "tx_abi_version")]
    internal static partial uint AbiVersion();

    [LibraryImport(LibraryName, EntryPoint = "tx_buffer_free")]
    internal static partial void BufferFree(NativeBuffer buffer);

    [LibraryImport(LibraryName, EntryPoint = "tx_error_free")]
    internal static partial void ErrorFree(IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "tx_error_code")]
    internal static partial int ErrorCode(IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "tx_error_json")]
    internal static partial NativeBuffer ErrorJson(IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "tx_workbook_parse", StringMarshalling = StringMarshalling.Utf8)]
    internal static unsafe partial IntPtr WorkbookParse(
        byte* data, nuint length, string? optionsJson, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "tx_workbook_ast_json")]
    internal static partial NativeBuffer WorkbookAstJson(IntPtr workbook, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "tx_workbook_free")]
    internal static partial void WorkbookFree(IntPtr workbook);

    [LibraryImport(LibraryName, EntryPoint = "tx_workbook_live_count")]
    internal static partial uint WorkbookLiveCount();
}
