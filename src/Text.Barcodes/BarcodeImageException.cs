namespace M3scalinum.Text.Barcodes;

/// <summary>
/// The bytes handed to <see cref="Barcode.Decode"/> are not a PNG this build can read: truncated,
/// not a PNG at all, or a PNG whose pixel format the decoder could not turn into luminance.
/// </summary>
/// <remarks>
/// A PNG that is readable and holds no barcode is <em>not</em> this exception: that is a
/// <see cref="DecodeResult"/> with <see cref="DecodeResult.Success"/> false, because a page without
/// a symbol is the normal case for a caller scanning a stack of documents.
/// </remarks>
public sealed class BarcodeImageException : BarcodeException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">Why the image could not be read.</param>
    public BarcodeImageException(string message)
        : base(BarcodeErrorCode.Decode, message)
    {
    }
}
