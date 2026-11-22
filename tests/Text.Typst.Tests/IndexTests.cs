using System;
using M3scalinum.Text.Documents;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

/// <summary>
/// The index marks and the index block, asserted on the markup. The page numbers the block prints
/// are a layout fact: <c>IndexPageTests</c> in the integration suite asserts them against the real
/// compiler.
/// </summary>
public sealed class IndexTests
{
    [Fact]
    public void Writes_an_invisible_mark_carrying_the_term_and_the_page_counter()
    {
        var markup = TypstDocument.Create()
            .Paragraph(c => c.Text("Body"))
            .IndexMark("Cache")
            .ToMarkup();

        Assert.Contains(
            "#context metadata((m3s-index: true, term: \"Cache\", sub: none, page: counter(page).at(here()).first()))",
            markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Writes_a_subterm_and_escapes_it()
    {
        var markup = TypstDocument.Create()
            .IndexMark("Cache", "eviction \"policy\"")
            .ToMarkup();

        Assert.Contains("sub: \"eviction \\\"policy\\\"\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Trims_the_term_and_the_subterm()
    {
        var markup = TypstDocument.Create()
            .IndexMark("  Cache  ", "  size ")
            .ToMarkup();

        Assert.Contains("term: \"Cache\", sub: \"size\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Writes_an_index_that_queries_the_marks_and_sorts_them()
    {
        var markup = TypstDocument.Create()
            .Index()
            .ToMarkup();

        Assert.Contains("query(metadata)", markup, StringComparison.Ordinal);
        Assert.Contains(".sorted(key: index-fold)", markup, StringComparison.Ordinal);
        Assert.Contains(".dedup()", markup, StringComparison.Ordinal);
        Assert.Contains("par(hanging-indent: 1.2em)", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Writes_the_index_title_as_a_heading()
    {
        var markup = TypstDocument.Create()
            .Index(new TypstContent().Text("Index"))
            .ToMarkup();

        Assert.Contains("= Index", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Writes_no_heading_when_the_index_has_no_title()
    {
        var markup = TypstDocument.Create()
            .Index()
            .ToMarkup();

        Assert.DoesNotContain("= Index", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_blank_term()
    {
        Assert.Throws<ArgumentException>(() => TypstDocument.Create().IndexMark(" "));
    }

    [Fact]
    public void Rejects_a_blank_subterm()
    {
        Assert.Throws<ArgumentException>(() => TypstDocument.Create().IndexMark("Cache", "  "));
    }

    [Fact]
    public void The_mark_adds_no_visible_text()
    {
        var markup = TypstDocument.Create()
            .Paragraph(c => c.Text("Body"))
            .IndexMark("Cache")
            .ToMarkup();

        Assert.DoesNotContain("#Cache", markup, StringComparison.Ordinal);
        Assert.Contains("Body", markup, StringComparison.Ordinal);
        Assert.True(
            markup.IndexOf("#context metadata", StringComparison.Ordinal) > markup.IndexOf("Body", StringComparison.Ordinal),
            "the mark stays after the paragraph it follows");
    }
}
