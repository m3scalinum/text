using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace M3scalinum.Text.Barcodes.Internal;

/// <summary>An 8-bit greyscale image, ready to be an image XObject.</summary>
/// <param name="Pixels">One byte per pixel, row-major from the top-left corner.</param>
/// <param name="Width">The width in pixels.</param>
/// <param name="Height">The height in pixels.</param>
internal readonly record struct GreyscaleRaster(byte[] Pixels, int Width, int Height);

/// <summary>
/// Reads back the PNG this package's own writer produced: 8-bit greyscale, not interlaced.
/// </summary>
/// <remarks>
/// <para>
/// A label has to place the symbol at a physical size, and a PDF places an <em>image</em>: the
/// pixels, not the file that carries them. The raster this package writes is the one format it also
/// reads, so the decoder is 60 lines of the PNG specification instead of an imaging dependency in a
/// leaf package — the same trade the test helper makes for its fixtures.
/// </para>
/// <para>
/// Anything else is refused by name: a colour PNG (a styled symbol), 16 bits per channel, a
/// palette, interlacing. The label draws a symbol in black on white; a caller who wants colour wants
/// a document, not a label.
/// </para>
/// </remarks>
internal static class PngGreyscale
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Decodes an 8-bit greyscale, non-interlaced PNG.</summary>
    /// <exception cref="BarcodeException">The bytes are not such a PNG.</exception>
    public static GreyscaleRaster Decode(byte[] png)
    {
        if (png.Length < Signature.Length + 8
            || !png.AsSpan(0, Signature.Length).SequenceEqual(Signature))
        {
            throw new BarcodeException(BarcodeErrorCode.Decode, "the label's symbol is not a PNG");
        }

        var offset = Signature.Length;
        var width = 0;
        var height = 0;

        using var compressed = new MemoryStream();

        while (offset + 8 <= png.Length)
        {
            var length = ReadBigEndian(png, offset);
            var type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            var data = offset + 8;

            if (length < 0 || data + length + 4 > png.Length)
            {
                throw new BarcodeException(
                    BarcodeErrorCode.Decode, $"the PNG's {type} chunk runs past its end");
            }

            switch (type)
            {
                case "IHDR":
                    width = ReadBigEndian(png, data);
                    height = ReadBigEndian(png, data + 4);

                    if (png[data + 8] != 8)
                    {
                        throw new BarcodeException(
                            BarcodeErrorCode.Decode,
                            $"the label's symbol is {png[data + 8]} bits per channel; it must be 8");
                    }

                    if (png[data + 9] != 0)
                    {
                        throw new BarcodeException(
                            BarcodeErrorCode.Decode,
                            $"the label's symbol is in colour model {png[data + 9]}; it must be greyscale");
                    }

                    if (png[data + 12] != 0)
                    {
                        throw new BarcodeException(
                            BarcodeErrorCode.Decode,
                            "an interlaced PNG cannot be a label's symbol");
                    }

                    break;

                case "IDAT":
                    compressed.Write(png, data, length);
                    break;

                case "IEND":
                    offset = png.Length;
                    continue;
            }

            offset = data + length + 4;
        }

        if (width <= 0 || height <= 0 || compressed.Length == 0)
        {
            throw new BarcodeException(
                BarcodeErrorCode.Decode, "the PNG holds no image data");
        }

        var stride = width + 1;
        var filtered = new byte[(long)stride * height];

        compressed.Position = 0;
        using (var inflate = new ZLibStream(compressed, CompressionMode.Decompress, leaveOpen: true))
        {
            var read = 0;
            while (read < filtered.Length)
            {
                var count = inflate.Read(filtered, read, filtered.Length - read);

                if (count == 0)
                {
                    break;
                }

                read += count;
            }

            if (read != filtered.Length)
            {
                throw new BarcodeException(
                    BarcodeErrorCode.Decode,
                    $"the PNG holds {read} bytes of scanlines, its header promises {filtered.Length}");
            }
        }

        return new GreyscaleRaster(Unfilter(filtered, width, height), width, height);
    }

    /// <summary>Reverses the per-row filters of the PNG specification, §9.</summary>
    private static byte[] Unfilter(byte[] filtered, int width, int height)
    {
        var pixels = new byte[(long)width * height];
        var stride = width + 1;
        var previous = new byte[width];

        for (var row = 0; row < height; row++)
        {
            var filter = filtered[row * stride];
            var source = row * stride + 1;
            var target = row * width;
            var current = new byte[width];

            for (var column = 0; column < width; column++)
            {
                int raw = filtered[source + column];
                int left = column > 0 ? current[column - 1] : 0;
                int up = previous[column];
                int upLeft = column > 0 ? previous[column - 1] : 0;

                current[column] = filter switch
                {
                    0 => (byte)raw,
                    1 => (byte)(raw + left),
                    2 => (byte)(raw + up),
                    3 => (byte)(raw + ((left + up) / 2)),
                    // Paeth: the neighbour closest to left + up - up-left.
                    4 => (byte)(raw + Paeth(left, up, upLeft)),
                    _ => throw new BarcodeException(
                        BarcodeErrorCode.Decode,
                        $"the PNG uses row filter {filter}, which the specification does not define"),
                };
            }

            Array.Copy(current, 0, pixels, target, width);
            previous = current;
        }

        return pixels;
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        var estimate = left + up - upLeft;
        var toLeft = Math.Abs(estimate - left);
        var toUp = Math.Abs(estimate - up);
        var toUpLeft = Math.Abs(estimate - upLeft);

        return toLeft <= toUp && toLeft <= toUpLeft ? left : toUp <= toUpLeft ? up : upLeft;
    }

    private static int ReadBigEndian(byte[] data, int offset)
        => (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
}
