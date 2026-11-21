using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// Builds small PDFs by hand for the reader tests. These are not conformance fixtures — the real
/// compiler's output is what the conformance rules are tested against — but a cross-reference entry
/// pointing one byte off, or a stream with a TIFF predictor, is easier to state than to find.
/// </summary>
internal static class PdfTestDocument
{
    /// <summary>The fragments of a tagged document that PDF/UA-1 requires, one object each.</summary>
    /// <remarks>
    /// Object numbers: 1 catalog, 2 page tree, 3 page, 4 structure tree root, 5 Document element,
    /// 6 paragraph element, 7 parent tree numbers array, 8 document information.
    /// </remarks>
    public static string[] Tagged() =>
    [
        "<</Type/Catalog/Pages 2 0 R/MarkInfo<</Marked true>>/Lang(en)/StructTreeRoot 4 0 R>>",
        "<</Type/Pages/Count 1/Kids[3 0 R]>>",
        "<</Type/Page/Parent 2 0 R/MediaBox[0 0 100 100]/StructParents 0>>",
        "<</Type/StructTreeRoot/K[5 0 R]/ParentTree<</Nums[0 7 0 R]>>>>",
        "<</Type/StructElem/S/Document/P 4 0 R/K[6 0 R]>>",
        "<</Type/StructElem/S/P/P 5 0 R/K[0]>>",
        "[6 0 R]",
        "<</Title(Conformance test)>>",
    ];

    /// <summary>A tagged document with its information dictionary wired into the trailer.</summary>
    public static byte[] TaggedPdf(Action<string[]>? change = null)
    {
        var bodies = Tagged();
        change?.Invoke(bodies);

        return Document(bodies, "/Info 8 0 R");
    }

    /// <summary>A document whose catalog and page tree are the only objects.</summary>
    public static byte[] Minimal() =>
        Document(
        [
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Count 1/Kids[3 0 R]>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 100 100]>>",
        ]);

    /// <summary>
    /// A document with a classic cross-reference table. Objects are numbered from 1 in order; the
    /// catalog is object 1 and is the trailer's <c>/Root</c>.
    /// </summary>
    public static byte[] Document(IReadOnlyList<string> bodies, string trailerExtra = "")
    {
        var text = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int>();

        for (var index = 0; index < bodies.Count; index++)
        {
            offsets.Add(text.Length);
            text.Append((index + 1).ToString(CultureInfo.InvariantCulture))
                .Append(" 0 obj\n")
                .Append(bodies[index])
                .Append("\nendobj\n");
        }

        var xref = text.Length;
        text.Append("xref\n0 ").Append((bodies.Count + 1).ToString(CultureInfo.InvariantCulture)).Append('\n');
        text.Append("0000000000 65535 f \r\n");

        foreach (var offset in offsets)
        {
            text.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \r\n");
        }

        text.Append("trailer\n<</Size ").Append((bodies.Count + 1).ToString(CultureInfo.InvariantCulture))
            .Append("/Root 1 0 R").Append(trailerExtra).Append(">>\nstartxref\n")
            .Append(xref.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF\n");

        return Encoding.Latin1.GetBytes(text.ToString());
    }

    /// <summary>
    /// A document whose cross-reference is a stream. <paramref name="xrefDictionary"/> is written
    /// verbatim as the stream dictionary and may contain <c>{length}</c>, replaced by the byte count
    /// of <paramref name="xrefData"/>; the stream is stored uncompressed, so a test controls the
    /// entries exactly.
    /// </summary>
    public static byte[] XrefStreamDocument(string xrefDictionary, byte[] xrefData, params string[] bodies)
        => XrefStreamDocument(xrefDictionary, xrefData, out _, bodies);

    /// <summary>
    /// The same, also reporting each object's byte offset so a test can write type-1 entries. The
    /// cross-reference stream itself is object <c>bodies.Length + 1</c>.
    /// </summary>
    public static byte[] XrefStreamDocument(
        string xrefDictionary,
        byte[] xrefData,
        out IReadOnlyList<int> offsets,
        params string[] bodies)
    {
        var text = new StringBuilder("%PDF-1.7\n");
        var written = new List<int>();

        for (var index = 0; index < bodies.Length; index++)
        {
            written.Add(text.Length);
            text.Append((index + 1).ToString(CultureInfo.InvariantCulture))
                .Append(" 0 obj\n")
                .Append(bodies[index])
                .Append("\nendobj\n");
        }

        var xrefOffset = text.Length;
        var number = bodies.Length + 1;
        var dictionary = xrefDictionary.Replace(
            "{length}",
            xrefData.Length.ToString(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);

        text.Append(number.ToString(CultureInfo.InvariantCulture))
            .Append(" 0 obj\n")
            .Append(dictionary)
            .Append("\nstream\n");

        var prefix = Encoding.Latin1.GetBytes(text.ToString());
        var suffix = Encoding.Latin1.GetBytes(
            string.Create(
                CultureInfo.InvariantCulture,
                $"\nendstream\nendobj\nstartxref\n{xrefOffset}\n%%EOF\n"));

        offsets = written;

        return [.. prefix, .. xrefData, .. suffix];
    }

    /// <summary>An xref stream entry: one type byte, a four-byte offset and a two-byte generation.</summary>
    public static byte[] StreamEntry(int type, long offset, int generation = 0)
        =>
        [
            (byte)type,
            (byte)(offset >> 24),
            (byte)(offset >> 16),
            (byte)(offset >> 8),
            (byte)offset,
            (byte)(generation >> 8),
            (byte)generation,
        ];

    /// <summary>Copies <paramref name="pdf"/> replacing one occurrence of <paramref name="from"/>.</summary>
    public static byte[] Replace(byte[] pdf, string from, string to)
    {
        var text = Encoding.Latin1.GetString(pdf);
        var index = text.IndexOf(from, StringComparison.Ordinal);

        if (index < 0)
        {
            throw new InvalidOperationException($"the fixture no longer contains '{from}'");
        }

        return Encoding.Latin1.GetBytes(string.Concat(text.AsSpan(0, index), to, text.AsSpan(index + from.Length)));
    }

    /// <summary>A dictionary with the entries a filter test needs.</summary>
    public static PdfDictionary Dictionary(params (string Key, PdfValue Value)[] entries)
    {
        var dictionary = new PdfDictionary();

        foreach (var (key, value) in entries)
        {
            dictionary.Entries[key] = value;
        }

        return dictionary;
    }

}
