using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// Page-level operations on a PDF: merge several documents, extract, split, rotate and remove pages.
/// </summary>
/// <remarks>
/// Every operation rebuilds the file rather than appending an incremental update: objects are
/// renumbered, the page tree is rewritten with the pages that survive, and a fresh cross-reference
/// table is written. A page keeps its content streams, its resources, its fonts, its images and its
/// annotations, and a font shared by two pages is copied once.
/// <para>
/// A rebuild does not carry the document-level structures that describe the document as a whole:
/// tagging (<c>/StructTreeRoot</c>, <c>/MarkInfo</c>), outlines, named destinations and the
/// <c>/AcroForm</c> dictionary are dropped by merge, extraction, splitting and removal. Rotate is the
/// exception: it copies the catalog as it is, so tagging, outlines and form fields survive a
/// rotation. Text extraction, redaction, encryption and OCR are out of scope, and an encrypted
/// document is refused rather than silently damaged.
/// </para>
/// </remarks>
public static class PdfPages
{
    /// <summary>
    /// Merges <paramref name="documents"/> into one PDF, in the order given, with every page's
    /// content, resources and annotations preserved.
    /// </summary>
    /// <param name="documents">The PDFs to merge, each as its whole file.</param>
    /// <returns>The merged document.</returns>
    /// <exception cref="ArgumentNullException">An element is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">No document was given.</exception>
    /// <exception cref="PdfOperationException">
    /// A document is not a PDF, is encrypted, has no page, or names a page the writer cannot rebuild.
    /// </exception>
    public static byte[] Merge(params byte[][] documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        if (documents.Length == 0)
        {
            throw new ArgumentException("merging needs at least one document", nameof(documents));
        }

        var opened = new List<PdfFile>(documents.Length);
        foreach (var document in documents)
        {
            ArgumentNullException.ThrowIfNull(document);
            opened.Add(Open(document, "merge"));
        }

        var writer = new PdfObjectWriter();
        var catalogNumber = writer.Reserve();
        var pagesNumber = writer.Reserve();
        var kids = new PdfArray();

        var major = 1;
        var minor = 7;

        foreach (var file in opened)
        {
            if (file.MajorVersion > major || (file.MajorVersion == major && file.MinorVersion > minor))
            {
                major = file.MajorVersion;
                minor = file.MinorVersion;
            }

            var pages = file.Pages();
            if (pages.Count == 0)
            {
                throw new PdfOperationException("a document being merged has no page");
            }

            var copier = new PdfObjectCopier(file, writer);
            var numbers = new List<int>(pages.Count);

            foreach (var page in pages)
            {
                var number = copier.AssignPage(RequireNumber(page));
                numbers.Add(number);
                kids.Items.Add(new PdfReference(number));
            }

            for (var index = 0; index < pages.Count; index++)
            {
                writer.Set(numbers[index], Page(copier, file, pages[index], pagesNumber));
            }
        }

        writer.Set(pagesNumber, Tree(kids));
        writer.Set(catalogNumber, Catalog(new PdfReference(pagesNumber), null));

        return writer.Build(catalogNumber, major, minor);
    }

    /// <summary>
    /// Merges the PDFs read from <paramref name="documents"/>, in the order given. Each stream is read
    /// from its current position to its end and is not disposed.
    /// </summary>
    /// <param name="documents">The streams to merge.</param>
    /// <returns>The merged document.</returns>
    /// <exception cref="ArgumentNullException">An element is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">No document was given.</exception>
    /// <exception cref="PdfOperationException">A document cannot be merged.</exception>
    public static byte[] Merge(params Stream[] documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        if (documents.Length == 0)
        {
            throw new ArgumentException("merging needs at least one document", nameof(documents));
        }

        var bytes = new List<byte[]>(documents.Length);

        foreach (var document in documents)
        {
            ArgumentNullException.ThrowIfNull(document);

            using var buffer = new MemoryStream();
            document.CopyTo(buffer);
            bytes.Add(buffer.ToArray());
        }

        return Merge(bytes.ToArray());
    }

    /// <summary>
    /// Returns a document holding the pages <paramref name="pageRanges"/> names, in document order.
    /// </summary>
    /// <param name="pdf">The source document.</param>
    /// <param name="pageRanges">One-indexed pages, for example <c>1-3,5</c> or <c>2-</c>.</param>
    /// <returns>The extracted pages as a complete PDF.</returns>
    /// <exception cref="PdfOperationException">The document cannot be read or rebuilt.</exception>
    /// <exception cref="ArgumentException">The selection is not a page selection.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The selection names a page that does not exist.</exception>
    public static byte[] Extract(byte[] pdf, string pageRanges)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentException.ThrowIfNullOrWhiteSpace(pageRanges);

        var file = Open(pdf, "extract pages from");
        var pages = Pages(file, "extract pages from");
        var selected = PdfPageRange.Parse(pageRanges, pages.Count);

        return Rebuild(file, pages, selected);
    }

    /// <summary>
    /// Splits <paramref name="pdf"/> into consecutive parts of at most <paramref name="pagesPerFile"/>
    /// pages each. A last part may be shorter.
    /// </summary>
    /// <param name="pdf">The source document.</param>
    /// <param name="pagesPerFile">How many pages each part holds; at least one.</param>
    /// <returns>One document per part, in page order.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pagesPerFile"/> is less than one.</exception>
    /// <exception cref="PdfOperationException">The document cannot be read or rebuilt.</exception>
    public static IReadOnlyList<byte[]> Split(byte[] pdf, int pagesPerFile)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentOutOfRangeException.ThrowIfLessThan(pagesPerFile, 1);

        var file = Open(pdf, "split");
        var pages = Pages(file, "split");

        var parts = new List<byte[]>();
        for (var start = 0; start < pages.Count; start += pagesPerFile)
        {
            var indexes = new List<int>();
            for (var index = start; index < Math.Min(start + pagesPerFile, pages.Count); index++)
            {
                indexes.Add(index);
            }

            parts.Add(Rebuild(file, pages, indexes));
        }

        return parts;
    }

    /// <summary>
    /// Adds <paramref name="degrees"/> to the <c>/Rotate</c> entry of the selected pages, clockwise.
    /// </summary>
    /// <param name="pdf">The source document.</param>
    /// <param name="degrees">A multiple of 90; the result is normalised to 0, 90, 180 or 270.</param>
    /// <param name="pageRanges">
    /// One-indexed pages to rotate; every page when <see langword="null"/> or empty.
    /// </param>
    /// <returns>The rotated document.</returns>
    /// <exception cref="ArgumentException"><paramref name="degrees"/> is not a multiple of 90.</exception>
    /// <exception cref="PdfOperationException">The document cannot be read or rebuilt.</exception>
    public static byte[] Rotate(byte[] pdf, int degrees, string? pageRanges = null)
    {
        ArgumentNullException.ThrowIfNull(pdf);

        if (degrees % 90 != 0)
        {
            throw new ArgumentException($"a rotation of {degrees} degrees is not a multiple of 90", nameof(degrees));
        }

        var file = Open(pdf, "rotate");
        var pages = Pages(file, "rotate");

        var selected = string.IsNullOrWhiteSpace(pageRanges)
            ? Enumerable.Range(0, pages.Count).ToList()
            : PdfPageRange.Parse(pageRanges, pages.Count);

        var writer = new PdfObjectWriter();
        var catalogNumber = writer.Reserve();
        var copier = new PdfObjectCopier(file, writer);
        var numbers = new List<int>(pages.Count);

        foreach (var page in pages)
        {
            numbers.Add(copier.AssignPage(RequireNumber(page)));
        }

        // The catalog is copied as it is: /Pages then resolves to a copied page tree whose kids are
        // the page numbers reserved above, and tagging, outlines and form fields keep working.
        var catalog = copier.CopyDictionary(RequireCatalog(file));

        for (var index = 0; index < pages.Count; index++)
        {
            var page = pages[index];
            var rotation = EffectiveRotation(file, page.Dictionary);
            var target = selected.Contains(index) ? Normalize(rotation + degrees) : rotation;

            var dictionary = copier.CopyDictionary(page.Dictionary, "Rotate");
            dictionary.Entries["Rotate"] = new PdfNumber(target);
            writer.Set(numbers[index], dictionary);
        }

        writer.Set(catalogNumber, catalog);

        return writer.Build(catalogNumber, file.MajorVersion, file.MinorVersion);
    }

    /// <summary>Returns the document without the pages <paramref name="pageRanges"/> names.</summary>
    /// <param name="pdf">The source document.</param>
    /// <param name="pageRanges">One-indexed pages to remove, for example <c>1-3,5</c> or <c>2-</c>.</param>
    /// <returns>The remaining pages as a complete PDF.</returns>
    /// <exception cref="PdfOperationException">
    /// The document cannot be read or rebuilt, or the selection would remove every page.
    /// </exception>
    public static byte[] Remove(byte[] pdf, string pageRanges)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentException.ThrowIfNullOrWhiteSpace(pageRanges);

        var file = Open(pdf, "remove pages from");
        var pages = Pages(file, "remove pages from");
        var removed = PdfPageRange.Parse(pageRanges, pages.Count).ToHashSet();

        var kept = new List<int>();
        for (var index = 0; index < pages.Count; index++)
        {
            if (!removed.Contains(index))
            {
                kept.Add(index);
            }
        }

        if (kept.Count == 0)
        {
            throw new PdfOperationException(
                $"the selection removes all {pages.Count} page(s), which would leave no document");
        }

        return Rebuild(file, pages, kept);
    }

    /// <summary>Builds a document holding <paramref name="selected"/> of <paramref name="pages"/>.</summary>
    private static byte[] Rebuild(PdfFile file, IReadOnlyList<PdfPage> pages, IReadOnlyList<int> selected)
    {
        var writer = new PdfObjectWriter();
        var catalogNumber = writer.Reserve();
        var pagesNumber = writer.Reserve();
        var copier = new PdfObjectCopier(file, writer);
        var kids = new PdfArray();
        var kept = selected.Distinct().Order().ToList();
        var numbers = new List<int>(kept.Count);

        for (var index = 0; index < pages.Count; index++)
        {
            if (!kept.Contains(index))
            {
                copier.ExcludePage(RequireNumber(pages[index]));
            }
        }

        for (var index = 0; index < pages.Count; index++)
        {
            if (!kept.Contains(index))
            {
                continue;
            }

            var number = copier.AssignPage(RequireNumber(pages[index]));
            numbers.Add(number);
            kids.Items.Add(new PdfReference(number));
        }

        for (var index = 0; index < numbers.Count; index++)
        {
            writer.Set(numbers[index], Page(copier, file, pages[kept[index]], pagesNumber));
        }

        writer.Set(pagesNumber, Tree(kids));

        var language = file.Catalog?["Lang"] is { } lang && lang is not PdfNullValue
            ? copier.Copy(lang)
            : null;

        writer.Set(catalogNumber, Catalog(new PdfReference(pagesNumber), language));

        return writer.Build(catalogNumber, file.MajorVersion, file.MinorVersion);
    }

    /// <summary>Builds one page dictionary under a fresh page tree.</summary>
    private static PdfDictionary Page(
        PdfObjectCopier copier,
        PdfFile file,
        PdfPage page,
        int parentNumber)
    {
        var inherited = new List<string>();
        foreach (var key in new[] { "Resources", "MediaBox", "CropBox", "Rotate" })
        {
            if (!page.Dictionary.ContainsKey(key))
            {
                inherited.Add(key);
            }
        }

        var dictionary = copier.CopyDictionary(page.Dictionary, ["Parent", "Annots", "StructParents", .. inherited]);
        dictionary.Entries["Type"] = new PdfName("Page");
        dictionary.Entries["Parent"] = new PdfReference(parentNumber);

        foreach (var key in inherited)
        {
            var value = Inherited(file, page.Dictionary, key);
            if (value is not null)
            {
                dictionary.Entries[key] = copier.Copy(value);
            }
        }

        if (dictionary["MediaBox"] is null)
        {
            throw new PdfOperationException(
                "a page has no /MediaBox, not even an inherited one, so the page cannot be rebuilt");
        }

        if (page.Dictionary["Annots"] is { } annots)
        {
            var annotations = new PdfArray();

            foreach (var item in file.ResolveArray(annots))
            {
                copier.ClearDangling();
                var annotation = copier.Copy(item);

                // An annotation whose link or widget points at a page this operation dropped is not
                // written: keeping it would leave a destination into a page the file no longer holds.
                if (!copier.Dangling)
                {
                    annotations.Items.Add(annotation);
                }
            }

            if (annotations.Items.Count > 0)
            {
                dictionary.Entries["Annots"] = annotations;
            }
        }

        return dictionary;
    }

    private static PdfDictionary Tree(PdfArray kids)
    {
        var dictionary = new PdfDictionary();
        dictionary.Entries["Type"] = new PdfName("Pages");
        dictionary.Entries["Count"] = new PdfNumber(kids.Items.Count);
        dictionary.Entries["Kids"] = kids;

        return dictionary;
    }

    private static PdfDictionary Catalog(PdfReference pages, PdfValue? language)
    {
        var dictionary = new PdfDictionary();
        dictionary.Entries["Type"] = new PdfName("Catalog");
        dictionary.Entries["Pages"] = pages;

        if (language is not null)
        {
            dictionary.Entries["Lang"] = language;
        }

        return dictionary;
    }

    /// <summary>Reads <paramref name="key"/> from the page or the nearest ancestor that carries it.</summary>
    private static PdfValue? Inherited(PdfFile file, PdfDictionary page, string key)
    {
        var current = page;

        for (var depth = 0; depth < 64; depth++)
        {
            if (current[key] is { } value && value is not PdfNullValue)
            {
                return value;
            }

            current = file.ResolveDictionary(current["Parent"]);
            if (current is null)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>The <c>/Rotate</c> a reader applies, inherited or not, normalised to 0..359.</summary>
    private static int EffectiveRotation(PdfFile file, PdfDictionary page)
        => file.Resolve(Inherited(file, page, "Rotate")) is PdfNumber number && number.IsInteger
            ? Normalize(number.AsInteger)
            : 0;

    private static int Normalize(long degrees)
    {
        var normalized = degrees % 360;

        if (normalized < 0)
        {
            normalized += 360;
        }

        return (int)normalized;
    }

    private static int RequireNumber(PdfPage page)
    {
        if (page.Number == 0)
        {
            throw new PdfOperationException(
                "a page is named directly in the page tree instead of by object, so it cannot be renumbered");
        }

        return page.Number;
    }

    private static PdfDictionary RequireCatalog(PdfFile file)
        => file.Catalog ?? throw new PdfOperationException("the document has no readable catalog");

    private static IReadOnlyList<PdfPage> Pages(PdfFile file, string operation)
    {
        var pages = file.Pages();

        if (pages.Count == 0)
        {
            throw new PdfOperationException($"the document has no page, so there is nothing to {operation}");
        }

        return pages;
    }

    private static PdfFile Open(byte[] pdf, string operation) => PdfSource.Open(pdf, operation);
}
