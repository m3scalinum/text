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
    int EngineRecognize(
        IntPtr engine,
        byte[] image,
        double pageConfidence,
        double wordConfidence,
        double maxRejectedWordRatio,
        out NativeBuffer json,
        out IntPtr error);

    /// <summary>Renders and recognises pages of a PDF; the result document is written to <paramref name="json"/>.</summary>
    int EngineRecognizePdf(
        IntPtr engine,
        byte[] pdf,
        uint dpi,
        uint firstPage,
        uint pageCount,
        double pageConfidence,
        double wordConfidence,
        double maxRejectedWordRatio,
        out NativeBuffer json,
        out IntPtr error);

    /// <summary>
    /// Recognises an encoded image after the requested preprocessing; the result document
    /// is written to <paramref name="json"/>.
    /// </summary>
    /// <remarks>
    /// The default implementation refuses. A double written before preprocessing existed
    /// therefore keeps compiling and cannot silently drop a stage a caller asked for: the
    /// failure is loud instead of a page read at the wrong angle.
    /// </remarks>
    int EngineRecognizePreprocessed(
        IntPtr engine,
        byte[] image,
        double pageConfidence,
        double wordConfidence,
        double maxRejectedWordRatio,
        bool detectRotation,
        bool deskew,
        bool denoise,
        out NativeBuffer json,
        out IntPtr error)
        => throw new NotSupportedException("this native backend does not preprocess images");

    /// <summary>
    /// Renders and recognises pages of a PDF after the requested preprocessing; the result
    /// document is written to <paramref name="json"/>.
    /// </summary>
    /// <remarks>Same default as <see cref="EngineRecognizePreprocessed"/>.</remarks>
    int EngineRecognizePdfPreprocessed(
        IntPtr engine,
        byte[] pdf,
        uint dpi,
        uint firstPage,
        uint pageCount,
        double pageConfidence,
        double wordConfidence,
        double maxRejectedWordRatio,
        bool detectRotation,
        bool deskew,
        bool denoise,
        out NativeBuffer json,
        out IntPtr error)
        => throw new NotSupportedException("this native backend does not preprocess pages");

    /// <summary>The <c>{"code","message"}</c> document of an error object.</summary>
    NativeBuffer ErrorJson(IntPtr error);

    /// <summary>
    /// Inspects model bytes without loading them; the report is written to
    /// <paramref name="json"/>.
    /// </summary>
    /// <remarks>
    /// The default implementation refuses, so a double written before models were
    /// identified keeps compiling and fails loudly rather than silently reporting
    /// no identity.
    /// </remarks>
    int ModelInspect(byte[] model, int kind, out NativeBuffer json, out IntPtr error)
        => throw new NotSupportedException("this native backend does not inspect models");

    /// <summary>
    /// Validates and identifies the two models of a directory; the report is
    /// written to <paramref name="json"/>.
    /// </summary>
    /// <remarks>Same default as <see cref="ModelInspect"/>.</remarks>
    int ModelSetInspect(byte[] directory, out NativeBuffer json, out IntPtr error)
        => throw new NotSupportedException("this native backend does not inspect model directories");

    /// <summary>Creates an engine from the two models of a directory.</summary>
    /// <remarks>Same default as <see cref="ModelInspect"/>.</remarks>
    IntPtr EngineCreateFromDirectory(byte[] directory, byte[]? dictionary, out IntPtr error)
        => throw new NotSupportedException("this native backend cannot load models from a directory");

    /// <summary>The identity of the models an engine runs.</summary>
    /// <remarks>Same default as <see cref="ModelInspect"/>.</remarks>
    int EngineModels(IntPtr engine, out NativeBuffer json, out IntPtr error)
        => throw new NotSupportedException("this native backend does not identify its models");

    /// <summary>
    /// Creates an engine holding one model set per language from a JSON plan.
    /// </summary>
    /// <remarks>
    /// The default refuses, so a double written before language selection existed
    /// fails loudly rather than pretending an engine it cannot build.
    /// </remarks>
    IntPtr EngineCreateLanguages(byte[] plan, out IntPtr error)
        => throw new NotSupportedException("this native backend cannot load a language plan");

    /// <summary>Recognises an image with the model set of a named language.</summary>
    /// <remarks>Same default as <see cref="EngineCreateLanguages"/>.</remarks>
    int EngineRecognizeLanguage(
        IntPtr engine,
        byte[] image,
        double pageConfidence,
        double wordConfidence,
        double maxRejectedWordRatio,
        byte[] language,
        bool detectRotation,
        bool deskew,
        bool denoise,
        out NativeBuffer json,
        out IntPtr error)
        => throw new NotSupportedException("this native backend cannot read in a named language");

    /// <summary>Renders and recognises pages, each with the set of its language.</summary>
    /// <remarks>Same default as <see cref="EngineCreateLanguages"/>.</remarks>
    int EngineRecognizePdfLanguages(
        IntPtr engine,
        byte[] pdf,
        uint dpi,
        uint firstPage,
        uint pageCount,
        double pageConfidence,
        double wordConfidence,
        double maxRejectedWordRatio,
        byte[] pageLanguages,
        bool detectRotation,
        bool deskew,
        bool denoise,
        out NativeBuffer json,
        out IntPtr error)
        => throw new NotSupportedException("this native backend cannot choose a language per page");

    /// <summary>The number of pages a PDF holds, as <c>{"pages":N}</c>.</summary>
    /// <remarks>Same default as <see cref="EngineCreateLanguages"/>.</remarks>
    int PdfPageCount(byte[] pdf, out NativeBuffer json, out IntPtr error)
        => throw new NotSupportedException("this native backend cannot count the pages of a PDF");

    /// <summary>
    /// Reports the size an image declares in its format header, without decoding it, or
    /// refuses it past the two ceilings.
    /// </summary>
    /// <remarks>
    /// The default refuses, so a double written before the image ceilings existed fails
    /// loudly rather than pretending an image it never measured.
    /// </remarks>
    int ImageAdmit(
        byte[] image,
        ulong maximumPixels,
        nuint maximumBytes,
        out NativeBuffer json,
        out IntPtr error)
        => throw new NotSupportedException("this native backend cannot measure an image");

    /// <summary>Recognises an image under explicit decode ceilings.</summary>
    /// <remarks>Same default as <see cref="ImageAdmit"/>.</remarks>
    int EngineRecognizeLimited(
        IntPtr engine,
        byte[] image,
        ulong maximumPixels,
        nuint maximumBytes,
        double pageConfidence,
        double wordConfidence,
        double maxRejectedWordRatio,
        out NativeBuffer json,
        out IntPtr error)
        => throw new NotSupportedException("this native backend cannot bound an image decode");

    /// <summary>Recognises an image under explicit decode ceilings, after preprocessing.</summary>
    /// <remarks>Same default as <see cref="ImageAdmit"/>.</remarks>
    int EngineRecognizePreprocessedLimited(
        IntPtr engine,
        byte[] image,
        ulong maximumPixels,
        nuint maximumBytes,
        double pageConfidence,
        double wordConfidence,
        double maxRejectedWordRatio,
        bool detectRotation,
        bool deskew,
        bool denoise,
        out NativeBuffer json,
        out IntPtr error)
        => throw new NotSupportedException("this native backend cannot bound an image decode");

    /// <summary>Recognises an image in a named language, under explicit decode ceilings.</summary>
    /// <remarks>Same default as <see cref="ImageAdmit"/>.</remarks>
    int EngineRecognizeLanguageLimited(
        IntPtr engine,
        byte[] image,
        ulong maximumPixels,
        nuint maximumBytes,
        double pageConfidence,
        double wordConfidence,
        double maxRejectedWordRatio,
        byte[] language,
        bool detectRotation,
        bool deskew,
        bool denoise,
        out NativeBuffer json,
        out IntPtr error)
        => throw new NotSupportedException("this native backend cannot bound an image decode");

    /// <summary>Frees an error object.</summary>
    void ErrorFree(IntPtr error);

    /// <summary>Frees a buffer the library allocated.</summary>
    void BufferFree(NativeBuffer buffer);
}
