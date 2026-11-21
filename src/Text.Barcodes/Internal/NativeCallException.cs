using System;
using System.Text;

namespace M3scalinum.Text.Barcodes.Internal;

/// <summary>
/// A failed native call. Deliberately internal: the public layer translates it into a
/// <see cref="BarcodeException"/> subtype, so no user code ever sees an interop shape.
/// </summary>
internal sealed class NativeCallException : Exception
{
    public NativeCallException(BarcodeStatus status, string payload, string message, string? traceback = null)
        : base(message)
    {
        Status = status;
        Payload = payload;
        Traceback = traceback;
    }

    public BarcodeStatus Status { get; }

    /// <summary>Raw JSON error payload (Spec 20 §4).</summary>
    public string Payload { get; }

    public string? Traceback { get; }

    /// <summary>
    /// Reads the error slot, frees it, and throws. Called for every non-zero status.
    /// </summary>
    internal static void Throw(int status, IntPtr error, string operation)
    {
        if (status == (int)BarcodeStatus.Ok)
        {
            if (error != IntPtr.Zero)
            {
                NativeMethods.ErrorFree(error);
            }

            return;
        }

        var payload = string.Empty;
        var traceback = string.Empty;
        try
        {
            if (error != IntPtr.Zero)
            {
                payload = ReadAndFreeString(NativeMethods.ErrorJson(error));
                traceback = ReadAndFreeString(NativeMethods.ErrorTraceback(error));
            }
        }
        finally
        {
            if (error != IntPtr.Zero)
            {
                NativeMethods.ErrorFree(error);
            }
        }

        var code = (BarcodeStatus)status;
        throw new NativeCallException(
            code,
            payload,
            $"{operation} failed: {code}",
            traceback.Length == 0 ? null : traceback);
    }

    /// <summary>
    /// Reads the status carried by the error object itself, then throws. Used when a call returns a
    /// null handle and the status is only known from the payload.
    /// </summary>
    internal static void ThrowFromError(IntPtr error, string operation)
    {
        if (error == IntPtr.Zero)
        {
            throw new NativeCallException(
                BarcodeStatus.Panic, string.Empty, $"{operation} failed without an error payload");
        }

        Throw(NativeMethods.ErrorCode(error), error, operation);
    }

    /// <summary>Copies a native buffer into a managed array and frees it.</summary>
    internal static byte[] ReadAndFree(NativeBuffer buffer)
    {
        try
        {
            if (buffer.Pointer == IntPtr.Zero || buffer.Length == 0)
            {
                return [];
            }

            var bytes = new byte[(int)buffer.Length];
            System.Runtime.InteropServices.Marshal.Copy(buffer.Pointer, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            NativeMethods.BufferFree(buffer);
        }
    }

    internal static string ReadAndFreeString(NativeBuffer buffer)
        => Encoding.UTF8.GetString(ReadAndFree(buffer));

    /// <summary>The message carried by the JSON payload, or the status name when there is none.</summary>
    internal string Describe()
    {
        var message = ReadMessage(Payload);
        return message.Length == 0 ? Status.ToString() : message;
    }

    private static string ReadMessage(string payload)
    {
        if (payload.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(payload);
            return document.RootElement.TryGetProperty("message", out var message)
                ? message.GetString() ?? string.Empty
                : string.Empty;
        }
        catch (System.Text.Json.JsonException)
        {
            return string.Empty;
        }
    }
}
