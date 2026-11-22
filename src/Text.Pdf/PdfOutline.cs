using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// Reads and writes a PDF's bookmarks: the <c>/Outlines</c> tree the catalog names.
/// </summary>
/// <remarks>
/// <para>
/// Reading follows <c>/Outlines</c> → <c>/First</c> → <c>/Next</c> and the <c>/First</c> of every
/// item, so the hierarchy a viewer shows is the hierarchy this type returns. A destination is
/// resolved whether the item carries a direct array (<c>[page /XYZ left top zoom]</c>), a named
/// destination (<c>/Names</c> name tree or the older <c>/Dests</c> dictionary), or a
/// <c>/GoTo</c> action whose <c>/D</c> holds the array; the page it points at becomes a one-based
/// number in the flattened page tree.
/// </para>
/// <para>
/// Writing never rebuilds the document: the new items, a new outline root and the updated catalog
/// are appended as an incremental update, followed by a cross-reference section that chains to the
/// previous one. Every byte of the original file stays where it was, which is what keeps an
/// existing signature over it verifiable.
/// </para>
/// <para>
/// Colors (<c>/C</c>), icons and the other presentation entries read by some viewers are not
/// exposed: a bookmark is its title, its page, its children and its open, bold and italic state.
/// </para>
/// </remarks>
public sealed class PdfOutline
{
    /// <summary>How deep an outline may nest before the file is treated as broken.</summary>
    private const int MaxDepth = 32;

    /// <summary>How many entries one level may hold before the file is treated as broken.</summary>
    private const int MaxEntriesPerLevel = 10_000;

    private PdfOutline(IReadOnlyList<PdfOutlineEntry> entries) => Entries = entries;

    /// <summary>The top-level bookmarks, in document order; empty when the document has no outline.</summary>
    public IReadOnlyList<PdfOutlineEntry> Entries { get; }

    /// <summary>Reads the outline of <paramref name="pdf"/>.</summary>
    /// <param name="pdf">The document, as its whole file.</param>
    /// <returns>The bookmarks, or an outline with no entry when the document carries none.</returns>
    /// <exception cref="PdfOperationException">
    /// The bytes are not a PDF, or the document is encrypted and must be decrypted first with
    /// <see cref="PdfEncryption.Open"/>.
    /// </exception>
    public static PdfOutline Read(ReadOnlySpan<byte> pdf)
    {
        var bytes = pdf.ToArray();

        if (!PdfFile.TryOpen(bytes, out var file))
        {
            throw new PdfOperationException("the bytes are not a PDF, so they carry no outline");
        }

        if (file.IsEncrypted)
        {
            throw new PdfOperationException(
                "the document is encrypted, so its outline cannot be read until it is decrypted: open it with PdfEncryption.Open and use the bytes it returns");
        }

        if (file.Catalog is null || file.ResolveDictionary(file.Catalog["Outlines"]) is not { } outlines)
        {
            return new PdfOutline([]);
        }

        var flattened = file.Pages();
        var pages = new Dictionary<int, int>(flattened.Count);
        for (var index = 0; index < flattened.Count; index++)
        {
            if (flattened[index].Number > 0)
            {
                pages[flattened[index].Number] = index + 1;
            }
        }

        var named = NamedDestinations(file, file.Catalog);
        var entries = new List<PdfOutlineEntry>();
        ReadSiblings(file, outlines["First"], pages, flattened.Count, named, entries, 0);

        return new PdfOutline(entries);
    }

    /// <summary>
    /// Returns a copy of <paramref name="pdf"/> whose catalog names an outline built from
    /// <paramref name="entries"/>, appended as an incremental update.
    /// </summary>
    /// <param name="pdf">The document to attach the bookmarks to.</param>
    /// <param name="entries">
    /// The top-level bookmarks, in the order a viewer shows them. An empty collection removes the
    /// outline by pointing the catalog at an empty one.
    /// </param>
    /// <returns>The updated document.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is <see langword="null"/>.</exception>
    /// <exception cref="PdfOperationException">
    /// The bytes are not a PDF, the document is encrypted, an entry names no page or a page the
    /// document does not have, or the catalog holds something this writer cannot restate.
    /// </exception>
    public static byte[] Write(ReadOnlySpan<byte> pdf, IEnumerable<PdfOutlineEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var bytes = pdf.ToArray();
        var file = PdfSource.Open(bytes, "write an outline");

        var roots = new List<PdfOutlineEntry>(entries);
        var flattened = file.Pages();

        Validate(roots, flattened.Count, 0);

        var text = Encoding.Latin1.GetString(bytes);
        var writer = new PdfOutlineWriter(text, file.Size);

        var catalog = new PdfDictionary();
        foreach (var (key, value) in file.Catalog!.Entries)
        {
            catalog.Entries[key] = value;
        }

        if (!file.TryResolveReference(file.Trailer["Root"], out var catalogNumber, out _))
        {
            catalogNumber = writer.Reserve();
        }

        var outlineRoot = writer.Reserve();
        var nodes = Assign(writer, roots, 0);

        WriteItems(writer, nodes, outlineRoot, flattened);
        WriteOutlineRoot(writer, outlineRoot, nodes);

        catalog.Entries["Outlines"] = new PdfReference(outlineRoot);
        catalog.Entries["PageMode"] = new PdfName("UseOutlines");
        writer.Add(catalogNumber, PdfOutlineWriter.Serialize(catalog));

        return writer.Finish(catalogNumber, PreviousXref(text), file.Trailer["ID"]);
    }

    /// <summary>Walks the <c>/Next</c> chain that starts at <paramref name="first"/>.</summary>
    private static void ReadSiblings(
        PdfFile file,
        PdfValue? first,
        Dictionary<int, int> pages,
        int pageCount,
        Dictionary<string, PdfValue> named,
        List<PdfOutlineEntry> into,
        int depth)
    {
        if (depth > MaxDepth)
        {
            return;
        }

        var seen = new HashSet<int>();
        var current = first;

        for (var guard = 0; guard < MaxEntriesPerLevel && current is not null; guard++)
        {
            if (file.ResolveDictionary(current) is not { } item)
            {
                return;
            }

            // A cycle in /Next would otherwise be walked until the guard ran out; an item that is
            // not an indirect object cannot be part of one, so only those need remembering.
            if (file.TryResolveReference(current, out var number, out _) && !seen.Add(number))
            {
                return;
            }

            into.Add(ReadItem(file, item, pages, pageCount, named, depth));
            current = item["Next"];
        }
    }

    private static PdfOutlineEntry ReadItem(
        PdfFile file,
        PdfDictionary item,
        Dictionary<int, int> pages,
        int pageCount,
        Dictionary<string, PdfValue> named,
        int depth)
    {
        var title = (file.Resolve(item["Title"]) as PdfString)?.Decode() ?? string.Empty;
        var (page, destination) = ResolveDestination(file, DestinationOf(file, item), pages, pageCount, named, null, 0);

        var children = new List<PdfOutlineEntry>();
        ReadSiblings(file, item["First"], pages, pageCount, named, children, depth + 1);

        var count = (file.Resolve(item["Count"]) as PdfNumber) is { IsInteger: true } countNumber
            ? (int)countNumber.AsInteger
            : 0;
        var flags = (file.Resolve(item["F"]) as PdfNumber) is { IsInteger: true } flagNumber
            ? (int)flagNumber.AsInteger
            : 0;

        var entry = new PdfOutlineEntry(title, page)
        {
            Destination = destination,
            IsOpen = children.Count > 0 && count >= 0,
            IsBold = (flags & 2) != 0,
            IsItalic = (flags & 1) != 0,
        };

        entry.AddRange(children);

        return entry;
    }

    /// <summary>The item's <c>/Dest</c>, or the <c>/D</c> of the <c>/GoTo</c> action it carries instead.</summary>
    private static PdfValue? DestinationOf(PdfFile file, PdfDictionary item)
    {
        if (item["Dest"] is { } direct)
        {
            return direct;
        }

        if (file.ResolveDictionary(item["A"]) is { } action &&
            file.Resolve(action["S"]) is PdfName { Value: "GoTo" })
        {
            return action["D"];
        }

        return null;
    }

    /// <summary>
    /// Resolves a destination to the page it opens and the shape it has, following a name through
    /// the document's named destinations and a destination dictionary to its <c>/D</c>.
    /// </summary>
    private static (int? Page, PdfOutlineDestination? Destination) ResolveDestination(
        PdfFile file,
        PdfValue? value,
        Dictionary<int, int> pages,
        int pageCount,
        Dictionary<string, PdfValue> named,
        string? name,
        int depth)
    {
        if (value is null || depth > 8)
        {
            return default;
        }

        switch (file.Resolve(value))
        {
            case PdfName destinationName:
                if (named.TryGetValue(destinationName.Value, out var namedTarget))
                {
                    return ResolveDestination(file, namedTarget, pages, pageCount, named, destinationName.Value, depth + 1);
                }

                return (null, new PdfOutlineDestination(destinationName.Value, string.Empty, null, null, null));

            case PdfString text:
                if (named.TryGetValue(text.Decode(), out var stringTarget))
                {
                    return ResolveDestination(file, stringTarget, pages, pageCount, named, text.Decode(), depth + 1);
                }

                return (null, new PdfOutlineDestination(text.Decode(), string.Empty, null, null, null));

            case PdfDictionary dictionary:
                return dictionary["D"] is { } inner
                    ? ResolveDestination(file, inner, pages, pageCount, named, name, depth + 1)
                    : default;

            case PdfArray array:
                return ResolveArrayDestination(file, array, pages, pageCount, name);

            default:
                return default;
        }
    }

    private static (int? Page, PdfOutlineDestination? Destination) ResolveArrayDestination(
        PdfFile file,
        PdfArray array,
        Dictionary<int, int> pages,
        int pageCount,
        string? name)
    {
        var page = PageOf(file, array.Items, pages, pageCount);
        var fit = array.Items.Count > 1 && file.Resolve(array.Items[1]) is PdfName fitName ? fitName.Value : string.Empty;

        double? Coordinate(int index)
            => index < array.Items.Count && file.Resolve(array.Items[index]) is PdfNumber number ? number.Value : null;

        var (left, top, zoom) = fit switch
        {
            "XYZ" => (Coordinate(2), Coordinate(3), Coordinate(4)),
            "FitH" or "FitBH" => (null, Coordinate(2), null),
            "FitV" or "FitBV" => (Coordinate(2), null, null),
            _ => ((double?)null, (double?)null, (double?)null),
        };

        return (page, new PdfOutlineDestination(name, fit, left, top, zoom));
    }

    /// <summary>The one-based page the first element of a destination array points at.</summary>
    private static int? PageOf(PdfFile file, List<PdfValue> items, Dictionary<int, int> pages, int pageCount)
    {
        if (items.Count == 0)
        {
            return null;
        }

        if (file.TryResolveReference(items[0], out var number, out _))
        {
            return pages.TryGetValue(number, out var page) ? page : null;
        }

        // A destination may name the page by its index rather than by reference; the format counts
        // page numbers from zero there, and a viewer shows them from one.
        if (file.Resolve(items[0]) is PdfNumber { IsInteger: true } index)
        {
            var zeroBased = (int)index.AsInteger;

            return zeroBased >= 0 && zeroBased < pageCount ? zeroBased + 1 : null;
        }

        return null;
    }

    /// <summary>The catalog's named destinations, from <c>/Dests</c> and from the <c>/Names</c> tree.</summary>
    private static Dictionary<string, PdfValue> NamedDestinations(PdfFile file, PdfDictionary catalog)
    {
        var table = new Dictionary<string, PdfValue>(StringComparer.Ordinal);

        if (file.ResolveDictionary(catalog["Dests"]) is { } older)
        {
            foreach (var (key, value) in older.Entries)
            {
                table[key] = value;
            }
        }

        if (file.ResolveDictionary(catalog["Names"]) is { } names &&
            file.ResolveDictionary(names["Dests"]) is { } tree)
        {
            CollectNameTree(file, tree, table, 0);
        }

        return table;
    }

    private static void CollectNameTree(PdfFile file, PdfDictionary node, Dictionary<string, PdfValue> table, int depth)
    {
        if (depth > MaxDepth)
        {
            return;
        }

        var names = file.ResolveArray(node["Names"]);
        for (var index = 0; index + 1 < names.Count; index += 2)
        {
            var key = KeyOf(file, names[index]);
            if (key is not null)
            {
                table[key] = names[index + 1];
            }
        }

        foreach (var kid in file.ResolveArray(node["Kids"]))
        {
            if (file.ResolveDictionary(kid) is { } child)
            {
                CollectNameTree(file, child, table, depth + 1);
            }
        }
    }

    private static string? KeyOf(PdfFile file, PdfValue value)
        => file.Resolve(value) switch
        {
            PdfString text => text.Decode(),
            PdfName name => name.Value,
            _ => null,
        };

    /// <summary>Refuses an entry a viewer could not follow: one without a page, or one past the last page.</summary>
    private static void Validate(IEnumerable<PdfOutlineEntry> entries, int pageCount, int depth)
    {
        if (depth > 64)
        {
            throw new PdfOperationException("the outline nests more than 64 levels deep, which is a cycle rather than a plan");
        }

        foreach (var entry in entries)
        {
            if (entry.PageNumber is not { } page)
            {
                throw new PdfOperationException(
                    $"the entry '{entry.Title}' names no page, and a written bookmark needs the 1-based page it opens");
            }

            if (page < 1 || page > pageCount)
            {
                throw new PdfOperationException(
                    $"the entry '{entry.Title}' targets page {page}, but the document has {pageCount}");
            }

            Validate(entry.Children, pageCount, depth + 1);
        }
    }

    /// <summary>Assigns every new object its number, depth first, so a parent can name its children.</summary>
    private static List<Node> Assign(PdfOutlineWriter writer, IEnumerable<PdfOutlineEntry> entries, int depth)
    {
        if (depth > 64)
        {
            throw new PdfOperationException("the outline nests more than 64 levels deep, which is a cycle rather than a plan");
        }

        var nodes = new List<Node>();

        foreach (var entry in entries)
        {
            var node = new Node(entry, writer.Reserve());
            node.Children.AddRange(Assign(writer, entry.Children, depth + 1));
            nodes.Add(node);
        }

        return nodes;
    }

    private static void WriteItems(
        PdfOutlineWriter writer,
        List<Node> nodes,
        int parent,
        IReadOnlyList<PdfPage> pages)
    {
        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            var previous = index > 0 ? nodes[index - 1].Number : (int?)null;
            var next = index + 1 < nodes.Count ? nodes[index + 1].Number : (int?)null;

            writer.Add(node.Number, ItemBody(node, parent, previous, next, pages));
            WriteItems(writer, node.Children, node.Number, pages);
        }
    }

    private static string ItemBody(
        Node node,
        int parent,
        int? previous,
        int? next,
        IReadOnlyList<PdfPage> pages)
    {
        var entry = node.Entry;
        var builder = new StringBuilder("<<");
        builder.Append("/Title").Append(PdfOutlineWriter.TextString(entry.Title));
        builder.Append("/Parent ").Append(parent.ToString(CultureInfo.InvariantCulture)).Append(" 0 R");

        if (previous is { } before)
        {
            builder.Append("/Prev ").Append(before.ToString(CultureInfo.InvariantCulture)).Append(" 0 R");
        }

        if (next is { } after)
        {
            builder.Append("/Next ").Append(after.ToString(CultureInfo.InvariantCulture)).Append(" 0 R");
        }

        if (node.Children.Count > 0)
        {
            var visible = VisibleCount(node);
            builder.Append("/First ").Append(node.Children[0].Number.ToString(CultureInfo.InvariantCulture)).Append(" 0 R")
                   .Append("/Last ").Append(node.Children[^1].Number.ToString(CultureInfo.InvariantCulture)).Append(" 0 R")
                   .Append("/Count ").Append((entry.IsOpen ? visible : -visible).ToString(CultureInfo.InvariantCulture));
        }

        builder.Append("/Dest").Append(Destination(entry, pages[entry.PageNumber!.Value - 1].Number));

        var flags = (entry.IsBold ? 2 : 0) | (entry.IsItalic ? 1 : 0);
        if (flags != 0)
        {
            builder.Append("/F ").Append(flags.ToString(CultureInfo.InvariantCulture));
        }

        return builder.Append(">>").ToString();
    }

    /// <summary>The destination array an entry is written with, at the page's top-left corner by default.</summary>
    private static string Destination(PdfOutlineEntry entry, int pageNumber)
    {
        var page = pageNumber.ToString(CultureInfo.InvariantCulture) + " 0 R";
        var destination = entry.Destination;

        static string Number(double? value)
            => value is { } number ? PdfSyntax.Number(number) : "null";

        return destination?.Fit switch
        {
            "Fit" => $"[{page}/Fit]",
            "FitH" or "FitBH" => $"[{page}/{destination.Fit} {Number(destination.Top)}]",
            "FitV" or "FitBV" => $"[{page}/{destination.Fit} {Number(destination.Left)}]",
            _ => $"[{page}/XYZ {Number(destination?.Left)} {Number(destination?.Top)} {Number(destination?.Zoom)}]",
        };
    }

    private static void WriteOutlineRoot(PdfOutlineWriter writer, int outlineRoot, List<Node> nodes)
    {
        var builder = new StringBuilder("<</Type/Outlines/Count ");
        builder.Append(VisibleTopLevel(nodes).ToString(CultureInfo.InvariantCulture));

        if (nodes.Count > 0)
        {
            builder.Append("/First ").Append(nodes[0].Number.ToString(CultureInfo.InvariantCulture)).Append(" 0 R")
                   .Append("/Last ").Append(nodes[^1].Number.ToString(CultureInfo.InvariantCulture)).Append(" 0 R");
        }

        writer.Add(outlineRoot, builder.Append(">>").ToString());
    }

    /// <summary>How many entries a viewer shows when the node is open: its children and their open descendants.</summary>
    private static int VisibleCount(Node node)
    {
        var visible = 0;

        foreach (var child in node.Children)
        {
            visible += 1 + (child.Entry.IsOpen ? VisibleCount(child) : 0);
        }

        return visible;
    }

    private static int VisibleTopLevel(List<Node> nodes)
    {
        var visible = 0;

        foreach (var node in nodes)
        {
            visible += 1 + (node.Entry.IsOpen ? VisibleCount(node) : 0);
        }

        return visible;
    }

    /// <summary>The offset the previous cross-reference section starts at, which the new trailer chains to.</summary>
    private static int PreviousXref(string text)
    {
        var marker = text.LastIndexOf("startxref", StringComparison.Ordinal);
        if (marker < 0)
        {
            throw new PdfOperationException("the document has no startxref, so it cannot be updated");
        }

        var index = marker + "startxref".Length;
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        var start = index;
        while (index < text.Length && char.IsAsciiDigit(text[index]))
        {
            index++;
        }

        if (start == index)
        {
            throw new PdfOperationException("the document's startxref names no offset, so it cannot be updated");
        }

        return int.Parse(text.AsSpan(start, index - start), CultureInfo.InvariantCulture);
    }

    /// <summary>One bookmark with the object number it was given and the children assigned under it.</summary>
    /// <param name="Entry">The caller's bookmark.</param>
    /// <param name="Number">Its object number in the update.</param>
    private sealed record Node(PdfOutlineEntry Entry, int Number)
    {
        /// <summary>The children, with their own numbers.</summary>
        public List<Node> Children { get; } = [];
    }
}
