using System;
using System.Collections.Generic;
using System.Linq;
using M3scalinum.Text.Documents;
using M3scalinum.Text.Typst.Compilation;
using Xunit;

namespace M3scalinum.Text.Typst.Integration.Tests;

/// <summary>A table taller than a page, laid out by the real compiler.</summary>
/// <remarks>
/// Markup cannot say whether a header reappeared on page 2 or whether a row was cut in two: that is
/// the layout's decision. These tests read the text of every page and ask it.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class TablePaginationTests
{
    private const int Rows = 200;

    private static TypstDocument LongTable(bool repeatHeader, bool keepRowsWhole) => TypstDocument.Create()
        .Page(page => page.Paper("a4").Margin(TypstLength.Cm(2)))
        .Table(table =>
        {
            table
                .Columns(TypstLength.Fr(1), TypstLength.Fr(1))
                .RepeatHeader(repeatHeader)
                .HeaderRow("ITEMCOLUMN", "AMOUNTCOLUMN");

            for (var index = 0; index < Rows; index++)
            {
                table.Row(
                    new TypstTableCell($"ROW{index}START") { Breakable = keepRowsWhole ? false : null },
                    new TypstTableCell($"ROW{index}END") { Breakable = keepRowsWhole ? false : null });
            }
        });

    private static CompilationResult Compile(TypstDocument document)
    {
        using var world = new TypstWorldBuilder().AddSource("main.typ", document.ToMarkup()).Build();
        return new TypstCompiler().Compile(world);
    }

    private static int PagesHolding(IReadOnlyList<string> pages, string marker) =>
        pages.Count(page => page.Contains(marker, StringComparison.Ordinal));

    [Fact]
    public void A_header_repeats_on_every_page_a_long_table_spans()
    {
        using var result = Compile(LongTable(repeatHeader: true, keepRowsWhole: false));
        var pages = result.ExtractTextPages();

        Assert.True(pages.Count > 2, $"the table should span more than two pages, it spans {pages.Count}");
        Assert.Equal(pages.Count, PagesHolding(pages, "ITEMCOLUMN"));
        Assert.Equal(pages.Count, PagesHolding(pages, "AMOUNTCOLUMN"));
    }

    [Fact]
    public void A_header_asked_not_to_repeat_stays_on_the_first_page()
    {
        using var result = Compile(LongTable(repeatHeader: false, keepRowsWhole: false));
        var pages = result.ExtractTextPages();

        Assert.True(pages.Count > 2, $"the table should span more than two pages, it spans {pages.Count}");
        Assert.Equal(1, PagesHolding(pages, "ITEMCOLUMN"));
        Assert.Contains("ITEMCOLUMN", pages[0], StringComparison.Ordinal);
    }

    [Fact]
    public void A_row_kept_whole_is_never_cut_between_two_pages()
    {
        using var result = Compile(LongTable(repeatHeader: true, keepRowsWhole: true));
        var pages = result.ExtractTextPages();

        Assert.True(pages.Count > 2, $"the table should span more than two pages, it spans {pages.Count}");

        for (var index = 0; index < Rows; index++)
        {
            var start = PagesHolding(pages, $"ROW{index}START");
            var end = PagesHolding(pages, $"ROW{index}END");

            Assert.Equal(1, start);
            Assert.Equal(1, end);
        }
    }

    /// <summary>Two rows tall enough that the second one cannot share the first page.</summary>
    private static TypstDocument TallRows(bool keepRowsWhole)
    {
        var filler = string.Join(" ", Enumerable.Repeat("segment", 400));

        return TypstDocument.Create()
            .Page(page => page.Paper("a4").Margin(TypstLength.Cm(2)))
            .Table(table =>
            {
                table
                    .Columns(TypstLength.Fr(1), TypstLength.Fr(1))
                    .HeaderRow("ITEMCOLUMN", "AMOUNTCOLUMN");

                for (var index = 0; index < 2; index++)
                {
                    table.Row(
                        new TypstTableCell($"TALL{index}START {filler} TALL{index}END")
                        {
                            Breakable = keepRowsWhole ? false : null,
                        },
                        new TypstTableCell($"TALL{index}SIDE"));
                }
            });
    }

    [Fact]
    public void A_row_left_breakable_is_cut_between_two_pages()
    {
        using var result = Compile(TallRows(keepRowsWhole: false));
        var pages = result.ExtractTextPages();

        Assert.True(pages.Count >= 2, $"the table should span two pages, it spans {pages.Count}");

        var start = pages.ToList().FindIndex(page => page.Contains("TALL1START", StringComparison.Ordinal));
        var end = pages.ToList().FindIndex(page => page.Contains("TALL1END", StringComparison.Ordinal));

        Assert.True(start >= 0 && end >= 0, "both halves of the row should be readable");
        Assert.NotEqual(start, end);
    }

    [Fact]
    public void The_same_row_kept_whole_moves_to_the_next_page_instead()
    {
        using var result = Compile(TallRows(keepRowsWhole: true));
        var pages = result.ExtractTextPages();

        Assert.True(pages.Count >= 2, $"the table should span two pages, it spans {pages.Count}");

        var start = pages.ToList().FindIndex(page => page.Contains("TALL1START", StringComparison.Ordinal));
        var end = pages.ToList().FindIndex(page => page.Contains("TALL1END", StringComparison.Ordinal));

        Assert.True(start >= 0 && end >= 0, "both halves of the row should be readable");
        Assert.Equal(start, end);
    }
}
