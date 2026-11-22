namespace M3scalinum.Text.Barcodes;

/// <summary>
/// A GS1 element string that cannot be read: an Application Identifier this build does not know, a
/// value outside the length the standard gives its identifier, a check digit that does not match,
/// or a string that is not a Digital Link.
/// </summary>
/// <remarks>
/// The message names the identifier at fault, so a caller who printed the wrong field learns which
/// one. Nothing is dropped silently: an unknown identifier is a failure, never free text.
/// </remarks>
public sealed class BarcodeGs1Exception : BarcodeException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">Which identifier or value was refused, and why.</param>
    public BarcodeGs1Exception(string message)
        : base(BarcodeErrorCode.Gs1, message)
    {
    }
}
