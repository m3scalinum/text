using System;

namespace M3scalinum.Text.SharpTypst.Documents;

/// <summary>A stroke: a thickness, optionally with a paint.</summary>
public readonly record struct TypstStroke
{
    private readonly string _value;

    private TypstStroke(string value) => _value = value;

    /// <summary>No stroke.</summary>
    public static TypstStroke None => new("none");

    /// <summary>A solid stroke of the given thickness, in black unless a paint is given.</summary>
    public static TypstStroke Solid(TypstLength thickness, ITypstPaint? paint = null)
        => paint is null
            ? new TypstStroke(thickness.ToString())
            : new TypstStroke($"{thickness} + {paint.ToTypst()}");

    /// <summary>A stroke with an explicit paint.</summary>
    public static TypstStroke Of(TypstLength thickness, ITypstPaint paint)
    {
        ArgumentNullException.ThrowIfNull(paint);
        return new TypstStroke($"{thickness} + {paint.ToTypst()}");
    }

    /// <summary>Emits the Typst expression for this stroke.</summary>
    public string ToTypst() => _value;

    /// <inheritdoc />
    public override string ToString() => _value;
}