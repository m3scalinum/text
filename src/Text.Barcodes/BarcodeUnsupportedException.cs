namespace M3scalinum.Text.Barcodes;

/// <summary>
/// The requested symbology is not one this build writes and reads. The message lists the ten it
/// does, so a caller that guessed a name learns the right one from the exception.
/// </summary>
public sealed class BarcodeUnsupportedException : BarcodeException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">Which symbology was asked for, and which ones exist.</param>
    public BarcodeUnsupportedException(string message)
        : base(BarcodeErrorCode.Unsupported, message)
    {
    }
}
