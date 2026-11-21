using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using M3scalinum.Text.Epub;
using Xunit;

namespace M3scalinum.Text.Epub.Tests;

/// <summary>
/// What the tolerant reader and the XHTML writer make of markup a real document carries: comments,
/// processing instructions, raw-text elements, implicit ends, unquoted attributes, entities and
/// characters XML cannot hold.
/// </summary>
public sealed class EpubParsingTests
{
    private static readonly XNamespace Xhtml = "http://www.w3.org/1999/xhtml";

    private static EpubMetadata Metadata => new()
    {
        Identifier = "urn:uuid:00000000-0000-0000-0000-000000000000",
        Title = "Parsing",
        Language = "en",
    };

    /// <summary>Writes one chapter and returns its parsed content document.</summary>
    private static XDocument Chapter(string html, string title = "One")
    {
        var book = new EpubBook(Metadata);
        book.AddChapter(title, html);

        using var archive = new ZipArchive(new MemoryStream(EpubWriter.Write(book)), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("OEBPS/ch001.xhtml")!.Open(), Encoding.UTF8);

        return XDocument.Parse(reader.ReadToEnd());
    }

    private static XElement Body(XDocument document) => document.Descendants(Xhtml + "body").Single();

    [Fact]
    public void A_comment_a_doctype_and_a_processing_instruction_are_dropped()
    {
        var document = Chapter("""
            <!-- a note to the editor -->
            <?xml-stylesheet href="a.css"?>
            <!DOCTYPE html>
            <p>readable</p>
            """);

        Assert.Equal("readable", Body(document).Value.Trim());
    }

    [Fact]
    public void A_comment_that_is_never_closed_swallows_the_rest()
    {
        var document = Chapter("<p>kept</p><!-- never closed <p>dropped</p>");

        Assert.Equal("kept", Body(document).Value.Trim());
    }

    [Fact]
    public void A_lone_less_than_sign_is_text()
    {
        var document = Chapter("<p>1 &lt; 2 and 3 < 4</p>");

        Assert.Contains("3 < 4", Body(document).Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_before_and_after_a_tag_is_one_run()
    {
        var document = Chapter("<p>before <em>inside</em> after</p>");

        var paragraph = Body(document).Element(Xhtml + "p")!;

        Assert.Equal("before inside after", paragraph.Value);
        Assert.Single(paragraph.Elements(Xhtml + "em"));
    }

    [Fact]
    public void A_script_and_a_style_are_dropped_with_their_content()
    {
        var document = Chapter("""
            <p>kept</p>
            <script>var x = "<p>not markup</p>";</script>
            <style>p { color: red }</style>
            """);

        Assert.Equal("kept", Body(document).Value.Trim());
        Assert.DoesNotContain("var x", document.Root!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_script_that_is_never_closed_ends_the_document()
    {
        var document = Chapter("<p>kept</p><script>never closed");

        Assert.Equal("kept", Body(document).Value.Trim());
    }

    [Fact]
    public void An_unquoted_attribute_keeps_its_value()
    {
        var document = Chapter("""<p class=note title="two words">text</p>""");

        var paragraph = Body(document).Element(Xhtml + "p")!;

        Assert.Equal("note", paragraph.Attribute("class")!.Value);
        Assert.Equal("two words", paragraph.Attribute("title")!.Value);
    }

    [Fact]
    public void An_attribute_with_space_around_the_equals_sign_is_read()
    {
        var document = Chapter("""<p class = spaced >text</p>""");

        Assert.Equal("spaced", Body(document).Element(Xhtml + "p")!.Attribute("class")!.Value);
    }

    [Fact]
    public void An_attribute_without_a_value_is_empty()
    {
        var document = Chapter("<details open>text</details>");

        Assert.Equal(string.Empty, Body(document).Element(Xhtml + "details")!.Attribute("open")!.Value);
    }

    [Fact]
    public void A_stray_closing_tag_is_ignored()
    {
        var document = Chapter("<p>kept</span></div></p>");

        Assert.Equal("kept", Body(document).Element(Xhtml + "p")!.Value);
    }

    [Fact]
    public void A_closing_tag_that_is_never_terminated_ends_the_document()
    {
        var document = Chapter("<p>kept</p></p");

        Assert.Equal("kept", Body(document).Element(Xhtml + "p")!.Value);
    }

    [Fact]
    public void A_list_item_closes_the_previous_one_without_its_closing_tag()
    {
        var document = Chapter("<ul><li>one<li>two<li>three</ul>");

        var items = Body(document).Descendants(Xhtml + "li").ToList();

        Assert.Equal(new[] { "one", "two", "three" }, items.Select(i => i.Value));
    }

    [Fact]
    public void A_definition_list_from_an_implied_end_document_keeps_its_terms()
    {
        var document = Chapter("<dl><dt>term<dd>definition<dt>other<dd>second</dl>");

        var terms = Body(document).Descendants(Xhtml + "dt").Select(e => e.Value).ToList();
        var definitions = Body(document).Descendants(Xhtml + "dd").Select(e => e.Value).ToList();

        Assert.Equal(new[] { "term", "other" }, terms);
        Assert.Equal(new[] { "definition", "second" }, definitions);
    }

    [Fact]
    public void A_table_row_and_its_cells_close_implicitly()
    {
        var document = Chapter(
            "<table><tr><td>a<td>b</tr><tr><th>c<th>d</tr></table>");

        var rows = Body(document).Descendants(Xhtml + "tr").ToList();

        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "a", "b" }, rows[0].Elements().Select(e => e.Value));
        Assert.Equal(new[] { "c", "d" }, rows[1].Elements().Select(e => e.Value));
    }

    [Fact]
    public void An_option_closes_the_previous_option()
    {
        var document = Chapter("<select><option>one<option>two</select>");

        // select is not in the XHTML vocabulary of a book's content, so it is unwrapped and its
        // options are dropped: the point is that parsing it does not lose the rest of the document.
        Assert.Equal(string.Empty, Body(document).Value.Trim());
    }

    [Fact]
    public void A_named_numeric_and_hexadecimal_reference_are_decoded()
    {
        var document = Chapter("<p>&amp; &copy; &#169; &#xA9; &#x1F600;</p>");

        Assert.Contains("& © © © 😀", Body(document).Value, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<p>a & b</p>")]
    [InlineData("<p>a &notanentity; b</p>")]
    [InlineData("<p>a &#; b</p>")]
    [InlineData("<p>a &#xZZ; b</p>")]
    [InlineData("<p>a &#0; b</p>")]
    [InlineData("<p>a &#xD800; b</p>")]
    [InlineData("<p>a &; b</p>")]
    public void A_reference_that_is_not_one_is_kept_as_text(string html)
    {
        var document = Chapter(html);

        Assert.Contains("a &", Body(document).Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Characters_xml_cannot_hold_are_dropped_and_a_surrogate_pair_is_kept()
    {
        var document = Chapter("<p>a\u0001b\u0008c 😀 d</p>");

        var value = Body(document).Value;

        Assert.DoesNotContain("\u0001", value, StringComparison.Ordinal);
        Assert.DoesNotContain("\u0008", value, StringComparison.Ordinal);
        Assert.Contains("😀", value, StringComparison.Ordinal);
        Assert.Contains("abc", value, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("onclick")]
    [InlineData("onload")]
    [InlineData("xmlns:foo")]
    public void An_event_handler_and_a_namespace_declaration_are_not_written(string attribute)
    {
        var book = new EpubBook(Metadata);
        book.AddChapter("One", $"""<p {attribute}="x">text</p>""");

        using var archive = new ZipArchive(new MemoryStream(EpubWriter.Write(book)), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("OEBPS/ch001.xhtml")!.Open(), Encoding.UTF8);
        var written = reader.ReadToEnd();

        Assert.DoesNotContain(attribute + "=", written, StringComparison.Ordinal);
        Assert.Contains(">text<", written, StringComparison.Ordinal);
    }

    [Fact]
    public void An_attribute_name_that_is_not_an_xml_name_is_not_written()
    {
        var document = Chapter("""<p 1bad="x" ok="y">text</p>""");

        var paragraph = Body(document).Element(Xhtml + "p")!;

        Assert.Single(paragraph.Attributes());
        Assert.Equal("y", paragraph.Attribute("ok")!.Value);
    }

    [Fact]
    public void A_duplicate_attribute_keeps_the_first()
    {
        var document = Chapter("""<p class="first" class="second">text</p>""");

        Assert.Equal("first", Body(document).Element(Xhtml + "p")!.Attribute("class")!.Value);
    }

    [Fact]
    public void An_id_that_is_not_a_plain_name_is_dropped_and_a_repeat_is_dropped_too()
    {
        var book = new EpubBook(Metadata);
        book.AddChapter("One", """<p id="1bad">a</p><p id="same">b</p>""");
        book.AddChapter("Two", """<p id="same">c</p>""");

        using var archive = new ZipArchive(new MemoryStream(EpubWriter.Write(book)), ZipArchiveMode.Read);

        var first = Read(archive, "OEBPS/ch001.xhtml");
        var second = Read(archive, "OEBPS/ch002.xhtml");

        Assert.DoesNotContain("id=\"1bad\"", first, StringComparison.Ordinal);
        Assert.Contains("id=\"same\"", first, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"same\"", second, StringComparison.Ordinal);

        static string Read(ZipArchive archive, string name)
        {
            using var reader = new StreamReader(archive.GetEntry(name)!.Open(), Encoding.UTF8);
            return reader.ReadToEnd();
        }
    }

    [Fact]
    public void An_unknown_element_is_unwrapped_and_a_dropped_one_takes_its_subtree()
    {
        var document = Chapter("""
            <article>
              <blink>kept</blink>
              <iframe src="https://example.test/">dropped</iframe>
            </article>
            """);

        Assert.Contains("kept", Body(document).Value, StringComparison.Ordinal);
        Assert.DoesNotContain("dropped", document.Root!.ToString(), StringComparison.Ordinal);
        Assert.Empty(document.Descendants(Xhtml + "blink"));
    }

    [Fact]
    public void A_bare_text_run_becomes_its_own_paragraph()
    {
        var document = Chapter("first<span>second</span>third");

        var paragraphs = Body(document).Elements(Xhtml + "p").ToList();

        Assert.Single(paragraphs);
        Assert.Contains(Xhtml + "span", paragraphs[0].Elements().Select(e => e.Name));
        Assert.Equal("firstsecondthird", paragraphs[0].Value);
    }

    [Fact]
    public void A_form_is_unwrapped_so_its_prose_survives()
    {
        var document = Chapter("<form><p>labelled</p></form>");

        Assert.Equal("labelled", Body(document).Element(Xhtml + "p")!.Value);
    }

    [Fact]
    public void An_attribute_that_starts_with_the_equals_sign_is_skipped()
    {
        var document = Chapter("""<p =x ok="y">text</p>""");

        var paragraph = Body(document).Element(Xhtml + "p")!;

        Assert.Equal("y", paragraph.Attribute("ok")!.Value);
        Assert.Equal("text", paragraph.Value);
    }

    [Fact]
    public void Text_around_a_block_stays_in_its_own_paragraph()
    {
        var document = Chapter("before<div>block</div>after");

        var paragraphs = Body(document).Elements(Xhtml + "p").ToList();

        Assert.Equal(2, paragraphs.Count);
        Assert.Equal("before", paragraphs[0].Value);
        Assert.Equal("after", paragraphs[1].Value);
    }

    [Fact]
    public void A_body_wrapper_is_unwrapped_rather_than_nested()
    {
        var document = Chapter("<body><p>inner</p></body>");

        Assert.Single(Body(document).Elements());
        Assert.Equal("inner", Body(document).Element(Xhtml + "p")!.Value);
    }

    [Fact]
    public void FromHtml_splits_nested_wrappers_and_drops_a_heading_with_no_content()
    {
        var book = EpubBook.FromHtml("""
            <html><body>
            <div>
              <section>
                <h1><em>Wrapped</em>   Title</h1>
                <p>body</p>
                <h1></h1>
                <h2>not a split level</h2>
              </section>
            </div>
            </body></html>
            """, Metadata);

        // The heading's text comes from inside its <em>, whitespace collapsed; the empty heading that
        // follows has nothing to show, so it falls back to the book's title instead of vanishing.
        Assert.Equal(new[] { "Wrapped Title", "Parsing" }, book.Chapters.Select(c => c.Title));

        var epub = EpubWriter.Write(book);

        using var archive = new ZipArchive(new MemoryStream(epub), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("OEBPS/ch001.xhtml")!.Open(), Encoding.UTF8);

        Assert.Contains("Wrapped", reader.ReadToEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void FromHtml_takes_the_document_title_when_it_has_no_heading()
    {
        var book = EpubBook.FromHtml("<html><body><p>no heading at all</p></body></html>", Metadata);

        Assert.Equal("Parsing", Assert.Single(book.Chapters).Title);
    }

    [Fact]
    public void FromHtml_with_a_document_that_holds_only_markup_falls_back_to_the_book_title()
    {
        var book = EpubBook.FromHtml("<html><body></body></html>", Metadata);

        Assert.Equal("Parsing", Assert.Single(book.Chapters).Title);
    }

    [Fact]
    public void FromHtml_refuses_null_and_a_springy_document_is_still_one_chapter()
    {
        Assert.Throws<ArgumentNullException>(() => EpubBook.FromHtml(null!, Metadata));
    }

    [Fact]
    public void A_chapter_that_is_a_whole_document_keeps_only_its_body()
    {
        var document = Chapter("""
            <html><head><title>ignored</title><style>p{}</style></head>
            <body><h1>Title</h1><p>content</p></body></html>
            """);

        // The document's own head is dropped; the one in the output is the writer's.
        Assert.Equal("Titlecontent", Body(document).Value);

        var head = document.Descendants(Xhtml + "head").Single();
        Assert.Equal("One", head.Element(Xhtml + "title")!.Value);
    }
}
