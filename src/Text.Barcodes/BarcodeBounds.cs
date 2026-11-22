namespace M3scalinum.Text.Barcodes;

/// <summary>
/// The axis-aligned box enclosing the points the detector reported for one symbol, in pixels of the
/// image that was handed to <see cref="Barcode.DecodeAll"/>.
/// </summary>
/// <remarks>
/// The box is the extent of <see cref="DecodeResult.Points"/> and nothing more: a quiet zone around
/// the symbol is not included, because whether one exists is the image's business. A symbology whose
/// detector reports no points leaves <see cref="DecodeResult.Bounds"/> null.
/// </remarks>
/// <param name="X">Horizontal position of the left edge, in pixels.</param>
/// <param name="Y">Vertical position of the top edge, in pixels.</param>
/// <param name="Width">Width of the box, in pixels.</param>
/// <param name="Height">Height of the box, in pixels.</param>
public readonly record struct BarcodeBounds(float X, float Y, float Width, float Height)
{
    /// <summary>The right edge, <see cref="X"/> plus <see cref="Width"/>.</summary>
    public float Right => X + Width;

    /// <summary>The bottom edge, <see cref="Y"/> plus <see cref="Height"/>.</summary>
    public float Bottom => Y + Height;

    /// <summary>A readable rendering, for logs and for a failing assertion.</summary>
    /// <returns>The box as <c>x,y width×height</c>.</returns>
    public override string ToString() => $"{X},{Y} {Width}x{Height}";
}
