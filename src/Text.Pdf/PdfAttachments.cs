using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// The embedded files of a PDF: listing them, taking their bytes out, adding one and removing one.
/// </summary>
/// <remarks>
/// <para>
/// A PDF attaches a file twice over: the name tree at <c>/Names /EmbeddedFiles</c> gives each file
/// the name a reader shows, and the catalog's <c>/AF</c> array lists the files that are associated
/// with the document as a whole, which is what an e-invoice relies on. <see cref="Read"/> follows
/// both and reports a file found in each once.
/// </para>
/// <para>
/// Adding and removing append an incremental update, the way <see cref="PdfSigner"/> and
/// <see cref="PdfDocumentTimestamp"/> do: the bytes already in the file stay where they are, so a
/// signature over them still covers them and nothing is renumbered. Removal therefore unlinks an
/// attachment — its objects remain in the file as unreachable history — and the document reads as if
/// it had never carried it. A document whose cross-reference section is a stream rather than a
/// classic table cannot take an update here and is refused rather than rewritten.
/// </para>
/// </remarks>
public static class PdfAttachments
{
    /// <summary>Lists the embedded files <paramref name="pdf"/> carries, in the order it names them.</summary>
    /// <param name="pdf">The document's bytes.</param>
    /// <returns>One entry per file, or an empty list when the document carries none.</returns>
    /// <exception cref="PdfOperationException">
    /// The bytes are not a PDF, or it is encrypted: <see cref="PdfEncryption.Open"/> is what makes an
    /// encrypted document readable first.
    /// </exception>
    public static IReadOnlyList<PdfAttachment> Read(ReadOnlySpan<byte> pdf)
        => Collect(PdfSource.Open(pdf.ToArray(), "list the attachments of"));

    /// <summary>Returns the exact bytes of the attachment named <paramref name="name"/>.</summary>
    /// <param name="pdf">The document's bytes.</param>
    /// <param name="name">The attachment's name, compared ordinally: what <see cref="PdfAttachment.Name"/> reports.</param>
    /// <returns>The file the sender embedded, byte for byte, with no re-encoding.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    /// <exception cref="PdfOperationException">
    /// The document cannot be read, or it carries no attachment of that name.
    /// </exception>
    public static byte[] Extract(ReadOnlySpan<byte> pdf, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        foreach (var attachment in Read(pdf))
        {
            if (string.Equals(attachment.Name, name, StringComparison.Ordinal))
            {
                return attachment.Content;
            }
        }

        throw new PdfOperationException($"the document has no attachment named '{name}'");
    }

    /// <summary>Returns a copy of <paramref name="pdf"/> carrying <paramref name="content"/> as an attachment.</summary>
    /// <param name="pdf">The document to update.</param>
    /// <param name="name">The name a reader will show, and the name <see cref="Extract"/> takes back.</param>
    /// <param name="content">The file's bytes; they are stored Flate-compressed and handed back unchanged.</param>
    /// <param name="mimeType">
    /// The <c>/Subtype</c> of the embedded file stream, such as <c>text/xml</c>. When it is
    /// <see langword="null"/> it is guessed from the name's extension and falls back to
    /// <c>application/octet-stream</c>.
    /// </param>
    /// <param name="role">
    /// The <c>/AFRelationship</c> to declare. <see cref="PdfAttachmentRole.Unspecified"/> writes none —
    /// which is legal in plain PDF and breaks a claim of PDF/A-4f, where the relationship is required.
    /// </param>
    /// <param name="description">A description for <c>/Desc</c>, or <see langword="null"/> for none.</param>
    /// <returns>The document with the attachment appended as an incremental update.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    /// <exception cref="PdfOperationException">
    /// The document cannot be read, already carries an attachment of that name, or cannot take an
    /// incremental update.
    /// </exception>
    public static byte[] Add(
        ReadOnlySpan<byte> pdf,
        string name,
        ReadOnlySpan<byte> content,
        string? mimeType = null,
        PdfAttachmentRole role = PdfAttachmentRole.Unspecified,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var bytes = pdf.ToArray();
        var file = PdfSource.Open(bytes, "add an attachment to");
        var document = Update(bytes);

        if (Collect(file).Any(attachment => string.Equals(attachment.Name, name, StringComparison.Ordinal)))
        {
            throw new PdfOperationException(
                $"the document already has an attachment named '{name}'; remove it before adding another of that name");
        }

        var mime = string.IsNullOrWhiteSpace(mimeType) ? GuessMimeType(name) : mimeType.Trim();
        var compressed = Deflate(content);
        var entries = EmbeddedFiles(file);

        var specNumber = document.NextObjectNumber();
        var streamNumber = specNumber + 1;
        var treeNumber = specNumber + 2;
        var specReference = Reference(specNumber);

        var append = new PdfAppend(document.Text);
        append.Add(streamNumber, StreamBody(compressed, content.Length, mime));
        append.Add(specNumber, FileSpecBody(name, streamNumber, role, description));
        append.Add(treeNumber, NameTreeBody([.. entries, new NameEntry(name, new PdfReference(specNumber))]));

        append.Add(document.RootNumber, AddToCatalog(document, append, specReference, Reference(treeNumber)));

        return Encoding.Latin1.GetBytes(append.Finish(document, treeNumber + 1));
    }

    /// <summary>Returns a copy of <paramref name="pdf"/> with the attachment named <paramref name="name"/> removed.</summary>
    /// <param name="pdf">The document to update.</param>
    /// <param name="name">The attachment's name, compared ordinally.</param>
    /// <returns>The document with the attachment unlinked, as an incremental update.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    /// <exception cref="PdfOperationException">
    /// The document cannot be read, carries no attachment of that name, or cannot take an incremental
    /// update.
    /// </exception>
    /// <remarks>
    /// The file's objects are not reclaimed — an incremental update cannot take bytes back — so the
    /// bytes of a removed attachment stay in the file. Removing an attachment is a statement that the
    /// document no longer offers it, not that the file no longer contains it.
    /// </remarks>
    public static byte[] Remove(ReadOnlySpan<byte> pdf, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var bytes = pdf.ToArray();
        var file = PdfSource.Open(bytes, "remove an attachment from");
        var document = Update(bytes);

        var entries = EmbeddedFiles(file);
        PdfValue? removed = entries
            .Where(entry => string.Equals(entry.Name, name, StringComparison.Ordinal))
            .Select(entry => entry.Spec)
            .FirstOrDefault();

        if (removed is null)
        {
            removed = Associated(file)
                .FirstOrDefault(item => string.Equals(NameOf(file, item), name, StringComparison.Ordinal));
        }

        if (removed is null)
        {
            throw new PdfOperationException($"the document has no attachment named '{name}'");
        }

        var remaining = entries.Where(entry => !SameSpec(entry.Spec, removed)).ToList();
        var append = new PdfAppend(document.Text);
        var treeValue = "<</Names[]>>";
        var size = document.Size;

        if (remaining.Count > 0)
        {
            var treeNumber = document.NextObjectNumber();
            append.Add(treeNumber, NameTreeBody(remaining));
            treeValue = Reference(treeNumber);
            size = treeNumber + 1;
        }

        var updated = Owner(document, out var ownerBody);
        var names = SetEmbeddedFiles(ownerBody, treeValue, updated != document.RootNumber);

        if (updated != document.RootNumber)
        {
            append.Add(updated, names);
            append.Add(document.RootNumber, RemoveFromArray(document.Catalog, "AF", ReferenceText(removed)));
        }
        else
        {
            append.Add(document.RootNumber, RemoveFromArray(names, "AF", ReferenceText(removed)));
        }

        return Encoding.Latin1.GetBytes(append.Finish(document, size));
    }

    /// <summary>
    /// Adds the file specification to the catalog's <c>/AF</c> array and points the name tree at
    /// <paramref name="treeReference"/>. Returns the catalog body to write; the owner object, when it is
    /// not the catalog, has already been appended.
    /// </summary>
    private static string AddToCatalog(PdfDocument document, PdfAppend append, string specReference, string treeReference)
    {
        var owner = Owner(document, out var ownerBody);
        var names = SetEmbeddedFiles(ownerBody, treeReference, owner != document.RootNumber);

        if (owner != document.RootNumber)
        {
            append.Add(owner, names);
            return AppendToArray(document.Catalog, "AF", specReference);
        }

        return AppendToArray(names, "AF", specReference);
    }

    /// <summary>The object that holds the document's name dictionary, and its body.</summary>
    private static int Owner(PdfDocument document, out string body)
    {
        var catalog = document.Catalog;

        if (!PdfSyntax.TryReference(catalog, "Names", out var number))
        {
            body = catalog;
            return document.RootNumber;
        }

        if (document.Objects.TryGetValue(number, out var entry) && entry.Body.StartsWith("<<", StringComparison.Ordinal))
        {
            body = entry.Body;
            return number;
        }

        throw new PdfOperationException(
            "the document's name dictionary is an object this writer cannot rewrite as an incremental update");
    }

    /// <summary>Points the owner's <c>/EmbeddedFiles</c> at <paramref name="reference"/>.</summary>
    private static string SetEmbeddedFiles(string body, string reference, bool insideNameDictionary)
    {
        if (Locate(body, "EmbeddedFiles") is { } located)
        {
            return Replace(body, located.ValueStart, located.ValueLength, reference);
        }

        if (insideNameDictionary)
        {
            return InsertBeforeClose(body, "/EmbeddedFiles " + reference);
        }

        if (Locate(body, "Names") is { } names)
        {
            if (IsDictionaryAt(body, names.ValueStart))
            {
                return Replace(body, names.ValueStart + 2, 0, "/EmbeddedFiles " + reference);
            }

            throw new PdfOperationException(
                "the document's /Names entry is not a dictionary this writer can update");
        }

        return InsertBeforeClose(body, "/Names<</EmbeddedFiles " + reference + ">>");
    }

    private static bool IsDictionaryAt(string body, int start)
        => start + 1 < body.Length && body[start] == '<' && body[start + 1] == '<';

    /// <summary>Reads every attachment of an opened document.</summary>
    private static List<PdfAttachment> Collect(PdfFile file)
    {
        var found = new List<PdfAttachment>();
        var seen = new HashSet<int>();

        foreach (var (key, spec) in EmbeddedFiles(file))
        {
            Add(spec, key);
        }

        foreach (var spec in Associated(file))
        {
            Add(spec, null);
        }

        return found;

        void Add(PdfValue? spec, string? key)
        {
            // A file specification is an indirect object in every producer's output; the array and the
            // name tree then name the same object, and it must appear once.
            if (spec is PdfReference reference && !seen.Add(reference.Number))
            {
                return;
            }

            if (file.ResolveDictionary(spec) is not { } dictionary)
            {
                return;
            }

            var embedded = file.ResolveDictionary(dictionary["EF"]);
            var stream = embedded is null
                ? null
                : file.ResolveStream(embedded["F"]) ?? file.ResolveStream(embedded["UF"]);

            if (stream is null)
            {
                return;
            }

            var content = file.Decode(stream);
            var stored = content is null;
            content ??= stream.Data;

            var name = Text(file, dictionary["UF"]) ?? Text(file, dictionary["F"]) ?? key ?? string.Empty;
            if (name.Length == 0 || found.Exists(item => string.Equals(item.Name, name, StringComparison.Ordinal)))
            {
                return;
            }

            var mime = Name(file, stream.Dictionary["Subtype"]) ?? Name(file, dictionary["Subtype"]);
            var role = Role(file, dictionary["AFRelationship"]) ?? Role(file, stream.Dictionary["AFRelationship"]);

            found.Add(new PdfAttachment(
                name,
                Text(file, dictionary["F"]),
                mime,
                role ?? PdfAttachmentRole.Unspecified,
                content,
                stored));
        }
    }

    /// <summary>The associated files: the items of <c>/AF</c>, or the one value when it is not an array.</summary>
    private static List<PdfValue> Associated(PdfFile file)
    {
        // A producer may write one file specification without an array around it; it is still an
        // associated file, and a reader that insisted on an array would lose it.
        var value = file.Catalog?["AF"];

        return file.Resolve(value) switch
        {
            null => [],
            PdfArray array => array.Items,
            _ => [value!],
        };
    }

    /// <summary>The name tree's entries, in the order it lists them.</summary>
    private static List<NameEntry> EmbeddedFiles(PdfFile file)
    {
        var entries = new List<NameEntry>();

        if (file.Catalog?["Names"] is not { } names)
        {
            return entries;
        }

        if (file.ResolveDictionary(names)?["EmbeddedFiles"] is not { } root)
        {
            return entries;
        }

        Walk(file, root, entries, []);

        return entries;
    }

    private static void Walk(PdfFile file, PdfValue? node, List<NameEntry> entries, HashSet<int> visited)
    {
        if (file.TryResolveReference(node, out var number, out _) && !visited.Add(number))
        {
            return;
        }

        if (file.ResolveDictionary(node) is not { } dictionary)
        {
            return;
        }

        foreach (var kid in file.ResolveArray(dictionary["Kids"]))
        {
            Walk(file, kid, entries, visited);
        }

        var items = file.ResolveArray(dictionary["Names"]);
        for (var index = 0; index + 1 < items.Count; index += 2)
        {
            entries.Add(new NameEntry(
                Text(file, items[index]) ?? NameOf(file, items[index + 1]) ?? string.Empty,
                items[index + 1]));
        }
    }

    /// <summary>The name a file specification declares, for a key-less entry such as an <c>/AF</c> item.</summary>
    private static string? NameOf(PdfFile file, PdfValue? spec)
        => file.ResolveDictionary(spec) is { } dictionary
            ? Text(file, dictionary["UF"]) ?? Text(file, dictionary["F"])
            : null;

    private static string? Text(PdfFile file, PdfValue? value) => file.Resolve(value) switch
    {
        PdfString text => text.Decode(),
        PdfName name => name.Value,
        _ => null,
    };

    private static string? Name(PdfFile file, PdfValue? value)
        => file.Resolve(value) is PdfName name ? name.Value : Text(file, value);

    private static PdfAttachmentRole? Role(PdfFile file, PdfValue? value) => Name(file, value) switch
    {
        "Source" => PdfAttachmentRole.Source,
        "Data" => PdfAttachmentRole.Data,
        "Alternative" => PdfAttachmentRole.Alternative,
        "Supplement" => PdfAttachmentRole.Supplement,
        _ => null,
    };

    /// <summary>The document as an updatable revision, or an exception naming what blocks the update.</summary>
    private static PdfDocument Update(byte[] pdf)
    {
        try
        {
            return PdfDocument.Parse(Encoding.Latin1.GetString(pdf));
        }
        catch (PdfSignatureException exception)
        {
            throw new PdfOperationException(
                "the document cannot take an incremental update: " + exception.Message, exception);
        }
    }

    private static string Reference(int number) => number.ToString(CultureInfo.InvariantCulture) + " 0 R";

    /// <summary>Guesses a MIME type from an extension, and says so when it cannot.</summary>
    private static string GuessMimeType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".xml" => "text/xml",
        ".pdf" => "application/pdf",
        ".json" => "application/json",
        ".csv" => "text/csv",
        ".txt" => "text/plain",
        ".html" or ".htm" => "text/html",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".zip" => "application/zip",
        _ => "application/octet-stream",
    };

    private static byte[] Deflate(ReadOnlySpan<byte> content)
    {
        using var output = new MemoryStream();

        using (var deflate = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(content);
        }

        return output.ToArray();
    }

    private static string StreamBody(byte[] compressed, int length, string mime)
    {
        var size = length.ToString(CultureInfo.InvariantCulture);

        return "<</Type/EmbeddedFile/Subtype/" + EscapeName(mime)
            + "/Length " + compressed.Length.ToString(CultureInfo.InvariantCulture)
            + "/Filter/FlateDecode/Params<</Size " + size + ">>"
            + ">>\nstream\n" + Encoding.Latin1.GetString(compressed) + "\nendstream";
    }

    /// <summary>Writes a name, escaping every byte a name may not hold directly, <c>/</c> included.</summary>
    private static string EscapeName(string name)
    {
        var builder = new StringBuilder(name.Length);

        foreach (var value in Encoding.UTF8.GetBytes(name))
        {
            if (value is > 0x20 and < 0x7F && value is not (byte)'#' and not (byte)'(' and not (byte)')'
                and not (byte)'<' and not (byte)'>' and not (byte)'[' and not (byte)']'
                and not (byte)'{' and not (byte)'}' and not (byte)'/' and not (byte)'%')
            {
                builder.Append((char)value);
                continue;
            }

            builder.Append('#').Append(value.ToString("X2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static string FileSpecBody(string name, int streamNumber, PdfAttachmentRole role, string? description)
    {
        var builder = new StringBuilder("<</Type/Filespec/F")
            .Append(LatinHex(name))
            .Append("/UF").Append(UnicodeHex(name))
            .Append("/EF<</F ").Append(streamNumber.ToString(CultureInfo.InvariantCulture)).Append(" 0 R>>");

        if (role != PdfAttachmentRole.Unspecified)
        {
            builder.Append("/AFRelationship/").Append(role.ToString());
        }

        if (!string.IsNullOrWhiteSpace(description))
        {
            builder.Append("/Desc").Append(UnicodeHex(description));
        }

        return builder.Append(">>").ToString();
    }

    private static string NameTreeBody(IEnumerable<NameEntry> entries)
    {
        var items = entries
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .Select(entry => UnicodeHex(entry.Name) + " " + ReferenceText(entry.Spec));

        return "<</Names[" + string.Join(" ", items) + "]>>";
    }

    /// <summary>A text string as UTF-16BE hexadecimal, which carries any name without escaping.</summary>
    private static string UnicodeHex(string text)
        => "<FEFF" + Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(text)) + ">";

    /// <summary>A PDFDocEncoding string as hexadecimal, which for the names PDF files carry is Latin-1.</summary>
    private static string LatinHex(string text)
        => "<" + Convert.ToHexString(Encoding.Latin1.GetBytes(text)) + ">";

    /// <summary>The text of a value the writer must restate, as a reference or a serialised value.</summary>
    private static string ReferenceText(PdfValue? value) => value switch
    {
        PdfReference reference => Reference(reference.Number),
        null => throw new PdfOperationException("an attachment has no file specification to write"),
        _ => Serialize(value),
    };

    /// <summary>Writes a value read from the file back out, so an inline file specification survives an update.</summary>
    private static string Serialize(PdfValue value) => value switch
    {
        PdfReference reference => Reference(reference.Number),
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
            $"an attachment's file specification holds a {value.GetType().Name}, which this writer cannot carry into an update"),
    };

    private static bool SameSpec(PdfValue left, PdfValue right)
        => left is PdfReference a && right is PdfReference b ? a.Number == b.Number : ReferenceEquals(left, right);

    /// <summary>Adds <paramref name="item"/> to the array at <paramref name="key"/>, or starts one.</summary>
    private static string AppendToArray(string body, string key, string item)
    {
        if (Locate(body, key) is not { } located)
        {
            return InsertBeforeClose(body, "/" + key + "[" + item + "]");
        }

        var value = body.Substring(located.ValueStart, located.ValueLength);
        if (!value.StartsWith('['))
        {
            // A producer may write one file specification without an array; the addition wraps it.
            return Replace(
                body,
                located.ValueStart,
                located.ValueLength,
                "[" + value.Trim() + " " + item + "]");
        }

        var inner = value.AsSpan(1, value.Length - 2).Trim();

        return Replace(
            body,
            located.ValueStart,
            located.ValueLength,
            inner.IsEmpty ? "[" + item + "]" : "[" + inner.ToString() + " " + item + "]");
    }

    /// <summary>Removes <paramref name="item"/> from the array at <paramref name="key"/>, dropping the key when it empties.</summary>
    private static string RemoveFromArray(string body, string key, string item)
    {
        if (Locate(body, key) is not { } located)
        {
            return body;
        }

        var value = body.Substring(located.ValueStart, located.ValueLength);
        if (!value.StartsWith('['))
        {
            // A producer may write one file specification without an array around it. It is still the
            // value of the key, so removing it means removing the key.
            return string.Equals(value.Trim(), item, StringComparison.Ordinal) ? RemoveEntry(body, located) : body;
        }

        var kept = new List<string>();
        var index = 1;

        while (index < value.Length - 1)
        {
            while (index < value.Length - 1 && char.IsWhiteSpace(value[index]))
            {
                index++;
            }

            var end = EndOfValue(value, index);
            if (end <= index)
            {
                break;
            }

            var text = value.Substring(index, end - index);
            if (!string.Equals(text.Trim(), item, StringComparison.Ordinal))
            {
                kept.Add(text);
            }

            index = end;
        }

        if (kept.Count > 0)
        {
            return Replace(body, located.ValueStart, located.ValueLength, "[" + string.Join(" ", kept) + "]");
        }

        return RemoveEntry(body, located);
    }

    /// <summary>Drops an entry and the whitespace that introduced it: an empty array says nothing that no key says.</summary>
    private static string RemoveEntry(string body, Located located)
    {
        var start = located.KeyStart;
        while (start > 0 && char.IsWhiteSpace(body[start - 1]))
        {
            start--;
        }

        return Replace(body, start, located.ValueStart + located.ValueLength - start, string.Empty);
    }

    /// <summary>Where a key and its value sit in a dictionary body.</summary>
    private readonly record struct Located(int KeyStart, int ValueStart, int ValueLength);

    private static Located? Locate(string body, string key)
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
    private static int EndOfValue(string body, int start)
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

    /// <summary>Replaces <paramref name="length"/> characters at <paramref name="start"/>; a zero length inserts.</summary>
    private static string Replace(string body, int start, int length, string value)
        => string.Concat(body.AsSpan(0, start), value, body.AsSpan(start + length));

    /// <summary>Inserts <paramref name="value"/> before the <c>&gt;&gt;</c> that closes the dictionary the body holds.</summary>
    private static string InsertBeforeClose(string body, string value)
    {
        var open = body.IndexOf("<<", StringComparison.Ordinal);
        var close = open < 0 ? -1 : PdfSyntax.CloseDictionary(body, open);

        if (close < 0)
        {
            throw new PdfOperationException("a dictionary of the document is not text this writer can update");
        }

        return Replace(body, close, 0, value);
    }

    /// <summary>One entry of the name tree: the name a reader shows, and the file specification it points at.</summary>
    private readonly record struct NameEntry(string Name, PdfValue Spec);
}
