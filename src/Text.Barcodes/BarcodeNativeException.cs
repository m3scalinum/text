namespace M3scalinum.Text.Barcodes;

/// <summary>
/// The native library failed in a way this package does not model: a panic inside the encoder, or a
/// library that reports an ABI version this assembly was not built for.
/// </summary>
public sealed class BarcodeNativeException : BarcodeException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What the native library reported.</param>
    public BarcodeNativeException(string message)
        : base(BarcodeErrorCode.Native, message)
    {
    }

    /// <summary>Creates the exception with the panic backtrace, when there is one.</summary>
    /// <param name="message">What the native library reported.</param>
    /// <param name="traceback">The native traceback, empty when there is none.</param>
    public BarcodeNativeException(string message, string traceback)
        : base(BarcodeErrorCode.Native, message)
    {
        Traceback = traceback;
    }

    /// <summary>The native traceback, or <see langword="null"/> when the failure carried none.</summary>
    public string? Traceback { get; }
}
