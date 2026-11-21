using System;

namespace M3scalinum.Text.Ocr.Internal;

/// <summary>
/// The native calls <see cref="OcrEngineHandle"/> needs, as an interface.
/// </summary>
/// <remarks>
/// <para>
/// The production implementation is <see cref="NativeLibraryOcr"/>, which forwards to
/// the <c>LibraryImport</c> declarations in <see cref="NativeMethods"/>. It is the only
/// implementation <see cref="OcrEngine.FromModels"/> ever reaches.
/// </para>
/// <para>
/// The tests substitute a deterministic implementation, because an engine needs two
/// models that weigh tens of megabytes and this repository ships none. That substitution
/// is what lets the handshake, the status-to-exception translation, the out-buffer
/// lifetime and the dispose path be exercised without a model, instead of being
/// asserted only on a machine that holds one.
/// </para>
/// </remarks>
internal interface IOcrNative
{
    /// <summary>The ABI version the library reports.</summary>
    uint AbiVersion();

    /// <summary>A pointer to the NUL-terminated build-information JSON.</summary>
    IntPtr BuildInfo();

    /// <summary>Creates an engine handle, or returns <see cref="IntPtr.Zero"/> and an error.</summary>
    IntPtr EngineCreate(
        byte[] detectionModel,
        byte[] recognitionModel,
        byte[]? dictionary,
        out IntPtr error);

    /// <summary>Drops an engine handle. A null handle is a no-op.</summary>
    void EngineFree(IntPtr engine);

    /// <summary>The number of engine handles alive in the library.</summary>
    uint EngineLiveCount();

    /// <summary>Recognises an encoded image; the result document is written to <paramref name="json"/>.</summary>
    int EngineRecognize(IntPtr engine, byte[] image, out NativeBuffer json, out IntPtr error);

    /// <summary>Renders and recognises pages of a PDF; the result document is written to <paramref name="json"/>.</summary>
    int EngineRecognizePdf(
        IntPtr engine,
        byte[] pdf,
        uint dpi,
        uint firstPage,
        uint pageCount,
        out NativeBuffer json,
        out IntPtr error);

    /// <summary>The <c>{"code","message"}</c> document of an error object.</summary>
    NativeBuffer ErrorJson(IntPtr error);

    /// <summary>Frees an error object.</summary>
    void ErrorFree(IntPtr error);

    /// <summary>Frees a buffer the library allocated.</summary>
    void BufferFree(NativeBuffer buffer);
}
