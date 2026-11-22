using System;
using M3scalinum.Text.Documents;
using M3scalinum.Text.Typst.Compilation;
using Xunit;

namespace M3scalinum.Text.Typst.Integration.Tests;

/// <summary>
/// Bibliography, citations and the index against the real compiler: what the emitted Typst actually
/// lays out, including the page numbers the index reads from the layout.
/// </summary>
[Trait("Category", "Integration")]
public sealed class BibliographyAndIndexTests
{
    private const string Bibtex = """
        @article{chen2022,
          author = {Chen, Wei and Okafor, Ada and Rossi, Luca},
          title = {Fast layout},
          journal = {Journal of Typesetting},
          volume = {7},
          pages = {1--10},
          year = {2022}
        }

        @book{dupo2024,
          author = {Dupont, Jean},
          title = {The Book of Layout},
          publisher = {Acme Press},
          year = {2024}
        }

        @inproceedings{okafor2021,
          author = {Okafor, Ada},
          title = {Streaming pages},
          booktitle = {Proceedings of Layout},
          pages = {77--88},
          year = {2021}
        }

        @article{smith2020,
          author = {Smith, John},
          title = {A note on counters},
          journal = {Notes},
          year = {2020}
        }

        @misc{zola2019,
          author = {Zola, Emile},
          title = {A short note},
          year = {2019}
        }
        """;

    private static CompilationResult Compile(string markup)
        => new TypstCompiler().Compile(
            new TypstWorldBuilder().AddSource("main.typ", markup).Build(),
            new TypstCompilationOptions { DocumentKind = TypstDocumentKind.Paged });

    [Fact]
    public void Renders_five_citations_and_a_numbered_bibliography()
    {
        var document = TypstDocument.Create();

        document
            .Page(page => page.Paper("a4").Margin(TypstLength.Cm(2)))
            .Heading(1, "Citations")
            .Paragraph(c => c.Text("Layout is linear ").Cite(document, "chen2022").Text("."))
            .Citation("dupo2024")
            .Citation("okafor2021")
            .Citation("smith2020")
            .Citation("zola2019")
            .Citation("chen2022");

        document.Bibliography(TypstBibliography.Parse(Bibtex));

        using var result = Compile(document.ToMarkup());

        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, result.PageCount);

        var text = result.ExtractText(0);

        Assert.Contains("Layout is linear [1].", text, StringComparison.Ordinal);

        // The list follows the order of first citation, not the order of the .bib file.
        var first = text.IndexOf("[1] W. Chen, A. Okafor, L. Rossi. Fast layout", StringComparison.Ordinal);
        var second = text.IndexOf("[2] J. Dupont. The Book of Layout", StringComparison.Ordinal);
        var third = text.IndexOf("[3] A. Okafor. Streaming pages", StringComparison.Ordinal);
        var fourth = text.IndexOf("[4] J. Smith. A note on counters", StringComparison.Ordinal);
        var fifth = text.IndexOf("[5] E. Zola. A short note", StringComparison.Ordinal);

        Assert.True(first >= 0, "the first cited work opens the list");
        Assert.True(first < second && second < third && third < fourth && fourth < fifth);
        Assert.Contains("Journal of Typesetting 7, 1\u201310, 2022.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Renders_an_author_date_bibliography_sorted_by_author()
    {
        var document = TypstDocument.Create();

        document
            .Page(page => page.Paper("a4").Margin(TypstLength.Cm(2)))
            .Heading(1, "Author-date")
            .Paragraph(c => c.Text("Layout is linear ").Cite(document, "okafor2021").Text("."))
            .Citation("zola2019")
            .Citation("dupo2024")
            .Citation("chen2022")
            .Citation("smith2020");

        document.Bibliography(TypstBibliography.Parse(Bibtex), TypstBibliographyStyle.AuthorDate);

        using var result = Compile(document.ToMarkup());

        Assert.Empty(result.Diagnostics);

        var text = result.ExtractText(0);

        Assert.Contains("(Okafor, 2021)", text, StringComparison.Ordinal);

        var chen = text.IndexOf("Chen, W., Okafor, A., Rossi, L. (2022).", StringComparison.Ordinal);
        var dupont = text.IndexOf("Dupont, J. (2024).", StringComparison.Ordinal);
        var smith = text.IndexOf("Smith, J. (2020).", StringComparison.Ordinal);
        var zola = text.IndexOf("Zola, E. (2019).", StringComparison.Ordinal);

        Assert.True(chen >= 0 && chen < dupont && dupont < smith && smith < zola);
        Assert.DoesNotContain("[1]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Renders_a_thirty_page_manual_and_an_index_with_the_real_page_numbers()
    {
        var document = TypstDocument.Create()
            .Page(page => page.Paper("a4").Margin(TypstLength.Cm(2)));

        for (var page = 1; page <= 30; page++)
        {
            document.Paragraph($"Page {page} of the manual.");

            switch (page)
            {
                case 2:
                    document.IndexMark("Cache");
                    break;
                case 5:
                    document.IndexMark("Cache", "eviction");
                    break;
                case 12:
                    document.IndexMark("buffer");
                    break;
                case 30:
                    document.IndexMark("Zebra");
                    break;
            }

            if (page < 30)
            {
                document.PageBreak();
            }
        }

        document.PageBreak().Index(new TypstContent().Text("Index"));

        using var result = Compile(document.ToMarkup());

        Assert.Empty(result.Diagnostics);
        Assert.Equal(31, result.PageCount);

        var pages = result.ExtractTextPages();
        var index = pages[30];

        // Sorted case-insensitively: buffer, Cache, Zebra. Each line carries the page its mark was
        // laid out on, read from counter(page) rather than counted by the model.
        Assert.Contains("buffer 12", index, StringComparison.Ordinal);
        Assert.Contains("Cache 2", index, StringComparison.Ordinal);
        Assert.Contains("eviction 5", index, StringComparison.Ordinal);
        Assert.Contains("Zebra 30", index, StringComparison.Ordinal);
        Assert.True(
            index.IndexOf("buffer", StringComparison.Ordinal) < index.IndexOf("Cache", StringComparison.Ordinal),
            "buffer sorts before Cache");
        Assert.True(
            index.IndexOf("Cache", StringComparison.Ordinal) < index.IndexOf("Zebra", StringComparison.Ordinal),
            "Cache sorts before Zebra");

        // The marks themselves print nothing: the page they sit on holds only its own sentence.
        Assert.Equal("Page 2 of the manual.", pages[1].Trim());
        Assert.Equal("Page 5 of the manual.", pages[4].Trim());
    }

    [Fact]
    public void Reports_the_page_a_mark_inside_a_floating_figure_lands_on()
    {
        var document = TypstDocument.Create()
            .Page(page => page.Paper("a4").Margin(TypstLength.Cm(2)))
            .Paragraph("A page of text that leaves no room for a tall figure.");

        for (var index = 0; index < 30; index++)
        {
            document.Paragraph("Filler paragraph to push the figure to the next page.");
        }

        document
            .Raw(
                "#figure(placement: top, block(height: 18cm)[FIGUREBODY "
                + "#context metadata((m3s-index: true, term: \"Floating\", sub: none, "
                + "page: counter(page).at(here()).first()))], caption: [A tall figure])")
            .PageBreak()
            .Paragraph("After the figure.")
            .PageBreak()
            .Index(new TypstContent().Text("Index"));

        using var result = Compile(document.ToMarkup());

        Assert.Empty(result.Diagnostics);

        var pages = result.ExtractTextPages();

        // The figure could not fit on page one, so Typst floated it to page two; the mark reports
        // the page the figure is laid out on, which is the page the reader sees it on.
        Assert.Contains("FIGUREBODY", pages[1], StringComparison.Ordinal);
        Assert.DoesNotContain("FIGUREBODY", pages[0], StringComparison.Ordinal);
        Assert.Contains("Floating 2", pages[^1], StringComparison.Ordinal);
    }
}
