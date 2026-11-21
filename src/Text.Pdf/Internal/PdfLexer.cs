using System;
using System.Globalization;
using System.Text;

namespace M3scalinum.Text.Pdf;

/// <summary>The lexer and object builder for <see cref="PdfFile"/>.</summary>
internal sealed partial class PdfFile
{
    /// <summary>The deepest object nesting the reader accepts.</summary>
    private const int MaxDepth = 64;

    private static bool IsWhitespace(byte value) => value is 0 or 9 or 10 or 12 or 13 or 32;

    private static bool IsDelimiter(byte value)
        => value is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']'
            or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';

    private static bool IsSeparator(byte value) => IsWhitespace(value) || IsDelimiter(value);

    private static void SkipWhitespace(ref Cursor cursor)
    {
        while (cursor.Position < cursor.Data.Length)
        {
            var value = cursor.Data[cursor.Position];

            if (IsWhitespace(value))
            {
                cursor.Position++;
                continue;
            }

            if (value == (byte)'%')
            {
                while (cursor.Position < cursor.Data.Length && cursor.Data[cursor.Position] is not (10 or 13))
                {
                    cursor.Position++;
                }

                continue;
            }

            return;
        }
    }

    private static bool TryReadKeyword(ref Cursor cursor, ReadOnlySpan<byte> keyword)
    {
        SkipWhitespace(ref cursor);

        if (cursor.Position + keyword.Length > cursor.Data.Length ||
            !cursor.Data.AsSpan(cursor.Position, keyword.Length).SequenceEqual(keyword))
        {
            return false;
        }

        var after = cursor.Position + keyword.Length;
        if (after < cursor.Data.Length && !IsSeparator(cursor.Data[after]))
        {
            return false;
        }

        cursor.Position = after;
        return true;
    }

    private static bool TryReadInteger(ref Cursor cursor, out long value)
    {
        SkipWhitespace(ref cursor);

        var start = cursor.Position;
        var position = start;

        while (position < cursor.Data.Length && cursor.Data[position] is >= (byte)'0' and <= (byte)'9')
        {
            position++;
        }

        if (position == start || position - start > 18)
        {
            value = 0;
            return false;
        }

        value = 0;
        for (var i = start; i < position; i++)
        {
            value = (value * 10) + (cursor.Data[i] - '0');
        }

        cursor.Position = position;
        return true;
    }

    private static ReadOnlySpan<byte> ReadToken(ref Cursor cursor)
    {
        SkipWhitespace(ref cursor);

        var start = cursor.Position;
        while (cursor.Position < cursor.Data.Length && !IsSeparator(cursor.Data[cursor.Position]))
        {
            cursor.Position++;
        }

        return cursor.Data.AsSpan(start, cursor.Position - start);
    }

    private static PdfValue ParseValue(ref Cursor cursor, int depth)
    {
        if (depth > MaxDepth)
        {
            throw new PdfFormatException($"an object is nested more than {MaxDepth} levels deep");
        }

        SkipWhitespace(ref cursor);

        if (cursor.AtEnd)
        {
            return PdfValue.Null;
        }

        var value = cursor.Data[cursor.Position];

        switch (value)
        {
            case (byte)'/':
                return ParseName(ref cursor);
            case (byte)'(':
                return ParseLiteralString(ref cursor);
            case (byte)'[':
                return ParseArray(ref cursor, depth);
            case (byte)'<':
                if (cursor.Position + 1 < cursor.Data.Length && cursor.Data[cursor.Position + 1] == (byte)'<')
                {
                    return ParseDictionary(ref cursor, depth);
                }

                return ParseHexString(ref cursor);
            case (byte)'+':
            case (byte)'-':
            case (byte)'.':
            case >= (byte)'0' and <= (byte)'9':
                return ParseNumberOrReference(ref cursor);
        }

        var token = ReadToken(ref cursor);
        if (token.SequenceEqual("true"u8))
        {
            return new PdfBoolean(true);
        }

        if (token.SequenceEqual("false"u8))
        {
            return new PdfBoolean(false);
        }

        if (token.SequenceEqual("null"u8))
        {
            return PdfValue.Null;
        }

        throw new PdfFormatException($"byte {cursor.Position} begins an unexpected token");
    }

    private static PdfName ParseName(ref Cursor cursor)
    {
        // The leading slash is at the current position.
        cursor.Position++;

        var bytes = new System.Collections.Generic.List<byte>();
        while (cursor.Position < cursor.Data.Length && !IsSeparator(cursor.Data[cursor.Position]))
        {
            var value = cursor.Data[cursor.Position++];

            if (value == (byte)'#' &&
                cursor.Position + 1 < cursor.Data.Length &&
                char.IsAsciiHexDigit((char)cursor.Data[cursor.Position]) &&
                char.IsAsciiHexDigit((char)cursor.Data[cursor.Position + 1]))
            {
                var text = Encoding.ASCII.GetString(cursor.Data, cursor.Position, 2);
                bytes.Add(byte.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                cursor.Position += 2;
                continue;
            }

            bytes.Add(value);
        }

        return new PdfName(Encoding.Latin1.GetString(bytes.ToArray()));
    }

    private static PdfString ParseLiteralString(ref Cursor cursor)
    {
        // The opening parenthesis is at the current position.
        cursor.Position++;

        var bytes = new System.Collections.Generic.List<byte>();
        var nesting = 1;

        while (cursor.Position < cursor.Data.Length)
        {
            var value = cursor.Data[cursor.Position++];

            switch (value)
            {
                case (byte)'\\':
                    if (cursor.AtEnd)
                    {
                        break;
                    }

                    var escaped = cursor.Data[cursor.Position++];
                    switch (escaped)
                    {
                        case (byte)'n':
                            bytes.Add(10);
                            break;
                        case (byte)'r':
                            bytes.Add(13);
                            break;
                        case (byte)'t':
                            bytes.Add(9);
                            break;
                        case (byte)'b':
                            bytes.Add(8);
                            break;
                        case (byte)'f':
                            bytes.Add(12);
                            break;
                        case (byte)'(':
                        case (byte)')':
                        case (byte)'\\':
                            bytes.Add(escaped);
                            break;
                        case (byte)'\r':
                            if (!cursor.AtEnd && cursor.Data[cursor.Position] == (byte)'\n')
                            {
                                cursor.Position++;
                            }

                            break;
                        case (byte)'\n':
                            break;
                        case >= (byte)'0' and <= (byte)'7':
                            var octal = escaped - '0';
                            var digits = 1;
                            while (digits < 3 && !cursor.AtEnd && cursor.Data[cursor.Position] is >= (byte)'0' and <= (byte)'7')
                            {
                                octal = (octal * 8) + (cursor.Data[cursor.Position] - '0');
                                cursor.Position++;
                                digits++;
                            }

                            bytes.Add((byte)octal);
                            break;
                        default:
                            bytes.Add(escaped);
                            break;
                    }

                    break;
                case (byte)'(':
                    nesting++;
                    bytes.Add(value);
                    break;
                case (byte)')':
                    nesting--;
                    if (nesting == 0)
                    {
                        return new PdfString(bytes.ToArray());
                    }

                    bytes.Add(value);
                    break;
                default:
                    bytes.Add(value);
                    break;
            }
        }

        throw new PdfFormatException("a literal string is not closed");
    }

    private static PdfString ParseHexString(ref Cursor cursor)
    {
        // The opening angle bracket is at the current position.
        cursor.Position++;

        var digits = new StringBuilder();
        while (cursor.Position < cursor.Data.Length && cursor.Data[cursor.Position] != (byte)'>')
        {
            var value = (char)cursor.Data[cursor.Position++];
            if (IsWhitespace((byte)value))
            {
                continue;
            }

            if (!char.IsAsciiHexDigit(value))
            {
                throw new PdfFormatException($"a hexadecimal string holds '{value}'");
            }

            digits.Append(value);
        }

        if (cursor.AtEnd)
        {
            throw new PdfFormatException("a hexadecimal string is not closed");
        }

        cursor.Position++;

        if (digits.Length % 2 != 0)
        {
            digits.Append('0');
        }

        return new PdfString(Convert.FromHexString(digits.ToString()));
    }

    private static PdfArray ParseArray(ref Cursor cursor, int depth)
    {
        // The opening bracket is at the current position.
        cursor.Position++;

        var array = new PdfArray();
        while (true)
        {
            SkipWhitespace(ref cursor);

            if (cursor.AtEnd)
            {
                throw new PdfFormatException("an array is not closed");
            }

            if (cursor.Data[cursor.Position] == (byte)']')
            {
                cursor.Position++;
                return array;
            }

            array.Items.Add(ParseValue(ref cursor, depth + 1));
        }
    }

    private static PdfDictionary ParseDictionary(ref Cursor cursor, int depth)
    {
        // The opening double angle bracket is at the current position.
        cursor.Position += 2;

        var dictionary = new PdfDictionary();
        while (true)
        {
            SkipWhitespace(ref cursor);

            if (cursor.AtEnd)
            {
                throw new PdfFormatException("a dictionary is not closed");
            }

            if (cursor.Data[cursor.Position] == (byte)'>' &&
                cursor.Position + 1 < cursor.Data.Length &&
                cursor.Data[cursor.Position + 1] == (byte)'>')
            {
                cursor.Position += 2;
                return dictionary;
            }

            if (cursor.Data[cursor.Position] != (byte)'/')
            {
                throw new PdfFormatException($"byte {cursor.Position} begins a dictionary key that is not a name");
            }

            var name = ParseName(ref cursor).Value;
            dictionary.Entries[name] = ParseValue(ref cursor, depth + 1);
        }
    }

    private static PdfValue ParseNumberOrReference(ref Cursor cursor)
    {
        var start = cursor.Position;
        var token = ReadToken(ref cursor);

        var simple = true;
        foreach (var value in token)
        {
            if (value is < (byte)'0' or > (byte)'9')
            {
                simple = false;
                break;
            }
        }

        if (simple)
        {
            var save = cursor.Position;
            if (TryReadInteger(ref cursor, out _) && TryReadKeyword(ref cursor, "R"u8))
            {
                if (long.TryParse(Encoding.ASCII.GetString(token), NumberStyles.None, CultureInfo.InvariantCulture, out var number) &&
                    number is >= 0 and <= int.MaxValue)
                {
                    return new PdfReference((int)number);
                }
            }

            cursor.Position = save;
        }

        cursor.Position = start;
        var text = ReadToken(ref cursor);
        var value2 = double.Parse(Encoding.ASCII.GetString(text), NumberStyles.Float, CultureInfo.InvariantCulture);

        return new PdfNumber(value2);
    }
}
