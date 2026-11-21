using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace M3scalinum.Text.Pdf;

/// <summary>An image, decoded enough to be embedded as a PDF image XObject.</summary>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="ColorSpace">The PDF colour space name, e.g. <c>DeviceRGB</c>.</param>
/// <param name="Filter">The PDF filter that reads <paramref name="Data"/>, e.g. <c>FlateDecode</c>.</param>
/// <param name="Data">The bytes an image XObject carries.</param>
/// <param name="SoftMask">The alpha channel of an RGBA PNG, as a greyscale image, or <see langword="null"/>.</param>
internal sealed record PdfImageInfo(
    int Width,
    int Height,
    string ColorSpace,
    string Filter,
    ReadOnlyMemory<byte> Data,
    PdfImageInfo? SoftMask = null);

/// <summary>
/// Reads the header of a PNG or a JPEG, and expands the PNG into raw samples so it can be written
/// with <c>/FlateDecode</c>. A JPEG is embedded as it is, with <c>/DCTDecode</c>: its samples stay
/// compressed and are never touched.
/// </summary>
internal static class PdfImage
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Reads <paramref name="bytes"/> as a PNG or a JPEG.</summary>
    /// <exception cref="PdfSignatureException">The bytes are neither, or the PNG is beyond this reader.</exception>
    public static PdfImageInfo Parse(ReadOnlyMemory<byte> bytes)
    {
        var span = bytes.Span;

        if (span.Length >= PngSignature.Length && span[..PngSignature.Length].SequenceEqual(PngSignature))
        {
            return ParsePng(bytes);
        }

        if (span.Length >= 4 && span[0] == 0xFF && span[1] == 0xD8)
        {
            return ParseJpeg(bytes);
        }

        throw new PdfSignatureException(
            "the appearance image is neither a PNG nor a JPEG; supply the file bytes as they are");
    }

    private static PdfImageInfo ParsePng(ReadOnlyMemory<byte> bytes)
    {
        var span = bytes.Span;
        var offset = PngSignature.Length;

        var width = 0;
        var height = 0;
        var bitDepth = 0;
        var colorType = 0;
        var interlace = 0;
        byte[]? palette = null;
        var idat = new MemoryStream();

        while (offset + 8 <= span.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(span.Slice(offset, 4));
            var type = System.Text.Encoding.ASCII.GetString(span.Slice(offset + 4, 4).ToArray());

            if (length < 0 || offset + 12 + length > span.Length)
            {
                throw new PdfSignatureException("the PNG is truncated: a chunk runs past the end of the file");
            }

            var data = span.Slice(offset + 8, length);

            switch (type)
            {
                case "IHDR":
                    width = BinaryPrimitives.ReadInt32BigEndian(data[..4]);
                    height = BinaryPrimitives.ReadInt32BigEndian(data.Slice(4, 4));
                    bitDepth = data[8];
                    colorType = data[9];
                    interlace = data[12];
                    break;

                case "PLTE":
                    palette = data.ToArray();
                    break;

                case "IDAT":
                    idat.Write(data);
                    break;

                default:
                    break;
            }

            offset += 12 + length;

            if (type == "IEND")
            {
                break;
            }
        }

        if (width <= 0 || height <= 0)
        {
            throw new PdfSignatureException("the PNG carries no image header");
        }

        if (bitDepth != 8)
        {
            throw new PdfSignatureException($"the PNG has a bit depth of {bitDepth}; only 8-bit PNGs are supported");
        }

        if (interlace != 0)
        {
            throw new PdfSignatureException("interlaced PNGs are not supported; save the image without interlacing");
        }

        if (colorType is not (0 or 2 or 3 or 6))
        {
            throw new PdfSignatureException($"the PNG has colour type {colorType}, which is not supported");
        }

        if (colorType == 3 && palette is null)
        {
            throw new PdfSignatureException("the indexed PNG carries no palette");
        }

        var channels = colorType switch
        {
            0 => 1,
            2 => 3,
            3 => 1,
            6 => 4,
            _ => throw new PdfSignatureException("the PNG has an unsupported colour type"),
        };

        idat.Position = 0;
        byte[] samples;
        using (var inflate = new ZLibStream(idat, CompressionMode.Decompress, leaveOpen: true))
        {
            using var raw = new MemoryStream();
            inflate.CopyTo(raw);
            samples = Unfilter(raw.ToArray(), width, height, channels);
        }

        return colorType switch
        {
            0 => new PdfImageInfo(width, height, "DeviceGray", "FlateDecode", Deflate(samples)),
            2 => new PdfImageInfo(width, height, "DeviceRGB", "FlateDecode", Deflate(samples)),
            3 => new PdfImageInfo(width, height, "DeviceRGB", "FlateDecode", Deflate(Expand(palette!, samples, width, height))),
            _ => WithAlpha(width, height, samples),
        };
    }

    /// <summary>RGBA samples become RGB plus a greyscale soft mask.</summary>
    private static PdfImageInfo WithAlpha(int width, int height, byte[] samples)
    {
        var pixels = width * height;
        var rgb = new byte[pixels * 3];
        var alpha = new byte[pixels];

        for (var index = 0; index < pixels; index++)
        {
            rgb[(index * 3) + 0] = samples[index * 4];
            rgb[(index * 3) + 1] = samples[(index * 4) + 1];
            rgb[(index * 3) + 2] = samples[(index * 4) + 2];
            alpha[index] = samples[(index * 4) + 3];
        }

        return new PdfImageInfo(width, height, "DeviceRGB", "FlateDecode", Deflate(rgb))
        {
            SoftMask = new PdfImageInfo(width, height, "DeviceGray", "FlateDecode", Deflate(alpha)),
        };
    }

    private static byte[] Expand(byte[] palette, byte[] indices, int width, int height)
    {
        var rgb = new byte[width * height * 3];

        for (var index = 0; index < width * height; index++)
        {
            var entry = indices[index] * 3;
            if (entry + 2 >= palette.Length)
            {
                throw new PdfSignatureException("the PNG palette is shorter than the image indices");
            }

            rgb[(index * 3) + 0] = palette[entry];
            rgb[(index * 3) + 1] = palette[entry + 1];
            rgb[(index * 3) + 2] = palette[entry + 2];
        }

        return rgb;
    }

    /// <summary>Undoes the per-row filters of the PNG specification (section 9.2).</summary>
    private static byte[] Unfilter(byte[] filtered, int width, int height, int channels)
    {
        var stride = width * channels;
        var expected = (stride + 1) * height;

        if (filtered.Length < expected)
        {
            throw new PdfSignatureException(
                $"the PNG carries {filtered.Length} image bytes where {expected} were expected");
        }

        var output = new byte[stride * height];

        for (var row = 0; row < height; row++)
        {
            var filter = filtered[row * (stride + 1)];
            var source = row * (stride + 1) + 1;
            var target = row * stride;

            for (var column = 0; column < stride; column++)
            {
                var left = column >= channels ? output[target + column - channels] : (byte)0;
                var above = row > 0 ? output[target - stride + column] : (byte)0;
                var upperLeft = row > 0 && column >= channels ? output[target - stride + column - channels] : (byte)0;
                var value = filtered[source + column];

                output[target + column] = filter switch
                {
                    0 => value,
                    1 => (byte)(value + left),
                    2 => (byte)(value + above),
                    3 => (byte)(value + ((left + above) / 2)),
                    4 => (byte)(value + Paeth(left, above, upperLeft)),
                    _ => throw new PdfSignatureException($"the PNG uses unknown row filter {filter}"),
                };
            }
        }

        return output;
    }

    private static int Paeth(byte left, byte above, byte upperLeft)
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

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var deflate = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data);
        }

        return output.ToArray();
    }

    private static PdfImageInfo ParseJpeg(ReadOnlyMemory<byte> bytes)
    {
        var span = bytes.Span;
        var offset = 2;

        while (offset + 4 <= span.Length)
        {
            if (span[offset] != 0xFF)
            {
                offset++;
                continue;
            }

            var marker = span[offset + 1];
            offset += 2;

            // Standalone markers carry no length.
            if (marker is 0xD8 or 0xD9 or 0x01 || marker is >= 0xD0 and <= 0xD7)
            {
                continue;
            }

            if (offset + 2 > span.Length)
            {
                break;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(offset, 2));
            if (length < 2 || offset + length > span.Length)
            {
                break;
            }

            var isStartOfFrame = marker is >= 0xC0 and <= 0xCF
                and not 0xC4 and not 0xC8 and not 0xCC;

            if (isStartOfFrame)
            {
                var height = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(offset + 3, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(offset + 5, 2));
                var components = span[offset + 7];

                if (width <= 0 || height <= 0)
                {
                    throw new PdfSignatureException("the JPEG declares no frame size");
                }

                return components switch
                {
                    1 => new PdfImageInfo(width, height, "DeviceGray", "DCTDecode", bytes),
                    3 => new PdfImageInfo(width, height, "DeviceRGB", "DCTDecode", bytes),
                    4 => new PdfImageInfo(width, height, "DeviceCMYK", "DCTDecode", bytes),
                    _ => throw new PdfSignatureException(
                        $"the JPEG has {components} colour components, which no PDF colour space matches"),
                };
            }

            if (marker == 0xDA)
            {
                break;
            }

            offset += length;
        }

        throw new PdfSignatureException("the JPEG carries no frame header, so its size is unknown");
    }
}
