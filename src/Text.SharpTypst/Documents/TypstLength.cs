using System;
using System.Globalization;

namespace M3scalinum.Text.SharpTypst.Documents;

/// <summary>A Typst length: a number with a unit.</summary>
public readonly record struct TypstLength
{
    private readonly double _value;
    private readonly string _unit;

    private TypstLength(double value, string unit)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "a length must be finite");
        }

        _value = value;
        _unit = unit;
    }

    /// <summary>Centimetres.</summary>
    public static TypstLength Cm(double value) => new(value, "cm");

    /// <summary>Millimetres.</summary>
    public static TypstLength Mm(double value) => new(value, "mm");

    /// <summary>Points (1/72 inch).</summary>
    public static TypstLength Pt(double value) => new(value, "pt");

    /// <summary>Ems, relative to the current font size.</summary>
    public static TypstLength Em(double value) => new(value, "em");

    /// <summary>A percentage of the available space.</summary>
    public static TypstLength Percent(double value) => new(value, "%");

    /// <summary>A fraction of the remaining space.</summary>
    public static TypstLength Fr(double value) => new(value, "fr");

    /// <summary>Zero points.</summary>
    public static TypstLength Zero => new(0, "pt");

    /// <summary>Formats the length with the invariant culture, trailing zeros trimmed.</summary>
    public override string ToString()
        => _value.ToString("0.###", CultureInfo.InvariantCulture) + _unit;
}
