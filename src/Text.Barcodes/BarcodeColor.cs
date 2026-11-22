using System;
using System.Globalization;

namespace M3scalinum.Text.Barcodes;

/// <summary>
/// A colour, 8 bits per channel, sRGB.
/// </summary>
/// <remarks>
/// <para>
/// sRGB is the space the contrast ratio is defined in, so no conversion happens anywhere: the bytes
/// here are the bytes in the raster, and the ratio <see cref="Barcode.ContrastRatio"/> measures is
/// computed from them by the definition in WCAG 2.1 rather than by an approximation of it.
/// </para>
/// <para>
/// A QR Code is dark modules on a light ground, so <see cref="BarcodeOptions.Foreground"/> — the
/// module — must stay darker than <see cref="BarcodeOptions.Background"/>, and the pair must clear
/// the contrast floor. <see cref="Barcode.Encode"/> refuses a pair that does not.
/// </para>
/// </remarks>
public readonly record struct BarcodeColor
{
    /// <summary>Creates a colour from its three channels.</summary>
    /// <param name="r">The red channel.</param>
    /// <param name="g">The green channel.</param>
    /// <param name="b">The blue channel.</param>
    public BarcodeColor(byte r, byte g, byte b)
    {
        R = r;
        G = g;
        B = b;
    }

    /// <summary>The red channel.</summary>
    public byte R { get; }

    /// <summary>The green channel.</summary>
    public byte G { get; }

    /// <summary>The blue channel.</summary>
    public byte B { get; }

    /// <summary>Black, the default module colour.</summary>
    public static BarcodeColor Black => new(0, 0, 0);

    /// <summary>White, the default background colour.</summary>
    public static BarcodeColor White => new(255, 255, 255);

    /// <summary>Reads a colour from six hexadecimal digits, with or without a leading <c>#</c>.</summary>
    /// <param name="hex">The text to parse, for example <c>#1f3a93</c>.</param>
    /// <returns>The colour.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="hex"/> is null.</exception>
    /// <exception cref="FormatException"><paramref name="hex"/> is not six hexadecimal digits.</exception>
    public static BarcodeColor FromHex(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);

        var digits = hex.StartsWith('#') ? hex[1..] : hex;
        if (digits.Length != 6
            || !byte.TryParse(digits[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            || !byte.TryParse(digits[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            || !byte.TryParse(digits[4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            throw new FormatException(
                $"a colour is six hexadecimal digits, for example #1f3a93; got '{hex}'");
        }

        return new BarcodeColor(r, g, b);
    }

    /// <summary>The colour as <c>#rrggbb</c>, lowercase, as CSS and SVG spell it.</summary>
    /// <returns>The colour in hexadecimal.</returns>
    public string ToHex() => string.Create(
        CultureInfo.InvariantCulture, $"#{R:x2}{G:x2}{B:x2}");

    /// <summary>The colour as the <c>0xRRGGBB</c> integer the native library takes.</summary>
    internal int ToPacked() => (R << 16) | (G << 8) | B;

    /// <summary>The WCAG 2.1 relative luminance of the colour, in <c>[0, 1]</c>.</summary>
    /// <returns>The luminance.</returns>
    /// <remarks>
    /// The definition, not an approximation: each channel is linearised with the 0.03928 threshold,
    /// then weighted 0.2126, 0.7152 and 0.0722 — the ITU-R BT.709 coefficients WCAG 2.1 states.
    /// </remarks>
    public double RelativeLuminance() => 0.2126 * Linear(R)
        + 0.7152 * Linear(G)
        + 0.0722 * Linear(B);

    private static double Linear(byte channel)
    {
        var value = channel / 255.0;
        return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
