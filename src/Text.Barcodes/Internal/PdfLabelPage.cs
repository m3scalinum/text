using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace M3scalinum.Text.Barcodes.Internal;

/// <summary>
/// A one-page PDF canvas: filled text, rules and one greyscale image, enough for a label.
/// </summary>
/// <remarks>
/// <para>
/// This is not a PDF library and does not pretend to be one. The package is a leaf: it draws the
/// label it knows how to draw, on one page, with the two standard Helvetica faces every reader has
/// built in, and writes the smallest document that renders. Anything more — several pages, fonts of
/// the caller's choosing, transparencies, a signature — belongs to <c>M3scalinum.Text.Pdf</c>, and
/// reaching for it here would put the PDF stack — its renderer, its native library and its signing
/// dependencies — under a leaf package (Spec 20 §13.4).
/// </para>
/// <para>
/// The bytes are deterministic: no timestamp, no document identifier, no producer version. Two
/// renders of the same label are the same file, which is what makes a rebuild reproducible.
/// </para>
/// </remarks>
internal sealed class PdfLabelPage
{
    private const int ImageObject = 7;

    private readonly double _width;
    private readonly double _height;
    private readonly StringBuilder _content = new();

    private byte[]? _image;
    private int _imageWidth;
    private int _imageHeight;
    private double _imageX;
    private double _imageY;
    private double _imageWidthPoints;
    private double _imageHeightPoints;

    /// <summary>Creates a page of the given size, in PostScript points.</summary>
    public PdfLabelPage(double widthPoints, double heightPoints)
    {
        if (!double.IsFinite(widthPoints) || widthPoints <= 0
            || !double.IsFinite(heightPoints) || heightPoints <= 0)
        {
            throw new BarcodeException(
                BarcodeErrorCode.InvalidArgument,
                $"a label page takes a positive size in points, got {widthPoints} x {heightPoints}");
        }

        _width = widthPoints;
        _height = heightPoints;
    }

    /// <summary>Draws one line of text, its left end and baseline at the given point.</summary>
    /// <param name="text">The text. Characters outside Windows-1252 are written as <c>?</c>.</param>
    /// <param name="xPoints">Left end, in points from the page's left edge.</param>
    /// <param name="yPoints">Baseline, in points from the page's bottom edge.</param>
    /// <param name="sizePoints">Font size, in points.</param>
    /// <param name="bold">Bold face instead of regular.</param>
    /// <param name="grey">Ink, from 0 (black) to 1 (white).</param>
    public void DrawText(
        string text, double xPoints, double yPoints, double sizePoints, bool bold, double grey)
    {
        var encoded = WinAnsiText.Encode(text);

        if (encoded.Length == 0)
        {
            return;
        }

        _content.Append("BT /")
            .Append(bold ? "F2" : "F1")
            .Append(' ')
            .Append(Number(sizePoints))
            .Append(" Tf ")
            .Append(Number(grey))
            .Append(" g 1 0 0 1 ")
            .Append(Number(xPoints))
            .Append(' ')
            .Append(Number(yPoints))
            .Append(" Tm (")
            .Append(WinAnsiText.Escape(encoded))
            .Append(") Tj ET\n");
    }

    /// <summary>Draws a horizontal rule between two x coordinates.</summary>
    public void DrawRule(double fromPoints, double toPoints, double yPoints, double grey)
    {
        _content.Append("q 0.6 w ")
            .Append(Number(grey))
            .Append(" G ")
            .Append(Number(fromPoints))
            .Append(' ')
            .Append(Number(yPoints))
            .Append(" m ")
            .Append(Number(toPoints))
            .Append(' ')
            .Append(Number(yPoints))
            .Append(" l S Q\n");
    }

    /// <summary>
    /// Places the one image the page carries: 8-bit greyscale, already decompressed, drawn into the
    /// given rectangle.
    /// </summary>
    /// <exception cref="BarcodeException">The page already carries an image, or the pixels do not
    /// match the dimensions given.</exception>
    public void DrawGreyscaleImage(
        byte[] pixels,
        int width,
        int height,
        double xPoints,
        double yPoints,
        double widthPoints,
        double heightPoints)
    {
        if (_image is not null)
        {
            throw new BarcodeException(
                BarcodeErrorCode.Unsupported,
                "a shipping label carries one symbol; this page already carries an image");
        }

        if (pixels.Length != width * height)
        {
            throw new BarcodeException(
                BarcodeErrorCode.InvalidArgument,
                $"the image is {width} × {height} pixels, so it needs {width * height} bytes, got {pixels.Length}");
        }

        _image = pixels;
        _imageWidth = width;
        _imageHeight = height;
        _imageX = xPoints;
        _imageY = yPoints;
        _imageWidthPoints = widthPoints;
        _imageHeightPoints = heightPoints;

        _content.Append("q ")
            .Append(Number(widthPoints))
            .Append(" 0 0 ")
            .Append(Number(heightPoints))
            .Append(' ')
            .Append(Number(xPoints))
            .Append(' ')
            .Append(Number(yPoints))
            .Append(" cm /Im0 Do Q\n");
    }

    /// <summary>Serialises the page, its resources and its cross-reference table.</summary>
    /// <returns>The PDF bytes.</returns>
    public byte[] Build()
    {
        var objects = new List<byte[]>
        {
            // 1: catalog.
            Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
            // 2: page tree.
            Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            // 3: the page. Populated below, once the resources are known.
            Array.Empty<byte>(),
            // 4: the content stream.
            Stream(Ascii(_content.ToString())),
            // 5 and 6: the two standard faces, so no font is embedded and none is needed.
            Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"),
            Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>"),
            // 7: the image XObject, absent when the page carries none.
            _image is null ? Ascii("null") : Image(),
            // 8: the producer, and nothing else: no date, so the bytes are reproducible.
            Ascii("<< /Producer (M3scalinum.Text.Barcodes) >>"),
        };

        var resources = new StringBuilder("<< /Font << /F1 5 0 R /F2 6 0 R >>");
        if (_image is not null)
        {
            resources.Append(" /XObject << /Im0 ").Append(ImageObject).Append(" 0 R >>");
        }

        resources.Append(" >>");

        objects[2] = Ascii(
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Number(_width)} {Number(_height)}] "
            + $"/Resources {resources} /Contents 4 0 R >>");

        return Assemble(objects, objects.Count);
    }

    private byte[] Image()
    {
        using var compressed = new MemoryStream();
        using (var deflate = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(_image!, 0, _image!.Length);
        }

        var body = new MemoryStream();
        var dictionary = Ascii(
            $"<< /Type /XObject /Subtype /Image /Width {_imageWidth} /Height {_imageHeight} "
            + "/ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode "
            + $"/Length {compressed.Length} >>");
        body.Write(dictionary, 0, dictionary.Length);
        body.Write(StreamPrefix, 0, StreamPrefix.Length);
        compressed.Position = 0;
        compressed.CopyTo(body);
        body.Write(StreamSuffix, 0, StreamSuffix.Length);
        return body.ToArray();
    }

    /// <summary>
    /// The file header, with the four high bytes a PDF reader expects after the comment marker so
    /// that a transfer which rewrites line endings cannot corrupt the file.
    /// </summary>
    private static readonly byte[] Header =
        [.. Encoding.ASCII.GetBytes("%PDF-1.7\n%"), 0xE2, 0xE3, 0xCF, 0xD3, 0x0A];

    private static readonly byte[] StreamPrefix = Ascii("\nstream\n");
    private static readonly byte[] StreamSuffix = Ascii("\nendstream");

    /// <summary>A stream object: the dictionary, then the bytes it measures.</summary>
    private static byte[] Stream(byte[] data)
    {
        var body = new MemoryStream();
        var dictionary = Ascii($"<< /Length {data.Length} >>");
        body.Write(dictionary, 0, dictionary.Length);
        body.Write(StreamPrefix, 0, StreamPrefix.Length);
        body.Write(data, 0, data.Length);
        body.Write(StreamSuffix, 0, StreamSuffix.Length);
        return body.ToArray();
    }

    /// <summary>Header, objects, cross-reference table, trailer.</summary>
    private static byte[] Assemble(List<byte[]> bodies, int count)
    {
        using var pdf = new MemoryStream();
        pdf.Write(Header, 0, Header.Length);

        var offsets = new long[count + 1];
        for (var index = 0; index < count; index++)
        {
            offsets[index + 1] = pdf.Position;
            var header = Ascii($"{index + 1} 0 obj\n");
            pdf.Write(header, 0, header.Length);
            pdf.Write(bodies[index], 0, bodies[index].Length);
            pdf.Write(Ascii("\nendobj\n"));
        }

        var xref = pdf.Position;
        pdf.Write(Ascii($"xref\n0 {count + 1}\n"));
        pdf.Write(Ascii("0000000000 65535 f \n"));

        for (var index = 1; index <= count; index++)
        {
            pdf.Write(Ascii($"{offsets[index]:D10} 00000 n \n"));
        }

        pdf.Write(Ascii(
            $"trailer\n<< /Size {count + 1} /Root 1 0 R /Info 8 0 R >>\nstartxref\n{xref}\n%%EOF\n"));

        return pdf.ToArray();
    }

    /// <summary>A number the way a content stream wants it: dot, three decimals at most.</summary>
    private static string Number(double value)
        => Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);
}

/// <summary>Windows-1252, the encoding both standard Helvetica faces are declared with.</summary>
/// <remarks>
/// A label carries an address, and an address carries accents. Windows-1252 is what a PDF reader
/// applies to a simple font unless told otherwise, and it is a superset of Latin-1, so the address
/// of a French, German or Spanish consignee prints as written. A character outside it becomes
/// <c>?</c> rather than an escape a reader would ignore: a box nobody can read is worse than one
/// with a visible gap in it.
/// </remarks>
internal static class WinAnsiText
{
    /// <summary>The Windows-1252 bytes for <paramref name="text"/>.</summary>
    public static byte[] Encode(string text)
    {
        var bytes = new List<byte>(text.Length);

        foreach (var character in text)
        {
            if (character is >= ' ' and <= '~')
            {
                bytes.Add((byte)character);
            }
            else if (character is >= '\u00a0' and <= '\u00ff')
            {
                bytes.Add((byte)character);
            }
            else if (Special.TryGetValue(character, out var code))
            {
                bytes.Add(code);
            }
            else if (character is '\t')
            {
                bytes.Add((byte)' ');
            }
            else
            {
                bytes.Add((byte)'?');
            }
        }

        return [.. bytes];
    }

    /// <summary>Escapes the four characters a PDF literal string reserves, and high bytes.</summary>
    public static string Escape(byte[] data)
    {
        var text = new StringBuilder(data.Length);

        foreach (var value in data)
        {
            if (value is (byte)'(' or (byte)')' or (byte)'\\')
            {
                text.Append('\\').Append((char)value);
            }
            else if (value < 0x20 || value > 0x7e)
            {
                text.Append('\\').Append(Convert.ToString(value, 8).PadLeft(3, '0'));
            }
            else
            {
                text.Append((char)value);
            }
        }

        return text.ToString();
    }

    /// <summary>The characters Windows-1252 puts in 0x80–0x9F, where Latin-1 has controls.</summary>
    private static readonly Dictionary<char, byte> Special = new()
    {
        ['\u20ac'] = 0x80, // euro
        ['\u201a'] = 0x82,
        ['\u0192'] = 0x83,
        ['\u201e'] = 0x84,
        ['\u2026'] = 0x85, // ellipsis
        ['\u2020'] = 0x86,
        ['\u2021'] = 0x87,
        ['\u02c6'] = 0x88,
        ['\u2030'] = 0x89,
        ['\u0160'] = 0x8a,
        ['\u2039'] = 0x8b,
        ['\u0152'] = 0x8c, // OE
        ['\u017d'] = 0x8e,
        ['\u2018'] = 0x91,
        ['\u2019'] = 0x92,
        ['\u201c'] = 0x93,
        ['\u201d'] = 0x94,
        ['\u2022'] = 0x95,
        ['\u2013'] = 0x96, // en dash
        ['\u2014'] = 0x97, // em dash
        ['\u02dc'] = 0x98,
        ['\u2122'] = 0x99,
        ['\u0161'] = 0x9a,
        ['\u203a'] = 0x9b,
        ['\u0153'] = 0x9c, // oe
        ['\u017e'] = 0x9e,
        ['\u0178'] = 0x9f,
    };
}
