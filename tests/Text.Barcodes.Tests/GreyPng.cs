using System;
using System.IO;
using System.IO.Compression;

namespace M3scalinum.Text.Barcodes.Tests;

/// <summary>An 8-bit greyscale image, the encoding both the package and this helper write.</summary>
internal sealed class GreyImage
{
    public GreyImage(int width, int height)
    {
        Width = width;
        Height = height;
        Pixels = new byte[width * height];
        Array.Fill(Pixels, (byte)0xFF);
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>One byte per pixel, row-major, 0 black and 255 white.</summary>
    public byte[] Pixels { get; }

    /// <summary>Copies <paramref name="source"/> onto this image with its top-left corner at x,y.</summary>
    public void Paste(GreyImage source, int x, int y)
    {
        for (var row = 0; row < source.Height; row++)
        {
            Array.Copy(
                source.Pixels,
                row * source.Width,
                Pixels,
                (y + row) * Width + x,
                source.Width);
        }
    }
}

/// <summary>
/// Reads and writes 8-bit greyscale PNGs, so a test can put two symbols on one page.
/// </summary>
/// <remarks>
/// The package's own raster is the only PNG this reads: 8 bits per channel, colour type 0, no
/// interlacing. Writing uses filter 0 on every row and a real zlib stream, which every decoder —
/// including the one the package carries — accepts. A committed fixture would hide what these
/// tests build, and a bitmap library would add a dependency for a hundred lines.
/// </remarks>
internal static class GreyPng
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Decodes a PNG the package wrote.</summary>
    public static GreyImage Decode(byte[] png)
    {
        if (!png.AsSpan(0, 8).SequenceEqual(Signature))
        {
            throw new InvalidDataException("not a PNG");
        }

        var offset = 8;
        var width = 0;
        var height = 0;
        using var compressed = new MemoryStream();

        while (offset + 8 <= png.Length)
        {
            var length = (int)ReadBigEndian(png, offset);
            var type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            var data = png.AsSpan(offset + 8, length);

            switch (type)
            {
                case "IHDR":
                    width = (int)ReadBigEndian(png, offset + 8);
                    height = (int)ReadBigEndian(png, offset + 12);
                    if (png[offset + 16] != 8 || png[offset + 17] != 0)
                    {
                        throw new InvalidDataException(
                            $"only 8-bit greyscale is read, got depth {png[offset + 16]} colour type {png[offset + 17]}");
                    }

                    break;
                case "IDAT":
                    compressed.Write(data);
                    break;
                case "IEND":
                    offset = png.Length;
                    continue;
            }

            offset += 12 + length;
        }

        compressed.Position = 0;
        var raw = Inflate(compressed.ToArray(), (width + 1) * height);

        // One byte per pixel means the filter stride is one byte, so a filter reduces to the byte
        // itself, the one to its left and the one above it.
        var image = new GreyImage(width, height);

        for (var row = 0; row < height; row++)
        {
            var filter = raw[row * (width + 1)];
            var line = raw.AsSpan(row * (width + 1) + 1, width);

            for (var column = 0; column < width; column++)
            {
                var left = column == 0 ? (byte)0 : image.Pixels[row * width + column - 1];
                var up = row == 0 ? (byte)0 : image.Pixels[(row - 1) * width + column];
                var upLeft = row == 0 || column == 0
                    ? (byte)0
                    : image.Pixels[(row - 1) * width + column - 1];

                var value = filter switch
                {
                    0 => line[column],
                    1 => (byte)(line[column] + left),
                    2 => (byte)(line[column] + up),
                    3 => (byte)(line[column] + (left + up) / 2),
                    4 => (byte)(line[column] + Paeth(left, up, upLeft)),
                    _ => throw new InvalidDataException($"unknown filter {filter}"),
                };

                image.Pixels[row * width + column] = value;
            }
        }

        return image;
    }

    /// <summary>Encodes an image as an 8-bit greyscale PNG with filter 0 on every row.</summary>
    public static byte[] Encode(GreyImage image)
    {
        using var png = new MemoryStream();
        png.Write(Signature);

        var header = new byte[13];
        WriteBigEndian(header, 0, (uint)image.Width);
        WriteBigEndian(header, 4, (uint)image.Height);
        header[8] = 8;  // bit depth
        header[9] = 0;  // colour type: greyscale
        WriteChunk(png, "IHDR", header);

        var raw = new byte[(image.Width + 1) * image.Height];
        for (var row = 0; row < image.Height; row++)
        {
            raw[row * (image.Width + 1)] = 0; // filter: none
            Array.Copy(
                image.Pixels,
                row * image.Width,
                raw,
                row * (image.Width + 1) + 1,
                image.Width);
        }

        WriteChunk(png, "IDAT", Deflate(raw));
        WriteChunk(png, "IEND", []);

        return png.ToArray();
    }

    private static byte Paeth(byte left, byte up, byte upLeft)
    {
        var estimate = left + up - upLeft;
        var toLeft = Math.Abs(estimate - left);
        var toUp = Math.Abs(estimate - up);
        var toUpLeft = Math.Abs(estimate - upLeft);

        if (toLeft <= toUp && toLeft <= toUpLeft)
        {
            return left;
        }

        return toUp <= toUpLeft ? up : upLeft;
    }

    private static byte[] Inflate(byte[] data, int expected)
    {
        using var source = new MemoryStream(data);
        using var zlib = new ZLibStream(source, CompressionMode.Decompress);

        var buffer = new byte[expected];
        var read = 0;
        while (read < expected)
        {
            var chunk = zlib.Read(buffer, read, expected - read);
            if (chunk == 0)
            {
                throw new InvalidDataException(
                    $"the image data stopped at {read} of {expected} bytes");
            }

            read += chunk;
        }

        return buffer;
    }

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }

    private static uint ReadBigEndian(byte[] data, int offset)
        => ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16)
            | ((uint)data[offset + 2] << 8) | data[offset + 3];

    private static void WriteBigEndian(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)(value >> 24);
        data[offset + 1] = (byte)(value >> 16);
        data[offset + 2] = (byte)(value >> 8);
        data[offset + 3] = (byte)value;
    }

    private static void WriteChunk(Stream png, string type, byte[] data)
    {
        var header = new byte[4];
        WriteBigEndian(header, 0, (uint)data.Length);
        png.Write(header);

        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        png.Write(typeBytes);
        png.Write(data);

        var crc = Crc32(typeBytes, data);
        var trailer = new byte[4];
        WriteBigEndian(trailer, 0, crc);
        png.Write(trailer);
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        var crc = 0xFFFF_FFFFu;

        foreach (var value in type)
        {
            crc = Step(crc, value);
        }

        foreach (var value in data)
        {
            crc = Step(crc, value);
        }

        return crc ^ 0xFFFF_FFFFu;
    }

    private static uint Step(uint crc, byte value)
    {
        crc ^= value;
        for (var bit = 0; bit < 8; bit++)
        {
            crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB8_8320u : crc >> 1;
        }

        return crc;
    }
}
