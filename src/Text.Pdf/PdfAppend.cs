using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// Builds the block an incremental update appends: new and rewritten objects, then a
/// cross-reference section that chains to the previous one.
/// </summary>
/// <remarks>
/// Object numbers may be added in any order and rewritten: the cross-reference section is written
/// in ascending number order, in one subsection per run of consecutive numbers, so an update that
/// rewrites the catalog (object 1) and appends objects 12 to 20 produces two subsections and marks
/// nothing free.
/// </remarks>
internal sealed class PdfAppend
{
    private readonly StringBuilder _text;
    private readonly Dictionary<int, int> _entries = [];

    /// <summary>Starts an update on <paramref name="pdf"/>, appending after its last byte.</summary>
    public PdfAppend(string pdf)
    {
        _text = new StringBuilder(pdf);
        if (!pdf.EndsWith('\n'))
        {
            _text.Append('\n');
        }
    }

    /// <summary>Where the next object would start, which is also the length of the built file.</summary>
    public int Length => _text.Length;

    /// <summary>Appends <c>N 0 obj</c> + <paramref name="body"/> + <c>endobj</c>, and returns its offset.</summary>
    public int Add(int number, string body)
    {
        var offset = _text.Length;

        _text.Append(number.ToString(CultureInfo.InvariantCulture)).Append(" 0 obj\n")
             .Append(body)
             .Append("\nendobj\n");

        // A rewrite replaces the entry: the earlier definition stays in the file as an orphan, and
        // the cross-reference section points at the newest one.
        _entries[number] = offset;

        return offset;
    }

    /// <summary>The file as it stands.</summary>
    public string Text => _text.ToString();

    /// <summary>
    /// Closes the update with a cross-reference section and a trailer that chains to the previous
    /// one, and returns the whole file.
    /// </summary>
    /// <param name="document">The revision the update was built on.</param>
    /// <param name="size">The trailer's <c>/Size</c>: one past the highest object number.</param>
    public string Finish(PdfDocument document, int size)
    {
        var xrefOffset = _text.Length;
        var numbers = new List<int>(_entries.Keys);
        numbers.Sort();

        _text.Append("xref\n");

        var index = 0;
        while (index < numbers.Count)
        {
            var start = numbers[index];
            var count = 1;

            while (index + count < numbers.Count && numbers[index + count] == start + count)
            {
                count++;
            }

            _text.Append(start.ToString(CultureInfo.InvariantCulture)).Append(' ')
                 .Append(count.ToString(CultureInfo.InvariantCulture)).Append('\n');

            for (var entry = 0; entry < count; entry++)
            {
                _text.Append(_entries[numbers[index + entry]].ToString("D10", CultureInfo.InvariantCulture))
                     .Append(" 00000 n \n");
            }

            index += count;
        }

        _text.Append("trailer\n<</Size ").Append(size.ToString(CultureInfo.InvariantCulture))
             .Append("/Root ").Append(document.RootNumber.ToString(CultureInfo.InvariantCulture)).Append(" 0 R")
             .Append("/Prev ").Append(document.PreviousXref.ToString(CultureInfo.InvariantCulture));

        if (document.Id is { } id)
        {
            _text.Append("/ID ").Append(id);
        }

        _text.Append(">>\nstartxref\n").Append(xrefOffset.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF\n");

        return _text.ToString();
    }
}
