using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// Builds PNG and JPEG bytes for the appearance tests, and knows what the PNG decoder should make of
/// them, so a test can compare the embedded samples instead of trusting the writer.
/// </summary>
internal static class TestImages
{
    /// <summary>A solid RGB PNG.</summary>
    public static byte[] Rgb(int width, int height, byte red, byte green, byte blue)
        => Png(width, height, colorType: 2, Samples(width, height, 3, [red, green, blue]));

    /// <summary>An RGB PNG whose rows each use a different filter, to exercise the decoder.</summary>
    public static byte[] Filtered(int width, int height)
        => Png(
            width,
            height,
            colorType: 2,
            Gradient(width, height),
            filters: [0, 1, 2, 3, 4, 1, 4, 2, 3]);

    /// <summary>A grey PNG.</summary>
    public static byte[] Gray(int width, int height, byte value)
        => Png(width, height, colorType: 0, Samples(width, height, 1, [value]));

    /// <summary>An RGBA PNG, whose alpha channel should become a soft mask.</summary>
    public static byte[] Rgba(int width, int height, byte alpha)
        => Png(width, height, colorType: 6, Samples(width, height, 4, [10, 20, 30, alpha]));

    /// <summary>An indexed PNG with a three-entry palette.</summary>
    public static byte[] Indexed(int width, int height)
    {
        var samples = Samples(width, height, 1, [1]);
        return Png(width, height, colorType: 3, samples, palette: [255, 0, 0, 0, 255, 0, 0, 0, 255]);
    }

    /// <summary>The RGB samples an <see cref="Indexed"/> image expands to.</summary>
    public static byte[] IndexedExpanded(int width, int height)
    {
        var samples = Samples(width, height, 1, [1]);
        var expanded = new byte[samples.Length * 3];

        for (var index = 0; index < samples.Length; index++)
        {
            var entry = samples[index] * 3;
            expanded[index * 3] = (byte)(entry == 3 ? 0 : 255);
            expanded[(index * 3) + 1] = (byte)(entry == 3 ? 255 : 0);
            expanded[(index * 3) + 2] = 0;
        }

        return expanded;
    }

    /// <summary>The samples of a <see cref="Filtered"/> image, as the decoder should produce them.</summary>
    public static byte[] Gradient(int width, int height)
    {
        var samples = new byte[width * height * 3];

        for (var index = 0; index < width * height; index++)
        {
            samples[index * 3] = (byte)(index * 7 % 256);
            samples[(index * 3) + 1] = (byte)(index * 13 % 256);
            samples[(index * 3) + 2] = (byte)(index * 29 % 256);
        }

        return samples;
    }

    /// <summary>A PNG with a header and an image data chunk, computing real CRC-32 values.</summary>
    public static byte[] Png(
        int width,
        int height,
        byte colorType,
        byte[] samples,
        byte[]? palette = null,
        int bitDepth = 8,
        int interlace = 0,
        IReadOnlyList<int>? filters = null,
        int? declaredHeight = null)
    {
        var channels = colorType switch
        {
            0 => 1,
            2 => 3,
            3 => 1,
            6 => 4,
            _ => 1,
        };

        var stride = width * channels;
        var rows = new List<byte[]>();

        for (var row = 0; row < height; row++)
        {
            var filter = filters is null ? 0 : filters[row % filters.Count];
            var line = new byte[stride + 1];
            line[0] = (byte)filter;

            for (var column = 0; column < stride; column++)
            {
                var value = At(samples, (row * stride) + column);
                var left = column >= channels ? At(samples, (row * stride) + column - channels) : (byte)0;
                var above = row > 0 ? At(samples, ((row - 1) * stride) + column) : (byte)0;
                var upperLeft = row > 0 && column >= channels ? At(samples, ((row - 1) * stride) + column - channels) : (byte)0;

                var prediction = filter switch
                {
                    0 => 0,
                    1 => left,
                    2 => above,
                    3 => (left + above) / 2,
                    4 => Paeth(left, above, upperLeft),
                    _ => 0,
                };

                line[column + 1] = (byte)(value - prediction);
            }

            rows.Add(line);
        }

        using var compressed = new MemoryStream();
        using (var deflate = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            foreach (var row in rows)
            {
                deflate.Write(row);
            }
        }

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), declaredHeight ?? height);
        header[8] = (byte)bitDepth;
        header[9] = colorType;
        header[12] = (byte)interlace;
        WriteChunk(png, "IHDR", header);

        if (palette is not null)
        {
            WriteChunk(png, "PLTE", palette);
        }

        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);

        return png.ToArray();
    }

    /// <summary>
    /// A JPEG header with a frame, enough for a reader that embeds the file as it is: this library
    /// never decodes JPEG samples, it copies them.
    /// </summary>
    public static byte[] Jpeg(int width, int height, int components = 3)
    {
        var length = 8 + (3 * components);
        var frame = new List<byte> { 0xFF, 0xC0, (byte)(length >> 8), (byte)(length & 0xFF), 0x08 };
        frame.Add((byte)(height >> 8));
        frame.Add((byte)(height & 0xFF));
        frame.Add((byte)(width >> 8));
        frame.Add((byte)(width & 0xFF));
        frame.Add((byte)components);

        for (var component = 1; component <= components; component++)
        {
            frame.Add((byte)component);
            frame.Add(0x11);
            frame.Add(0x00);
        }

        var jpeg = new List<byte> { 0xFF, 0xD8 };
        jpeg.AddRange(frame);
        jpeg.AddRange([0xFF, 0xD9]);

        return jpeg.ToArray();
    }

    private static byte At(byte[] samples, int index)
        => index < samples.Length ? samples[index] : (byte)0;

    private static byte[] Samples(int width, int height, int channels, byte[] pixel)
    {
        var samples = new byte[width * height * channels];

        for (var index = 0; index < width * height; index++)
        {
            Array.Copy(pixel, 0, samples, index * channels, Math.Min(channels, pixel.Length));
        }

        return samples;
    }

    private static int Paeth(int left, int above, int upperLeft)
    {
        var estimate = left + above - upperLeft;
        var toLeft = Math.Abs(estimate - left);
        var toAbove = Math.Abs(estimate - above);
        var toUpperLeft = Math.Abs(estimate - upperLeft);

        if (toLeft <= toAbove && toLeft <= toUpperLeft)
        {
            return left;
        }

        return toAbove <= toUpperLeft ? above : upperLeft;
    }

    private static void WriteChunk(Stream png, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        png.Write(length);

        var name = Encoding.ASCII.GetBytes(type);
        png.Write(name);
        png.Write(data);

        var crcInput = new byte[name.Length + data.Length];
        name.CopyTo(crcInput, 0);
        data.CopyTo(crcInput, name.Length);

        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(crcInput));
        png.Write(crc);
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;

        foreach (var value in data)
        {
            crc ^= value;

            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
