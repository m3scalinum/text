using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// Appends an outline as an incremental update: the new items, a new outline root and the rewritten
/// catalog, then a cross-reference section that chains to the previous one.
/// </summary>
/// <remarks>
/// The file's bytes are kept as they are and the update is appended after them, which is the same
/// shape <see cref="PdfDocumentTimestamp"/> and signing produce: a signature that covers the
/// original revision stays inside its byte range, and a viewer reads the newest catalog. The
/// update is written as a classic cross-reference table even when the previous section was a
/// stream, which the format allows and which costs nothing to accept.
/// </remarks>
internal sealed class PdfOutlineWriter
{
    private readonly StringBuilder _text;
    private readonly Dictionary<int, int> _offsets = [];
    private int _next;

    /// <summary>Starts an update after <paramref name="pdf"/>'s last byte.</summary>
    /// <param name="pdf">The document, one character per byte.</param>
    /// <param name="nextObjectNumber">The first object number the update may use.</param>
    public PdfOutlineWriter(string pdf, int nextObjectNumber)
    {
        _text = new StringBuilder(pdf);

        if (pdf.Length > 0 && pdf[^1] != '\n')
        {
            _text.Append('\n');
        }

        _next = nextObjectNumber;
    }

    /// <summary>Takes the next free object number.</summary>
    public int Reserve() => _next++;

    /// <summary>Appends <c>N 0 obj</c> + <paramref name="body"/> + <c>endobj</c>.</summary>
    /// <param name="number">The object number, obtained from <see cref="Reserve"/>.</param>
    /// <param name="body">The object's text, a dictionary in practice.</param>
    public void Add(int number, string body)
    {
        _offsets[number] = _text.Length;

        _text.Append(number.ToString(CultureInfo.InvariantCulture)).Append(" 0 obj\n")
             .Append(body)
             .Append("\nendobj\n");
    }

    /// <summary>
    /// Closes the update with a cross-reference section and a trailer that chains to the previous
    /// one, and returns the whole file.
    /// </summary>
    /// <param name="rootNumber">The catalog's object number, which the trailer's <c>/Root</c> names.</param>
    /// <param name="previousXref">The offset of the previous cross-reference section.</param>
    /// <param name="identifier">The trailer's <c>/ID</c>, copied so the document keeps its identity.</param>
    public byte[] Finish(int rootNumber, int previousXref, PdfValue? identifier)
    {
        var xrefOffset = _text.Length;
        var numbers = new List<int>(_offsets.Keys);
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
                _text.Append(_offsets[numbers[index + entry]].ToString("D10", CultureInfo.InvariantCulture))
                     .Append(" 00000 n \n");
            }

            index += count;
        }

        _text.Append("trailer\n<</Size ").Append(_next.ToString(CultureInfo.InvariantCulture))
             .Append("/Root ").Append(rootNumber.ToString(CultureInfo.InvariantCulture)).Append(" 0 R")
             .Append("/Prev ").Append(previousXref.ToString(CultureInfo.InvariantCulture));

        if (identifier is not null)
        {
            _text.Append("/ID ").Append(Serialize(identifier));
        }

        _text.Append(">>\nstartxref\n").Append(xrefOffset.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF\n");

        return Encoding.Latin1.GetBytes(_text.ToString());
    }

    /// <summary>A title as a PDF text string: UTF-16BE with a byte-order mark, so a heading keeps its accents.</summary>
    /// <param name="value">The text to encode.</param>
    public static string TextString(string value)
        => "<FEFF" + Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(value)) + ">";

    /// <summary>Restates a value the reader parsed, for a rewrite of the object that holds it.</summary>
    /// <param name="value">The value to write.</param>
    /// <exception cref="PdfOperationException">The value is a stream, which this writer does not restate.</exception>
    public static string Serialize(PdfValue value) => Serialize(value, 0);

    private static string Serialize(PdfValue value, int depth)
    {
        if (depth > 128)
        {
            throw new PdfOperationException("the object graph is nested too deeply to be written");
        }

        switch (value)
        {
            case PdfNullValue:
                return "null";
            case PdfBoolean boolean:
                return boolean.Value ? "true" : "false";
            case PdfNumber number:
                return PdfSyntax.Number(number.Value);
            case PdfName name:
                return "/" + EscapeName(name.Value);
            case PdfString text:
                return "<" + Convert.ToHexString(text.Bytes) + ">";
            case PdfReference reference:
                return string.Create(CultureInfo.InvariantCulture, $"{reference.Number} 0 R");
            case PdfArray array:
                return SerializeArray(array, depth);
            case PdfDictionary dictionary:
                return SerializeDictionary(dictionary, depth);
            case PdfStream:
                throw new PdfOperationException(
                    "the object holds a stream, which an outline update cannot restate; only the catalog is rewritten");
            default:
                throw new PdfOperationException(
                    $"the object of type {value.GetType().Name} cannot be written");
        }
    }

    private static string SerializeArray(PdfArray array, int depth)
    {
        var builder = new StringBuilder("[");

        for (var index = 0; index < array.Items.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(' ');
            }

            builder.Append(Serialize(array.Items[index], depth + 1));
        }

        return builder.Append(']').ToString();
    }

    private static string SerializeDictionary(PdfDictionary dictionary, int depth)
    {
        var keys = new List<string>(dictionary.Entries.Keys);
        keys.Sort(StringComparer.Ordinal);

        var builder = new StringBuilder("<<");

        foreach (var key in keys)
        {
            builder.Append('/').Append(EscapeName(key)).Append(' ')
                   .Append(Serialize(dictionary.Entries[key], depth + 1));
        }

        return builder.Append(">>").ToString();
    }

    /// <summary>Escapes a name so the bytes it carries survive a round trip through the lexer.</summary>
    private static string EscapeName(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            var code = (byte)character;

            if (character > 0x7E ||
                character is < '!' or '#' or '(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '/' or '%')
            {
                builder.Append('#').Append(code.ToString("X2", CultureInfo.InvariantCulture));
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}
