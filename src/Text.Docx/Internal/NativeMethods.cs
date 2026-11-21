using System;
using System.Runtime.InteropServices;

namespace M3scalinum.Text.Docx.Internal;

/// <summary>Mirror of the native <c>TnBuffer</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeBuffer
{
    public IntPtr Pointer;

    public nuint Length;
}

/// <summary>
/// Raw P/Invoke declarations. Nothing here is public: callers go through <see cref="DocxNative"/>.
/// </summary>
internal static partial class NativeMethods
{
    internal const string LibraryName = "sharp_text_docx";

    [LibraryImport(LibraryName, EntryPoint = "td_abi_version")]
    internal static partial uint AbiVersion();

    [LibraryImport(LibraryName, EntryPoint = "td_buffer_free")]
    internal static partial void BufferFree(NativeBuffer buffer);

    [LibraryImport(LibraryName, EntryPoint = "td_error_free")]
    internal static partial void ErrorFree(IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "td_error_code")]
    internal static partial int ErrorCode(IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "td_error_json")]
    internal static partial NativeBuffer ErrorJson(IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "td_document_parse", StringMarshalling = StringMarshalling.Utf8)]
    internal static unsafe partial IntPtr DocumentParse(
        byte* data, nuint length, string? optionsJson, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "td_document_ast_json")]
    internal static partial NativeBuffer DocumentAstJson(IntPtr document, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "td_document_image")]
    internal static partial int DocumentImage(
        IntPtr document, ulong handle, out NativeBuffer buffer, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "td_document_image_count")]
    internal static partial uint DocumentImageCount(IntPtr document);

    [LibraryImport(LibraryName, EntryPoint = "td_document_free")]
    internal static partial void DocumentFree(IntPtr document);

    [LibraryImport(LibraryName, EntryPoint = "td_document_live_count")]
    internal static partial uint DocumentLiveCount();
}
