namespace M3scalinum.Text.Barcodes;

/// <summary>The error correction a QR Code is written with.</summary>
/// <remarks>
/// Only QR Code takes a level in these terms: Aztec takes a percentage of the codewords and PDF417 a
/// level from 0 to 8, and rxing spells both differently. <see cref="BarcodeOptions"/> therefore
/// refuses a level on any other symbology instead of silently ignoring it.
/// </remarks>
public enum BarcodeErrorCorrection
{
    /// <summary>Whatever the symbology defaults to: L for a QR Code.</summary>
    Default = 0,

    /// <summary>L: recovers about 7% of the symbol.</summary>
    Low = 1,

    /// <summary>M: about 15%.</summary>
    Medium = 2,

    /// <summary>Q: about 25%.</summary>
    Quartile = 3,

    /// <summary>H: about 30%, the level for a code that will be printed small or read dirty.</summary>
    High = 4,
}

/// <summary>The spelling each level takes across the ABI.</summary>
internal static class BarcodeErrorCorrectionNames
{
    public static string? ToAbiName(this BarcodeErrorCorrection level) => level switch
    {
        BarcodeErrorCorrection.Default => null,
        BarcodeErrorCorrection.Low => "L",
        BarcodeErrorCorrection.Medium => "M",
        BarcodeErrorCorrection.Quartile => "Q",
        BarcodeErrorCorrection.High => "H",
        _ => null,
    };
}
