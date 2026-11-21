using System;
using System.IO;

namespace M3scalinum.Text.Barcodes.Tests;

/// <summary>
/// Writes an 8-bit greyscale PNG, all white, without an image library.
/// </summary>
/// <remarks>
/// The "no barcode here" case needs a real PNG — a decoder has to accept the file and then find
/// nothing in it — and a test fixture committed as bytes would hide what the test is about. This is
/// the smallest honest producer: IHDR, one IDAT of stored-deflate scanlines, IEND.
/// </remarks>
internal static class BlankPng
{
    public static byte[] Create(int width, int height, byte grey = 0xFF)
    {
        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        WriteBigEndian(header, 0, width);
        WriteBigEndian(header, 4, height);
        header[8] = 8;  // bit depth
        header[9] = 0;  // colour type: greyscale
        WriteChunk(png, "IHDR", header);

        using (var raw = new MemoryStream())
        {
            var row = new byte[width + 1];
            row[0] = 0; // filter: none
            for (var y = 0; y < height; y++)
            {
                Array.Fill(row, grey, 1, width);
                raw.Write(row);
            }

            WriteChunk(png, "IDAT", Zlib(raw.ToArray()));
        }

        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    /// <summary>A zlib stream of stored (uncompressed) deflate blocks.</summary>
    private static byte[] Zlib(byte[] data)
    {
        const int MaxBlock = 0xFFFF;

        using var stream = new MemoryStream();
        stream.WriteByte(0x78); // CMF: deflate, 32K window
        stream.WriteByte(0x01); // FLG: no dictionary, fastest

        var offset = 0;
        do
        {
            var length = Math.Min(MaxBlock, data.Length - offset);
            var final = offset + length >= data.Length;

            stream.WriteByte(final ? (byte)0x01 : (byte)0x00);
            stream.WriteByte((byte)(length & 0xFF));
            stream.WriteByte((byte)(length >> 8));
            stream.WriteByte((byte)(~length & 0xFF));
            stream.WriteByte((byte)((~length >> 8) & 0xFF));
            stream.Write(data, offset, length);

            offset += length;
        }
        while (offset < data.Length);

        var adler = Adler32(data);
        stream.WriteByte((byte)(adler >> 24));
        stream.WriteByte((byte)(adler >> 16));
        stream.WriteByte((byte)(adler >> 8));
        stream.WriteByte((byte)adler);

        return stream.ToArray();
    }

    private static uint Adler32(byte[] data)
    {
        const uint Modulus = 65521;
        uint a = 1, b = 0;

        foreach (var value in data)
        {
            a = (a + value) % Modulus;
            b = (b + a) % Modulus;
        }

        return (b << 16) | a;
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, data.Length);
        stream.Write(length);

        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);

        var crc = Crc32(typeBytes, data);
        var crcBytes = new byte[4];
        WriteBigEndian(crcBytes, 0, crc);
        stream.Write(crcBytes);
    }

    private static void WriteBigEndian(Span<byte> destination, int offset, uint value)
    {
        destination[offset] = (byte)(value >> 24);
        destination[offset + 1] = (byte)(value >> 16);
        destination[offset + 2] = (byte)(value >> 8);
        destination[offset + 3] = (byte)value;
    }

    private static void WriteBigEndian(Span<byte> destination, int offset, int value)
    {
        destination[offset] = (byte)(value >> 24);
        destination[offset + 1] = (byte)(value >> 16);
        destination[offset + 2] = (byte)(value >> 8);
        destination[offset + 3] = (byte)value;
    }

    private static uint[]? _crcTable;

    private static uint Crc32(byte[] type, byte[] data)
    {
        var table = _crcTable ??= BuildCrcTable();
        var crc = 0xFFFFFFFFu;

        foreach (var value in type)
        {
            crc = table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        foreach (var value in data)
        {
            crc = table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFF;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < 256; index++)
        {
            var value = index;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) == 1 ? 0xEDB88320 ^ (value >> 1) : value >> 1;
            }

            table[index] = value;
        }

        return table;
    }
}
