using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// The text surgery an incremental update needs before it can append anything: finding the value of a
/// key in a dictionary body, replacing it, inserting an entry, and writing a value read from the file
/// back out.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PdfAppend"/> writes the objects; this finds the spans they must replace. A dictionary
/// body is the text between the <c>&lt;&lt;</c> that opens it and the <c>endobj</c> that closes the
/// object, so editing it as text leaves every other byte of the revision where it was — which is the
/// point of an incremental update: a signature over the earlier bytes stays valid.
/// </para>
/// <para>
/// The scan is syntax, not semantics: it knows where a value starts and ends (a dictionary, a hex
/// string, a literal string with escapes, an array, a number that is the head of an indirect
/// reference) and nothing else. That is enough to restate one entry of a dictionary a producer wrote,
/// which is all an update ever needs.
/// </para>
/// </remarks>
internal static class PdfIncrementalUpdate
{
    /// <summary>Where a key and its value sit in a dictionary body.</summary>
    /// <param name="KeyStart">The offset of the <c>/</c> that starts the key.</param>
    /// <param name="ValueStart">The offset of the first character of the value.</param>
    /// <param name="ValueLength">How many characters the value takes.</param>
    public readonly record struct Located(int KeyStart, int ValueStart, int ValueLength);

    /// <summary>Finds <paramref name="key"/> and the span of the value that follows it.</summary>
    /// <param name="body">The dictionary body.</param>
    /// <param name="key">The key, without its slash.</param>
    /// <returns>The span, or <see langword="null"/> when the dictionary does not carry the key.</returns>
    public static Located? Locate(string body, string key)
    {
        var match = Regex.Match(
            body,
            "/" + Regex.Escape(key) + @"(?![A-Za-z0-9])\s*",
            RegexOptions.CultureInvariant);

        if (!match.Success)
        {
            return null;
        }

        var start = match.Index + match.Length;
        var end = EndOfValue(body, start);

        return end < 0 ? null : new Located(match.Index, start, end - start);
    }

    /// <summary>The index just past the value that starts at <paramref name="start"/>, or -1.</summary>
    /// <param name="body">The text to scan.</param>
    /// <param name="start">Where the value starts.</param>
    public static int EndOfValue(string body, int start)
    {
        while (start < body.Length && char.IsWhiteSpace(body[start]))
        {
            start++;
        }

        if (start >= body.Length)
        {
            return -1;
        }

        switch (body[start])
        {
            case '<' when IsDictionaryAt(body, start):
                var close = PdfSyntax.CloseDictionary(body, start);
                return close < 0 ? -1 : close + 2;
            case '<':
                var hex = body.IndexOf('>', start);
                return hex < 0 ? -1 : hex + 1;
            case '(':
                return LiteralEnd(body, start);
            case '[':
                return ArrayEnd(body, start);
            case ']' or '>' or ')':
                return -1;
            default:
                var index = start;
                while (index < body.Length
                    && !char.IsWhiteSpace(body[index])
                    && body[index] is not ('<' or '>' or '[' or ']' or '(' or ')' or '/' or '%'))
                {
                    index++;
                }

                // A number followed by "gen R" is one indirect reference, not three values: replacing
                // the value of a key must take "12 0 R" whole, or the rest is left behind as garbage.
                if (char.IsAsciiDigit(body[start]))
                {
                    var reference = Regex.Match(
                        body.AsSpan(index).ToString(),
                        @"^\s+\d+\s+R(?![A-Za-z0-9])",
                        RegexOptions.CultureInvariant);

                    if (reference.Success)
                    {
                        index += reference.Length;
                    }
                }

                return index;
        }
    }

    /// <summary>Whether a dictionary opens at <paramref name="start"/>.</summary>
    /// <param name="body">The text to scan.</param>
    /// <param name="start">Where the value starts.</param>
    public static bool IsDictionaryAt(string body, int start)
        => start + 1 < body.Length && body[start] == '<' && body[start + 1] == '<';

    /// <summary>Replaces <paramref name="length"/> characters at <paramref name="start"/>; a zero length inserts.</summary>
    /// <param name="body">The text to edit.</param>
    /// <param name="start">Where the replacement starts.</param>
    /// <param name="length">How many characters it replaces.</param>
    /// <param name="value">What it becomes.</param>
    public static string Replace(string body, int start, int length, string value)
        => string.Concat(body.AsSpan(0, start), value, body.AsSpan(start + length));

    /// <summary>Inserts <paramref name="value"/> before the <c>&gt;&gt;</c> that closes the dictionary the body holds.</summary>
    /// <param name="body">The dictionary body.</param>
    /// <param name="value">The entry to add.</param>
    /// <exception cref="PdfOperationException">The body is not a dictionary this writer can update.</exception>
    public static string InsertBeforeClose(string body, string value)
    {
        var open = body.IndexOf("<<", StringComparison.Ordinal);
        var close = open < 0 ? -1 : PdfSyntax.CloseDictionary(body, open);

        if (close < 0)
        {
            throw new PdfOperationException("a dictionary of the document is not text this writer can update");
        }

        return Replace(body, close, 0, value);
    }

    /// <summary>
    /// Writes a value read from the file back out, so a dictionary an update must copy survives it.
    /// </summary>
    /// <param name="value">The value to write.</param>
    /// <returns>The value as PDF syntax.</returns>
    /// <exception cref="PdfOperationException">
    /// The value is a stream, which this writer does not restate: a stream's bytes live outside the
    /// dictionary that describes it, and none of the dictionaries an update rewrites holds one.
    /// </exception>
    public static string Serialize(PdfValue value) => value switch
    {
        PdfReference reference => reference.Number.ToString(CultureInfo.InvariantCulture) + " 0 R",
        PdfDictionary dictionary => "<<"
            + string.Concat(dictionary.Entries.Select(entry => "/" + entry.Key + " " + Serialize(entry.Value)))
            + ">>",
        PdfArray array => "[" + string.Join(" ", array.Items.Select(Serialize)) + "]",
        PdfString text => "<" + Convert.ToHexString(text.Bytes) + ">",
        PdfName name => "/" + name.Value,
        PdfBoolean boolean => boolean.Value ? "true" : "false",
        PdfNumber number => number.ToString(),
        PdfNullValue => "null",
        _ => throw new PdfOperationException(
            $"a dictionary this writer must copy holds a {value.GetType().Name}, which it cannot write into an update"),
    };

    /// <summary>The index just past the literal string that starts at <paramref name="start"/>, or -1.</summary>
    private static int LiteralEnd(string body, int start)
    {
        var depth = 0;

        for (var index = start; index < body.Length; index++)
        {
            var current = body[index];

            if (current == '\\')
            {
                index++;
                continue;
            }

            if (current == '(')
            {
                depth++;
                continue;
            }

            if (current == ')' && --depth == 0)
            {
                return index + 1;
            }
        }

        return -1;
    }

    /// <summary>The index just past the array that starts at <paramref name="start"/>, or -1.</summary>
    private static int ArrayEnd(string body, int start)
    {
        var index = start + 1;

        while (index < body.Length)
        {
            while (index < body.Length && char.IsWhiteSpace(body[index]))
            {
                index++;
            }

            if (index >= body.Length)
            {
                return -1;
            }

            if (body[index] == ']')
            {
                return index + 1;
            }

            var end = EndOfValue(body, index);
            if (end <= index)
            {
                return -1;
            }

            index = end;
        }

        return -1;
    }
}
