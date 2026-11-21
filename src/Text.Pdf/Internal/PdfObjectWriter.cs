using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// Writes a set of indirect objects as a complete PDF: header, objects, a classic cross-reference
/// table and a trailer. Object numbers are handed out by the writer, so a caller that rebuilds a
/// document never has to reason about numbering, and every stream's <c>/Length</c> is written from
/// the bytes it actually carries.
/// </summary>
/// <remarks>
/// The writer emits one cross-reference subsection covering <c>0</c> to <c>/Size - 1</c>. That is
/// the plainest form the format allows and every reader accepts it; a file this writer produces has
/// no incremental history, no object stream and no cross-reference stream, which is what makes the
/// output of a merge or an extraction something a strict reader can open without repair heuristics.
/// </remarks>
internal sealed class PdfObjectWriter
{
    private readonly SortedDictionary<int, PdfValue> _objects = [];
    private int _next = 1;

    /// <summary>The number the next reserved object will get.</summary>
    public int Count => _next - 1;

    /// <summary>Reserves an object number whose value will be set later.</summary>
    public int Reserve() => _next++;

    /// <summary>Stores <paramref name="value"/> under a number obtained from <see cref="Reserve"/>.</summary>
    public void Set(int number, PdfValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (number < 1 || number >= _next)
        {
            throw new ArgumentOutOfRangeException(nameof(number), number, "the object number was not reserved");
        }

        _objects[number] = value;
    }

    /// <summary>Appends <paramref name="value"/> as a new object and returns its number.</summary>
    public int Add(PdfValue value)
    {
        var number = Reserve();
        Set(number, value);

        return number;
    }

    /// <summary>
    /// Appends a stream, writing <c>/Length</c> from <paramref name="data"/> over whatever the
    /// dictionary declared.
    /// </summary>
    public int AddStream(PdfDictionary dictionary, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(data);

        dictionary.Entries["Length"] = new PdfNumber(data.Length);

        return Add(new PdfStream(dictionary, data));
    }

    /// <summary>The value stored under <paramref name="number"/>, or <see langword="null"/>.</summary>
    public PdfValue? Get(int number) => _objects.TryGetValue(number, out var value) ? value : null;

    /// <summary>Builds the file, with <paramref name="rootNumber"/> as the trailer's <c>/Root</c>.</summary>
    /// <param name="rootNumber">The catalog's object number.</param>
    /// <param name="major">The major version of the header.</param>
    /// <param name="minor">The minor version of the header.</param>
    /// <param name="trailerEntries">
    /// Entries to write in the trailer besides <c>/Size</c> and <c>/Root</c>, such as <c>/Info</c>,
    /// <c>/ID</c> or <c>/Encrypt</c>. They are written in ordinal order of their keys.
    /// </param>
    public byte[] Build(
        int rootNumber,
        int major = 1,
        int minor = 7,
        IReadOnlyDictionary<string, PdfValue>? trailerEntries = null)
    {
        using var output = new MemoryStream();

        WriteAscii(output, string.Create(
            CultureInfo.InvariantCulture,
            $"%PDF-{major}.{minor}\n"));

        output.Write([0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A]);

        var offsets = new SortedDictionary<int, int>();

        for (var number = 1; number < _next; number++)
        {
            // A reserved number that was never filled is written as the null object rather than
            // left out: a reference to it then resolves to null instead of to a missing object.
            var value = Get(number) ?? PdfValue.Null;

            offsets[number] = (int)output.Length;
            WriteAscii(output, number.ToString(CultureInfo.InvariantCulture) + " 0 obj\n");
            WriteValue(output, value, 0);
            WriteAscii(output, "\nendobj\n");
        }

        var xref = (int)output.Length;
        var size = _next;

        WriteAscii(output, string.Create(CultureInfo.InvariantCulture, $"xref\n0 {size}\n"));
        WriteAscii(output, "0000000000 65535 f \n");

        for (var number = 1; number < size; number++)
        {
            var offset = offsets.TryGetValue(number, out var written) ? written : 0;
            WriteAscii(output, offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        }

        WriteAscii(output, string.Create(
            CultureInfo.InvariantCulture,
            $"trailer\n<</Size {size}/Root {rootNumber} 0 R"));

        if (trailerEntries is not null)
        {
            foreach (var key in SortedKeys(trailerEntries))
            {
                WriteAscii(output, "/" + EscapeName(key) + " ");
                WriteValue(output, trailerEntries[key], 0);
            }
        }

        WriteAscii(output, string.Create(
            CultureInfo.InvariantCulture,
            $">>\nstartxref\n{xref}\n%%EOF\n"));

        return output.ToArray();
    }

    private static List<string> SortedKeys(IReadOnlyDictionary<string, PdfValue> dictionary)
    {
        var keys = new List<string>(dictionary.Keys);
        keys.Sort(StringComparer.Ordinal);

        return keys;
    }

    private static void WriteAscii(Stream output, string text)
    {
        var bytes = Encoding.Latin1.GetBytes(text);
        output.Write(bytes);
    }

    private static void WriteValue(Stream output, PdfValue value, int depth)
    {
        if (depth > 128)
        {
            throw new PdfOperationException("the object graph is nested too deeply to be written");
        }

        switch (value)
        {
            case PdfNullValue:
                WriteAscii(output, "null");
                break;
            case PdfBoolean boolean:
                WriteAscii(output, boolean.Value ? "true" : "false");
                break;
            case PdfNumber number:
                WriteAscii(output, PdfSyntax.Number(number.Value));
                break;
            case PdfName name:
                WriteAscii(output, "/" + EscapeName(name.Value));
                break;
            case PdfString text:
                WriteAscii(output, "<" + Convert.ToHexString(text.Bytes) + ">");
                break;
            case PdfArray array:
                WriteAscii(output, "[");
                for (var index = 0; index < array.Items.Count; index++)
                {
                    if (index > 0)
                    {
                        WriteAscii(output, " ");
                    }

                    WriteValue(output, array.Items[index], depth + 1);
                }

                WriteAscii(output, "]");
                break;
            case PdfDictionary dictionary:
                WriteDictionary(output, dictionary, depth);
                break;
            case PdfReference reference:
                WriteAscii(output, string.Create(
                    CultureInfo.InvariantCulture,
                    $"{reference.Number} 0 R"));
                break;
            case PdfStream stream:
                WriteDictionary(output, stream.Dictionary, depth);
                WriteAscii(output, "\nstream\n");
                output.Write(stream.Data);
                WriteAscii(output, "\nendstream");
                break;
            default:
                throw new PdfOperationException(
                    $"the object of type {value.GetType().Name} cannot be written");
        }
    }

    private static void WriteDictionary(Stream output, PdfDictionary dictionary, int depth)
    {
        WriteAscii(output, "<<");

        foreach (var key in SortedKeys(dictionary))
        {
            WriteAscii(output, "/" + EscapeName(key) + " ");
            WriteValue(output, dictionary.Entries[key], depth + 1);
        }

        WriteAscii(output, ">>");
    }

    private static List<string> SortedKeys(PdfDictionary dictionary)
    {
        var keys = new List<string>(dictionary.Entries.Keys);
        keys.Sort(StringComparer.Ordinal);

        return keys;
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
