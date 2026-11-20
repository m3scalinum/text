using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using M3scalinum.Text.SharpTypst;

namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>
/// Reads the invoice XML back out of a Factur-X PDF.
/// </summary>
/// <remarks>
/// This is the receiving half of the exchange: a platform, an auditor or a test wants the bytes the
/// sender embedded, not a re-serialization of them. The parser handles what a PDF writer produces
/// for an attachment — a <c>/Names</c> tree in the catalog, a <c>/Filespec</c> with an
/// <c>/EmbeddedFile</c> stream, and a Flate-compressed payload — and refuses anything else with a
/// message that says which part was missing.
/// </remarks>
internal static class PdfAttachments
{
    /// <summary>The names a Factur-X or ZUGFeRD producer uses, in the order they are preferred.</summary>
    private static readonly string[] PreferredNames =
    [
        "factur-x.xml",
        "zugferd-invoice.xml",
        "ZUGFeRD-invoice.xml",
        "cii.xml",
    ];

    public static bool TryExtract(string pdf, out string fileName, out byte[] data)
    {
        fileName = string.Empty;
        data = [];

        var root = RootNumber(pdf);
        if (root is null)
        {
            return false;
        }

        var catalog = BodyOf(pdf, root.Value);
        if (catalog is null)
        {
            return false;
        }

        var names = Resolve(pdf, catalog, "Names");
        if (names is null)
        {
            return false;
        }

        var embedded = Resolve(pdf, names, "EmbeddedFiles");
        if (embedded is null)
        {
            return false;
        }

        var array = Regex.Match(embedded, @"/Names\s*\[(.*?)\]", RegexOptions.Singleline);
        if (!array.Success)
        {
            return false;
        }

        var entries = ParseNameArray(array.Groups[1].Value);
        if (entries.Count == 0)
        {
            return false;
        }

        var chosen = Choose(entries);

        var filespec = BodyOf(pdf, chosen.Reference);
        if (filespec is null)
        {
            return false;
        }

        var stream = EmbeddedStream(pdf, filespec);
        if (stream is null)
        {
            return false;
        }

        fileName = chosen.Name;
        data = stream;
        return true;
    }

    private static (string Name, int Reference) Choose(List<(string Name, int Reference)> entries)
    {
        foreach (var preferred in PreferredNames)
        {
            foreach (var entry in entries)
            {
                if (string.Equals(entry.Name, preferred, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }
        }

        // No canonical name: take the first XML file, which is what a Factur-X producer writes.
        foreach (var entry in entries)
        {
            if (entry.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return entries[0];
    }

    private static List<(string Name, int Reference)> ParseNameArray(string content)
    {
        var entries = new List<(string, int)>();

        // A name is either a literal string `(factur-x.xml)` or a hex string `<66616374…>`.
        var matches = Regex.Matches(
            content,
            @"\((?<literal>(?:\\.|[^\\()])*)\)\s*(?<reference>\d+)\s+0\s+R|<(?<hex>[0-9A-Fa-f\s]+)>\s*(?<reference>\d+)\s+0\s+R",
            RegexOptions.Singleline);

        foreach (Match match in matches)
        {
            var name = match.Groups["literal"].Success
                ? Unescape(match.Groups["literal"].Value)
                : FromHex(match.Groups["hex"].Value);

            if (int.TryParse(match.Groups["reference"].Value, CultureInfo.InvariantCulture, out var reference))
            {
                entries.Add((name, reference));
            }
        }

        return entries;
    }

    /// <summary>Reads the <c>/EmbeddedFile</c> stream a filespec points at, inflating it if needed.</summary>
    private static byte[]? EmbeddedStream(string pdf, string filespec)
    {
        var ef = Regex.Match(filespec, @"/EF\s*<<(?<body>.*?)>>", RegexOptions.Singleline);
        if (!ef.Success)
        {
            return null;
        }

        // /F is the file, /UF the Unicode name of the same stream; either points at the data.
        var reference = Regex.Match(ef.Groups["body"].Value, @"/(?:F|UF)\s+(\d+)\s+0\s+R");
        if (!reference.Success)
        {
            return null;
        }

        var number = int.Parse(reference.Groups[1].Value, CultureInfo.InvariantCulture);
        var body = BodyOf(pdf, number);
        if (body is null)
        {
            return null;
        }

        var stream = StreamOf(body);
        if (stream is null)
        {
            return null;
        }

        var (dictionary, payload) = stream.Value;

        if (!dictionary.Contains("/Filter", StringComparison.Ordinal))
        {
            return payload;
        }

        if (!dictionary.Contains("FlateDecode", StringComparison.Ordinal))
        {
            var filter = Regex.Match(dictionary, @"/Filter\s*/(\w+)");
            var name = filter.Success ? filter.Groups[1].Value : "an unnamed filter";

            throw new FacturXFormatException(
                $"the embedded invoice uses {name}, which this reader does not implement; only FlateDecode is supported");
        }

        return Inflate(payload);
    }

    private static byte[] Inflate(byte[] payload)
    {
        try
        {
            using var input = new MemoryStream(payload, writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            return output.ToArray();
        }
        catch (InvalidDataException exception)
        {
            throw new FacturXFormatException(
                $"the embedded invoice is not valid Flate data: {exception.Message}", exception);
        }
    }

    private static int? RootNumber(string pdf)
    {
        var startxref = pdf.LastIndexOf("startxref", StringComparison.Ordinal);
        if (startxref < 0)
        {
            return null;
        }

        var trailer = pdf.LastIndexOf("trailer", startxref, StringComparison.Ordinal);
        if (trailer < 0)
        {
            return null;
        }

        var root = Regex.Match(pdf.Substring(trailer, startxref - trailer), @"/Root\s+(\d+)\s+0\s+R");

        return root.Success ? int.Parse(root.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>Follows <c>/Key N 0 R</c> to the object body, or returns the inline dictionary.</summary>
    private static string? Resolve(string pdf, string body, string key)
    {
        var reference = Regex.Match(body, "/" + key + @"\s+(\d+)\s+0\s+R");
        if (reference.Success)
        {
            var number = int.Parse(reference.Groups[1].Value, CultureInfo.InvariantCulture);
            return BodyOf(pdf, number);
        }

        var inline = Regex.Match(body, "/" + key + @"\s*<<");
        if (!inline.Success)
        {
            return null;
        }

        // A name tree nests: /Names<</EmbeddedFiles<</Names[…]>>>>. A non-greedy match would stop
        // at the inner `>>` and lose the rest, so the dictionary is read by balancing the delimiters.
        return Balanced(body, inline.Index + inline.Length - 2);
    }

    /// <summary>Returns the content of the dictionary whose <c>&lt;&lt;</c> starts at <paramref name="start"/>.</summary>
    private static string? Balanced(string text, int start)
    {
        var depth = 0;

        for (var index = start; index < text.Length - 1; index++)
        {
            if (text[index] == '<' && text[index + 1] == '<')
            {
                depth++;
                index++;
                continue;
            }

            if (text[index] == '>' && text[index + 1] == '>')
            {
                depth--;
                index++;

                if (depth == 0)
                {
                    return text.Substring(start + 2, index - 1 - (start + 2));
                }
            }
        }

        return null;
    }

    private static string? BodyOf(string pdf, int number)
    {
        var match = Regex.Match(pdf, @"(?:^|[\r\n])" + number + @"\s+0\s+obj\b", RegexOptions.Multiline);
        if (!match.Success)
        {
            return null;
        }

        var end = pdf.IndexOf("endobj", match.Index, StringComparison.Ordinal);

        return end < 0 ? null : pdf.Substring(match.Index, end - match.Index);
    }

    private static (string Dictionary, byte[] Payload)? StreamOf(string body)
    {
        var keyword = body.IndexOf("stream", StringComparison.Ordinal);
        if (keyword < 0)
        {
            return null;
        }

        var dictionary = body.Substring(0, keyword);

        var start = keyword + "stream".Length;
        if (start < body.Length && body[start] == '\r')
        {
            start++;
        }

        if (start < body.Length && body[start] == '\n')
        {
            start++;
        }

        var end = body.IndexOf("endstream", start, StringComparison.Ordinal);
        if (end < 0)
        {
            return null;
        }

        // The line break before `endstream` belongs to the writer, not to the data.
        if (end > start && body[end - 1] == '\n')
        {
            end--;
        }

        if (end > start && body[end - 1] == '\r')
        {
            end--;
        }

        return (dictionary, Encoding.Latin1.GetBytes(body.Substring(start, end - start)));
    }

    private static string Unescape(string value) => value
        .Replace("\\(", "(", StringComparison.Ordinal)
        .Replace("\\)", ")", StringComparison.Ordinal)
        .Replace("\\\\", "\\", StringComparison.Ordinal);

    private static string FromHex(string value)
    {
        var digits = value.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);

        if (digits.Length % 2 != 0)
        {
            digits += "0";
        }

        var bytes = new byte[digits.Length / 2];
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = byte.Parse(digits.AsSpan(index * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        return Encoding.UTF8.GetString(bytes);
    }
}
