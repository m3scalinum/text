namespace M3scalinum.Text.Barcodes;

/// <summary>
/// The payload cannot be encoded in the requested symbology: wrong length for a fixed-length code,
/// a character the symbology has no encoding for, or a payload past the symbol's capacity.
/// </summary>
public sealed class BarcodeFormatException : BarcodeException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">Which symbology refused which payload, and why.</param>
    public BarcodeFormatException(string message)
        : base(BarcodeErrorCode.Encode, message)
    {
    }
}
