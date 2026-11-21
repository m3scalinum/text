using System;

namespace M3scalinum.Text.Barcodes.Tests;

/// <summary>Reads the two fields of a PNG header a test cares about.</summary>
/// <remarks>
/// Ten bytes of parsing instead of an image library: the assertion is that the encoder wrote the
/// size it promised, and IHDR is where a PNG states it.
/// </remarks>
internal static class PngHeader
{
    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static bool IsPng(ReadOnlySpan<byte> png)
        => png.Length >= 24 && png[..8].SequenceEqual(Signature);

    public static int Width(byte[] png)
    {
        Ensure(png);
        return (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
    }

    public static int Height(byte[] png)
    {
        Ensure(png);
        return (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
    }

    /// <summary>The bit depth and colour type of the IHDR chunk: 0 is greyscale, 8 bits is 8-bit.</summary>
    public static (byte BitDepth, byte ColourType) Format(byte[] png)
    {
        Ensure(png);
        return (png[24], png[25]);
    }

    private static void Ensure(byte[] png)
    {
        if (!IsPng(png))
        {
            throw new ArgumentException("the bytes are not a PNG", nameof(png));
        }
    }
}
