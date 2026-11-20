using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace M3scalinum.Text.Pdf;

/// <summary>The parts of a PDF an incremental update needs to find.</summary>
internal sealed record PdfStructure(int PreviousXref, int RootNumber, int Size, string? Id, string Catalog)
{
    public static PdfStructure Parse(string pdf)
    {
        var startxref = pdf.LastIndexOf("startxref", StringComparison.Ordinal);
        if (startxref < 0)
        {
            throw new PdfSignatureException("the data is not a PDF: no startxref");
        }

        var previousXref = ReadInt(pdf, startxref + "startxref".Length);

        var trailerPos = pdf.LastIndexOf("trailer", startxref, StringComparison.Ordinal);
        if (trailerPos < 0)
        {
            throw new PdfSignatureException("the PDF has no trailer dictionary before startxref");
        }

        var trailer = pdf.Substring(trailerPos, startxref - trailerPos);

        var root = Regex.Match(trailer, @"/Root\s+(\d+)\s+0\s+R");
        if (!root.Success)
        {
            throw new PdfSignatureException("the PDF trailer names no /Root");
        }

        var rootNumber = int.Parse(root.Groups[1].Value, CultureInfo.InvariantCulture);

        var sizeMatch = Regex.Match(trailer, @"/Size\s+(\d+)");
        var size = sizeMatch.Success
            ? int.Parse(sizeMatch.Groups[1].Value, CultureInfo.InvariantCulture)
            : rootNumber + 1;

        var id = Regex.Match(trailer, @"/ID\s*(\[[^\]]*\])");

        return new PdfStructure(
            previousXref,
            rootNumber,
            size,
            id.Success ? id.Groups[1].Value : null,
            BodyOf(pdf, rootNumber));
    }

    /// <summary>The object body, from <c>N 0 obj</c> to <c>endobj</c>.</summary>
    private static string BodyOf(string pdf, int number)
    {
        var match = Regex.Match(pdf, @"(?:^|[\r\n])" + number + @"\s+0\s+obj\b", RegexOptions.Multiline);
        if (!match.Success)
        {
            throw new PdfSignatureException($"object {number} is missing from the PDF");
        }

        var end = pdf.IndexOf("endobj", match.Index, StringComparison.Ordinal);
        if (end < 0)
        {
            throw new PdfSignatureException($"object {number} is not closed");
        }

        // The `N 0 obj` header is stripped because the caller writes it again: keeping it produced
        // `38 0 obj\n38 0 obj\n<<…`, which a strict reader rejects at the first object.
        var start = pdf.IndexOf('<', match.Index + match.Length);
        if (start < 0 || start > end)
        {
            throw new PdfSignatureException($"object {number} is not a dictionary");
        }

        return pdf.Substring(start, end - start).TrimEnd();
    }

    private static int ReadInt(string text, int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        var start = index;
        while (index < text.Length && char.IsDigit(text[index]))
        {
            index++;
        }

        if (start == index)
        {
            throw new PdfSignatureException("expected a number after startxref");
        }

        return int.Parse(text.AsSpan(start, index - start), CultureInfo.InvariantCulture);
    }
}
