using System;
using System.Globalization;

namespace M3scalinum.Text.SharpTypst.Documents;

/// <summary>An angle.</summary>
public readonly record struct TypstAngle
{
    private readonly double _value;
    private readonly string _unit;

    private TypstAngle(double value, string unit)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "an angle must be finite");
        }

        _value = value;
        _unit = unit;
    }

    /// <summary>Degrees.</summary>
    public static TypstAngle Degrees(double value) => new(value, "deg");

    /// <summary>Radians.</summary>
    public static TypstAngle Radians(double value) => new(value, "rad");

    /// <summary>Turns, where 1 is a full circle.</summary>
    public static TypstAngle Turns(double value) => new(value, "turn");

    /// <summary>Zero degrees.</summary>
    public static TypstAngle Zero => Degrees(0);

    /// <inheritdoc />
    public override string ToString()
        => _value.ToString("0.###", CultureInfo.InvariantCulture) + _unit;
}