using System;

namespace M3scalinum.Text.Barcodes;

/// <summary>
/// A square centre reserved for the caller's mark: a share of the symbol's area, filled flat.
/// </summary>
/// <remarks>
/// <para>
/// The region is centred on the symbol, aligns to the module grid, and is cleared of modules — which
/// costs error correction, so the size is bounded by the level the symbol is written with and by the
/// room the symbol has between its timing patterns. A share past either bound is refused rather than
/// quietly shrunk.
/// </para>
/// <para>
/// The fill is flat on purpose. This package reserves and clears the region; a real mark — a
/// photograph, a logotype with counters — is composited by the caller's layout over that region, or
/// over the raster. What no caller should do is scale the symbol after the fact: a resampled module
/// is a blurred module.
/// </para>
/// </remarks>
public sealed class BarcodeLogo
{
    /// <summary>
    /// The share of the symbol's <em>area</em> the square covers, in <c>(0, 1]</c>. 0.2 is a fifth of
    /// the symbol; the side of the square is the square root of that share.
    /// </summary>
    /// <remarks>
    /// The level's nominal capacity — 7 % at L, 15 % at M, 25 % at Q and 30 % at H — is the ceiling,
    /// and it is a ceiling rather than a target: the renderer reads the result back with this
    /// package's own decoder and refuses a design that does not come out. The shares that read back
    /// in practice are lower; Spec 20 §11 carries the measured table.
    /// </remarks>
    public double Fraction { get; init; }

    /// <summary>
    /// The colour the region is filled with, or <see langword="null"/> for the background colour.
    /// </summary>
    public BarcodeColor? Fill { get; init; }

    /// <summary>A square cleared to the background colour.</summary>
    /// <param name="fraction">The share of the symbol's area, in <c>(0, 1]</c>.</param>
    /// <returns>The reserved centre.</returns>
    public static BarcodeLogo Cleared(double fraction) => new() { Fraction = fraction };

    /// <summary>A square of one flat colour, for a brand plate or a placeholder.</summary>
    /// <param name="fraction">The share of the symbol's area, in <c>(0, 1]</c>.</param>
    /// <param name="fill">The colour to fill it with.</param>
    /// <returns>The reserved centre.</returns>
    public static BarcodeLogo Solid(double fraction, BarcodeColor fill)
        => new() { Fraction = fraction, Fill = fill };

    /// <summary>Checks the share before the native call, so the message names the option.</summary>
    /// <exception cref="BarcodeException">The share is not a number in <c>(0, 1]</c>.</exception>
    internal void Validate()
    {
        if (!double.IsFinite(Fraction) || Fraction <= 0 || Fraction > 1)
        {
            throw new BarcodeException(
                BarcodeErrorCode.InvalidArgument,
                $"BarcodeLogo.Fraction is a share of the symbol between 0 and 1, got {Fraction}");
        }
    }
}
