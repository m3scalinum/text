using System;
using System.Runtime.InteropServices;

namespace M3scalinum.Text.Pdf.Internal;

/// <summary>
/// Raw P/Invoke declarations. Nothing here is public: callers go through <see cref="PdfRaster"/>.
/// </summary>
internal static partial class PdfRasterNativeMethods
{
    internal const string LibraryName = "sharp_text_pdf";

    [LibraryImport(LibraryName, EntryPoint = "tp_abi_version")]
    internal static partial uint AbiVersion();

    [LibraryImport(LibraryName, EntryPoint = "tp_build_info")]
    internal static partial IntPtr BuildInfo();

    [LibraryImport(LibraryName, EntryPoint = "tp_error_free")]
    internal static partial void ErrorFree(IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "tp_error_code")]
    internal static partial int ErrorCode(IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "tp_error_json")]
    internal static partial NativeBuffer ErrorJson(IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "tp_buffer_free")]
    internal static partial void BufferFree(NativeBuffer buffer);

    [LibraryImport(LibraryName, EntryPoint = "tp_page_count")]
    internal static unsafe partial long PageCount(
        byte* data,
        nuint length,
        out IntPtr error);

    [LibraryImport(
        LibraryName,
        EntryPoint = "tp_page_render",
        StringMarshalling = StringMarshalling.Utf8)]
    internal static unsafe partial int PageRender(
        byte* data,
        nuint length,
        uint page,
        string? options,
        out NativeBuffer image,
        out NativeBuffer json,
        out IntPtr error);

    internal static string ReadUtf8(IntPtr pointer)
        => Marshal.PtrToStringUTF8(pointer) ?? string.Empty;
}

/// <summary>
/// A buffer Rust allocated and this side must hand back to <c>tp_buffer_free</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeBuffer
{
    public IntPtr Pointer;
    public nuint Length;

    internal byte[] ToArray()
    {
        if (Pointer == IntPtr.Zero || Length == 0)
        {
            return [];
        }

        var bytes = new byte[(int)Length];
        Marshal.Copy(Pointer, bytes, 0, bytes.Length);
        return bytes;
    }
}
