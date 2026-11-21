using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace M3scalinum.Text.Pdf;

/// <summary>One indirect object: where it starts in the file, and the dictionary it holds.</summary>
/// <param name="Number">The object number.</param>
/// <param name="Offset">The offset of the <c>N 0 obj</c> header.</param>
/// <param name="BodyStart">The offset of the first <c>&lt;</c> after the header.</param>
/// <param name="BodyEnd">The offset of the <c>endobj</c> that closes it.</param>
/// <param name="Body">Everything between <paramref name="BodyStart"/> and <paramref name="BodyEnd"/>.</param>
internal sealed record PdfObject(int Number, int Offset, int BodyStart, int BodyEnd, string Body);

/// <summary>
/// The parts of a PDF that an incremental update or a verification needs: the object table, the
/// trailer, the page tree, and the signature dictionaries.
/// </summary>
/// <remarks>
/// The reader scans for <c>N 0 obj</c> headers rather than following the cross-reference table. An
/// incremental update appends a newer definition of an object, so the <em>last</em> definition wins:
/// that is the state a reader sees, and it is the state the signature was computed over.
/// </remarks>
internal sealed class PdfDocument
{
    private static readonly Regex ObjectHeader = new(
        @"(?m)^[ \t]*(\d+)[ \t]+0[ \t]+obj\b",
        RegexOptions.CultureInvariant);

    private readonly Dictionary<int, PdfObject> _objects = [];

    private PdfDocument(string text, int previousXref, int rootNumber, int size, string? id)
    {
        Text = text;
        PreviousXref = previousXref;
        RootNumber = rootNumber;
        Size = size;
        Id = id;
    }

    /// <summary>The file, decoded one character per byte (Latin-1).</summary>
    public string Text { get; }

    /// <summary>The offset the previous cross-reference section starts at.</summary>
    public int PreviousXref { get; }

    /// <summary>The catalog number named by the trailer's <c>/Root</c>.</summary>
    public int RootNumber { get; }

    /// <summary>The trailer's <c>/Size</c>, or the highest object number plus one.</summary>
    public int Size { get; }

    /// <summary>The trailer's <c>/ID</c> array, verbatim, or <see langword="null"/>.</summary>
    public string? Id { get; }

    public IReadOnlyDictionary<int, PdfObject> Objects => _objects;

    /// <summary>Reads the trailer and indexes every object in the file.</summary>
    /// <exception cref="PdfSignatureException">The data is not a PDF this code can update.</exception>
    public static PdfDocument Parse(string pdf)
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

        var root = Regex.Match(trailer, @"/Root\s+(\d+)\s+0\s+R", RegexOptions.CultureInvariant);
        if (!root.Success)
        {
            throw new PdfSignatureException("the PDF trailer names no /Root");
        }

        var rootNumber = int.Parse(root.Groups[1].Value, CultureInfo.InvariantCulture);

        var sizeMatch = Regex.Match(trailer, @"/Size\s+(\d+)", RegexOptions.CultureInvariant);
        var size = sizeMatch.Success
            ? int.Parse(sizeMatch.Groups[1].Value, CultureInfo.InvariantCulture)
            : rootNumber + 1;

        var id = Regex.Match(trailer, @"/ID\s*(\[[^\]]*\])", RegexOptions.CultureInvariant);

        var document = new PdfDocument(pdf, previousXref, rootNumber, size, id.Success ? id.Groups[1].Value : null);
        document.Index();

        if (!document._objects.ContainsKey(rootNumber))
        {
            throw new PdfSignatureException($"object {rootNumber} is missing from the PDF");
        }

        return document;
    }

    /// <summary>The object, or an exception naming the number that is missing.</summary>
    public PdfObject Require(int number)
        => _objects.TryGetValue(number, out var value)
            ? value
            : throw new PdfSignatureException($"object {number} is missing from the PDF");

    /// <summary>The catalog dictionary, as written in the last revision.</summary>
    public string Catalog => Require(RootNumber).Body;

    /// <summary>The first object number no object in the file uses.</summary>
    public int NextObjectNumber()
    {
        var highest = 0;
        foreach (var number in _objects.Keys)
        {
            highest = Math.Max(highest, number);
        }

        return Math.Max(Size, highest + 1);
    }

    /// <summary>
    /// The page objects, in reading order, by following <c>/Kids</c> from the catalog's page tree.
    /// </summary>
    public IReadOnlyList<int> PageNumbers()
    {
        var pages = new List<int>();

        if (PdfSyntax.TryReference(Catalog, "Pages", out var node))
        {
            WalkPageTree(node, pages, []);
        }

        return pages;
    }

    private void WalkPageTree(int number, List<int> pages, HashSet<int> visited)
    {
        if (!_objects.TryGetValue(number, out var node) || !visited.Add(number))
        {
            return;
        }

        if (PdfSyntax.ArrayBody(node.Body, "Kids") is { } kids)
        {
            foreach (Match match in Regex.Matches(kids, @"(\d+)\s+0\s+R", RegexOptions.CultureInvariant))
            {
                WalkPageTree(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), pages, visited);
            }

            return;
        }

        pages.Add(number);
    }

    /// <summary>
    /// Every signature dictionary in the file, oldest first: the objects that carry a
    /// <c>/ByteRange</c>. A document timestamp is a signature dictionary too.
    /// </summary>
    public IReadOnlyList<PdfSignatureDictionary> SignatureDictionaries()
    {
        var found = new List<PdfSignatureDictionary>();

        foreach (var entry in _objects.Values)
        {
            var body = entry.Body;
            if (!body.Contains("/ByteRange", StringComparison.Ordinal))
            {
                continue;
            }

            var range = Regex.Match(body, @"/ByteRange\s*\[\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s*\]", RegexOptions.CultureInvariant);
            if (!range.Success)
            {
                continue;
            }

            var contents = Regex.Match(body, @"/Contents\s*<([0-9A-Fa-f\s]*)>", RegexOptions.CultureInvariant);
            if (!contents.Success)
            {
                continue;
            }

            found.Add(new PdfSignatureDictionary(
                entry.Number,
                entry.Offset,
                [
                    long.Parse(range.Groups[1].Value, CultureInfo.InvariantCulture),
                    long.Parse(range.Groups[2].Value, CultureInfo.InvariantCulture),
                    long.Parse(range.Groups[3].Value, CultureInfo.InvariantCulture),
                    long.Parse(range.Groups[4].Value, CultureInfo.InvariantCulture),
                ],
                entry.BodyStart + contents.Groups[1].Index,
                contents.Groups[1].Length,
                PdfSyntax.Name(body, "Type") is "DocTimeStamp",
                PdfSyntax.Text(body, "M")));
        }

        found.Sort((left, right) => left.Offset.CompareTo(right.Offset));
        return found;
    }

    /// <summary>The bytes of a stream object, using its <c>/Length</c>.</summary>
    /// <remarks>
    /// The length is authoritative: trimming trailing whitespace instead would eat a DER structure
    /// whose signature happens to end in <c>0x0A</c> or <c>0x0D</c>, one time in a hundred and
    /// twenty-eight, which is exactly the kind of defect that only shows up in production.
    /// </remarks>
    public static ReadOnlySpan<byte> StreamBytes(PdfObject entry)
    {
        var keyword = entry.Body.IndexOf("stream", StringComparison.Ordinal);
        if (keyword < 0)
        {
            return default;
        }

        var start = keyword + "stream".Length;
        if (start < entry.Body.Length && entry.Body[start] == '\r')
        {
            start++;
        }

        if (start < entry.Body.Length && entry.Body[start] == '\n')
        {
            start++;
        }

        var end = entry.Body.IndexOf("endstream", start, StringComparison.Ordinal);
        if (end < 0)
        {
            return default;
        }

        var length = PdfSyntax.Integer(entry.Body.Substring(0, keyword), "Length");
        if (length is { } known && known >= 0 && start + known <= end)
        {
            return System.Text.Encoding.Latin1.GetBytes(entry.Body.Substring(start, known));
        }

        var body = entry.Body.Substring(start, end - start).TrimEnd('\n', '\r');
        return System.Text.Encoding.Latin1.GetBytes(body);
    }

    private void Index()
    {
        foreach (Match match in ObjectHeader.Matches(Text))
        {
            var number = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var bodyStart = Text.IndexOf('<', match.Index + match.Length);
            if (bodyStart < 0)
            {
                continue;
            }

            var bodyEnd = Text.IndexOf("endobj", bodyStart, StringComparison.Ordinal);
            if (bodyEnd < 0)
            {
                throw new PdfSignatureException($"object {number} is not closed");
            }

            _objects[number] = new PdfObject(
                number,
                match.Index,
                bodyStart,
                bodyEnd,
                Text.Substring(bodyStart, bodyEnd - bodyStart).TrimEnd());
        }
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

/// <summary>A signature dictionary: the <c>/ByteRange</c> it declares and where its <c>/Contents</c> sit.</summary>
/// <param name="Number">The object number.</param>
/// <param name="Offset">The offset of the object header.</param>
/// <param name="ByteRange">The four numbers of <c>/ByteRange</c>.</param>
/// <param name="ContentsStart">The offset of the first hex digit of <c>/Contents</c>.</param>
/// <param name="ContentsLength">The number of hex digits.</param>
/// <param name="IsDocumentTimestamp">Whether the dictionary declares <c>/Type /DocTimeStamp</c>.</param>
/// <param name="ClaimedTime">The raw <c>/M</c> value, or <see langword="null"/>.</param>
internal sealed record PdfSignatureDictionary(
    int Number,
    int Offset,
    long[] ByteRange,
    int ContentsStart,
    int ContentsLength,
    bool IsDocumentTimestamp,
    string? ClaimedTime);
