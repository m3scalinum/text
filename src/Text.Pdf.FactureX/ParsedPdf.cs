using System;
using System.Globalization;
using System.Text.RegularExpressions;
using M3scalinum.Text.Typst;

namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>The parts of a PDF an incremental update needs to find.</summary>
internal sealed record ParsedPdf(
    int PreviousXref,
    int RootNumber,
    int Size,
    string? Id,
    int MetadataNumber,
    string Xmp)
{
    public static ParsedPdf Parse(string pdf)
    {
        var startxref = pdf.LastIndexOf("startxref", StringComparison.Ordinal);
        if (startxref < 0)
        {
            throw new InvalidOperationException("the data is not a PDF: no startxref");
        }

        var previousXref = ReadInt(pdf, startxref + "startxref".Length);

        var trailerPos = pdf.LastIndexOf("trailer", startxref, StringComparison.Ordinal);
        if (trailerPos < 0)
        {
            throw new InvalidOperationException("the PDF has no trailer dictionary before startxref");
        }

        var trailer = pdf.Substring(trailerPos, startxref - trailerPos);

        var root = Regex.Match(trailer, @"/Root\s+(\d+)\s+0\s+R");
        if (!root.Success)
        {
            throw new InvalidOperationException("the PDF trailer names no /Root");
        }

        var rootNumber = int.Parse(root.Groups[1].Value, CultureInfo.InvariantCulture);

        var sizeMatch = Regex.Match(trailer, @"/Size\s+(\d+)");
        var size = sizeMatch.Success
            ? int.Parse(sizeMatch.Groups[1].Value, CultureInfo.InvariantCulture)
            : rootNumber + 1;

        var id = Regex.Match(trailer, @"/ID\s*(\[[^\]]*\])");

        var rootObject = BodyOf(pdf, rootNumber);
        var metadata = Regex.Match(rootObject, @"/Metadata\s+(\d+)\s+0\s+R");
        if (!metadata.Success)
        {
            throw new InvalidOperationException("the PDF catalog carries no /Metadata reference");
        }

        var metadataNumber = int.Parse(metadata.Groups[1].Value, CultureInfo.InvariantCulture);
        var (xmp, dictionary) = StreamOf(pdf, metadataNumber);

        if (dictionary.Contains("/Filter", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"the /Metadata stream of object {metadataNumber} is compressed, and this patch only rewrites a plain stream");
        }

        return new ParsedPdf(
            previousXref,
            rootNumber,
            size,
            id.Success ? id.Groups[1].Value : null,
            metadataNumber,
            xmp);
    }

    private static string BodyOf(string pdf, int number)
    {
        var match = Regex.Match(pdf, @"(?:^|[\r\n])" + number + @"\s+0\s+obj\b", RegexOptions.Multiline);
        if (!match.Success)
        {
            throw new InvalidOperationException($"object {number} is missing from the PDF");
        }

        var end = pdf.IndexOf("endobj", match.Index, StringComparison.Ordinal);
        return pdf.Substring(match.Index, end - match.Index);
    }

    private static (string Xmp, string Dictionary) StreamOf(string pdf, int number)
    {
        var match = Regex.Match(pdf, @"(?:^|[\r\n])" + number + @"\s+0\s+obj", RegexOptions.Multiline);
        if (!match.Success)
        {
            throw new InvalidOperationException($"object {number} is missing from the PDF");
        }

        var streamKeyword = pdf.IndexOf("stream", match.Index, StringComparison.Ordinal);
        if (streamKeyword < 0)
        {
            throw new InvalidOperationException($"object {number} has no stream");
        }

        var dictionary = pdf.Substring(match.Index, streamKeyword - match.Index);

        var dataStart = streamKeyword + "stream".Length;
        if (dataStart < pdf.Length && pdf[dataStart] == '\r')
        {
            dataStart++;
        }

        if (dataStart < pdf.Length && pdf[dataStart] == '\n')
        {
            dataStart++;
        }

        var dataEnd = pdf.IndexOf("endstream", dataStart, StringComparison.Ordinal);
        if (dataEnd < 0)
        {
            throw new InvalidOperationException($"object {number} has no endstream");
        }

        // The line break before `endstream` is the writer's, not the data.
        if (dataEnd > dataStart && pdf[dataEnd - 1] == '\n')
        {
            dataEnd--;
        }

        if (dataEnd > dataStart && pdf[dataEnd - 1] == '\r')
        {
            dataEnd--;
        }

        return (pdf.Substring(dataStart, dataEnd - dataStart), dictionary);
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
            throw new InvalidOperationException("expected a number after startxref");
        }

        return int.Parse(text.AsSpan(start, index - start), CultureInfo.InvariantCulture);
    }
}
