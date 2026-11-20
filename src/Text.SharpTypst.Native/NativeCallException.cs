using System;
using System.Runtime.InteropServices;

namespace M3scalinum.Text.SharpTypst.Native;

/// <summary>
/// A failed native call. Deliberately internal: the public layer translates it into a
/// <c>TypstException</c> subtype, so no user code ever sees an interop shape.
/// </summary>
internal sealed class NativeCallException : Exception
{
    public NativeCallException(NativeStatus status, string payload, string message, string? traceback = null)
        : base(message)
    {
        Status = status;
        Payload = payload;
        Traceback = traceback;
    }

    public NativeStatus Status { get; }

    /// <summary>Raw JSON error payload (Spec 02 §6).</summary>
    public string Payload { get; }

    public string? Traceback { get; }

    /// <summary>
    /// Reads the error slot, frees it, and throws. Called for every non-zero status.
    /// </summary>
    internal static void ThrowIfFailed(int status, IntPtr error, string operation)
    {
        if (status == (int)NativeStatus.Ok)
        {
            if (error != IntPtr.Zero)
            {
                NativeMethods.ErrorFree(error);
            }

            return;
        }

        Throw(status, error, operation);
    }

    /// <summary>
    /// Reads the status carried by the error object itself, then throws. Used when a call
    /// returns a null handle and the status is only known from the payload.
    /// </summary>
    internal static void ThrowFromError(IntPtr error, string operation)
    {
        if (error == IntPtr.Zero)
        {
            throw new NativeCallException(
                NativeStatus.Panic, string.Empty, $"{operation} failed without an error payload");
        }

        var status = NativeMethods.ErrorCode(error);
        Throw(status, error, operation);
    }

    /// <summary>Reads and frees the error slot, then throws.</summary>
    internal static void Throw(int status, IntPtr error, string operation)
    {
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

        var code = (NativeStatus)status;
        throw new NativeCallException(
            code,
            payload,
            $"{operation} failed: {code}",
            traceback.Length == 0 ? null : traceback);
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
            Marshal.Copy(buffer.Pointer, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            NativeMethods.BufferFree(buffer);
        }
    }

    internal static string ReadAndFreeString(NativeBuffer buffer)
        => System.Text.Encoding.UTF8.GetString(ReadAndFree(buffer));
}
