using System;
using System.Runtime.InteropServices;
using System.Text;
using M3scalinum.Text.Ocr.Internal;

namespace M3scalinum.Text.Ocr.Tests;

/// <summary>
/// An <see cref="IOcrNative"/> with no library behind it.
/// </summary>
/// <remarks>
/// The ABI double: it hands back whatever handle, document, status and error the test
/// asked for, and records what the managed side passed it. That is what makes the
/// handshake, the status translation, the out-buffer lifetime and the dispose path
/// testable on a machine that holds no models, which is every machine this repository
/// checks on.
/// </remarks>
internal sealed class FakeNativeLibrary : IOcrNative
{
    /// <summary>A handle value the fake reports as live.</summary>
    internal static readonly IntPtr HandleValue = new(0x1000);

    /// <summary>An error object the fake reports as raised.</summary>
    internal static readonly IntPtr ErrorValue = new(0x2000);

    /// <summary>The handle <see cref="EngineCreate"/> returns.</summary>
    internal IntPtr CreatedHandle { get; set; } = HandleValue;

    /// <summary>Whether <see cref="EngineCreate"/> also sets an error object.</summary>
    internal bool ErrorAlongsideHandle { get; set; }

    /// <summary>The status the recognise calls return; 0 is success.</summary>
    internal int Status { get; set; }

    /// <summary>The result document a successful recognise call returns.</summary>
    internal byte[] Document { get; set; } = Encoding.UTF8.GetBytes(
        """{"width":100,"height":50,"text":"hello","meanConfidence":0.9,"lines":[]}""");

    /// <summary>A document returned alongside a failing status, to test the leak path.</summary>
    internal byte[]? DocumentOnFailure { get; set; }

    /// <summary>The <c>code</c> of the error object.</summary>
    internal string ErrorCode { get; set; } = "unknown";

    /// <summary>The <c>message</c> of the error object.</summary>
    internal string ErrorMessage { get; set; } = "the call failed";

    /// <summary>When set, the exact JSON the error object reports instead of code and message.</summary>
    internal string? ErrorDocumentOverride { get; set; }

    /// <summary>Whether <see cref="BuildInfo"/> throws <see cref="DllNotFoundException"/>.</summary>
    internal bool BuildInfoUnavailable { get; set; }

    /// <summary>The build-information JSON.</summary>
    internal string BuildInfoJson { get; set; } = """{"ocrs":"0.14"}""";

    /// <summary>The ABI version <see cref="AbiVersion"/> reports.</summary>
    internal uint AbiVersionValue { get; set; } = 1;

    /// <summary>The handle count <see cref="EngineLiveCount"/> reports.</summary>
    internal uint LiveCount { get; set; } = 1;

    internal int CreateCalls { get; private set; }

    internal int FreeCalls { get; private set; }

    internal int RecognizeCalls { get; private set; }

    internal int RecognizePdfCalls { get; private set; }

    internal int BufferFreeCalls { get; private set; }

    internal int ErrorFreeCalls { get; private set; }

    internal IntPtr LastEngine { get; private set; }

    internal byte[]? LastImage { get; private set; }

    internal byte[]? LastPdf { get; private set; }

    internal uint LastDpi { get; private set; }

    internal uint LastFirstPage { get; private set; }

    internal uint LastPageCount { get; private set; }

    internal byte[]? LastDetectionModel { get; private set; }

    internal byte[]? LastRecognitionModel { get; private set; }

    internal byte[]? LastDictionary { get; private set; }

    public uint AbiVersion() => AbiVersionValue;

    public IntPtr BuildInfo()
    {
        if (BuildInfoUnavailable)
        {
            throw new DllNotFoundException("sharp_text_ocr is not loadable here");
        }

        return Marshal.StringToCoTaskMemUTF8(BuildInfoJson);
    }

    public IntPtr EngineCreate(
        byte[] detectionModel,
        byte[] recognitionModel,
        byte[]? dictionary,
        out IntPtr error)
    {
        CreateCalls++;
        LastDetectionModel = detectionModel;
        LastRecognitionModel = recognitionModel;
        LastDictionary = dictionary;
        error = ErrorAlongsideHandle ? ErrorValue : IntPtr.Zero;

        return CreatedHandle;
    }

    public void EngineFree(IntPtr engine)
    {
        FreeCalls++;
        LastEngine = engine;
    }

    public uint EngineLiveCount() => LiveCount;

    public int EngineRecognize(IntPtr engine, byte[] image, out NativeBuffer json, out IntPtr error)
    {
        RecognizeCalls++;
        LastEngine = engine;
        LastImage = image;

        return Complete(Status == 0 ? Document : DocumentOnFailure, out json, out error);
    }

    public int EngineRecognizePdf(
        IntPtr engine,
        byte[] pdf,
        uint dpi,
        uint firstPage,
        uint pageCount,
        out NativeBuffer json,
        out IntPtr error)
    {
        RecognizePdfCalls++;
        LastEngine = engine;
        LastPdf = pdf;
        LastDpi = dpi;
        LastFirstPage = firstPage;
        LastPageCount = pageCount;

        return Complete(Status == 0 ? Document : DocumentOnFailure, out json, out error);
    }

    public NativeBuffer ErrorJson(IntPtr error)
        => Buffer(Encoding.UTF8.GetBytes(ErrorDocumentOverride
            ?? $"{{\"code\":\"{ErrorCode}\",\"message\":\"{ErrorMessage}\"}}"));

    public void ErrorFree(IntPtr error) => ErrorFreeCalls++;

    public void BufferFree(NativeBuffer buffer)
    {
        BufferFreeCalls++;

        if (buffer.Pointer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(buffer.Pointer);
        }
    }

    private int Complete(byte[]? document, out NativeBuffer json, out IntPtr error)
    {
        json = document is null ? default : Buffer(document);
        error = Status == 0 ? IntPtr.Zero : ErrorValue;

        return Status;
    }

    private static NativeBuffer Buffer(byte[] bytes)
    {
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);

        return new NativeBuffer { Pointer = pointer, Length = (nuint)bytes.Length };
    }
}
