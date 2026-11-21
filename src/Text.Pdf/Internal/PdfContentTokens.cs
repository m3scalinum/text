using System;
using System.Globalization;
using System.Text;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// Token reading for content streams. A content stream is a sequence of operands followed by an
/// operator, which is not the grammar of a file: an unrecognised bare word there is an operator, not
/// a syntax error. The lexer itself is the file reader's, so a literal string, a hexadecimal string
/// and a name mean the same thing in both places.
/// </summary>
internal sealed partial class PdfFile
{
    /// <summary>
    /// Reads the next token of a content stream. Exactly one of <paramref name="operand"/> and
    /// <paramref name="keyword"/> is set: a number, name, string, array or dictionary is an operand;
    /// anything else is an operator or a bare keyword such as <c>BT</c> or <c>true</c>.
    /// </summary>
    /// <param name="data">The decoded content stream.</param>
    /// <param name="position">Where to read; advanced past the token.</param>
    /// <param name="operand">The operand read, or <see langword="null"/>.</param>
    /// <param name="keyword">The operator read, or <see langword="null"/>.</param>
    internal static bool TryReadContentToken(
        byte[] data,
        ref int position,
        out PdfValue? operand,
        out string? keyword)
    {
        operand = null;
        keyword = null;

        var cursor = new Cursor(data, position);
        SkipWhitespace(ref cursor);

        if (cursor.AtEnd)
        {
            position = cursor.Position;
            return false;
        }

        var before = cursor.Position;

        switch (data[cursor.Position])
        {
            case (byte)'/':
                operand = ParseName(ref cursor);
                break;
            case (byte)'(':
                operand = ParseLiteralString(ref cursor);
                break;
            case (byte)'[':
                operand = ParseArray(ref cursor, 0);
                break;
            case (byte)'<':
                operand = cursor.Position + 1 < data.Length && data[cursor.Position + 1] == (byte)'<'
                    ? ParseDictionary(ref cursor, 0)
                    : ParseHexString(ref cursor);
                break;
            case (byte)'{':
            case (byte)'}':
            case (byte)']':
            case (byte)')':
            case (byte)'>':
                // A stray delimiter closes nothing this reader tracks; consuming it keeps the walk
                // moving instead of reading the same byte forever.
                cursor.Position++;
                keyword = Encoding.Latin1.GetString(data, before, 1);
                break;
            default:
                keyword = ReadOperandOrOperator(ref cursor, out operand);
                break;
        }

        if (cursor.Position == before)
        {
            cursor.Position++;
        }

        position = cursor.Position;
        return true;
    }

    /// <summary>
    /// Reads a bare token and decides whether it is a number, a boolean, <c>null</c> or an operator.
    /// A dictionary or array nested in a content stream is read by the branches above, so this only
    /// sees scalars and operators.
    /// </summary>
    private static string? ReadOperandOrOperator(ref Cursor cursor, out PdfValue? operand)
    {
        operand = null;

        var token = ReadToken(ref cursor);
        if (token.IsEmpty)
        {
            return null;
        }

        if (token.SequenceEqual("true"u8))
        {
            operand = new PdfBoolean(true);
            return null;
        }

        if (token.SequenceEqual("false"u8))
        {
            operand = new PdfBoolean(false);
            return null;
        }

        if (token.SequenceEqual("null"u8))
        {
            operand = PdfValue.Null;
            return null;
        }

        var text = Encoding.Latin1.GetString(token);

        // A content-stream token never names an indirect object, so "1 0 R" cannot occur: the number
        // is parsed on its own rather than through the file reader's reference lookahead.
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            operand = new PdfNumber(number);
            return null;
        }

        return text;
    }
}
