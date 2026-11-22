using System;
using System.Runtime.InteropServices;
using System.Text;
using M3scalinum.Text.Ocr.Internal;

namespace M3scalinum.Text.Ocr.Tests;

/// <summary>
/// An <see cref="IOcrNative"/> that also answers the model calls, on top of the
/// recording double.
/// </summary>
/// <remarks>
/// Composition rather than a second copy of the ABI: every recognition call still
/// goes to <see cref="FakeNativeLibrary"/>, which records it, and only the four
/// model entry points are answered here. That is what lets the managed mapping of a
/// model error, the preflight document and the engine identity be asserted on a
/// machine that holds no model.
/// </remarks>
internal sealed class FakeModelNative : IOcrNative
{
    private readonly FakeNativeLibrary _inner = new();

    /// <summary>The double every non-model call is forwarded to.</summary>
    internal FakeNativeLibrary Inner => _inner;

    /// <summary>The status <see cref="ModelSetInspect"/> returns.</summary>
    internal int ModelSetStatus { get; set; }

    /// <summary>The report <see cref="ModelSetInspect"/> writes on success.</summary>
    internal string ModelSetDocument { get; set; } = IdentityDocument;

    /// <summary>The error document <see cref="ModelSetInspect"/> reports on failure.</summary>
    internal string? ModelSetError { get; set; }

    /// <summary>The status <see cref="EngineCreateFromDirectory"/> reports; 0 creates a handle.</summary>
    internal IntPtr DirectoryHandle { get; set; } = FakeNativeLibrary.HandleValue;

    /// <summary>The error document the directory create reports when it returns no handle.</summary>
    internal string? DirectoryError { get; set; }

    /// <summary>The report <see cref="EngineModels"/> writes.</summary>
    internal string EngineModelsDocument { get; set; } = IdentityDocument;

    /// <summary>Whether <see cref="EngineModels"/> fails.</summary>
    internal bool EngineModelsFails { get; set; }

    /// <summary>The directory bytes the last directory call received.</summary>
    internal byte[]? LastDirectory { get; private set; }

    /// <summary>The handle <see cref="EngineModels"/> was asked about.</summary>
    internal IntPtr LastModelsHandle { get; private set; }

    /// <summary>A well-formed identity document, with distinct digests per role.</summary>
    internal const string IdentityDocument =
        """
        {"detection":{"role":"detection","format":"rten","length":8192,
        "sha256":"1111111111111111111111111111111111111111111111111111111111111111"},
        "recognition":{"role":"recognition","format":"rten","length":16384,
        "sha256":"2222222222222222222222222222222222222222222222222222222222222222"},
        "set":"3333333333333333333333333333333333333333333333333333333333333333"}
        """;

    public uint AbiVersion() => _inner.AbiVersion();

    public IntPtr BuildInfo() => _inner.BuildInfo();

    public IntPtr EngineCreate(
        byte[] detectionModel,
        byte[] recognitionModel,
        byte[]? dictionary,
        out IntPtr error)
        => _inner.EngineCreate(detectionModel, recognitionModel, dictionary, out error);

    public void EngineFree(IntPtr engine) => _inner.EngineFree(engine);

    public uint EngineLiveCount() => _inner.EngineLiveCount();

    public int EngineRecognize(
        IntPtr engine,
        byte[] image,
        double pageConfidence,
        double wordConfidence,
        double maxRejectedWordRatio,
        out NativeBuffer json,
        out IntPtr error)
        => _inner.EngineRecognize(
            engine, image, pageConfidence, wordConfidence, maxRejectedWordRatio, out json, out error);

    public int EngineRecognizePdf(
        IntPtr engine,
        byte[] pdf,
        uint dpi,
        uint firstPage,
        uint pageCount,
        double pageConfidence,
        double wordConfidence,
        double maxRejectedWordRatio,
        out NativeBuffer json,
        out IntPtr error)
        => _inner.EngineRecognizePdf(
            engine, pdf, dpi, firstPage, pageCount,
            pageConfidence, wordConfidence, maxRejectedWordRatio, out json, out error);

    public NativeBuffer ErrorJson(IntPtr error) => _inner.ErrorJson(error);

    public void ErrorFree(IntPtr error) => _inner.ErrorFree(error);

    public void BufferFree(NativeBuffer buffer) => _inner.BufferFree(buffer);

    public int ModelInspect(byte[] model, int kind, out NativeBuffer json, out IntPtr error)
    {
        // The bytes are never a model in these tests; the mapping of the *document*
        // is what is under test, so a fixed refusal is enough.
        json = default;
        error = FakeNativeLibrary.ErrorValue;

        return 4;
    }

    public int ModelSetInspect(byte[] directory, out NativeBuffer json, out IntPtr error)
    {
        LastDirectory = directory;

        if (ModelSetStatus != 0)
        {
            json = default;
            error = FakeNativeLibrary.ErrorValue;
            return ModelSetStatus;
        }

        // The native preflight echoes the directory it read, so the document the
        // managed side parses is the one the library would write.
        var named = System.Text.Json.JsonSerializer.Serialize(Encoding.UTF8.GetString(directory));
        json = Buffer("{\"directory\":" + named + "," + ModelSetDocument.TrimStart().TrimStart('{'));
        error = IntPtr.Zero;
        return 0;
    }

    public IntPtr EngineCreateFromDirectory(byte[] directory, byte[]? dictionary, out IntPtr error)
    {
        LastDirectory = directory;

        if (DirectoryHandle == IntPtr.Zero)
        {
            error = FakeNativeLibrary.ErrorValue;
            return IntPtr.Zero;
        }

        error = IntPtr.Zero;
        return DirectoryHandle;
    }

    public int EngineModels(IntPtr engine, out NativeBuffer json, out IntPtr error)
    {
        LastModelsHandle = engine;

        if (EngineModelsFails)
        {
            json = default;
            error = FakeNativeLibrary.ErrorValue;
            return 4;
        }

        json = Buffer(EngineModelsDocument);
        error = IntPtr.Zero;
        return 0;
    }

    /// <summary>Overrides the error document the model calls report.</summary>
    internal void UseError(string document) => _inner.ErrorDocumentOverride = document;

    private static NativeBuffer Buffer(string document)
    {
        var bytes = Encoding.UTF8.GetBytes(document);
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);

        return new NativeBuffer { Pointer = pointer, Length = (nuint)bytes.Length };
    }
}
