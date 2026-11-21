using System;

namespace M3scalinum.Text.Barcodes;

/// <summary>What a caller can change about the symbol <see cref="Barcode.Encode"/> produces.</summary>
/// <remarks>
/// Everything here has a default that works: <c>Barcode.Encode(symbology, payload)</c> is the common
/// call, and an option exists only where a real document needed it.
/// </remarks>
public sealed class BarcodeOptions
{
    /// <summary>
    /// Pixels per module in <see cref="BarcodeImage.Png"/>; four by default. An integer factor,
    /// never a resampling: a bar edge stays sharp. One is accepted, and is below what the Aztec
    /// detector needs to read a symbol back, so the default is four.
    /// </summary>
    public int Scale { get; init; } = 4;

    /// <summary>
    /// Quiet zone in modules on each side, or <see langword="null"/> for the symbology's own default:
    /// four for a QR Code, one for a DataMatrix, two for an Aztec or a PDF417, five for a
    /// one-dimensional symbol.
    /// </summary>
    public int? QuietZone { get; init; }

    /// <summary>
    /// Height of a one-dimensional symbol's bars, in modules, or <see langword="null"/> for forty.
    /// Ignored by the two-dimensional symbologies, whose height is set by their own layout.
    /// </summary>
    public int? BarHeight { get; init; }

    /// <summary>
    /// Error correction for a QR Code. Any other symbology refuses a level other than
    /// <see cref="BarcodeErrorCorrection.Default"/>, because "Q" means nothing to an Aztec symbol.
    /// </summary>
    public BarcodeErrorCorrection ErrorCorrection { get; init; } = BarcodeErrorCorrection.Default;

    /// <summary>Validates the options against a symbology, throwing before the native call.</summary>
    internal void Validate(BarcodeSymbology symbology)
    {
        if (Scale is < 1 or > 64)
        {
            throw new BarcodeException(
                BarcodeErrorCode.InvalidArgument,
                $"Scale must be between 1 and 64 pixels per module, got {Scale}");
        }

        if (QuietZone is < 0)
        {
            throw new BarcodeException(
                BarcodeErrorCode.InvalidArgument,
                $"QuietZone must not be negative, got {QuietZone}");
        }

        if (BarHeight is < 1 or > 1024)
        {
            throw new BarcodeException(
                BarcodeErrorCode.InvalidArgument,
                $"BarHeight must be between 1 and 1024 modules, got {BarHeight}");
        }

        if (ErrorCorrection != BarcodeErrorCorrection.Default && symbology != BarcodeSymbology.QrCode)
        {
            throw new BarcodeException(
                BarcodeErrorCode.InvalidArgument,
                $"ErrorCorrection applies to a QR Code only; {symbology} has no error correction level in these terms");
        }
    }
}
