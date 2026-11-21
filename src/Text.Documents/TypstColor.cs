using System;
using System.Collections.Generic;
using System.Globalization;

namespace M3scalinum.Text.Documents;

/// <summary>A colour.</summary>
public readonly record struct TypstColor : ITypstPaint
{
    private readonly string _value;

    private TypstColor(string value) => _value = value;

    /// <summary>Black.</summary>
    public static TypstColor Black => Named("black");

    /// <summary>White.</summary>
    public static TypstColor White => Named("white");

    /// <summary>Mid grey.</summary>
    public static TypstColor Gray => Named("gray");

    /// <summary>
    /// A colour from Typst's named palette. Only the names Typst defines are accepted, so a
    /// typo fails here instead of at compile time.
    /// </summary>
    public static TypstColor Named(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!NamedColors.Contains(name))
        {
            throw new ArgumentException(
                $"'{name}' is not a Typst colour name; use Hex or Rgb for other colours",
                nameof(name));
        }

        return new TypstColor(name);
    }

    private static readonly HashSet<string> NamedColors = new(StringComparer.Ordinal)
    {
        "black", "gray", "silver", "white",
        "navy", "blue", "aqua", "teal", "eastern", "purple", "fuchsia",
        "maroon", "red", "orange", "yellow", "olive", "green", "lime",
    };

    /// <summary>An opaque RGB colour.</summary>
    public static TypstColor Rgb(byte red, byte green, byte blue)
        => new($"rgb({red}, {green}, {blue})");

    /// <summary>An RGB colour with an alpha channel between 0 and 1.</summary>
    public static TypstColor Rgba(byte red, byte green, byte blue, double alpha)
    {
        if (alpha is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(alpha), alpha, "alpha is between 0 and 1");
        }

        return new TypstColor(
            $"rgb({red}, {green}, {blue}, {alpha.ToString("0.###", CultureInfo.InvariantCulture)})");
    }

    /// <summary>A colour from a hex string, with or without the leading <c>#</c>.</summary>
    public static TypstColor Hex(string hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hex);
        var digits = hex.StartsWith('#') ? hex[1..] : hex;

        if (digits.Length is not (3 or 6 or 8) || !IsHex(digits))
        {
            throw new ArgumentException($"'{hex}' is not a 3, 6 or 8 digit hex colour", nameof(hex));
        }

        return new TypstColor($"rgb(\"#{digits}\")");
    }

    /// <summary>A greyscale colour, 0 is black and 1 is white.</summary>
    public static TypstColor Luma(double value)
    {
        if (value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "luma is between 0 and 1");
        }

        return new TypstColor($"luma({value.ToString("0.###", CultureInfo.InvariantCulture)})");
    }

    /// <inheritdoc />
    public string ToTypst() => _value;

    /// <inheritdoc />
    public override string ToString() => _value;

    private static bool IsHex(string text)
    {
        foreach (var c in text)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}