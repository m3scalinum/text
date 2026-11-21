using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
    /// Installs the library resolver once, before any P/Invoke runs, so an override
    /// path or the <c>runtimes/&lt;rid&gt;/native/</c> layout is found without a probe
    /// call. <see cref="OcrEngineHandle"/>'s static constructor is the single caller,
    /// which is why every entry point reaches the resolver first.
    /// </summary>
    internal static void Initialize() => NativeLibraryResolver.Initialize();

    /// <summary>The handle count the native library keeps, for leak tests.</summary>
    internal static uint LiveHandleCount() => NativeMethods.EngineLiveCount();

    internal static string BuildInfo()
    {
        try
        {
            return NativeMethods.ReadUtf8(NativeMethods.BuildInfo());
        }
        catch (DllNotFoundException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Turns an error object into an exception and frees it.
    /// </summary>
    internal static OcrException ToException(IntPtr error)
    {
        var code = "unknown";
        var message = "the native library failed without an error object";

        if (error != IntPtr.Zero)
        {
            try
            {
                var buffer = NativeMethods.ErrorJson(error);
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
                    NativeMethods.BufferFree(buffer);
                }
            }
            finally
            {
                NativeMethods.ErrorFree(error);
            }
        }

        return new OcrException(code, message);
    }
}

/// <summary>
/// An engine handle. It owns the models: disposing it drops them in Rust.
/// </summary>
internal sealed unsafe class OcrEngineHandle : IDisposable
{
    private IntPtr _handle;

    static OcrEngineHandle() => NativeOcr.Initialize();

    private OcrEngineHandle(IntPtr handle) => _handle = handle;

    internal static OcrEngineHandle Create(byte[] detectionModel, byte[] recognitionModel, byte[]? dictionary)
    {
        fixed (byte* detection = detectionModel)
        fixed (byte* recognition = recognitionModel)
        fixed (byte* alphabet = dictionary)
        {
            var handle = NativeMethods.EngineCreate(
                detection, (nuint)detectionModel.Length,
                recognition, (nuint)recognitionModel.Length,
                alphabet, (nuint)(dictionary?.Length ?? 0),
                out var error);

            if (handle == IntPtr.Zero)
            {
                throw NativeOcr.ToException(error);
            }

            if (error != IntPtr.Zero)
            {
                // A handle and an error together means a bug in Rust; never leak the error.
                NativeMethods.ErrorFree(error);
            }

            return new OcrEngineHandle(handle);
        }
    }

    internal byte[] Recognize(byte[] image)
    {
        var handle = Handle();

        fixed (byte* pixels = image)
        {
            var status = NativeMethods.EngineRecognize(
                handle, pixels, (nuint)image.Length, out var json, out var error);

            return Take(status, json, error);
        }
    }

    internal byte[] RecognizePdf(byte[] pdf, uint dpi, uint firstPage, uint pageCount)
    {
        var handle = Handle();

        fixed (byte* bytes = pdf)
        {
            var status = NativeMethods.EngineRecognizePdf(
                handle, bytes, (nuint)pdf.Length, dpi, firstPage, pageCount, out var json, out var error);

            return Take(status, json, error);
        }
    }

    public void Dispose()
    {
        var handle = _handle;
        _handle = IntPtr.Zero;

        if (handle != IntPtr.Zero)
        {
            NativeMethods.EngineFree(handle);
        }
    }

    private static byte[] Take(int status, NativeBuffer json, IntPtr error)
    {
        if (status != 0)
        {
            if (json.Pointer != IntPtr.Zero)
            {
                NativeMethods.BufferFree(json);
            }

            throw NativeOcr.ToException(error);
        }

        try
        {
            return json.ToArray();
        }
        finally
        {
            NativeMethods.BufferFree(json);
        }
    }

    private IntPtr Handle()
        => _handle != IntPtr.Zero
            ? _handle
            : throw new ObjectDisposedException(nameof(OcrEngine));
}
