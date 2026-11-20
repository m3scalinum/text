using System;
using System.Globalization;

namespace M3scalinum.Text.SharpTypst.Documents;

/// <summary>A gradient, usable wherever a paint is expected.</summary>
public sealed class TypstGradient : ITypstPaint
{
    private readonly string _value;

    private TypstGradient(string value) => _value = value;

    /// <summary>A linear gradient at an angle, with evenly spaced stops.</summary>
    public static TypstGradient Linear(double angleDegrees, params TypstColor[] stops)
    {
        ArgumentNullException.ThrowIfNull(stops);
        if (stops.Length < 2)
        {
            throw new ArgumentException("a gradient needs at least two stops", nameof(stops));
        }

        var list = string.Join(", ", Array.ConvertAll(stops, stop => stop.ToTypst()));
        return new TypstGradient(
            $"gradient.linear({list}, angle: {angleDegrees.ToString("0.###", CultureInfo.InvariantCulture)}deg)");
    }

    /// <summary>A radial gradient centred on the shape.</summary>
    public static TypstGradient Radial(params TypstColor[] stops)
    {
        ArgumentNullException.ThrowIfNull(stops);
        if (stops.Length < 2)
        {
            throw new ArgumentException("a gradient needs at least two stops", nameof(stops));
        }

        var list = string.Join(", ", Array.ConvertAll(stops, stop => stop.ToTypst()));
        return new TypstGradient($"gradient.radial({list})");
    }

    /// <inheritdoc />
    public string ToTypst() => _value;

    /// <inheritdoc />
    public override string ToString() => _value;
}