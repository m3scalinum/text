using System;
using System.Text;
using System.Text.Json;

namespace M3scalinum.Text.Ocr.Internal;

/// <summary>
/// The managed face of the native library: the probe, the error objects, and the
/// out-buffer lifecycle. Every call into Rust goes through here.
/// </summary>
internal static class NativeOcr
{
    /// <summary>
    /// The one implementation a consumer gets: the P/Invoke declarations.
    /// </summary>
    internal static IOcrNative Library { get; } = new NativeLibraryOcr();

    /// <summary>
    /// Installs the library resolver once, before any P/Invoke runs, so an override
    /// path or the <c>runtimes/&lt;rid&gt;/native/</c> layout is found without a probe
    /// call. <see cref="OcrEngineHandle"/>'s static constructor is the single caller,
    /// which is why every entry point reaches the resolver first.
    /// </summary>
    internal static void Initialize() => NativeLibraryResolver.Initialize();

    /// <summary>The handle count the native library keeps, for leak tests.</summary>
    internal static uint LiveHandleCount() => Library.EngineLiveCount();

    internal static string BuildInfo() => BuildInfo(Library);

    /// <summary>
    /// The build information, or an empty string when the library cannot be loaded:
    /// a host that asks what the package carries must get an answer, not a crash.
    /// </summary>
    internal static string BuildInfo(IOcrNative library)
    {
        try
        {
            return NativeMethods.ReadUtf8(library.BuildInfo());
        }
        catch (DllNotFoundException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Turns an error object into an exception and frees it.
    /// </summary>
    internal static OcrException ToException(IntPtr error) => ToException(Library, error);

    /// <summary>
    /// Turns an error object into an exception and frees it.
    /// </summary>
    /// <remarks>
    /// The status code returned alongside the error is not the contract: it is a
    /// convenience for a C host that has no JSON reader. The <c>code</c> inside the
    /// error document is what <see cref="OcrException.Code"/> reports, so the two
    /// halves can gain a status without the managed side learning a status table.
    /// </remarks>
    internal static OcrException ToException(IOcrNative library, IntPtr error)
    {
        var code = "unknown";
        var message = "the native library failed without an error object";

        if (error != IntPtr.Zero)
        {
            try
            {
                var buffer = library.ErrorJson(error);
                try
                {
                    var text = Encoding.UTF8.GetString(buffer.ToArray());
                    using var document = JsonDocument.Parse(text);
                    var root = document.RootElement;

                    if (root.TryGetProperty("code", out var codeElement))
                    {
                        code = codeElement.GetString() ?? code;
                    }

                    if (root.TryGetProperty("message", out var messageElement))
                    {
                        message = messageElement.GetString() ?? message;
                    }
                }
                finally
                {
                    library.BufferFree(buffer);
                }
            }
            finally
            {
                library.ErrorFree(error);
            }
        }

        return new OcrException(code, message);
    }
}

/// <summary>
/// The <see cref="IOcrNative"/> that talks to the Rust library. Every method here only
/// forwards: pinning and the out-parameters stay on this side of the interface, so
/// <see cref="OcrEngineHandle"/> holds no pointers and can be driven by a double.
/// </summary>
internal sealed unsafe class NativeLibraryOcr : IOcrNative
{
    /// <inheritdoc />
    public uint AbiVersion() => NativeMethods.AbiVersion();

    /// <inheritdoc />
    public IntPtr BuildInfo() => NativeMethods.BuildInfo();

    /// <inheritdoc />
    public IntPtr EngineCreate(
        byte[] detectionModel,
        byte[] recognitionModel,
        byte[]? dictionary,
        out IntPtr error)
    {
        fixed (byte* detection = detectionModel)
        fixed (byte* recognition = recognitionModel)
        fixed (byte* alphabet = dictionary)
        {
            return NativeMethods.EngineCreate(
                detection, (nuint)detectionModel.Length,
                recognition, (nuint)recognitionModel.Length,
                alphabet, (nuint)(dictionary?.Length ?? 0),
                out error);
        }
    }

    /// <inheritdoc />
    public void EngineFree(IntPtr engine) => NativeMethods.EngineFree(engine);

    /// <inheritdoc />
    public uint EngineLiveCount() => NativeMethods.EngineLiveCount();

    /// <inheritdoc />
    public int EngineRecognize(IntPtr engine, byte[] image, out NativeBuffer json, out IntPtr error)
    {
        fixed (byte* pixels = image)
        {
            return NativeMethods.EngineRecognize(engine, pixels, (nuint)image.Length, out json, out error);
        }
    }

    /// <inheritdoc />
    public int EngineRecognizePdf(
        IntPtr engine,
        byte[] pdf,
        uint dpi,
        uint firstPage,
        uint pageCount,
        out NativeBuffer json,
        out IntPtr error)
    {
        fixed (byte* bytes = pdf)
        {
            return NativeMethods.EngineRecognizePdf(
                engine, bytes, (nuint)pdf.Length, dpi, firstPage, pageCount, out json, out error);
        }
    }

    /// <inheritdoc />
    public NativeBuffer ErrorJson(IntPtr error) => NativeMethods.ErrorJson(error);

    /// <inheritdoc />
    public void ErrorFree(IntPtr error) => NativeMethods.ErrorFree(error);

    /// <inheritdoc />
    public void BufferFree(NativeBuffer buffer) => NativeMethods.BufferFree(buffer);
}

/// <summary>
/// An engine handle. It owns the models: disposing it drops them in Rust.
/// </summary>
internal sealed class OcrEngineHandle : IDisposable
{
    private readonly IOcrNative _library;
    private IntPtr _handle;

    static OcrEngineHandle() => NativeOcr.Initialize();

    private OcrEngineHandle(IOcrNative library, IntPtr handle)
    {
        _library = library;
        _handle = handle;
    }

    internal static OcrEngineHandle Create(byte[] detectionModel, byte[] recognitionModel, byte[]? dictionary)
        => Create(NativeOcr.Library, detectionModel, recognitionModel, dictionary);

    internal static OcrEngineHandle Create(
        IOcrNative library,
        byte[] detectionModel,
        byte[] recognitionModel,
        byte[]? dictionary)
    {
        var handle = library.EngineCreate(detectionModel, recognitionModel, dictionary, out var error);

        if (handle == IntPtr.Zero)
        {
            throw NativeOcr.ToException(library, error);
        }

        if (error != IntPtr.Zero)
        {
            // A handle and an error together means a bug in Rust; never leak the error.
            library.ErrorFree(error);
        }

        return new OcrEngineHandle(library, handle);
    }

    internal byte[] Recognize(byte[] image)
    {
        var handle = Handle();

        var status = _library.EngineRecognize(handle, image, out var json, out var error);

        return Take(status, json, error);
    }

    internal byte[] RecognizePdf(byte[] pdf, uint dpi, uint firstPage, uint pageCount)
    {
        var handle = Handle();

        var status = _library.EngineRecognizePdf(
            handle, pdf, dpi, firstPage, pageCount, out var json, out var error);

        return Take(status, json, error);
    }

    public void Dispose()
    {
        var handle = _handle;
        _handle = IntPtr.Zero;

        if (handle != IntPtr.Zero)
        {
            _library.EngineFree(handle);
        }
    }

    private byte[] Take(int status, NativeBuffer json, IntPtr error)
    {
        if (status != 0)
        {
            if (json.Pointer != IntPtr.Zero)
            {
                _library.BufferFree(json);
            }

            throw NativeOcr.ToException(_library, error);
        }

        try
        {
            return json.ToArray();
        }
        finally
        {
            _library.BufferFree(json);
        }
    }

    private IntPtr Handle()
        => _handle != IntPtr.Zero
            ? _handle
            : throw new ObjectDisposedException(nameof(OcrEngine));
}
