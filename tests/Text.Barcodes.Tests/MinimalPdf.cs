using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace M3scalinum.Text.Barcodes.Tests;

/// <summary>
/// Builds a one-page PDF whose page is a picture: what a scanned delivery note looks like to a
/// renderer, without committing a binary fixture.
/// </summary>
/// <remarks>
/// The page holds a single greyscale image XObject covering it, drawn through the standard
/// <c>q … cm /Im0 Do Q</c> sequence. That is the smallest document that exercises the recipe in
/// Spec 20 §9 — render the page, hand the pixels to <see cref="Barcode.DecodeAll"/> — and it is
/// written here in full so a reader can see there is no renderer hidden in it.
/// </remarks>
internal static class MinimalPdf
{
    public static byte[] WithImage(GreyImage image)
    {
        var imageData = Deflate(image.Pixels);

        // A4-ish proportion is irrelevant here: the page is the image, one point per pixel, so the
        // rendered page is the barcode plus its quiet zone and nothing else.
        var width = image.Width;
        var height = image.Height;

        var content = Encoding.ASCII.GetBytes(
            $"q {width} 0 0 {height} 0 0 cm /Im0 Do Q");

        using var pdf = new MemoryStream();
        var offsets = new long[6];

        Write(pdf, "%PDF-1.7\n");

        offsets[1] = pdf.Position;
        Write(pdf, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        offsets[2] = pdf.Position;
        Write(pdf, "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        offsets[3] = pdf.Position;
        Write(
            pdf,
            $"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}] " +
            "/Resources << /XObject << /Im0 4 0 R >> >> /Contents 5 0 R >>\nendobj\n");

        offsets[4] = pdf.Position;
        Write(
            pdf,
            $"4 0 obj\n<< /Type /XObject /Subtype /Image /Width {width} /Height {height} " +
            $"/ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode " +
            $"/Length {imageData.Length} >>\nstream\n");
        pdf.Write(imageData);
        Write(pdf, "\nendstream\nendobj\n");

        offsets[5] = pdf.Position;
        Write(pdf, $"5 0 obj\n<< /Length {content.Length} >>\nstream\n");
        pdf.Write(content);
        Write(pdf, "\nendstream\nendobj\n");

        var xref = pdf.Position;
        Write(pdf, "xref\n0 6\n0000000000 65535 f \n");
        for (var index = 1; index <= 5; index++)
        {
            Write(pdf, $"{offsets[index]:D10} 00000 n \n");
        }

        Write(pdf, $"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");

        return pdf.ToArray();
    }

    private static void Write(Stream stream, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        stream.Write(bytes);
    }

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();

        // FlateDecode is zlib-wrapped deflate, which is exactly what ZLibStream writes.
        using (var zlib = new ZLibStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }
}
