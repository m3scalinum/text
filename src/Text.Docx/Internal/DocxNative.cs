using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace M3scalinum.Text.Docx.Internal;

/// <summary>Owns a native parsed document.</summary>
internal sealed class DocxHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public DocxHandle()
        : base(ownsHandle: true)
    {
    }

    public DocxHandle(IntPtr handle)
        : base(ownsHandle: true) => SetHandle(handle);

    protected override bool ReleaseHandle()
    {
        NativeMethods.DocumentFree(handle);
        return true;
    }
}

/// <summary>
/// The safe face of the native reader: one method per call, each translating a native error into a
/// <see cref="DocxException"/> and each buffer freed before the call returns.
/// </summary>
internal static class DocxNative
{
    /// <summary>The options payload the native side reads, or null for its defaults.</summary>
    internal static string OptionsJson(bool extractImages, long maxDocumentBytes)
        => $"{{\"images\": {(extractImages ? "true" : "false")}, \"maxBytes\": {maxDocumentBytes}}}";

    static DocxNative()
    {
        // Installs the RID-aware resolver before the first call needs it.
        NativeLibraryResolver.Initialize();
    }

    /// <summary>Parses a package; the caller owns the returned handle.</summary>
    internal static unsafe DocxHandle Parse(byte[] document, string optionsJson)
    {
        ArgumentNullException.ThrowIfNull(document);

        IntPtr error = IntPtr.Zero;
        IntPtr handle;

        fixed (byte* data = document)
        {
            handle = NativeMethods.DocumentParse(
                data, (nuint)document.Length, optionsJson, out error);
        }

        if (handle == IntPtr.Zero)
        {
            throw Translate(error, "the document could not be parsed");
        }

        return new DocxHandle(handle);
    }

    /// <summary>The AST of a parsed document, as JSON.</summary>
    internal static string ReadAst(DocxHandle document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var buffer = NativeMethods.DocumentAstJson(document.DangerousGetHandle(), out var error);

        if (error != IntPtr.Zero)
        {
            throw Translate(error, "the document tree could not be read");
        }

        return TakeString(buffer);
    }

    /// <summary>How many media parts the document holds.</summary>
    internal static int ImageCount(DocxHandle document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return (int)NativeMethods.DocumentImageCount(document.DangerousGetHandle());
    }

    /// <summary>The bytes of one media part.</summary>
    internal static byte[] ReadImage(DocxHandle document, ulong handle)
    {
        ArgumentNullException.ThrowIfNull(document);

        var status = NativeMethods.DocumentImage(
            document.DangerousGetHandle(), handle, out var buffer, out var error);

        if (status != 0 || error != IntPtr.Zero)
        {
            throw Translate(error, $"the media part at handle {handle} could not be read");
        }

        return TakeBytes(buffer);
    }

    /// <summary>How many document handles are alive right now. Exposed for the tests.</summary>
    internal static int LiveCount() => (int)NativeMethods.DocumentLiveCount();

    /// <summary>Copies a native buffer out, then frees it.</summary>
    private static byte[] TakeBytes(NativeBuffer buffer)
    {
        if (buffer.Pointer == IntPtr.Zero || buffer.Length == 0)
        {
            return [];
        }

        try
        {
            var bytes = new byte[(int)buffer.Length];
            Marshal.Copy(buffer.Pointer, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            NativeMethods.BufferFree(buffer);
        }
    }

    private static string TakeString(NativeBuffer buffer) => Encoding.UTF8.GetString(TakeBytes(buffer));

    /// <summary>
    /// Turns a native error object into an exception, and frees it. A failure with no error object
    /// is a broken ABI, not a document problem, and is reported as such.
    /// </summary>
    private static DocxException Translate(IntPtr error, string fallback)
    {
        if (error == IntPtr.Zero)
        {
            return new DocxException(
                DocxErrorCode.Internal,
                $"{fallback}: the native library returned no error object");
        }

        try
        {
            var code = (DocxErrorCode)NativeMethods.ErrorCode(error);
            var json = Encoding.UTF8.GetString(TakeBytes(NativeMethods.ErrorJson(error)));
            var message = ReadMessage(json) ?? fallback;

            return new DocxException(code, message);
        }
        finally
        {
            NativeMethods.ErrorFree(error);
        }
    }

    /// <summary>The <c>message</c> of the error payload, which is the sentence a host shows.</summary>
    private static string? ReadMessage(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            var payload = System.Text.Json.JsonSerializer.Deserialize(
                json, DocxJsonContext.Default.DocxErrorPayloadDto);

            return string.IsNullOrEmpty(payload?.Message) ? null : payload!.Message;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
