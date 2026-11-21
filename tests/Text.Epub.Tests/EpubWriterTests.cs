using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using M3scalinum.Text.Epub;
using Xunit;

namespace M3scalinum.Text.Epub.Tests;

/// <summary>
/// The container as a reader sees it: every assertion reads the bytes back with
/// <see cref="ZipArchive"/> and parses the XML with <see cref="XDocument"/>, so a package that passes
/// is one another implementation can open, not one this writer happens to agree with itself about.
/// </summary>
public sealed class EpubWriterTests
{
    private static readonly XNamespace Xhtml = "http://www.w3.org/1999/xhtml";
    private static readonly XNamespace Opf = "http://www.idpf.org/2007/opf";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Ops = "http://www.idpf.org/2007/ops";

    private static readonly byte[] Cover =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
    ];

    private static EpubMetadata Metadata(string title = "The Typst Book") => new()
    {
        Identifier = "urn:uuid:5b2c1e0a-4a5c-4c8e-9d0f-6f5b0d2a7c11",
        Title = title,
        Language = "en",
        Author = "Ana Kovács",
        Publisher = "M3scalinum",
        Date = new DateOnly(2026, 2, 3),
        Modified = new DateTimeOffset(2026, 2, 3, 9, 30, 0, TimeSpan.Zero),
    };

    private static EpubBook TwoChapters()
    {
        var book = new EpubBook(Metadata());

        book.AddChapter("First Steps", """
            <h1>First Steps</h1>
            <p>Hello &amp; welcome to the book.<br>This line starts on its own.</p>
            <p><img src="images/cover.png" alt="The cover"/></p>
            """);
        book.AddChapter("Second Thoughts", """
            <h1>Second Thoughts</h1>
            <p id="second">The second chapter cites Typst&nbsp;&mdash; a typesetter.</p>
            """);
        book.AddAsset("images/cover.png", "image/png", Cover);

        return book;
    }

    private static ZipArchive Open(byte[] epub)
    {
        var archive = new ZipArchive(new MemoryStream(epub), ZipArchiveMode.Read);

        Assert.NotNull(archive.GetEntry("mimetype"));
        return archive;
    }

    private static string Text(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name);
        Assert.True(entry is not null, $"the archive has no entry '{name}'; it has {string.Join(", ", archive.Entries.Select(e => e.FullName))}");

        using var reader = new StreamReader(entry!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static XDocument Parse(string xml) =>
        XDocument.Parse(xml, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);

    private static (XDocument Package, string Path) Package(ZipArchive archive)
    {
        var container = Parse(Text(archive, "META-INF/container.xml"));
        var path = container.Descendants().Single(e => e.Name.LocalName == "rootfile").Attribute("full-path")!.Value;

        return (Parse(Text(archive, path)), path);
    }

    private static string Directory(string packagePath)
    {
        var slash = packagePath.LastIndexOf('/');
        return slash < 0 ? string.Empty : packagePath[..(slash + 1)];
    }

    [Fact]
    public void A_two_chapter_book_with_an_image_reopens_with_the_mimetype_first_and_stored()
    {
        var epub = EpubWriter.Write(TwoChapters());

        using var archive = Open(epub);

        Assert.Equal("application/epub+zip", Text(archive, "mimetype"));
        Assert.Equal("mimetype", archive.Entries[0].FullName);
        Assert.Equal("META-INF/container.xml", archive.Entries[1].FullName);
        Assert.Equal(archive.Entries[0].Length, archive.Entries[0].CompressedLength);

        // At the byte level, which is what a reading system sniffs: the local header, the name and
        // then the media type at offset 38 — the OCF layout, not just "uncompressed somewhere".
        Assert.Equal(0x04034B50u, BitConverter.ToUInt32(epub, 0));
        Assert.Equal(0, BitConverter.ToUInt16(epub, 8));
        Assert.Equal(8, BitConverter.ToUInt16(epub, 26));
        Assert.Equal(0, BitConverter.ToUInt16(epub, 28));
        Assert.Equal("mimetype", Encoding.ASCII.GetString(epub, 30, 8));
        Assert.Equal("application/epub+zip", Encoding.ASCII.GetString(epub, 38, 20));

        Assert.Contains(archive.Entries, e => e.FullName == "OEBPS/content.opf");
        Assert.Contains(archive.Entries, e => e.FullName == "OEBPS/nav.xhtml");
        Assert.Contains(archive.Entries, e => e.FullName == "OEBPS/book.css");
        Assert.Contains(archive.Entries, e => e.FullName == "OEBPS/images/cover.png");
        Assert.Equal(Cover, Read(archive, "OEBPS/images/cover.png"));

        static byte[] Read(ZipArchive archive, string name)
        {
            using var stream = archive.GetEntry(name)!.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
    }

    [Fact]
    public void The_container_points_at_a_package_that_declares_the_metadata()
    {
        var epub = EpubWriter.Write(TwoChapters());

        using var archive = Open(epub);
        var (package, path) = Package(archive);

        Assert.Equal("OEBPS/content.opf", path);
        Assert.Equal("package", package.Root!.Name.LocalName);
        Assert.Equal(Opf, package.Root.Name.Namespace);
        Assert.Equal("3.0", package.Root.Attribute("version")!.Value);
        Assert.Equal("bookid", package.Root.Attribute("unique-identifier")!.Value);

        var metadata = package.Root.Element(Opf + "metadata")!;
        var identifier = metadata.Element(Dc + "identifier")!;

        Assert.Equal("bookid", identifier.Attribute("id")!.Value);
        Assert.Equal("urn:uuid:5b2c1e0a-4a5c-4c8e-9d0f-6f5b0d2a7c11", identifier.Value);
        Assert.Equal("The Typst Book", metadata.Element(Dc + "title")!.Value);
        Assert.Equal("en", metadata.Element(Dc + "language")!.Value);
        Assert.Equal("Ana Kovács", metadata.Element(Dc + "creator")!.Value);
        Assert.Equal("M3scalinum", metadata.Element(Dc + "publisher")!.Value);
        Assert.Equal("2026-02-03", metadata.Element(Dc + "date")!.Value);

        var modified = metadata.Elements(Opf + "meta").Single(m => m.Attribute("property")!.Value == "dcterms:modified");
        Assert.Equal("2026-02-03T09:30:00Z", modified.Value);
    }

    [Fact]
    public void Every_manifest_href_resolves_and_every_spine_item_points_at_a_manifest_id()
    {
        var epub = EpubWriter.Write(TwoChapters());

        using var archive = Open(epub);
        var (package, path) = Package(archive);
        var directory = Directory(path);

        var manifest = package.Root!.Element(Opf + "manifest")!.Elements(Opf + "item").ToList();
        Assert.Equal(manifest.Count, manifest.Select(i => i.Attribute("id")!.Value).Distinct().Count());

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in manifest)
        {
            var id = item.Attribute("id")!.Value;
            var href = item.Attribute("href")!.Value;
            var mediaType = item.Attribute("media-type")!.Value;

            Assert.True(ids.Add(id), $"the manifest repeats the id '{id}'");
            Assert.DoesNotContain("..", href, StringComparison.Ordinal);
            Assert.NotNull(archive.GetEntry(directory + href));
            Assert.False(string.IsNullOrWhiteSpace(mediaType));
        }

        var nav = manifest.Single(i => i.Attribute("properties")?.Value == "nav");
        Assert.Equal("nav.xhtml", nav.Attribute("href")!.Value);
        Assert.Equal("application/xhtml+xml", nav.Attribute("media-type")!.Value);

        var spine = package.Root.Element(Opf + "spine")!.Elements(Opf + "itemref").ToList();
        Assert.Equal(2, spine.Count);

        foreach (var itemref in spine)
        {
            Assert.Contains(itemref.Attribute("idref")!.Value, ids);
        }

        var chapters = spine
            .Select(r => manifest.Single(i => i.Attribute("id")!.Value == r.Attribute("idref")!.Value))
            .ToList();

        Assert.Equal(new[] { "ch001.xhtml", "ch002.xhtml" }, chapters.Select(i => i.Attribute("href")!.Value));
        Assert.Equal("OEBPS/ch001.xhtml", directory + chapters[0].Attribute("href")!.Value);
    }

    [Fact]
    public void Every_content_document_parses_as_XHTML_in_the_EPUB_namespace()
    {
        var epub = EpubWriter.Write(TwoChapters());

        using var archive = Open(epub);
        var (package, _) = Package(archive);

        var documents = package.Root!.Element(Opf + "manifest")!.Elements(Opf + "item")
            .Where(i => i.Attribute("media-type")!.Value == "application/xhtml+xml")
            .Select(i => i.Attribute("href")!.Value)
            .ToList();

        Assert.Equal(3, documents.Count);

        foreach (var document in documents)
        {
            var parsed = Parse(Text(archive, "OEBPS/" + document));

            Assert.Equal(Xhtml + "html", parsed.Root!.Name);
            Assert.Equal("en", parsed.Root.Attribute(XNamespace.Xml + "lang")!.Value);
            Assert.NotNull(parsed.Root.Element(Xhtml + "head"));
            Assert.NotNull(parsed.Root.Element(Xhtml + "body"));

            foreach (var element in parsed.Descendants())
            {
                Assert.Equal(Xhtml, element.Name.Namespace);
            }

            var ids = parsed.Descendants().Select(e => (string?)e.Attribute("id")).Where(v => v is not null).ToList();
            Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        }

        var first = Parse(Text(archive, "OEBPS/ch001.xhtml"));
        Assert.Contains("\"?><!DOCTYPE html><html xmlns:epub=", Text(archive, "OEBPS/ch001.xhtml"), StringComparison.Ordinal);
        Assert.Equal("First Steps", first.Root!.Element(Xhtml + "head")!.Element(Xhtml + "title")!.Value);
        Assert.Contains(first.Descendants(Xhtml + "img"), i => i.Attribute("src")!.Value == "images/cover.png");
        Assert.Contains(first.Descendants(Xhtml + "link"), l => l.Attribute("href")!.Value == "book.css");
        Assert.StartsWith("Hello & welcome to the book.", first.Descendants(Xhtml + "p").First().Value, StringComparison.Ordinal);
        Assert.Contains("This line starts on its own.", first.Root!.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void The_navigation_document_is_the_manifest_nav_and_lists_the_chapters_in_order()
    {
        var epub = EpubWriter.Write(TwoChapters());

        using var archive = Open(epub);
        var nav = Parse(Text(archive, "OEBPS/nav.xhtml"));
        var toc = nav.Descendants(Xhtml + "nav").Single(n => n.Attribute(Ops + "type")?.Value == "toc");

        Assert.Equal("toc", toc.Attribute("id")!.Value);

        var links = toc.Descendants(Xhtml + "a").ToList();
        Assert.Equal(new[] { "First Steps", "Second Thoughts" }, links.Select(a => a.Value));
        Assert.Equal(new[] { "ch001.xhtml", "ch002.xhtml" }, links.Select(a => a.Attribute("href")!.Value));
    }

    [Fact]
    public void A_chapter_referencing_a_path_outside_the_container_is_refused()
    {
        var book = new EpubBook(Metadata());
        book.AddChapter("Escape", """<p><img src="../escape.png" alt="escape"/></p>""");

        var error = Assert.Throws<EpubValidationException>(() => EpubWriter.Write(book));

        Assert.Contains("..", error.Message, StringComparison.Ordinal);
        Assert.Contains("escape.png", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_chapter_referencing_a_file_no_asset_backs_is_refused()
    {
        var book = new EpubBook(Metadata());
        book.AddChapter("Missing", """<p><img src="images/missing.png" alt="missing"/></p>""");

        var error = Assert.Throws<EpubValidationException>(() => EpubWriter.Write(book));

        Assert.Contains("images/missing.png", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_asset_named_outside_the_container_is_refused_when_it_is_added()
    {
        var book = new EpubBook(Metadata());

        Assert.Throws<EpubValidationException>(() => book.AddAsset("../escape.png", Cover));
        Assert.Throws<EpubValidationException>(() => book.AddAsset("/absolute.png", Cover));
        Assert.Throws<EpubValidationException>(() => book.AddAsset("images\\cover.png", Cover));
        Assert.Throws<EpubValidationException>(() => book.AddAsset("nav.xhtml", Cover));
    }

    [Fact]
    public void Ids_are_unique_across_the_whole_book()
    {
        var book = new EpubBook(Metadata());
        book.AddChapter("One", """<p id="shared">first</p>""");
        book.AddChapter("Two", """<p id="shared">second</p><p id="own">third</p>""");

        var epub = EpubWriter.Write(book);

        using var archive = Open(epub);

        var ids = new List<string>();
        foreach (var name in new[] { "OEBPS/ch001.xhtml", "OEBPS/ch002.xhtml" })
        {
            ids.AddRange(Parse(Text(archive, name)).Descendants().Select(e => (string?)e.Attribute("id")).OfType<string>());
        }

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("own", ids);
    }

    [Fact]
    public void FromHtml_splits_a_document_at_its_headings()
    {
        const string Html = """
            <!DOCTYPE html>
            <html><head><title>ignored</title></head><body>
            <p>An abstract before the first heading.</p>
            <h1>Introduction</h1>
            <p>Where it starts.</p>
            <h2>A section</h2>
            <p>Inside the introduction.</p>
            <h1 id="middle">Method</h1>
            <p>How it works.</p>
            </body></html>
            """;

        var book = EpubBook.FromHtml(Html, Metadata("A Study"));

        Assert.Equal(new[] { "A Study", "Introduction", "Method" }, book.Chapters.Select(c => c.Title));

        var epub = EpubWriter.Write(book);

        using var archive = Open(epub);
        var nav = Parse(Text(archive, "OEBPS/nav.xhtml"));
        Assert.Equal(3, nav.Descendants(Xhtml + "a").Count());

        var second = Parse(Text(archive, "OEBPS/ch002.xhtml"));
        Assert.Equal("Introduction", second.Descendants(Xhtml + "h1").Single().Value);
        Assert.Contains(second.Descendants(Xhtml + "h2"), h => h.Value == "A section");
        Assert.DoesNotContain("Method", Text(archive, "OEBPS/ch002.xhtml"), StringComparison.Ordinal);

        var third = Parse(Text(archive, "OEBPS/ch003.xhtml"));
        Assert.Equal("Method", third.Descendants(Xhtml + "h1").Single().Value);
        Assert.Equal("middle", third.Descendants(Xhtml + "h1").Single().Attribute("id")!.Value);
        Assert.DoesNotContain("A Study", third.Root!.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void FromHtml_repairs_what_html_tolerates_and_xml_does_not()
    {
        var book = EpubBook.FromHtml("""
            <html><body>
            <p>a&nbsp;b<br>c&mdash;d &copy; 2026
            <ul><li>one<li>two</ul>
            </body></html>
            """, Metadata());

        var epub = EpubWriter.Write(book);

        using var archive = Open(epub);
        var document = Parse(Text(archive, "OEBPS/ch001.xhtml"));

        Assert.Equal("a\u00A0b", document.Descendants(Xhtml + "p").First().Nodes().OfType<XText>().First().Value.TrimEnd());
        Assert.Single(document.Descendants(Xhtml + "br"));
        Assert.Equal(2, document.Descendants(Xhtml + "li").Count());
        Assert.Contains("\u00A9 2026", document.Root!.Value, StringComparison.Ordinal);
        Assert.Contains("\u2014", document.Root.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Writing_to_a_stream_produces_the_same_bytes_as_the_array()
    {
        var book = TwoChapters();
        var first = EpubWriter.Write(book);

        using var buffer = new MemoryStream();
        EpubWriter.Write(book, buffer);

        Assert.Equal(first, buffer.ToArray());
    }

    [Fact]
    public void A_book_without_an_identifier_is_refused()
    {
        var book = new EpubBook(new EpubMetadata { Identifier = "  ", Title = "No identifier" });
        book.AddChapter("One", "<p>text</p>");

        Assert.Throws<EpubValidationException>(() => EpubWriter.Write(book));
    }

    [Fact]
    public void A_book_without_a_chapter_is_refused()
    {
        Assert.Throws<EpubValidationException>(() => EpubWriter.Write(new EpubBook(Metadata())));
    }

    [Fact]
    public void A_language_that_is_not_a_tag_is_refused()
    {
        var book = new EpubBook(new EpubMetadata { Identifier = "id", Title = "Title", Language = "not a tag" });
        book.AddChapter("One", "<p>text</p>");

        Assert.Throws<EpubValidationException>(() => EpubWriter.Write(book));
    }

    [Fact]
    public void The_same_book_written_twice_gives_the_same_bytes()
    {
        var first = EpubWriter.Write(TwoChapters());
        var second = EpubWriter.Write(TwoChapters());

        Assert.Equal(first, second);

        using var archive = new ZipArchive(new MemoryStream(first), ZipArchiveMode.Read);
        var stamp = archive.Entries[0].LastWriteTime;

        // ZIP stores a DOS timestamp with no zone, so what a reader gets back is the same wall clock
        // in its own offset; the date is what the writer promised.
        Assert.Equal(new DateTime(2026, 2, 3), stamp.Date);
        Assert.All(archive.Entries, e => Assert.Equal(stamp, e.LastWriteTime));
    }

    [Fact]
    public void Chapters_over_the_numbered_limit_are_refused()
    {
        var book = new EpubBook(Metadata());
        for (var i = 0; i < 1000; i++)
        {
            book.AddChapter("Chapter " + i.ToString(CultureInfo.InvariantCulture), "<p>text</p>");
        }

        Assert.Throws<EpubValidationException>(() => EpubWriter.Write(book));
    }
}
