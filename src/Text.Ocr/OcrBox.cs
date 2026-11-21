using System;

namespace M3scalinum.Text.Ocr;

/// <summary>
/// A rectangle in image pixels, with the origin at the top-left corner.
/// </summary>
public sealed class OcrBox
{
    internal OcrBox(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>The left edge, in pixels.</summary>
    public int X { get; }

    /// <summary>The top edge, in pixels.</summary>
    public int Y { get; }

    /// <summary>The width, in pixels.</summary>
    public int Width { get; }

    /// <summary>The height, in pixels.</summary>
    public int Height { get; }

    /// <inheritdoc />
    public override string ToString() => $"{Width}x{Height} at ({X}, {Y})";
}
