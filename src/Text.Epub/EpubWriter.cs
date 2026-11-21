using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using M3scalinum.Text.Epub.Internal;

namespace M3scalinum.Text.Epub;

/// <summary>
/// Writes a book as an EPUB 3 container: a ZIP whose first entry is the uncompressed
/// <c>mimetype</c>, then <c>META-INF/container.xml</c>, then a package document, the navigation
/// document, one XHTML content document per chapter, the stylesheet and every asset.
/// </summary>
/// <remarks>
/// The container is built with <see cref="ZipArchive"/> and nothing else. The <c>epub</c> crate is
/// GPL-3.0 and this repository is Apache-2.0, so it is excluded; the format is a ZIP with four
/// files a reader requires, which is what the writer emits.
/// <para>
/// What makes the result valid rather than merely a zip: <c>mimetype</c> is the first entry and is
/// stored (a reader sniffs those bytes at offset 0), every href the package declares resolves to an
/// entry, the navigation document is the manifest item with <c>properties="nav"</c>, every content
/// document is well-formed XHTML in the XHTML namespace, and the identifiers in the package and in
/// the ids of the content documents are unique.
/// </para>
/// </remarks>
public static class EpubWriter
{
    /// <summary>The path of the package document inside the container.</summary>
    public const string PackagePath = "OEBPS/content.opf";

    /// <summary>The media type a reader sniffs in the first entry of the archive.</summary>
    public const string MediaType = "application/epub+zip";

    private const string MimetypePath = "mimetype";
    private const string ContainerPath = "META-INF/container.xml";
    private const string NavPath = "OEBPS/nav.xhtml";
    private const string StylePath = "OEBPS/book.css";
    private const string OpfNamespace = "http://www.idpf.org/2007/opf";
    private const string DcNamespace = "http://purl.org/dc/elements/1.1/";
    private const string ContainerNamespace = "urn:oasis:names:tc:opendocument:xmlns:container";

    private const int MaxChapters = 999;

    /// <summary>The stylesheet every content document links, sized for a reading system's page.</summary>
    private const string StyleSheet =
        "body { margin: 5%; line-height: 1.45; }\n" +
        "h1, h2, h3, h4, h5, h6 { line-height: 1.2; margin: 1.2em 0 0.4em; }\n" +
        "p { margin: 0 0 0.6em; }\n" +
        "pre { white-space: pre-wrap; font-family: monospace; }\n" +
        "code, kbd, samp { font-family: monospace; }\n" +
        "img { max-width: 100%; }\n" +
        "blockquote { margin-left: 1.5em; font-style: italic; }\n" +
        "table { border-collapse: collapse; }\n" +
        "td, th { padding: 0.2em 0.5em; }\n";

    /// <summary>Writes the book to a new array.</summary>
    /// <param name="book">The book to write.</param>
    /// <returns>The bytes of the EPUB container.</returns>
    /// <exception cref="EpubValidationException">The book cannot be a valid container.</exception>
    public static byte[] Write(EpubBook book)
    {
        ArgumentNullException.ThrowIfNull(book);

        using var buffer = new MemoryStream();
        Build(book, buffer);

        return buffer.ToArray();
    }

    /// <summary>Writes the book to a stream, which is left open.</summary>
    /// <param name="book">The book to write.</param>
    /// <param name="destination">Where the container is written.</param>
    /// <exception cref="EpubValidationException">The book cannot be a valid container.</exception>
    public static void Write(EpubBook book, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(destination);

        Build(book, destination);
    }

    private static void Build(EpubBook book, Stream destination)
    {
        var metadata = book.Metadata;
        var language = ValidateLanguage(metadata.Language);
        var title = Require(metadata.Title, "title");
        var identifier = Require(metadata.Identifier, "identifier");

        if (book.Chapters.Count == 0)
        {
            throw new EpubValidationException("A book needs at least one chapter; the spine would be empty and no reader would open it.");
        }

        if (book.Chapters.Count > MaxChapters)
        {
            throw new EpubValidationException(
                "A book carries at most " + MaxChapters.ToString(CultureInfo.InvariantCulture) + " chapters, because the generated file names are numbered with three digits.");
        }

        var stamp = Stamp(metadata);
        var identifiers = new HashSet<string>(StringComparer.Ordinal) { "bookid", "nav", "css" };
        var references = new List<string>();
        var chapterNames = new List<string>(book.Chapters.Count);
        var chapterIds = new List<string>(book.Chapters.Count);
        var chapterDocuments = new List<string>(book.Chapters.Count);

        var xhtml = new XhtmlWriter(identifiers, references);

        for (var i = 0; i < book.Chapters.Count; i++)
        {
            var chapter = book.Chapters[i];
            var name = "ch" + (i + 1).ToString("D3", CultureInfo.InvariantCulture) + ".xhtml";
            var id = "ch" + (i + 1).ToString("D3", CultureInfo.InvariantCulture);
            var nodes = chapter.Nodes ?? Body(chapter.Html);

            chapterNames.Add(name);
            chapterIds.Add(id);
            chapterDocuments.Add(Xml(writer => xhtml.WriteDocument(writer, chapter.Title, language, "book.css", nodes)));
        }

        var known = new HashSet<string>(chapterNames, StringComparer.Ordinal);
        foreach (var asset in book.Assets)
        {
            known.Add(EpubPath.ValidateAssetName(asset.Name));
        }

        ValidateReferences(references, known);

        var assetIds = new List<string>(book.Assets.Count);
        for (var i = 0; i < book.Assets.Count; i++)
        {
            assetIds.Add("asset" + (i + 1).ToString("D3", CultureInfo.InvariantCulture));
        }

        var nav = BuildNavigation(title, language, book, chapterNames);
        var package = BuildPackage(metadata, title, identifier, language, stamp, chapterIds, chapterNames, book, assetIds);

        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);

        // The mimetype entry comes first and is stored: a reading system sniffs these twenty bytes at
        // offset 0, and a deflated entry would push them behind a compression header.
        AddText(archive, MimetypePath, MediaType, CompressionLevel.NoCompression, stamp);
        AddText(archive, ContainerPath, BuildContainer(), CompressionLevel.Optimal, stamp);
        AddText(archive, PackagePath, package, CompressionLevel.Optimal, stamp);
        AddText(archive, NavPath, nav, CompressionLevel.Optimal, stamp);
        AddText(archive, StylePath, StyleSheet, CompressionLevel.Optimal, stamp);

        for (var i = 0; i < chapterDocuments.Count; i++)
        {
            AddText(archive, "OEBPS/" + chapterNames[i], chapterDocuments[i], CompressionLevel.Optimal, stamp);
        }

        foreach (var asset in book.Assets)
        {
            AddBytes(archive, "OEBPS/" + asset.Name, asset.Content, CompressionLevel.Optimal, stamp);
        }
    }

    private static string Require(string value, string what)
    {
        if (value.Trim().Length == 0)
        {
            throw new EpubValidationException("The book has no " + what + "; an EPUB package cannot be valid without one.");
        }

        return value;
    }

    private static string ValidateLanguage(string language)
    {
        var value = language.Trim();
        if (value.Length == 0)
        {
            throw new EpubValidationException("The book has no language; a package document must declare one.");
        }

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '-')
            {
                throw new EpubValidationException("The language '" + language + "' is not a BCP 47 tag: use letters, digits and '-'.");
            }
        }

        return value;
    }

    /// <summary>The timestamp the package declares and every entry carries.</summary>
    private static DateTimeOffset Stamp(EpubMetadata metadata)
    {
        var stamp = metadata.Modified
            ?? (metadata.Date is { } date
                ? new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
                : DateTimeOffset.UtcNow);

        // ZIP stores two-second resolution, so the bytes of a book written twice are the same only
        // when the stamp is whole seconds.
        return new DateTimeOffset(stamp.UtcTicks - (stamp.UtcTicks % TimeSpan.TicksPerSecond), TimeSpan.Zero);
    }

    private static List<HtmlNode> Body(string html)
        => HtmlParser.Body(HtmlParser.Parse(html)).Children;

    private static void ValidateReferences(IReadOnlyList<string> references, HashSet<string> known)
    {
        foreach (var reference in references)
        {
            var path = reference;
            var hash = path.IndexOf('#', StringComparison.Ordinal);
            if (hash >= 0)
            {
                path = path[..hash];
            }

            if (path.Length == 0 || EpubPath.IsExternal(path))
            {
                continue;
            }

            EpubPath.Validate(path, "reference");

            if (!known.Contains(path))
            {
                throw new EpubValidationException(
                    "The chapter references '" + reference + "', which is neither a chapter nor an asset of this book; add it with AddAsset, or the reader shows a broken link.");
            }
        }
    }

    private static string BuildContainer()
        => Xml(writer =>
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("container", ContainerNamespace);
            writer.WriteAttributeString("version", "1.0");
            writer.WriteStartElement("rootfiles", ContainerNamespace);
            writer.WriteStartElement("rootfile", ContainerNamespace);
            writer.WriteAttributeString("full-path", PackagePath);
            writer.WriteAttributeString("media-type", "application/oebps-package+xml");
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndDocument();
        });

    private static string BuildPackage(
        EpubMetadata metadata,
        string title,
        string identifier,
        string language,
        DateTimeOffset stamp,
        List<string> chapterIds,
        List<string> chapterNames,
        EpubBook book,
        List<string> assetIds)
        => Xml(writer =>
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("package", OpfNamespace);
            writer.WriteAttributeString("version", "3.0");
            writer.WriteAttributeString("unique-identifier", "bookid");
            writer.WriteAttributeString("xml", "lang", XhtmlWriter.XmlNamespace, language);

            writer.WriteStartElement("metadata", OpfNamespace);
            writer.WriteAttributeString("xmlns", "dc", "http://www.w3.org/2000/xmlns/", DcNamespace);

            writer.WriteStartElement("dc", "identifier", DcNamespace);
            writer.WriteAttributeString("id", "bookid");
            writer.WriteString(identifier);
            writer.WriteEndElement();

            writer.WriteStartElement("dc", "title", DcNamespace);
            writer.WriteString(title);
            writer.WriteEndElement();

            writer.WriteStartElement("dc", "language", DcNamespace);
            writer.WriteString(language);
            writer.WriteEndElement();

            if (!string.IsNullOrWhiteSpace(metadata.Author))
            {
                writer.WriteStartElement("dc", "creator", DcNamespace);
                writer.WriteString(metadata.Author);
                writer.WriteEndElement();
            }

            if (!string.IsNullOrWhiteSpace(metadata.Publisher))
            {
                writer.WriteStartElement("dc", "publisher", DcNamespace);
                writer.WriteString(metadata.Publisher);
                writer.WriteEndElement();
            }

            if (metadata.Date is { } date)
            {
                writer.WriteStartElement("dc", "date", DcNamespace);
                writer.WriteString(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                writer.WriteEndElement();
            }

            writer.WriteStartElement("meta", OpfNamespace);
            writer.WriteAttributeString("property", "dcterms:modified");
            writer.WriteString(stamp.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
            writer.WriteEndElement();

            writer.WriteEndElement();

            writer.WriteStartElement("manifest", OpfNamespace);

            WriteItem(writer, "nav", "nav.xhtml", "application/xhtml+xml", "nav");
            WriteItem(writer, "css", "book.css", "text/css", null);

            for (var i = 0; i < chapterIds.Count; i++)
            {
                WriteItem(writer, chapterIds[i], chapterNames[i], "application/xhtml+xml", null);
            }

            for (var i = 0; i < book.Assets.Count; i++)
            {
                WriteItem(writer, assetIds[i], book.Assets[i].Name, book.Assets[i].MediaType, null);
            }

            writer.WriteEndElement();

            writer.WriteStartElement("spine", OpfNamespace);
            foreach (var id in chapterIds)
            {
                writer.WriteStartElement("itemref", OpfNamespace);
                writer.WriteAttributeString("idref", id);
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndDocument();
        });

    private static void WriteItem(XmlWriter writer, string id, string href, string mediaType, string? properties)
    {
        writer.WriteStartElement("item", OpfNamespace);
        writer.WriteAttributeString("id", id);
        writer.WriteAttributeString("href", href);
        writer.WriteAttributeString("media-type", mediaType);

        if (properties is not null)
        {
            writer.WriteAttributeString("properties", properties);
        }

        writer.WriteEndElement();
    }

    private static string BuildNavigation(string title, string language, EpubBook book, List<string> chapterNames)
        => Xml(writer =>
        {
            writer.WriteStartDocument();
            writer.WriteDocType("html", null, null, null);
            writer.WriteStartElement("html", XhtmlWriter.XhtmlNamespace);
            writer.WriteAttributeString("xmlns", "epub", "http://www.w3.org/2000/xmlns/", XhtmlWriter.EpubNamespace);
            writer.WriteAttributeString("xml", "lang", XhtmlWriter.XmlNamespace, language);
            writer.WriteAttributeString("lang", language);

            writer.WriteStartElement("head", XhtmlWriter.XhtmlNamespace);
            writer.WriteStartElement("title", XhtmlWriter.XhtmlNamespace);
            writer.WriteString("Contents");
            writer.WriteEndElement();
            writer.WriteStartElement("meta", XhtmlWriter.XhtmlNamespace);
            writer.WriteAttributeString("charset", "utf-8");
            writer.WriteEndElement();
            writer.WriteEndElement();

            writer.WriteStartElement("body", XhtmlWriter.XhtmlNamespace);
            writer.WriteStartElement("nav", XhtmlWriter.XhtmlNamespace);
            writer.WriteAttributeString("epub", "type", XhtmlWriter.EpubNamespace, "toc");
            writer.WriteAttributeString("id", "toc");
            writer.WriteAttributeString("role", "doc-toc");

            writer.WriteStartElement("h1", XhtmlWriter.XhtmlNamespace);
            writer.WriteString(title);
            writer.WriteEndElement();

            writer.WriteStartElement("ol", XhtmlWriter.XhtmlNamespace);
            for (var i = 0; i < chapterNames.Count; i++)
            {
                writer.WriteStartElement("li", XhtmlWriter.XhtmlNamespace);
                writer.WriteStartElement("a", XhtmlWriter.XhtmlNamespace);
                writer.WriteAttributeString("href", chapterNames[i]);
                writer.WriteString(book.Chapters[i].Title);
                writer.WriteEndElement();
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndDocument();
        });

    /// <summary>Serializes a document through <see cref="XmlWriter"/>, which is what makes it well-formed.</summary>
    private static string Xml(Action<XmlWriter> write)
    {
        using var buffer = new MemoryStream();

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false,
            NewLineHandling = NewLineHandling.None,
            CloseOutput = false,
        };

        using (var writer = XmlWriter.Create(buffer, settings))
        {
            write(writer);
        }

        return Encoding.UTF8.GetString(buffer.ToArray())
            .Replace("<!DOCTYPE html >", "<!DOCTYPE html>", StringComparison.Ordinal);
    }

    private static void AddText(ZipArchive archive, string name, string content, CompressionLevel level, DateTimeOffset stamp)
        => AddBytes(archive, name, Encoding.UTF8.GetBytes(content), level, stamp);

    private static void AddBytes(ZipArchive archive, string name, ReadOnlyMemory<byte> content, CompressionLevel level, DateTimeOffset stamp)
    {
        var entry = archive.CreateEntry(name, level);
        entry.LastWriteTime = stamp;

        using var target = entry.Open();
        target.Write(content.Span);
    }
}
