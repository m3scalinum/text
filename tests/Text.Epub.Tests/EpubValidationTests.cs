using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using M3scalinum.Text.Epub;
using M3scalinum.Text.Epub.Internal;
using Xunit;

namespace M3scalinum.Text.Epub.Tests;

/// <summary>
/// What a book must not be: the metadata, the paths, the assets, the references and the chapters
/// that would make a container a reader refuses.
/// </summary>
/// <remarks>
/// Each case names the rule the failure breaks, because the message is what a host shows the person
/// who built the book and the reason has to survive into it.
/// </remarks>
public sealed class EpubValidationTests
{
    private static readonly byte[] Bytes = [0x01, 0x02, 0x03];

    private static EpubMetadata Metadata(string title = "Title") => new()
    {
        Identifier = "urn:uuid:00000000-0000-0000-0000-000000000000",
        Title = title,
        Language = "en",
    };

    [Fact]
    public void A_book_without_a_title_is_refused()
    {
        var book = new EpubBook(new EpubMetadata { Identifier = "id", Title = "  ", Language = "en" });
        book.AddChapter("One", "<p>text</p>");

        var error = Assert.Throws<EpubValidationException>(() => EpubWriter.Write(book));

        Assert.Contains("title", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_book_without_a_language_is_refused()
    {
        var book = new EpubBook(new EpubMetadata { Identifier = "id", Title = "Title", Language = "  " });
        book.AddChapter("One", "<p>text</p>");

        var error = Assert.Throws<EpubValidationException>(() => EpubWriter.Write(book));

        Assert.Contains("language", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_language_with_a_separator_that_is_not_a_dash_is_refused()
    {
        var book = new EpubBook(new EpubMetadata { Identifier = "id", Title = "Title", Language = "en_GB" });
        book.AddChapter("One", "<p>text</p>");

        var error = Assert.Throws<EpubValidationException>(() => EpubWriter.Write(book));

        Assert.Contains("en_GB", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_regional_language_tag_is_accepted()
    {
        var book = new EpubBook(new EpubMetadata { Identifier = "id", Title = "Title", Language = "en-GB" });
        book.AddChapter("One", "<p>text</p>");

        var epub = EpubWriter.Write(book);

        using var archive = new ZipArchive(new MemoryStream(epub), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("OEBPS/content.opf")!.Open(), Encoding.UTF8);

        Assert.Contains("en-GB", reader.ReadToEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_book_with_a_date_but_no_modification_time_stamps_the_date()
    {
        var book = new EpubBook(new EpubMetadata
        {
            Identifier = "id",
            Title = "Title",
            Language = "en",
            Date = new DateOnly(2026, 5, 4),
        });
        book.AddChapter("One", "<p>text</p>");

        using var archive = new ZipArchive(new MemoryStream(EpubWriter.Write(book)), ZipArchiveMode.Read);

        Assert.All(archive.Entries, e => Assert.Equal(new DateTime(2026, 5, 4), e.LastWriteTime.Date));
    }

    [Fact]
    public void A_book_with_neither_a_date_nor_a_modification_time_still_writes()
    {
        var book = new EpubBook(Metadata());
        book.AddChapter("One", "<p>text</p>");

        using var archive = new ZipArchive(new MemoryStream(EpubWriter.Write(book)), ZipArchiveMode.Read);

        Assert.NotEmpty(archive.Entries);
    }

    [Fact]
    public void A_chapter_without_a_title_is_refused_when_it_is_created()
    {
        var error = Assert.Throws<ArgumentException>(() => new EpubChapter("   ", "<p>text</p>"));

        Assert.Equal("title", error.ParamName);
    }

    [Fact]
    public void An_asset_without_a_media_type_is_refused_when_it_is_created()
    {
        var error = Assert.Throws<ArgumentException>(() => new EpubAsset("images/a.png", "  ", Bytes));

        Assert.Equal("mediaType", error.ParamName);
    }

    [Theory]
    [InlineData("images/cover.png")]
    [InlineData("fonts/body.woff2")]
    [InlineData("cover.jpg")]
    public void An_asset_takes_its_media_type_from_its_extension(string name)
    {
        var asset = new EpubAsset(name, Bytes);

        Assert.False(string.IsNullOrWhiteSpace(asset.MediaType));
        Assert.Equal(name, asset.Name);
    }

    [Fact]
    public void An_asset_without_an_extension_needs_an_explicit_media_type()
    {
        var error = Assert.Throws<EpubValidationException>(() => new EpubAsset("LICENCE", Bytes));

        Assert.Contains("extension", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_asset_with_an_unknown_extension_needs_an_explicit_media_type()
    {
        var error = Assert.Throws<EpubValidationException>(() => new EpubAsset("images/art.psd", Bytes));

        Assert.Contains("art.psd", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("/cover.png", "absolute")]
    [InlineData("images\\cover.png", "backslash")]
    [InlineData("images/cover.png?x=1", "query")]
    [InlineData("#fragment", "fragment")]
    [InlineData("images:cover.png", "colon")]
    [InlineData("images//cover.png", "segment")]
    [InlineData("images/./cover.png", "segment")]
    [InlineData("images/../cover.png", "climbs")]
    [InlineData("images/cover icon.png", "contains")]
    public void A_path_that_would_leave_or_confuse_the_container_is_refused(string path, string reason)
    {
        var error = Assert.Throws<EpubValidationException>(() => EpubPath.Validate(path, "asset name"));

        Assert.Contains(reason, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_plain_relative_path_is_returned_unchanged()
    {
        Assert.Equal("images/cover-1_2.png", EpubPath.Validate("images/cover-1_2.png", "asset name"));
    }

    [Theory]
    [InlineData("content.opf")]
    [InlineData("nav.xhtml")]
    [InlineData("book.css")]
    [InlineData("mimetype")]
    [InlineData("ch001.xhtml")]
    [InlineData("CH012.XHTML")]
    public void A_name_the_writer_generates_is_reserved(string name)
    {
        var error = Assert.Throws<EpubValidationException>(() => EpubPath.ValidateAssetName(name));

        Assert.Contains("reserved", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_asset_name_twice_is_refused()
    {
        var book = new EpubBook(Metadata());
        book.AddAsset("images/cover.png", Bytes);

        var error = Assert.Throws<EpubValidationException>(
            () => book.AddAsset("images/cover.png", Bytes));

        Assert.Contains("already taken", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://example.test/logo.png")]
    [InlineData("http://example.test/logo.png")]
    [InlineData("//example.test/logo.png")]
    [InlineData("mailto:someone@example.test")]
    public void An_absolute_url_is_external(string reference)
    {
        Assert.True(EpubPath.IsExternal(reference));
    }

    [Theory]
    [InlineData("images/cover.png")]
    [InlineData("ch002.xhtml")]
    [InlineData("1chapter.xhtml")]
    public void A_relative_reference_is_not_external(string reference)
    {
        Assert.False(EpubPath.IsExternal(reference));
    }

    [Fact]
    public void A_link_to_a_chapter_that_does_not_exist_is_refused()
    {
        var book = new EpubBook(Metadata());
        book.AddChapter("One", """<p><a href="ch009.xhtml">later</a></p>""");

        var error = Assert.Throws<EpubValidationException>(() => EpubWriter.Write(book));

        Assert.Contains("ch009.xhtml", error.Message, StringComparison.Ordinal);
        Assert.Contains("neither a chapter nor an asset", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_link_to_another_chapter_resolves()
    {
        var book = new EpubBook(Metadata());
        book.AddChapter("One", """<p><a href="ch002.xhtml">next</a></p>""");
        book.AddChapter("Two", """<p><a href="ch001.xhtml#top">back</a></p>""");

        var epub = EpubWriter.Write(book);

        using var archive = new ZipArchive(new MemoryStream(epub), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("OEBPS/ch001.xhtml")!.Open(), Encoding.UTF8);

        Assert.Contains("href=\"ch002.xhtml\"", reader.ReadToEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_external_link_is_not_checked_against_the_manifest()
    {
        var book = new EpubBook(Metadata());
        book.AddChapter("One", """<p><a href="https://example.test/">out</a></p>""");

        var epub = EpubWriter.Write(book);

        using var archive = new ZipArchive(new MemoryStream(epub), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("OEBPS/ch001.xhtml")!.Open(), Encoding.UTF8);

        Assert.Contains("https://example.test/", reader.ReadToEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_fragment_only_link_needs_no_manifest_entry()
    {
        var book = new EpubBook(Metadata());
        book.AddChapter("One", """<p id="top">first</p><p><a href="#top">up</a></p>""");

        var epub = EpubWriter.Write(book);

        using var archive = new ZipArchive(new MemoryStream(epub), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("OEBPS/ch001.xhtml")!.Open(), Encoding.UTF8);

        Assert.Contains("href=\"#top\"", reader.ReadToEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_reference_that_climbs_out_of_the_container_is_refused()
    {
        var book = new EpubBook(Metadata());
        book.AddChapter("One", """<p><a href="fixtures/../../escape.xhtml">out</a></p>""");

        var error = Assert.Throws<EpubValidationException>(() => EpubWriter.Write(book));

        Assert.Contains("climbs", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_chapter_of_whitespace_only_is_written_as_an_empty_document_rather_than_failing()
    {
        // Nothing to show, but nothing invalid either: a reader opens a blank page rather than the
        // writer refusing a book over a chapter that is merely empty.
        var book = new EpubBook(Metadata());
        book.AddChapter("Empty", "   ");

        var epub = EpubWriter.Write(book);

        using var archive = new ZipArchive(new MemoryStream(epub), ZipArchiveMode.Read);
        var document = XDocument.Parse(
            new StreamReader(archive.GetEntry("OEBPS/ch001.xhtml")!.Open(), Encoding.UTF8).ReadToEnd());

        XNamespace xhtml = "http://www.w3.org/1999/xhtml";
        Assert.Empty(document.Descendants(xhtml + "body").Single().Elements());
    }

    [Fact]
    public void A_null_book_and_a_null_stream_are_argument_errors()
    {
        Assert.Throws<ArgumentNullException>(() => EpubWriter.Write((EpubBook)null!));
        Assert.Throws<ArgumentNullException>(() => EpubWriter.Write(new EpubBook(Metadata()), null!));
    }
}
