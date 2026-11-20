using System;

namespace M3scalinum.Text.SharpTypst.Documents;

/// <summary>A tiling pattern, usable wherever a paint is expected.</summary>
public sealed class TypstTiling : ITypstPaint
{
    private readonly string _value;

    private TypstTiling(string value) => _value = value;

    /// <summary>A tiling of the given cell size, filled with Typst content.</summary>
    public static TypstTiling Of(TypstLength width, TypstLength height, string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return new TypstTiling($"tiling(size: ({width}, {height}))[{body}]");
    }

    /// <inheritdoc />
    public string ToTypst() => _value;

    /// <inheritdoc />
    public override string ToString() => _value;
}