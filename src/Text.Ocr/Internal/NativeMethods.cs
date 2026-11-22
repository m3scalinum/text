using System;
using System.Runtime.InteropServices;

namespace M3scalinum.Text.Ocr.Internal;

/// <summary>
/// Raw P/Invoke declarations. Nothing here is public: callers go through
/// <see cref="NativeOcr"/>.
/// </summary>
internal static partial class NativeMethods
{
    internal const string LibraryName = "sharp_text_ocr";

    [LibraryImport(LibraryName, EntryPoint = "to_abi_version")]
    internal static partial uint AbiVersion();

    [LibraryImport(LibraryName, EntryPoint = "to_build_info")]
    internal static partial IntPtr BuildInfo();

    [LibraryImport(LibraryName, EntryPoint = "to_error_free")]
    internal static partial void ErrorFree(IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "to_error_code")]
    internal static partial int ErrorCode(IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "to_error_json")]
    internal static partial NativeBuffer ErrorJson(IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "to_buffer_free")]
    internal static partial void BufferFree(NativeBuffer buffer);

    [LibraryImport(LibraryName, EntryPoint = "to_engine_create")]
    internal static unsafe partial IntPtr EngineCreate(
        byte* detectionModel, nuint detectionLength,
        byte* recognitionModel, nuint recognitionLength,
        byte* dictionary, nuint dictionaryLength,
        out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "to_engine_free")]
    internal static partial void EngineFree(IntPtr engine);

    [LibraryImport(LibraryName, EntryPoint = "to_engine_live_count")]
    internal static partial uint EngineLiveCount();

    [LibraryImport(LibraryName, EntryPoint = "to_engine_recognize")]
    internal static unsafe partial int EngineRecognize(
        IntPtr engine, byte* image, nuint length,
        double pageConfidence, double wordConfidence, double maxRejectedWordRatio,
        out NativeBuffer json, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "to_engine_recognize_pdf")]
    internal static unsafe partial int EngineRecognizePdf(
        IntPtr engine, byte* pdf, nuint length, uint dpi, uint firstPage, uint pageCount,
        double pageConfidence, double wordConfidence, double maxRejectedWordRatio,
        out NativeBuffer json, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "to_engine_recognize_preprocessed")]
    internal static unsafe partial int EngineRecognizePreprocessed(
        IntPtr engine, byte* image, nuint length,
        double pageConfidence, double wordConfidence, double maxRejectedWordRatio,
        int detectRotation, int deskew, int denoise,
        out NativeBuffer json, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "to_engine_recognize_pdf_preprocessed")]
    internal static unsafe partial int EngineRecognizePdfPreprocessed(
        IntPtr engine, byte* pdf, nuint length, uint dpi, uint firstPage, uint pageCount,
        double pageConfidence, double wordConfidence, double maxRejectedWordRatio,
        int detectRotation, int deskew, int denoise,
        out NativeBuffer json, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "to_model_inspect")]
    internal static unsafe partial int ModelInspect(
        byte* model, nuint length, int kind, out NativeBuffer json, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "to_model_set_inspect")]
    internal static unsafe partial int ModelSetInspect(
        byte* directory, nuint length, out NativeBuffer json, out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "to_engine_create_from_directory")]
    internal static unsafe partial IntPtr EngineCreateFromDirectory(
        byte* directory, nuint length,
        byte* dictionary, nuint dictionaryLength,
        out IntPtr error);

    [LibraryImport(LibraryName, EntryPoint = "to_engine_models")]
    internal static partial int EngineModels(
        IntPtr engine, out NativeBuffer json, out IntPtr error);

    internal static string ReadUtf8(IntPtr pointer)
        => Marshal.PtrToStringUTF8(pointer) ?? string.Empty;
}

/// <summary>
/// A buffer Rust allocated and this side must hand back to <c>to_buffer_free</c>.
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
