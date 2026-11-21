using System.Collections.Generic;
using System.Text;

namespace M3scalinum.Text.Ocr.Tests;

/// <summary>
/// Writes a one-page PDF with one line of Helvetica text.
/// </summary>
/// <remarks>
/// The tests that need a scanned page cannot rasterise one in managed code: no
/// drawing library is available to them, on purpose. They rasterise this instead,
/// through the package under test, which is the same path a scanned document takes.
/// A standard-14 font needs no embedding, so the document is a few hundred bytes.
/// </remarks>
internal static class MinimalPdf
{
    internal static byte[] WithText(string text, int fontSize = 36)
    {
        var content = $"BT /F1 {fontSize} Tf 72 700 Td ({Escape(text)}) Tj ET\n";

        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] "
                + "/Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}endstream",
        };

        var builder = new StringBuilder();
        builder.Append("%PDF-1.4\n");

        var offsets = new List<int>(objects.Count);
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(builder.Length);
            builder.Append(index + 1).Append(" 0 obj\n").Append(objects[index]).Append("\nendobj\n");
        }

        var xref = builder.Length;
        builder.Append("xref\n0 ").Append(objects.Count + 1).Append('\n');
        builder.Append("0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            builder.Append(offset.ToString("D10", System.Globalization.CultureInfo.InvariantCulture))
                .Append(" 00000 n \n");
        }

        builder.Append("trailer\n<< /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R >>\n");
        builder.Append("startxref\n").Append(xref).Append("\n%%EOF\n");

        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static string Escape(string text)
    {
        var builder = new StringBuilder(text.Length);

        foreach (var character in text)
        {
            if (character is '(' or ')' or '\\')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
