using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace M3scalinum.Text.Pdf;

/// <summary>A problem noticed while reading the file, before any conformance rule is applied.</summary>
/// <param name="Code">The stable issue code.</param>
/// <param name="Target">The object the problem concerns.</param>
/// <param name="Message">What is wrong.</param>
internal sealed record PdfReadingProblem(string Code, string Target, string Message);

/// <summary>A page, its dictionary and the resources it inherits.</summary>
/// <param name="Number">The page's object number, or 0 when the page tree named it directly.</param>
/// <param name="Dictionary">The page dictionary.</param>
/// <param name="Resources">The effective <c>/Resources</c> dictionary, inherited if necessary.</param>
internal sealed record PdfPage(int Number, PdfDictionary Dictionary, PdfDictionary? Resources);

/// <summary>
/// A minimal, read-only PDF object reader: header, cross-reference (tables and streams), indirect
/// objects, object streams, and the stream filters the conformance checks need. It is deliberately
/// not a renderer and not an editor.
/// </summary>
internal sealed partial class PdfFile
{
    /// <summary>How many bytes at the end are searched for <c>startxref</c>.</summary>
    private const int StartXrefWindow = 4096;

    /// <summary>The most objects a cross-reference section may declare, as a guard against garbage.</summary>
    private const int MaxObjects = 1_000_000;

    /// <summary>
    /// The most bytes one stream may decode to.
    /// </summary>
    /// <remarks>
    /// A stream's declared <c>/Length</c> says nothing about what its filters produce: a few
    /// kilobytes of deflated zeroes inflate to hundreds of megabytes, and the bomb is bounded only by
    /// the caller's address space when no ceiling is applied. Thirty-two megabytes is past every
    /// stream this reader consumes — a content stream, an object stream, an ICC profile, a font
    /// program — and well below what a bomb reaches from a file small enough to be mailed.
    /// </remarks>
    internal const int MaxStreamBytes = 32 * 1024 * 1024;

    private readonly byte[] _data;
    private readonly Dictionary<int, XrefEntry> _entries = [];
    private readonly Dictionary<int, PdfValue> _cache = [];
    private readonly Dictionary<int, Dictionary<int, PdfValue>> _objectStreams = [];
    private readonly List<PdfReadingProblem> _problems = [];
    private readonly List<PdfDictionary> _trailers = [];
    private PdfStandardSecurity? _security;
    private int _encryptObjectNumber = -1;
    private bool _encryptMetadata = true;

    private PdfFile(byte[] data, int major, int minor)
    {
        _data = data;
        MajorVersion = major;
        MinorVersion = minor;
    }

    /// <summary>The version in the file header, before any catalog override.</summary>
    internal int MajorVersion { get; }

    /// <summary>The version in the file header, before any catalog override.</summary>
    internal int MinorVersion { get; }

    /// <summary>The cross-reference and syntax problems found while reading.</summary>
    internal IReadOnlyList<PdfReadingProblem> Problems => _problems;

    /// <summary>The merged trailer, newest section first.</summary>
    internal PdfDictionary Trailer { get; private set; } = new();

    /// <summary>Whether the trailer names an <c>/Encrypt</c> dictionary.</summary>
    internal bool IsEncrypted { get; private set; }

    /// <summary>
    /// The first element of the trailer's <c>/ID</c>, which an R4 document derives its file key from,
    /// or an empty array when the trailer names none.
    /// </summary>
    internal byte[] Identifier
        => Trailer["ID"] is PdfArray { Items.Count: > 0 } identifiers && identifiers.Items[0] is PdfString first
            ? first.Bytes
            : [];

    /// <summary>
    /// How many object numbers the file declares: its <c>/Size</c>, or one past the highest object
    /// the cross-reference holds when the two disagree.
    /// </summary>
    internal int Size
    {
        get
        {
            var highest = 0;
            foreach (var number in _entries.Keys)
            {
                highest = Math.Max(highest, number);
            }

            var size = Trailer["Size"] is PdfNumber number2 && number2.IsInteger ? (int)number2.AsInteger : 0;

            return Math.Max(size, highest + 1);
        }
    }

    /// <summary>The document catalog, when the trailer names one that resolves to a dictionary.</summary>
    internal PdfDictionary? Catalog { get; private set; }

    /// <summary>
    /// Switches the reader to a decrypted view of the same bytes: every object read from now on has
    /// its strings and streams decrypted with <paramref name="security"/>. Objects read before the
    /// key was known are dropped from the cache and read again, which is what makes a catalog that
    /// lives in an encrypted object stream resolvable at all.
    /// </summary>
    /// <param name="security">The handler, with its file key.</param>
    /// <param name="encryptObjectNumber">The object holding <c>/Encrypt</c>, written in the clear.</param>
    /// <param name="encryptMetadata">Whether <c>/EncryptMetadata</c> leaves the metadata stream encrypted.</param>
    internal void UseDecryption(PdfStandardSecurity security, int encryptObjectNumber, bool encryptMetadata)
    {
        _security = security;
        _encryptObjectNumber = encryptObjectNumber;
        _encryptMetadata = encryptMetadata;
        _cache.Clear();
        _objectStreams.Clear();
        Catalog = ResolveDictionary(Trailer["Root"]);
    }

    /// <summary>The generation the cross-reference records for <paramref name="number"/>.</summary>
    internal int GenerationOf(int number) => _entries.TryGetValue(number, out var entry) ? entry.Generation : 0;

    /// <summary>
    /// Reads <paramref name="data"/> as a PDF. Returns <see langword="false"/> when the bytes are not
    /// a PDF at all; a damaged cross-reference is reported through <see cref="Problems"/> instead.
    /// </summary>
    internal static bool TryOpen(byte[] data, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out PdfFile? file)
    {
        file = null;

        var header = data.AsSpan(0, Math.Min(data.Length, 1024));
        var marker = header.IndexOf("%PDF-"u8);
        if (marker < 0 || marker + 8 > header.Length)
        {
            return false;
        }

        var major = header[marker + 5] - '0';
        var minor = header[marker + 7] - '0';
        if (major is < 1 or > 2 || minor is < 0 or > 9)
        {
            return false;
        }

        file = new PdfFile(data, major, minor);
        file.Read();

        return true;
    }

    private void Read()
    {
        var startxref = FindStartXref();
        if (startxref < 0)
        {
            _problems.Add(new PdfReadingProblem(
                PdfConformanceCodes.XrefInvalid,
                "trailer",
                "the file has no startxref, so its cross-reference cannot be located"));
            return;
        }

        var offset = ReadNumberAt(startxref);
        if (offset <= 0 || offset >= _data.Length)
        {
            _problems.Add(new PdfReadingProblem(
                PdfConformanceCodes.XrefInvalid,
                "startxref",
                $"startxref points at byte {offset}, outside a file of {_data.Length} bytes"));
            return;
        }

        var visited = new HashSet<long>();
        while (offset > 0 && offset < _data.Length && visited.Add(offset))
        {
            PdfDictionary? sectionTrailer;
            try
            {
                sectionTrailer = ReadXrefSection(offset);
            }
            catch (PdfFormatException exception)
            {
                _problems.Add(new PdfReadingProblem(
                    PdfConformanceCodes.XrefInvalid,
                    $"offset {offset}",
                    exception.Message));
                break;
            }

            if (sectionTrailer is null)
            {
                break;
            }

            _trailers.Add(sectionTrailer);
            offset = sectionTrailer["Prev"] is PdfNumber previous && previous.IsInteger ? previous.AsInteger : 0;
        }

        MergeTrailers();
        ValidateEntries();

        if (Trailer["Encrypt"] is not null)
        {
            IsEncrypted = true;
        }

        Catalog = ResolveDictionary(Trailer["Root"]);
    }

    private int FindStartXref()
    {
        var windowStart = Math.Max(0, _data.Length - StartXrefWindow);
        var window = _data.AsSpan(windowStart);
        var index = window.LastIndexOf("startxref"u8);
        return index < 0 ? -1 : windowStart + index + "startxref"u8.Length;
    }

    private long ReadNumberAt(int position)
    {
        var cursor = new Cursor(_data, position);
        return TryReadInteger(ref cursor, out var value) ? value : -1;
    }

    private void MergeTrailers()
    {
        var merged = new PdfDictionary();
        merged.Entries["Size"] = new PdfNumber(0);

        foreach (var trailer in _trailers)
        {
            foreach (var (key, value) in trailer.Entries)
            {
                if (!merged.Entries.ContainsKey(key))
                {
                    merged.Entries[key] = value;
                }
            }
        }

        // The newest section wins for the keys that describe the whole file.
        if (_trailers.Count > 0)
        {
            foreach (var key in new[] { "Root", "Info", "Encrypt", "ID", "Size" })
            {
                if (_trailers[0].Entries.TryGetValue(key, out var value))
                {
                    merged.Entries[key] = value;
                }
            }
        }

        Trailer = merged;

        if (Trailer["Size"] is not PdfNumber size || !size.IsInteger || size.Value <= 0)
        {
            _problems.Add(new PdfReadingProblem(
                PdfConformanceCodes.TrailerInvalid,
                "trailer",
                "the trailer declares no usable /Size"));
        }
        else
        {
            var highest = 0;
            foreach (var number in _entries.Keys)
            {
                highest = Math.Max(highest, number);
            }

            if (size.AsInteger < highest + 1)
            {
                _problems.Add(new PdfReadingProblem(
                    PdfConformanceCodes.TrailerInvalid,
                    "trailer",
                    $"/Size is {size.AsInteger} but object {highest} exists, so at least {highest + 1} was expected"));
            }
        }
    }

    /// <summary>Checks that every in-use entry points at the object header it claims.</summary>
    private void ValidateEntries()
    {
        var reported = 0;

        foreach (var (number, entry) in _entries)
        {
            if (!entry.InUse)
            {
                continue;
            }

            if (entry.ObjectStream >= 0)
            {
                if (!_entries.ContainsKey(entry.ObjectStream))
                {
                    Report(number, $"entry {number} lives in object stream {entry.ObjectStream}, which the file does not declare");
                }

                continue;
            }

            if (entry.Offset <= 0 || entry.Offset >= _data.Length)
            {
                Report(number, $"entry {number} points at byte {entry.Offset}, outside a file of {_data.Length} bytes");
                continue;
            }

            var cursor = new Cursor(_data, (int)entry.Offset);
            if (!TryReadInteger(ref cursor, out var found) || found != number || !ReadGenerationAndObject(ref cursor))
            {
                Report(number, $"entry {number} points at byte {entry.Offset}, which does not hold object {number}");
            }

            void Report(int number, string message)
            {
                // One issue per broken entry, capped: a truncated file would otherwise produce one
                // issue per object and bury the rest of the report.
                reported++;
                if (reported <= 20)
                {
                    _problems.Add(new PdfReadingProblem(PdfConformanceCodes.XrefInvalid, $"xref entry {number}", message));
                }
                else if (reported == 21)
                {
                    _problems.Add(new PdfReadingProblem(
                        PdfConformanceCodes.XrefInvalid,
                        "xref",
                        "more than 20 cross-reference entries are wrong; the rest are not listed"));
                }
            }
        }
    }

    private static bool ReadGenerationAndObject(ref Cursor cursor)
    {
        return TryReadInteger(ref cursor, out _) && TryReadKeyword(ref cursor, "obj"u8);
    }

    /// <summary>Reads one cross-reference section; returns its trailer dictionary.</summary>
    private PdfDictionary? ReadXrefSection(long offset)
    {
        var cursor = new Cursor(_data, (int)offset);
        SkipWhitespace(ref cursor);

        if (TryReadKeyword(ref cursor, "xref"u8))
        {
            return ReadXrefTable(ref cursor);
        }

        if (ParseIndirectAt(offset) is not PdfStream stream || !stream.Dictionary.IsType("XRef"))
        {
            throw new PdfFormatException($"byte {offset} holds neither an xref table nor an xref stream");
        }

        ReadXrefStream(stream);
        return stream.Dictionary;
    }

    private PdfDictionary ReadXrefTable(ref Cursor cursor)
    {
        while (true)
        {
            SkipWhitespace(ref cursor);
            if (cursor.AtEnd)
            {
                throw new PdfFormatException("the xref table ends without a trailer");
            }

            if (TryReadKeyword(ref cursor, "trailer"u8))
            {
                SkipWhitespace(ref cursor);
                return ParseValue(ref cursor, 0) as PdfDictionary
                    ?? throw new PdfFormatException("the xref trailer is not a dictionary");
            }

            if (!TryReadInteger(ref cursor, out var start) || !TryReadInteger(ref cursor, out var count))
            {
                throw new PdfFormatException("an xref subsection header is not two integers");
            }

            if (start < 0 || count < 0 || count > MaxObjects)
            {
                throw new PdfFormatException($"an xref subsection declares {count} entries, which is not plausible");
            }

            for (var i = 0; i < count; i++)
            {
                SkipWhitespace(ref cursor);
                if (cursor.AtEnd ||
                    !TryReadInteger(ref cursor, out var entryOffset) ||
                    !TryReadInteger(ref cursor, out var generation))
                {
                    throw new PdfFormatException($"xref entry {start + i} is truncated");
                }

                SkipWhitespace(ref cursor);
                if (cursor.AtEnd || (cursor.Data[cursor.Position] is not ((byte)'n' or (byte)'f')))
                {
                    throw new PdfFormatException($"xref entry {start + i} has no in-use or free marker");
                }

                var inUse = cursor.Data[cursor.Position] == (byte)'n';
                cursor.Position++;

                var number = (int)(start + i);
                if (inUse && !_entries.ContainsKey(number))
                {
                    _entries[number] = new XrefEntry(entryOffset, (int)generation, -1, 0);
                }
                else if (!inUse && !_entries.ContainsKey(number))
                {
                    _entries[number] = new XrefEntry(0, (int)generation, -1, 0, InUse: false);
                }
            }
        }
    }

    private void ReadXrefStream(PdfStream stream)
    {
        if (stream.Dictionary["W"] is not PdfArray widths || widths.Items.Count < 3)
        {
            throw new PdfFormatException("the xref stream has no /W array of three widths");
        }

        var width = new int[3];
        for (var i = 0; i < 3; i++)
        {
            width[i] = widths.Items[i] is PdfNumber number && number.IsInteger ? (int)number.AsInteger : -1;
            if (width[i] is < 0 or > 8)
            {
                throw new PdfFormatException($"the xref stream declares a field width of {width[i]} bytes");
            }
        }

        var rowLength = width[0] + width[1] + width[2];
        if (rowLength <= 0)
        {
            throw new PdfFormatException("the xref stream declares empty rows");
        }

        var ranges = new List<(long Start, long Count)>();
        if (stream.Dictionary["Index"] is PdfArray index)
        {
            for (var i = 0; i + 1 < index.Items.Count; i += 2)
            {
                var start = index.Items[i] as PdfNumber;
                var count = index.Items[i + 1] as PdfNumber;
                if (start is null || count is null || !start.IsInteger || !count.IsInteger)
                {
                    throw new PdfFormatException("the xref stream /Index is not a list of integer pairs");
                }

                ranges.Add((start.AsInteger, count.AsInteger));
            }
        }
        else
        {
            var size = stream.Dictionary["Size"] as PdfNumber;
            ranges.Add((0, size is { IsInteger: true } ? size.AsInteger : 0));
        }

        var bytes = Decode(stream);
        if (bytes is null)
        {
            throw new PdfFormatException("the xref stream could not be decoded with the filters it declares");
        }

        var position = 0;
        foreach (var (start, count) in ranges)
        {
            for (long i = 0; i < count; i++)
            {
                if (position + rowLength > bytes.Length)
                {
                    throw new PdfFormatException("the xref stream is shorter than its /W and /Index declare");
                }

                var type = width[0] == 0 ? 1 : ReadField(bytes.AsSpan(position, width[0]));
                position += width[0];
                var second = ReadField(bytes.AsSpan(position, width[1]));
                position += width[1];
                var third = ReadField(bytes.AsSpan(position, width[2]));
                position += width[2];

                var number = start + i;
                if (number is < 0 or > MaxObjects)
                {
                    continue;
                }

                if (_entries.ContainsKey((int)number))
                {
                    continue;
                }

                _entries[(int)number] = type switch
                {
                    0 => new XrefEntry(0, (int)third, -1, 0, InUse: false),
                    1 => new XrefEntry(second, (int)third, -1, 0),
                    2 => new XrefEntry(0, 0, (int)second, (int)third),
                    _ => new XrefEntry(0, 0, -1, 0, InUse: false),
                };
            }
        }

        static long ReadField(ReadOnlySpan<byte> field)
        {
            long value = 0;
            foreach (var b in field)
            {
                value = (value << 8) | b;
            }

            return value;
        }
    }

    /// <summary>Reads the indirect object whose header starts at <paramref name="offset"/>.</summary>
    private PdfValue? ParseIndirectAt(long offset)
    {
        var cursor = new Cursor(_data, (int)offset);
        SkipWhitespace(ref cursor);

        if (!TryReadInteger(ref cursor, out var number) ||
            !TryReadInteger(ref cursor, out var generation) ||
            !TryReadKeyword(ref cursor, "obj"u8))
        {
            return null;
        }

        var value = ParseValue(ref cursor, 0);

        SkipWhitespace(ref cursor);
        PdfValue result = value is PdfDictionary dictionary && TryReadKeyword(ref cursor, "stream"u8)
            ? ReadStream(dictionary, ref cursor, (int)number)
            : value;

        // The /Encrypt dictionary is written in the clear, so it is never decrypted; an empty string
        // has nothing to decrypt and passes through untouched.
        return _security is not null && number != _encryptObjectNumber
            ? Decrypt(result, (int)number, (int)generation)
            : result;
    }

    /// <summary>
    /// Decrypts the strings and the stream data of an object, and corrects the <c>/Length</c> of a
    /// stream whose bytes grew or shrank. The cross-reference stream of an encrypted file is never
    /// encrypted, and neither is the metadata stream of a document that says so.
    /// </summary>
    private PdfValue Decrypt(PdfValue value, int number, int generation)
    {
        var security = _security!;

        if (value is PdfStream stream)
        {
            Decrypt(stream.Dictionary, number, generation);

            if (!security.EncryptsStreams || stream.Dictionary.IsType("XRef") || !DecryptsMetadata(stream.Dictionary))
            {
                return stream;
            }

            var data = security.DecryptObject(number, generation, stream.Data);
            stream.Dictionary.Entries["Length"] = new PdfNumber(data.Length);

            return new PdfStream(stream.Dictionary, data);
        }

        return PdfObjectTree.TransformStrings(
            value,
            security.EncryptsStrings ? bytes => security.DecryptObject(number, generation, bytes) : bytes => bytes);
    }

    private bool DecryptsMetadata(PdfDictionary stream)
        => _encryptMetadata || !stream.IsType("Metadata");

    private PdfStream ReadStream(PdfDictionary dictionary, ref Cursor cursor, int number)
    {
        if (!cursor.AtEnd && cursor.Data[cursor.Position] == (byte)'\r')
        {
            cursor.Position++;
        }

        if (!cursor.AtEnd && cursor.Data[cursor.Position] == (byte)'\n')
        {
            cursor.Position++;
        }

        var start = cursor.Position;
        var length = LengthOf(dictionary);
        var available = _data.Length - start;

        if (length is long declared && declared >= 0 && declared <= available)
        {
            var data = _data.AsSpan(start, (int)declared).ToArray();
            return new PdfStream(dictionary, data);
        }

        var end = _data.AsSpan(start).IndexOf("endstream"u8);
        if (end < 0)
        {
            _problems.Add(new PdfReadingProblem(
                PdfConformanceCodes.XrefInvalid,
                $"obj {number}",
                "the stream is not terminated by endstream"));
            return new PdfStream(dictionary, _data.AsSpan(start).ToArray());
        }

        var length2 = end;
        while (length2 > 0 && _data[start + length2 - 1] is 10 or 13)
        {
            length2--;
        }

        return new PdfStream(dictionary, _data.AsSpan(start, length2).ToArray());
    }

    private long? LengthOf(PdfDictionary dictionary)
    {
        return dictionary["Length"] switch
        {
            PdfNumber number when number.IsInteger => number.AsInteger,
            PdfReference reference => GetObject(reference.Number) is PdfNumber number && number.IsInteger
                ? number.AsInteger
                : null,
            _ => null,
        };
    }

    /// <summary>Resolves indirect references until a direct object is reached.</summary>
    internal PdfValue? Resolve(PdfValue? value)
    {
        for (var depth = 0; depth < 16 && value is PdfReference reference; depth++)
        {
            value = GetObject(reference.Number);
        }

        return value is PdfNullValue ? null : value;
    }

    /// <summary>Resolves <paramref name="value"/> to a dictionary, or <see langword="null"/>.</summary>
    internal PdfDictionary? ResolveDictionary(PdfValue? value)
        => Resolve(value) as PdfDictionary ?? (Resolve(value) as PdfStream)?.Dictionary;

    /// <summary>Resolves <paramref name="value"/> to a stream, or <see langword="null"/>.</summary>
    internal PdfStream? ResolveStream(PdfValue? value) => Resolve(value) as PdfStream;

    /// <summary>Resolves <paramref name="value"/> to an array of items, or an empty list.</summary>
    internal IReadOnlyList<PdfValue> ResolveArray(PdfValue? value)
        => Resolve(value) is PdfArray array ? array.Items : [];

    /// <summary>
    /// Resolves a reference and reports the object number it named, which the structure-tree checks
    /// need to compare a child's <c>/P</c> with its actual parent.
    /// </summary>
    internal bool TryResolveReference(PdfValue? value, out int number, out PdfValue? resolved)
    {
        number = 0;
        resolved = value;

        if (value is not PdfReference reference)
        {
            return false;
        }

        number = reference.Number;
        resolved = Resolve(reference);

        return true;
    }

    /// <summary>Reads an indirect object by number, from the file or from an object stream.</summary>
    internal PdfValue? GetObject(int number)
    {
        if (_cache.TryGetValue(number, out var cached))
        {
            return cached;
        }

        if (!_entries.TryGetValue(number, out var entry) || !entry.InUse)
        {
            return null;
        }

        if (entry.Offset == 0 && entry.ObjectStream < 0)
        {
            return null;
        }

        PdfValue? value;
        try
        {
            value = entry.ObjectStream >= 0
                ? FromObjectStream(entry.ObjectStream, number)
                : ParseIndirectAt(entry.Offset);

            // A repaired file may hold an object under a generator the xref does not name; the
            // number is what matters, and the entry was already validated.
        }
        catch (PdfFormatException exception)
        {
            // A syntax error inside one object must not take the whole report down: record it and
            // let the checks report the object as missing.
            _problems.Add(new PdfReadingProblem(
                PdfConformanceCodes.ObjectUnreadable,
                $"obj {number}",
                $"the object could not be parsed: {exception.Message}"));

            value = null;
        }

        _cache[number] = value ?? PdfValue.Null;
        return value ?? PdfValue.Null;
    }

    private PdfValue? FromObjectStream(int streamNumber, int number)
    {
        var objects = ObjectStream(streamNumber);
        return objects.TryGetValue(number, out var value) ? value : null;
    }

    private Dictionary<int, PdfValue> ObjectStream(int streamNumber)
    {
        if (_objectStreams.TryGetValue(streamNumber, out var cached))
        {
            return cached;
        }

        var objects = new Dictionary<int, PdfValue>();

        if (GetObject(streamNumber) is not PdfStream stream || !stream.Dictionary.IsType("ObjStm"))
        {
            _objectStreams[streamNumber] = objects;
            return objects;
        }

        var count = stream.Dictionary["N"] is PdfNumber n && n.IsInteger ? (int)n.AsInteger : 0;
        var first = stream.Dictionary["First"] is PdfNumber f && f.IsInteger ? (int)f.AsInteger : 0;
        var bytes = Decode(stream);

        if (bytes is null || count <= 0 || first <= 0 || first >= bytes.Length)
        {
            _objectStreams[streamNumber] = objects;
            return objects;
        }

        var header = new Cursor(bytes, 0);
        var pairs = new List<(int Number, int Offset)>(count);
        for (var i = 0; i < count; i++)
        {
            if (!TryReadInteger(ref header, out var objectNumber) || !TryReadInteger(ref header, out var relative))
            {
                break;
            }

            pairs.Add(((int)objectNumber, (int)relative));
        }

        foreach (var (objectNumber, relative) in pairs)
        {
            var position = first + relative;
            if (position < 0 || position >= bytes.Length)
            {
                continue;
            }

            var cursor = new Cursor(bytes, position);
            objects[objectNumber] = ParseValue(ref cursor, 0);
        }

        _objectStreams[streamNumber] = objects;
        return objects;
    }

    /// <summary>
    /// Decodes a stream's filters. Returns <see langword="null"/> for an unsupported filter or a
    /// stream that decodes past <see cref="MaxStreamBytes"/>, which is recorded as a reading problem.
    /// </summary>
    internal byte[]? Decode(PdfStream stream)
    {
        var filters = new List<string>();
        switch (Resolve(stream.Dictionary["Filter"]))
        {
            case PdfName name:
                filters.Add(name.Value);
                break;
            case PdfArray array:
                foreach (var item in array.Items)
                {
                    if (Resolve(item) is PdfName entry)
                    {
                        filters.Add(entry.Value);
                    }
                }

                break;
        }

        var data = stream.Data;
        foreach (var filter in filters)
        {
            switch (filter)
            {
                case "FlateDecode" or "Fl":
                    var inflated = Inflate(data);
                    if (inflated is null)
                    {
                        return null;
                    }

                    data = inflated;
                    break;
                case "ASCIIHexDecode" or "AHx":
                    data = AsciiHex(data);
                    break;
                case "ASCII85Decode" or "A85":
                case "LZWDecode" or "LZW":
                case "RunLengthDecode" or "RL":
                case "DCTDecode":
                case "JPXDecode":
                    return null;
                default:
                    return null;
            }
        }

        return Predict(data, Resolve(stream.Dictionary["DecodeParms"]) as PdfDictionary);
    }

    private byte[]? Inflate(byte[] data)
    {
        try
        {
            using var input = new MemoryStream(data, writable: false);
            using var inflater = new ZLibStream(input, CompressionMode.Decompress);

            return Read(inflater);
        }
        catch (InvalidDataException)
        {
            // Some producers write a raw deflate stream with no zlib header; anything that is not
            // deflate either is reported as undecodable rather than thrown at the caller.
            try
            {
                using var input = new MemoryStream(data, writable: false);
                using var inflater = new DeflateStream(input, CompressionMode.Decompress);

                return Read(inflater);
            }
            catch (InvalidDataException)
            {
                return null;
            }
        }

        byte[]? Read(Stream inflater)
        {
            using var output = new MemoryStream();
            var buffer = new byte[81920];

            while (true)
            {
                var read = inflater.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    return output.ToArray();
                }

                if (output.Length + read > MaxStreamBytes)
                {
                    // Refused while it is being read, not after it exists: the read stops one chunk
                    // past the ceiling, so a stream that would decode to gigabytes never allocates
                    // them. The caller sees the object as undecodable and the report names the rule.
                    _problems.Add(new PdfReadingProblem(
                        PdfConformanceCodes.ObjectUnreadable,
                        "stream",
                        $"a stream decodes past the {MaxStreamBytes}-byte limit of one decoded stream; its filters were abandoned at that size, so the object reads as undecodable"));

                    return null;
                }

                output.Write(buffer, 0, read);
            }
        }
    }

    private static byte[] AsciiHex(byte[] data)
    {
        var hex = Encoding.ASCII.GetString(data);
        var builder = new StringBuilder(hex.Length);

        foreach (var c in hex)
        {
            if (c == '>')
            {
                break;
            }

            if (Uri.IsHexDigit(c))
            {
                builder.Append(c);
            }
        }

        if (builder.Length % 2 != 0)
        {
            builder.Append('0');
        }

        return Convert.FromHexString(builder.ToString());
    }

    /// <summary>Undoes a PNG or TIFF predictor, as cross-reference streams use.</summary>
    private static byte[] Predict(byte[] data, PdfDictionary? parameters)
    {
        if (parameters is null)
        {
            return data;
        }

        var predictor = (int)IntegerOf(parameters, "Predictor", 1);
        if (predictor <= 1)
        {
            return data;
        }

        if (predictor < 10)
        {
            // TIFF predictor 2, applied to 8-bit samples only.
            var colors = (int)IntegerOf(parameters, "Colors", 1);
            var columns = (int)IntegerOf(parameters, "Columns", 1);
            var stride = colors * columns;
            if (stride <= 0)
            {
                return data;
            }

            for (var row = 0; row + stride <= data.Length; row += stride)
            {
                for (var i = colors; i < stride; i++)
                {
                    data[row + i] = (byte)(data[row + i] + data[row + i - colors]);
                }
            }

            return data;
        }

        var colors2 = (int)IntegerOf(parameters, "Colors", 1);
        var bits = (int)IntegerOf(parameters, "BitsPerComponent", 8);
        var columns2 = (int)IntegerOf(parameters, "Columns", 1);
        var rowLength = (colors2 * bits * columns2 + 7) / 8;

        if (rowLength <= 0)
        {
            return data;
        }

        var rows = data.Length / (rowLength + 1);
        var output = new byte[rows * rowLength];
        var previous = new byte[rowLength];

        for (var row = 0; row < rows; row++)
        {
            var source = row * (rowLength + 1);
            var filter = data[source];
            var current = output.AsSpan(row * rowLength, rowLength);
            data.AsSpan(source + 1, rowLength).CopyTo(current);

            for (var i = 0; i < rowLength; i++)
            {
                var left = i >= colors2 ? current[i - colors2] : 0;
                var up = previous[i];
                var upLeft = i >= colors2 ? previous[i - colors2] : 0;

                current[i] = filter switch
                {
                    1 => (byte)(current[i] + left),
                    2 => (byte)(current[i] + up),
                    3 => (byte)(current[i] + ((left + up) / 2)),
                    4 => (byte)(current[i] + Paeth(left, up, upLeft)),
                    _ => current[i],
                };
            }

            current.CopyTo(previous);

            static int Paeth(int left, int up, int upLeft)
            {
                var estimate = left + up - upLeft;
                var toLeft = Math.Abs(estimate - left);
                var toUp = Math.Abs(estimate - up);
                var toUpLeft = Math.Abs(estimate - upLeft);

                if (toLeft <= toUp && toLeft <= toUpLeft)
                {
                    return left;
                }

                return toUp <= toUpLeft ? up : upLeft;
            }
        }

        return output;
    }

    private static long IntegerOf(PdfDictionary dictionary, string key, long fallback)
        => dictionary[key] is PdfNumber number && number.IsInteger ? number.AsInteger : fallback;

    /// <summary>The page tree, flattened, with inherited resources resolved.</summary>
    internal IReadOnlyList<PdfPage> Pages()
    {
        var pages = new List<PdfPage>();

        if (Catalog is null)
        {
            return pages;
        }

        var root = ResolveDictionary(Catalog["Pages"]);
        if (root is null)
        {
            return pages;
        }

        var pending = new Queue<(int Number, PdfDictionary Node, PdfDictionary? Inherited)>();
        var visited = new HashSet<int>();
        pending.Enqueue((0, root, null));

        while (pending.Count > 0)
        {
            var (number, node, inherited) = pending.Dequeue();
            var resources = ResolveDictionary(node["Resources"]) ?? inherited;

            if (node.IsType("Page") || (node["Kids"] is null && node["Contents"] is not null))
            {
                pages.Add(new PdfPage(number, node, resources));
                continue;
            }

            foreach (var kid in ResolveArray(node["Kids"]))
            {
                if (TryResolveReference(kid, out var kidNumber, out var resolved) &&
                    resolved is PdfDictionary dictionary &&
                    visited.Add(kidNumber))
                {
                    pending.Enqueue((kidNumber, dictionary, resources));
                }
            }
        }

        return pages;
    }

    /// <summary>An entry in the cross-reference: a file offset or an object stream and index.</summary>
    private readonly record struct XrefEntry(long Offset, int Generation, int ObjectStream, int Index, bool InUse = true);

    /// <summary>A cursor over a byte array, positioned at the next unread byte.</summary>
    private ref struct Cursor(byte[] data, int position)
    {
        public byte[] Data { get; } = data;

        public int Position { get; set; } = position;

        public bool AtEnd => Position >= Data.Length;
    }
}
