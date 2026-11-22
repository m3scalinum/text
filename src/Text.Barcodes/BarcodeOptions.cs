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

    /// <summary>
    /// The module colour, or <see langword="null"/> for black.
    /// </summary>
    /// <remarks>
    /// It must stay darker than <see cref="Background"/>, and the pair must clear a contrast ratio of
    /// 4.5:1 — the WCAG 2.1 AA requirement for text, which a module a few tenths of a millimetre wide
    /// needs more than the 3:1 of the graphics criterion. <see cref="Barcode.ContrastRatio"/>
    /// measures it; <see cref="Barcode.Encode"/> refuses a pair below the floor and names the ratio it
    /// measured. A QR Code is dark modules on a light ground (ISO/IEC 18004), so white on black is
    /// refused even at 21:1.
    /// </remarks>
    public BarcodeColor? Foreground { get; init; }

    /// <summary>
    /// The background colour, quiet zone included, or <see langword="null"/> for white.
    /// </summary>
    public BarcodeColor? Background { get; init; }

    /// <summary>
    /// A square centre reserved for a logo, or <see langword="null"/> for none. QR Code only.
    /// </summary>
    /// <remarks>
    /// The region is cleared of modules, so it spends error correction: it is bounded by
    /// <see cref="ErrorCorrection"/>'s capacity and by the symbol's room between its timing patterns,
    /// and the raster is read back with this package's own decoder before it is returned. A design
    /// that does not read back is refused with a message saying so — the alternative would be a
    /// pretty symbol a scanner cannot get past.
    /// </remarks>
    public BarcodeLogo? Logo { get; init; }

    /// <summary>Whether any of the style options is set, which is what picks the styled ABI call.</summary>
    internal bool HasStyle => Foreground is not null || Background is not null || Logo is not null;

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

        Logo?.Validate();
    }
}
