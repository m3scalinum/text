using System;
using M3scalinum.Text.Typst.Compilation;
using Xunit;

namespace M3scalinum.Text.Typst.Integration.Tests;

/// <summary>
/// Per-page text read from the paged layout, against the real compiler.
/// </summary>
/// <remarks>
/// The point of these tests is the page boundary: the layout already decided where each page
/// starts, so the marker on page one must not be reachable from page two in either direction.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class PageTextTests
{
    private const string TwoPages = """
        #set page(paper: "a4", margin: 2cm)

        ZEBRAALPHA

        #pagebreak()

        ZEBRABETA
        """;

    private static CompilationResult Compile(
        string markup,
        TypstDocumentKind kind = TypstDocumentKind.Paged)
    {
        var world = new TypstWorldBuilder().AddSource("main.typ", markup).Build();
        var result = new TypstCompiler().Compile(world, new TypstCompilationOptions { DocumentKind = kind });
        world.Dispose();
        return result;
    }

    [Fact]
    public void A_marker_lands_on_its_own_page_and_not_on_the_other()
    {
        using var result = Compile(TwoPages);

        Assert.Equal(2, result.PageCount);

        var first = result.ExtractText(0);
        var second = result.ExtractText(1);

        Assert.Contains("ZEBRAALPHA", first, StringComparison.Ordinal);
        Assert.DoesNotContain("ZEBRABETA", first, StringComparison.Ordinal);
        Assert.Contains("ZEBRABETA", second, StringComparison.Ordinal);
        Assert.DoesNotContain("ZEBRAALPHA", second, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_page_is_read_in_page_order()
    {
        using var result = Compile(TwoPages);

        var pages = result.ExtractTextPages();

        Assert.Equal(result.PageCount, pages.Count);
        Assert.Equal(result.ExtractText(0), pages[0]);
        Assert.Equal(result.ExtractText(1), pages[1]);
    }

    [Fact]
    public void A_page_ends_with_exactly_one_newline()
    {
        using var result = Compile(TwoPages);

        foreach (var page in result.ExtractTextPages())
        {
            Assert.EndsWith("\n", page, StringComparison.Ordinal);
            Assert.DoesNotContain("\n\n", page, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Table_cells_and_a_footnote_are_part_of_the_page_they_are_on()
    {
        const string markup = """
            #set page(paper: "a4", margin: 2cm)

            #table(
              columns: 2,
              [Name], [Value],
              [alpha], [42],
            )

            A sentence with a note#footnote[FOOTNOTEMARK].

            #pagebreak()

            ZEBRABETA
            """;

        using var result = Compile(markup);

        var first = result.ExtractText(0);
        var second = result.ExtractText(1);

        Assert.Contains("alpha", first, StringComparison.Ordinal);
        Assert.Contains("42", first, StringComparison.Ordinal);
        Assert.Contains("FOOTNOTEMARK", first, StringComparison.Ordinal);
        Assert.DoesNotContain("FOOTNOTEMARK", second, StringComparison.Ordinal);
        Assert.Contains("ZEBRABETA", second, StringComparison.Ordinal);
    }

    [Fact]
    public void Non_latin_text_keeps_its_characters()
    {
        const string markup = """
            #set page(paper: "a4", margin: 2cm)

            Привет, мир.

            #pagebreak()

            Γειά σου κόσμε.
            """;

        using var result = Compile(markup);

        Assert.Contains("Привет", result.ExtractText(0), StringComparison.Ordinal);
        Assert.Contains("Γειά", result.ExtractText(1), StringComparison.Ordinal);
    }

    [Fact]
    public void Stylish_runs_of_one_line_stay_on_one_line()
    {
        const string markup = """
            #set page(paper: "a4", margin: 2cm)

            Alpha *beta* gamma.
            """;

        using var result = Compile(markup);

        var text = result.ExtractText(0);

        Assert.Contains("Alpha beta gamma.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Extraction_requires_a_paged_document()
    {
        using var result = Compile("= Title\n\nBody.", TypstDocumentKind.Html);

        var exception = Assert.Throws<TypstNativeException>(() => result.ExtractText(0));

        Assert.Equal(TypstErrorCode.InvalidArgument, exception.Code);
        Assert.Contains("paged", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_count_extraction_fails_for_a_non_paged_document()
    {
        using var result = Compile("= Title\n\nBody.", TypstDocumentKind.Html);

        var exception = Assert.Throws<TypstNativeException>(result.ExtractTextPages);

        Assert.Equal(TypstErrorCode.InvalidArgument, exception.Code);
    }

    [Fact]
    public void A_page_out_of_range_fails_with_the_valid_range_in_the_message()
    {
        using var result = Compile(TwoPages);

        var exception = Assert.Throws<TypstNativeException>(() => result.ExtractText(2));

        Assert.Equal(TypstErrorCode.InvalidArgument, exception.Code);
        Assert.Contains("0..2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Extraction_is_deterministic()
    {
        using var first = Compile(TwoPages);
        using var second = Compile(TwoPages);

        var left = first.ExtractTextPages();
        var right = second.ExtractTextPages();

        Assert.Equal(left, right);
    }

    [Fact]
    public void An_html_document_still_exports_text_the_old_way()
    {
        using var result = Compile("= Title\n\nBody.", TypstDocumentKind.Html);

        Assert.Contains("Title", result.ExportText(), StringComparison.Ordinal);
    }
}
