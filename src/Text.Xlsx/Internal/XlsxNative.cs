using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace M3scalinum.Text.Xlsx.Internal;

/// <summary>Owns a native parsed workbook.</summary>
internal sealed class XlsxHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public XlsxHandle(IntPtr handle)
        : base(ownsHandle: true) => SetHandle(handle);

    protected override bool ReleaseHandle()
    {
        NativeMethods.WorkbookFree(handle);
        return true;
    }
}

/// <summary>
/// The safe face of the native reader: one method per call, each translating a native error into an
/// <see cref="XlsxException"/> and each buffer freed before the call returns.
/// </summary>
internal static class XlsxNative
{
    /// <summary>The options payload the native side reads.</summary>
    internal static string OptionsJson(long maxWorkbookBytes, long maxCells)
        => $"{{\"maxBytes\": {maxWorkbookBytes}, \"maxCells\": {maxCells}}}";

    static XlsxNative()
    {
        // Installs the RID-aware resolver before the first call needs it.
        NativeLibraryResolver.Initialize();
    }

    /// <summary>Parses a workbook; the caller owns the returned handle.</summary>
    internal static unsafe XlsxHandle Parse(byte[] workbook, string optionsJson)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        IntPtr error = IntPtr.Zero;
        IntPtr handle;

        fixed (byte* data = workbook)
        {
            handle = NativeMethods.WorkbookParse(
                data, (nuint)workbook.Length, optionsJson, out error);
        }

        if (handle == IntPtr.Zero)
        {
            throw Translate(error, "the workbook could not be parsed");
        }

        return new XlsxHandle(handle);
    }

    /// <summary>The workbook as JSON.</summary>
    internal static string ReadAst(XlsxHandle workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        var buffer = NativeMethods.WorkbookAstJson(workbook.DangerousGetHandle(), out var error);

        if (error != IntPtr.Zero)
        {
            throw Translate(error, "the workbook tree could not be read");
        }

        return TakeString(buffer);
    }

    /// <summary>How many workbook handles are alive right now. Exposed for the tests.</summary>
    internal static int LiveCount() => (int)NativeMethods.WorkbookLiveCount();

    /// <summary>The ABI version the loaded library implements. Exposed for the tests.</summary>
    internal static uint AbiVersion() => NativeMethods.AbiVersion();

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
    /// is a broken ABI, not a workbook problem, and is reported as such.
    /// </summary>
    private static XlsxException Translate(IntPtr error, string fallback)
    {
        if (error == IntPtr.Zero)
        {
            return new XlsxException(
                XlsxErrorCode.Internal,
                $"{fallback}: the native library returned no error object");
        }

        try
        {
            var code = (XlsxErrorCode)NativeMethods.ErrorCode(error);
            var json = Encoding.UTF8.GetString(TakeBytes(NativeMethods.ErrorJson(error)));
            var message = ReadMessage(json) ?? fallback;

            return new XlsxException(code, message);
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
                json, XlsxJsonContext.Default.XlsxErrorPayloadDto);

            return string.IsNullOrEmpty(payload?.Message) ? null : payload!.Message;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
