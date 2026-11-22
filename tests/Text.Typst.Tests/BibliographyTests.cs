using System;
using System.Linq;
using M3scalinum.Text.Documents;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

/// <summary>
/// The BibTeX subset, the citation keys and the two styles — asserted on the markup the model emits.
/// What the compiler does with that markup is covered by the integration tests.
/// </summary>
public sealed class BibliographyTests
{
    internal const string Sample = """
        % A header comment outside an entry is ignored.

        @article{dupo2024,
          author = {Dupont, Jean and Durand, Marie},
          title = {A study of {TeX} and things},
          journal = {Journal of Things},
          volume = {12},
          number = {3},
          pages = {45--56},
          year = {2024},
          doi = {10.1000/xyz}
        }

        @book{smith2020,
          author = "Smith, John",
          title = "The Book of {R}ust",
          publisher = {Acme Press},
          year = 2020
        }

        @inproceedings{chen2022,
          author = {Chen, Wei and Okafor, Ada and Rossi, Luca},
          title = {Fast layout},
          booktitle = {Proceedings of Typesetting},
          pages = {1--10},
          year = {2022}
        }

        @misc{note2019,
          author = {Zola, Émile},
          title = {A note},
          year = {2019}
        }
        """;

    private static TypstBibliography SampleBibliography() => TypstBibliography.Parse(Sample);

    [Fact]
    public void Reads_the_entries_of_a_bibtex_file()
    {
        var bibliography = SampleBibliography();

        Assert.Equal(
            ["dupo2024", "smith2020", "chen2022", "note2019"],
            bibliography.Keys);
        Assert.Equal("article", bibliography["dupo2024"].EntryType);
        Assert.Equal("Journal of Things", bibliography["dupo2024"].Venue);
        Assert.Equal("12", bibliography["dupo2024"].Volume);
        Assert.Equal("45\u201356", bibliography["dupo2024"].Pages);
        Assert.Equal("Acme Press", bibliography["smith2020"].Publisher);
        Assert.Equal("Proceedings of Typesetting", bibliography["chen2022"].Venue);
    }

    [Fact]
    public void Drops_the_braces_that_protect_case()
    {
        Assert.Equal("A study of TeX and things", SampleBibliography()["dupo2024"].Title);
        Assert.Equal("The Book of Rust", SampleBibliography()["smith2020"].Title);
    }

    [Fact]
    public void Reads_a_bare_number_as_a_year()
    {
        Assert.Equal("2020", SampleBibliography()["smith2020"].Year);
    }

    [Fact]
    public void Splits_authors_on_and_and_keeps_the_family_name()
    {
        var authors = SampleBibliography()["dupo2024"].Authors;

        Assert.Equal(2, authors.Count);
        Assert.Equal("Dupont", authors[0].Family);
        Assert.Equal("Jean", authors[0].Given);
        Assert.Equal("J.", authors[0].Initials);
        Assert.Equal("Durand", authors[1].Family);
    }

    [Fact]
    public void Keeps_a_field_it_has_no_accessor_for()
    {
        var entries = TypstBibTexParser.Parse("@book{k, title={T}, keywords={alpha, beta}}");

        Assert.Equal("alpha, beta", entries[0].Field("keywords"));
        Assert.Null(entries[0].Field("publisher"));
    }

    [Fact]
    public void Rejects_a_string_macro()
    {
        var error = Assert.Throws<TypstBibTexParseException>(() =>
            TypstBibTexParser.Parse("@string{acme = \"Acme Press\"}"));

        Assert.Contains("@string", error.Message, StringComparison.Ordinal);
        Assert.True(error.Position >= 0);
    }

    [Fact]
    public void Rejects_a_hash_concatenation()
    {
        var error = Assert.Throws<TypstBibTexParseException>(() =>
            TypstBibTexParser.Parse("@book{k, publisher = \"Acme\" # \" Press\"}"));

        Assert.Contains("'#'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_macro_reference_in_a_field()
    {
        Assert.Throws<TypstBibTexParseException>(() =>
            TypstBibTexParser.Parse("@book{k, publisher = acme}"));
    }

    [Fact]
    public void Rejects_two_entries_with_the_same_key()
    {
        var error = Assert.Throws<TypstBibTexParseException>(() =>
            TypstBibTexParser.Parse("@book{k, title={A}} @book{k, title={B}}"));

        Assert.Contains("defined twice", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Numbers_five_citations_in_the_order_they_appear()
    {
        var markup = TypstDocument.Create()
            .Citation("chen2022")
            .Citation("dupo2024")
            .Citation("note2019")
            .Citation("smith2020")
            .Citation("dupo2024")
            .Bibliography(SampleBibliography())
            .ToMarkup();

        Assert.Contains("[1] W. Chen, A. Okafor, L. Rossi. _Fast layout_", markup, StringComparison.Ordinal);
        Assert.Contains("[2] J. Dupont, M. Durand. _A study of TeX and things_", markup, StringComparison.Ordinal);
        Assert.Contains("[3] \u00C9. Zola. _A note_", markup, StringComparison.Ordinal);
        Assert.Contains("[4] J. Smith. _The Book of Rust_", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("[5]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Gives_the_same_number_to_a_repeated_citation()
    {
        var markup = TypstDocument.Create()
            .Citation("smith2020")
            .Citation("dupo2024")
            .Citation("smith2020")
            .Bibliography(SampleBibliography())
            .ToMarkup();

        var body = markup[..markup.IndexOf("[1] J. Smith", StringComparison.Ordinal)];

        Assert.Equal(2, CountOf(body, "[1]"));
        Assert.Equal(1, CountOf(body, "[2]"));
    }

    [Fact]
    public void Writes_an_author_date_citation_with_one_two_and_three_authors()
    {
        var markup = TypstDocument.Create()
            .Citation("dupo2024")
            .Citation("chen2022")
            .Citation("smith2020")
            .Citation("dupo2024", prose: true)
            .Bibliography(SampleBibliography(), TypstBibliographyStyle.AuthorDate)
            .ToMarkup();

        Assert.Contains("(Dupont and Durand, 2024)", markup, StringComparison.Ordinal);
        Assert.Contains("(Chen et al., 2022)", markup, StringComparison.Ordinal);
        Assert.Contains("(Smith, 2020)", markup, StringComparison.Ordinal);
        Assert.Contains("Dupont and Durand (2024)", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Sorts_an_author_date_bibliography_by_author_then_year()
    {
        var markup = TypstDocument.Create()
            .Citation("smith2020")
            .Citation("chen2022")
            .Citation("dupo2024")
            .Citation("note2019")
            .Bibliography(SampleBibliography(), TypstBibliographyStyle.AuthorDate)
            .ToMarkup();

        var chen = markup.IndexOf("Chen, W.", StringComparison.Ordinal);
        var dupont = markup.IndexOf("Dupont, J.", StringComparison.Ordinal);
        var smith = markup.IndexOf("Smith, J.", StringComparison.Ordinal);
        var zola = markup.IndexOf("Zola, \u00C9.", StringComparison.Ordinal);

        Assert.True(chen < dupont, "Chen sorts before Dupont");
        Assert.True(dupont < smith, "Dupont sorts before Smith");
        Assert.True(smith < zola, "Smith sorts before Zola");
        Assert.DoesNotContain("[1]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Formats_a_book_and_a_proceedings_entry_differently()
    {
        var markup = TypstDocument.Create()
            .Citation("chen2022")
            .Citation("smith2020")
            .Bibliography(SampleBibliography())
            .ToMarkup();

        Assert.Contains(
            "[1] W. Chen, A. Okafor, L. Rossi. _Fast layout_. In Proceedings of Typesetting, 1\u201310, 2022.",
            markup,
            StringComparison.Ordinal);
        Assert.Contains(
            "[2] J. Smith. _The Book of Rust_. Acme Press, 2020.",
            markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Names_the_unknown_key_and_the_keys_that_exist()
    {
        var document = TypstDocument.Create()
            .Citation("ghost2000")
            .Bibliography(SampleBibliography());

        var error = Assert.Throws<TypstCitationException>(() => document.ToMarkup());

        Assert.Equal("ghost2000", error.Key);
        Assert.Contains("ghost2000", error.Message, StringComparison.Ordinal);
        Assert.Contains("dupo2024", error.Message, StringComparison.Ordinal);
        Assert.Equal(
            ["chen2022", "dupo2024", "note2019", "smith2020"],
            error.AvailableKeys);
        Assert.DoesNotContain("[?]", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Checks_the_key_immediately_when_the_bibliography_is_already_attached()
    {
        var document = TypstDocument.Create()
            .Bibliography(SampleBibliography());

        Assert.Throws<TypstCitationException>(() => document.Citation("ghost2000"));
    }

    [Fact]
    public void Refuses_a_citation_without_a_bibliography()
    {
        var document = TypstDocument.Create().Citation("dupo2024");

        var error = Assert.Throws<InvalidOperationException>(() => document.ToMarkup());

        Assert.Contains("bibliography", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_second_bibliography()
    {
        var document = TypstDocument.Create().Bibliography(SampleBibliography());

        Assert.Throws<InvalidOperationException>(() => document.Bibliography(SampleBibliography()));
    }

    [Fact]
    public void Escapes_markup_characters_in_a_field()
    {
        var bibliography = TypstBibliography.Parse(
            "@book{k, author={O'Neil, Pat}, title={Costs *and* benefits_ of #1}, year={2021}}");

        var markup = TypstDocument.Create()
            .Citation("k")
            .Bibliography(bibliography)
            .ToMarkup();

        Assert.Contains("_Costs \\*and\\* benefits\\_ of \\#1_", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Lists_every_entry_in_file_order_when_nothing_is_cited()
    {
        var markup = TypstDocument.Create()
            .Bibliography(SampleBibliography())
            .ToMarkup();

        var dupont = markup.IndexOf("Dupont", StringComparison.Ordinal);
        var smith = markup.IndexOf("Smith", StringComparison.Ordinal);

        Assert.True(dupont >= 0 && dupont < smith);
    }

    [Fact]
    public void Writes_a_prose_numeric_citation_with_its_author()
    {
        var markup = TypstDocument.Create()
            .Citation("chen2022", prose: true)
            .Bibliography(SampleBibliography())
            .ToMarkup();

        Assert.Contains("Chen et al. [1]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Writes_an_inline_citation_inside_a_paragraph()
    {
        var document = TypstDocument.Create();

        document
            .Paragraph(c => c.Text("The layout pass is linear ").Cite(document, "chen2022").Text("."))
            .Bibliography(SampleBibliography());

        var markup = document.ToMarkup();

        Assert.Contains("The layout pass is linear [1].", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Builds_a_bibliography_from_entries_another_reader_produced()
    {
        var entries = TypstBibTexParser.Parse(Sample);

        var bibliography = TypstBibliography.FromEntries(entries);

        Assert.Equal(4, bibliography.Entries.Count);
        Assert.True(bibliography.Contains("dupo2024"));
        Assert.Throws<ArgumentException>(() => TypstBibliography.FromEntries(entries.Concat(entries)));
    }

    private static int CountOf(string text, string needle)
    {
        var count = 0;
        var index = 0;

        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
