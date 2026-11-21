using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using M3scalinum.Text.Pdf;

namespace M3scalinum.Text.Mcp.Internal;

/// <summary>
/// Reads the text a PDF draws, one page at a time, from the content streams and the fonts'
/// <c>/ToUnicode</c> maps.
/// </summary>
/// <remarks>
/// <para>
/// This is the reverse direction of every other package in the suite: a PDF arriving at the server
/// was produced somewhere else, so it is parsed, not trusted. Objects are located by scanning the
/// file for <c>N 0 obj</c> headers rather than by following the cross-reference table — a table an
/// arbitrary producer wrote may be wrong, and a scan is what makes the reader survive it.
/// </para>
/// <para>
/// The page tree is walked in <c>/Kids</c> order, so two documents of the same pages read in the
/// order a viewer shows them. Text is decoded through the font's <c>/ToUnicode</c> map; a simple
/// font without one is read as single-byte WinAnsi, which is what it declares. A run of glyphs with
/// neither is skipped rather than guessed at, and a font that uses a stream filter this reader does
/// not implement — anything but <c>FlateDecode</c> — fails with the filter named, so a caller learns
/// why the text is missing instead of receiving an empty page.
/// </para>
/// <para>
/// Words are recovered from the positioning operators: a run the sender moved down starts a new
/// line, a run that moved right is separated by a space. That is enough to read a table row and a
/// sentence; it is not a layout engine, and a document that draws letters one at a time will read
/// with a space between them.
/// </para>
/// </remarks>
internal static class PdfPageText
{
    private static readonly byte[] Header = "%PDF-"u8.ToArray();

    /// <summary>The text drawn by each page, in page order.</summary>
    /// <exception cref="PdfOperationException">
    /// The bytes are not a PDF, are encrypted, or use a stream or font this reader cannot decode.
    /// </exception>
    internal static IReadOnlyList<string> Pages(ReadOnlySpan<byte> pdf)
    {
        var document = PdfTextDocument.Parse(pdf);
        return document.Pages();
    }

    /// <summary>The number of pages the document declares, read from its page tree.</summary>
    /// <exception cref="PdfOperationException">The bytes are not a PDF, or it has no page tree.</exception>
    internal static int PageCount(ReadOnlySpan<byte> pdf) => PdfTextDocument.Parse(pdf).Pages().Count;

    /// <summary>Whether the bytes start with a PDF header, which is the cheapest refusal available.</summary>
    /// <param name="pdf">The bytes a caller supplied.</param>
    internal static bool LooksLikePdf(ReadOnlySpan<byte> pdf)
        => pdf.Length >= Header.Length && pdf[..Header.Length].SequenceEqual(Header);
}

/// <summary>One parsed PDF: its objects, its page order, and the text each page draws.</summary>
internal sealed class PdfTextDocument
{
    private static readonly Regex ObjectHeader = new(
        @"(?<![0-9])(\d+)\s+\d+\s+obj\b",
        RegexOptions.CultureInvariant);

    private static readonly Regex StreamStart = new(
        @"\bstream\r?\n",
        RegexOptions.CultureInvariant);

    private static readonly Regex TypeEntry = new(
        @"/Type\s*/([A-Za-z]+)",
        RegexOptions.CultureInvariant);

    private readonly string _text;
    private readonly Dictionary<int, PdfTextObject> _objects = [];
    private readonly Dictionary<int, string?> _decoded = [];

    private PdfTextDocument(string text)
    {
        _text = text;
        Collect();
    }

    /// <summary>Parses the bytes, locating every object they carry.</summary>
    /// <exception cref="PdfOperationException">The bytes are not a PDF, or are encrypted.</exception>
    internal static PdfTextDocument Parse(ReadOnlySpan<byte> pdf)
    {
        if (!PdfPageText.LooksLikePdf(pdf))
        {
            throw new PdfOperationException("the bytes do not start with a PDF header");
        }

        // Every producer in this family writes its strings as Latin-1, and a byte read as a
        // character is what makes offsets and stream lengths line up with the file.
        var text = Encoding.Latin1.GetString(pdf);

        if (Regex.IsMatch(text, @"/Encrypt\s+\d+\s+0\s+R", RegexOptions.CultureInvariant))
        {
            throw new PdfOperationException(
                "the document is encrypted, so its text cannot be read without its key");
        }

        return new PdfTextDocument(text);
    }

    /// <summary>The text of every page, in <c>/Kids</c> order.</summary>
    /// <exception cref="PdfOperationException">A page cannot be read.</exception>
    internal IReadOnlyList<string> Pages()
    {
        var pages = PageOrder();
        var result = new List<string>(pages.Count);

        foreach (var page in pages)
        {
            result.Add(Page(page));
        }

        return result;
    }

    /// <summary>Walks the page tree from the catalog, falling back to the objects that call themselves pages.</summary>
    private List<int> PageOrder()
    {
        var ordered = new List<int>();
        int? root = null;

        foreach (var entry in _objects)
        {
            if (Has(entry.Value.Dictionary, "Catalog"))
            {
                root = entry.Key;
                break;
            }
        }

        if (root is { } catalogNumber
            && Dictionary(catalogNumber) is { } catalog
            && Reference(catalog, "Pages") is { } pages)
        {
            Walk(pages, ordered, 0);
        }

        if (ordered.Count == 0)
        {
            ordered.AddRange(_objects
                .Where(entry => Has(entry.Value.Dictionary, "Page"))
                .Select(entry => entry.Key)
                .OrderBy(number => number));
        }

        return ordered;
    }

    private void Walk(int number, List<int> ordered, int depth)
    {
        // A malformed tree can point at itself; the depth ceiling keeps that from being a hang.
        if (depth > 64 || Dictionary(number) is not { } dictionary)
        {
            return;
        }

        if (Has(dictionary, "Page"))
        {
            ordered.Add(number);
            return;
        }

        foreach (var kid in References(dictionary, "Kids"))
        {
            Walk(kid, ordered, depth + 1);
        }
    }

    /// <summary>The text one page draws.</summary>
    private string Page(int number)
    {
        var page = Dictionary(number)
            ?? throw new PdfOperationException($"the page object {number} cannot be read");

        var fonts = Fonts(page);
        var content = new StringBuilder();

        foreach (var reference in ArrayOrSingle(page, "Contents"))
        {
            if (_objects.TryGetValue(reference, out var stream) && stream.HasStream)
            {
                content.Append(Decode(stream)).Append('\n');
            }
        }

        return Content(content.ToString(), fonts);
    }

    /// <summary>Reads the fonts of a page, by the name its content stream selects them by.</summary>
    private Dictionary<string, PdfFontMap> Fonts(string page)
    {
        var fonts = new Dictionary<string, PdfFontMap>(StringComparer.Ordinal);
        var resources = Reference(page, "Resources") is { } number ? Dictionary(number) : null;

        if (resources is null || DictionaryEntry(resources, "Font") is not { } font)
        {
            return fonts;
        }

        foreach (Match match in Regex.Matches(
            font, @"/([^\s/<>\[\]()%{}]+)\s+(\d+)\s+0\s+R", RegexOptions.CultureInvariant))
        {
            var name = match.Groups[1].Value;
            var target = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);

            if (Dictionary(target) is { } dictionary)
            {
                fonts[name] = FontMap(target, dictionary);
            }
        }

        return fonts;
    }

    private PdfFontMap FontMap(int number, string dictionary)
    {
        if (Reference(dictionary, "ToUnicode") is { } toUnicode
            && _objects.TryGetValue(toUnicode, out var stream)
            && stream.HasStream)
        {
            var (codes, length) = Cmap(Decode(stream));

            if (codes.Count > 0)
            {
                return new PdfFontMap(codes, length);
            }
        }

        return PdfFontMap.WinAnsi;
    }

    /// <summary>Parses the <c>beginbfchar</c> and <c>beginbfrange</c> blocks of a CMap.</summary>
    private static (Dictionary<int, string> Codes, int CodeLength) Cmap(string text)
    {
        var codes = new Dictionary<int, string>();
        var codeLength = 0;

        foreach (Match block in Regex.Matches(
            text, @"beginbfchar(.*?)endbfchar", RegexOptions.Singleline | RegexOptions.CultureInvariant))
        {
            foreach (Match pair in Regex.Matches(
                block.Groups[1].Value,
                @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>",
                RegexOptions.CultureInvariant))
            {
                var source = pair.Groups[1].Value;
                codeLength = Math.Max(codeLength, source.Length / 2);
                codes[Convert.ToInt32(source, 16)] = Utf16(pair.Groups[2].Value);
            }
        }

        foreach (Match block in Regex.Matches(
            text, @"beginbfrange(.*?)endbfrange", RegexOptions.Singleline | RegexOptions.CultureInvariant))
        {
            foreach (Match range in Regex.Matches(
                block.Groups[1].Value,
                @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*(?:<([0-9A-Fa-f]+)>|\[([^\]]*)\])",
                RegexOptions.CultureInvariant))
            {
                var first = Convert.ToInt32(range.Groups[1].Value, 16);
                var last = Convert.ToInt32(range.Groups[2].Value, 16);
                codeLength = Math.Max(codeLength, range.Groups[1].Value.Length / 2);

                if (range.Groups[3].Success)
                {
                    var start = Convert.FromHexString(range.Groups[3].Value);

                    for (var code = first; code <= last; code++)
                    {
                        codes[code] = Utf16(Increment(start, code - first));
                    }
                }
                else
                {
                    var destinations = Regex.Matches(
                        range.Groups[4].Value, @"<([0-9A-Fa-f]+)>", RegexOptions.CultureInvariant);

                    for (var offset = 0; offset < destinations.Count && first + offset <= last; offset++)
                    {
                        codes[first + offset] = Utf16(destinations[offset].Groups[1].Value);
                    }
                }
            }
        }

        return (codes, codeLength);
    }

    private static byte[] Increment(byte[] value, int delta)
    {
        var result = (byte[])value.Clone();
        var carry = delta;

        for (var index = result.Length - 1; index >= 0 && carry > 0; index--)
        {
            var sum = result[index] + carry;
            result[index] = (byte)sum;
            carry = sum >> 8;
        }

        return result;
    }

    private static string Utf16(string hex) => Utf16(Convert.FromHexString(hex));

    private static string Utf16(byte[] bytes) => Encoding.BigEndianUnicode.GetString(bytes);

    /// <summary>Reads the text a content stream shows, with the separators its positioning implies.</summary>
    private static string Content(string content, Dictionary<string, PdfFontMap> fonts)
    {
        var builder = new StringBuilder();
        var operands = new List<string>();
        var current = PdfFontMap.WinAnsi;
        double? lastX = null;
        double? lastY = null;

        void Append(char separator)
        {
            if (builder.Length == 0)
            {
                return;
            }

            if (separator == '\n')
            {
                while (builder.Length > 0 && builder[^1] == ' ')
                {
                    builder.Length--;
                }

                if (builder.Length > 0 && builder[^1] != '\n')
                {
                    builder.Append('\n');
                }

                return;
            }

            if (!char.IsWhiteSpace(builder[^1]))
            {
                builder.Append(separator);
            }
        }

        void MoveTo(double x, double y)
        {
            if (lastY is { } previousY)
            {
                Append(Math.Abs(y - previousY) > 0.5 ? '\n' : ' ');
            }
            else if (lastX is { } previousX && x > previousX + 0.1)
            {
                Append(' ');
            }

            lastX = x;
            lastY = y;
        }

        void Show(string text)
        {
            if (text.Length > 0)
            {
                builder.Append(text);
            }
        }

        foreach (Match token in Tokens().Matches(content))
        {
            if (token.Groups["name"].Success)
            {
                operands.Add("/" + token.Groups["name"].Value);
                continue;
            }

            if (token.Groups["number"].Success || token.Groups["array"].Success)
            {
                operands.Add(token.Value);
                continue;
            }

            if (token.Groups["literal"].Success)
            {
                operands.Add("s:" + Unescape(token.Groups["literal"].Value));
                continue;
            }

            if (token.Groups["hex"].Success)
            {
                var digits = new string(token.Groups["hex"].Value.Where(Uri.IsHexDigit).ToArray());

                if (digits.Length % 2 != 0)
                {
                    digits += "0";
                }

                operands.Add("s:" + Encoding.Latin1.GetString(Convert.FromHexString(digits)));
                continue;
            }

            switch (token.Groups["operator"].Value)
            {
                case "Tf" when operands.Count >= 2 && fonts.TryGetValue(operands[^2].TrimStart('/'), out var font):
                    current = font;
                    break;

                case "BT":
                    lastX = null;
                    lastY = null;
                    break;

                case "cm" or "Tm" when Numbers(operands) is { Count: 6 } matrix:
                    // A text matrix resets per text object and carries no translation in most
                    // producers; the transformation that places a run is the `cm` before it.
                    if (matrix[4] != 0 || matrix[5] != 0)
                    {
                        MoveTo(matrix[4], matrix[5]);
                    }

                    break;

                case "Td" or "TD":
                    Append('\n');
                    break;

                case "T*":
                    Append('\n');
                    break;

                case "Tj" or "'" or "\"":
                    Show(current.Decode(Last(operands)));
                    break;

                case "TJ":
                    foreach (var operand in operands.Where(value => value.StartsWith("s:", StringComparison.Ordinal)))
                    {
                        Show(current.Decode(operand[2..]));
                    }

                    break;
            }

            operands.Clear();
        }

        return builder.ToString().TrimEnd();
    }

    private static string Last(List<string> operands)
        => operands.Count > 0 && operands[^1].StartsWith("s:", StringComparison.Ordinal)
            ? operands[^1][2..]
            : string.Empty;

    private static List<double>? Numbers(List<string> operands)
    {
        var start = Math.Max(0, operands.Count - 6);

        if (operands.Count < 6)
        {
            return null;
        }

        var numbers = new List<double>(6);

        for (var index = start; index < operands.Count; index++)
        {
            if (!double.TryParse(operands[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                return null;
            }

            numbers.Add(value);
        }

        return numbers;
    }

    private static string Unescape(string value)
    {
        if (!value.Contains('\\', StringComparison.Ordinal))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];

            if (character != '\\' || index + 1 >= value.Length)
            {
                builder.Append(character);
                continue;
            }

            var next = value[++index];

            switch (next)
            {
                case 'n':
                    builder.Append('\n');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case 'b':
                    builder.Append('\b');
                    break;
                case 'f':
                    builder.Append('\f');
                    break;
                case >= '0' and <= '7':
                    var code = next - '0';
                    var digits = 1;

                    while (digits < 3 && index + 1 < value.Length && value[index + 1] is >= '0' and <= '7')
                    {
                        code = (code * 8) + (value[++index] - '0');
                        digits++;
                    }

                    builder.Append((char)code);
                    break;
                default:
                    builder.Append(next);
                    break;
            }
        }

        return builder.ToString();
    }

    private static Regex Tokens() => new(
        @"/(?<name>[^\s/<>\[\]()%{}]+)"
        + @"|(?<number>[-+]?(?:\d+\.?\d*|\.\d+))"
        + @"|\((?<literal>(?:\\.|[^\\()])*)\)"
        + @"|<(?<hex>[0-9A-Fa-f\s]+)>"
        + @"|(?<array>[\[\]])"
        + @"|(?<operator>[A-Za-z'""*]+)",
        RegexOptions.CultureInvariant | RegexOptions.Singleline);

    /// <summary>Decodes a stream's bytes through its filter.</summary>
    private string Decode(PdfTextObject stream)
    {
        if (_decoded.TryGetValue(stream.Number, out var cached))
        {
            return cached ?? string.Empty;
        }

        var raw = stream.Bytes(_text);
        var filter = Filter(stream.Dictionary);

        var data = filter switch
        {
            null or "FlateDecode" => Inflate(raw),
            _ => throw new PdfOperationException(
                $"a stream of the document uses the '{filter}' filter, which this server cannot read"),
        };

        var text = Encoding.Latin1.GetString(data);
        _decoded[stream.Number] = text;
        return text;
    }

    private static byte[] Inflate(byte[] raw)
    {
        if (raw.Length == 0)
        {
            return raw;
        }

        try
        {
            using var input = new MemoryStream(raw);
            using var inflate = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream(raw.Length * 4);
            inflate.CopyTo(output);
            return output.ToArray();
        }
        catch (InvalidDataException)
        {
            // A producer that wrote its stream with no compression while declaring FlateDecode is
            // not unheard of; the bytes are then already the text.
            return raw;
        }
    }

    private static string? Filter(string dictionary)
    {
        // A filter is a name, or the first name of an array of them; only the first decides whether
        // the bytes are readable at all.
        var match = Regex.Match(
            dictionary,
            @"/Filter\s*(?:/\s*([A-Za-z0-9]+)|\[\s*/([A-Za-z0-9]+))",
            RegexOptions.CultureInvariant);

        return match.Success
            ? (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value)
            : null;
    }

    /// <summary>Collects every object the file carries, by the header it starts with.</summary>
    private void Collect()
    {
        foreach (Match match in ObjectHeader.Matches(_text))
        {
            var number = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var bodyStart = match.Index + match.Length;

            var endObject = _text.IndexOf("endobj", bodyStart, StringComparison.Ordinal);
            if (endObject < 0)
            {
                endObject = _text.Length;
            }

            var stream = StreamStart.Match(_text, bodyStart);

            if (stream.Success && stream.Index < endObject)
            {
                var dictionary = _text[bodyStart..stream.Index];
                var start = stream.Index + stream.Length;
                var length = StreamLength(dictionary, start);
                var end = length is { } size && start + size <= _text.Length
                    ? start + size
                    : _text.IndexOf("endstream", start, StringComparison.Ordinal) switch
                    {
                        < 0 => endObject,
                        var index => index,
                    };

                _objects[number] = new PdfTextObject(number, dictionary, start, Math.Max(0, end - start));
                continue;
            }

            _objects[number] = new PdfTextObject(number, _text[bodyStart..endObject], -1, 0);
        }
    }

    /// <summary>The declared stream length, which may itself be an indirect object.</summary>
    private int? StreamLength(string dictionary, int start)
    {
        var direct = Regex.Match(dictionary, @"/Length\s+(\d+)(?!\s+0\s+R)", RegexOptions.CultureInvariant);
        if (direct.Success)
        {
            return int.Parse(direct.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        if (Reference(dictionary, "Length") is { } number
            && _objects.TryGetValue(number, out var length)
            && int.TryParse(length.Dictionary.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var size))
        {
            return size;
        }

        return _text.IndexOf("endstream", start, StringComparison.Ordinal) is var end && end >= 0 ? end - start : null;
    }

    private string? Dictionary(int number)
        => _objects.TryGetValue(number, out var entry) ? entry.Dictionary : null;

    private static bool Has(string dictionary, string type)
    {
        var match = TypeEntry.Match(dictionary);
        return match.Success && string.Equals(match.Groups[1].Value, type, StringComparison.Ordinal);
    }

    /// <summary>The first reference a key holds, in a dictionary body.</summary>
    private static int? Reference(string dictionary, string key)
    {
        var match = Regex.Match(
            dictionary,
            "/" + Regex.Escape(key) + @"\s+(\d+)\s+0\s+R",
            RegexOptions.CultureInvariant);

        return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>The inline dictionary a key holds, or the one a reference names.</summary>
    private string? DictionaryEntry(string dictionary, string key)
    {
        var index = Regex.Match(
            dictionary,
            "/" + Regex.Escape(key) + @"\s*(?=<<|\d+\s+0\s+R)",
            RegexOptions.CultureInvariant);

        if (!index.Success)
        {
            return null;
        }

        var start = index.Index + index.Length;

        if (dictionary.AsSpan(start).StartsWith("<<"))
        {
            var end = Close(dictionary, start);
            return end < 0 ? null : dictionary[start..(end + 2)];
        }

        return Reference(dictionary, key) is { } number ? Dictionary(number) : null;
    }

    /// <summary>Every reference a key holds, whether it is an array or a single reference.</summary>
    private static List<int> ArrayOrSingle(string dictionary, string key)
    {
        var array = ArrayBody(dictionary, key);

        if (array is not null)
        {
            return Regex.Matches(array, @"(\d+)\s+0\s+R", RegexOptions.CultureInvariant)
                .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
                .ToList();
        }

        return Reference(dictionary, key) is { } number ? [number] : [];
    }

    private static List<int> References(string dictionary, string key)
        => ArrayOrSingle(dictionary, key);

    /// <summary>The text between the brackets of an array value, or <see langword="null"/>.</summary>
    private static string? ArrayBody(string dictionary, string key)
    {
        var match = Regex.Match(
            dictionary, "/" + Regex.Escape(key) + @"\s*\[", RegexOptions.CultureInvariant);

        if (!match.Success)
        {
            return null;
        }

        var start = match.Index + match.Length;
        var depth = 1;

        for (var index = start; index < dictionary.Length; index++)
        {
            depth += dictionary[index] switch
            {
                '[' => 1,
                ']' => -1,
                _ => 0,
            };

            if (depth == 0)
            {
                return dictionary[start..index];
            }
        }

        return null;
    }

    /// <summary>The index of the <c>&gt;&gt;</c> closing the dictionary that starts at <paramref name="start"/>.</summary>
    private static int Close(string text, int start)
    {
        var depth = 0;

        for (var index = start; index < text.Length - 1; index++)
        {
            if (text[index] == '<' && text[index + 1] == '<')
            {
                depth++;
                index++;
            }
            else if (text[index] == '>' && text[index + 1] == '>')
            {
                depth--;
                index++;

                if (depth == 0)
                {
                    return index - 1;
                }
            }
        }

        return -1;
    }

    /// <summary>One object of the file: its dictionary body, and where its stream sits when it has one.</summary>
    private sealed record PdfTextObject(int Number, string Dictionary, int StreamStart, int Length)
    {
        internal bool HasStream => StreamStart >= 0;

        internal byte[] Bytes(string text)
            => HasStream && Length > 0
                ? Encoding.Latin1.GetBytes(text.Substring(StreamStart, Length))
                : [];
    }
}

/// <summary>How one font's byte codes map to characters.</summary>
internal sealed class PdfFontMap
{
    /// <summary>What a simple font without a <c>/ToUnicode</c> map means: one byte, one WinAnsi character.</summary>
    internal static PdfFontMap WinAnsi { get; } = new([], 1);

    private readonly Dictionary<int, string> _codes;
    private readonly int _codeLength;

    internal PdfFontMap(Dictionary<int, string> codes, int codeLength)
    {
        _codes = codes;
        _codeLength = codeLength;
    }

    /// <summary>Decodes a run of bytes the stream showed under this font.</summary>
    internal string Decode(string text)
    {
        if (text.Length == 0)
        {
            return string.Empty;
        }

        if (_codes.Count == 0)
        {
            // One byte, one character: a simple font without a map declares exactly that.
            return text;
        }

        var bytes = Encoding.Latin1.GetBytes(text);
        var builder = new StringBuilder(bytes.Length);

        for (var index = 0; index + _codeLength <= bytes.Length; index += _codeLength)
        {
            var code = 0;

            for (var offset = 0; offset < _codeLength; offset++)
            {
                code = (code << 8) | bytes[index + offset];
            }

            if (_codes.TryGetValue(code, out var value))
            {
                builder.Append(value);
            }
        }

        return builder.ToString();
    }
}
