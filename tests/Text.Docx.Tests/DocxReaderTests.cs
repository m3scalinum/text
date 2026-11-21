using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using M3scalinum.Text.Docx;
using Xunit;

namespace M3scalinum.Text.Docx.Tests;

/// <summary>What the reader makes of the committed package.</summary>
/// <remarks>
/// These tests read <c>fixtures/report.docx</c> from disk rather than the builder's bytes in memory,
/// so the committed binary is what they hold to account. The builder is checked against it
/// separately, in <see cref="FixtureTests"/>.
/// </remarks>
public sealed class DocxReaderTests
{
    [Fact]
    public void A_heading_style_becomes_a_heading_level()
    {
        var document = Read();

        var headings = document.Blocks.OfType<DocxParagraph>().Where(p => p.HeadingLevel is not null).ToList();

        Assert.Equal(2, headings.Count);
        Assert.Equal(1, headings[0].HeadingLevel);
        Assert.Equal(DocxFixture.HeadingOne, headings[0].Text);
        Assert.Equal(2, headings[1].HeadingLevel);
        Assert.Equal(DocxFixture.HeadingTwo, headings[1].Text);
    }

    [Fact]
    public void A_heading_keeps_its_text_and_nothing_else()
    {
        var heading = Paragraphs(Read()).First(p => p.HeadingLevel == 1);

        var run = Assert.IsType<DocxTextRun>(Assert.Single(heading.Inlines));

        Assert.Equal(DocxFixture.HeadingOne, run.Text);
        Assert.False(run.Bold);
        Assert.False(run.Italic);
    }

    [Fact]
    public void A_run_keeps_its_bold_and_its_italic()
    {
        var paragraph = Paragraphs(Read()).First(p => p.Text == DocxFixture.BodyText);

        var runs = paragraph.Inlines.Cast<DocxTextRun>().ToList();

        Assert.Equal(5, runs.Count);

        Assert.Equal("Revenue grew ", runs[0].Text);
        Assert.False(runs[0].Bold);
        Assert.False(runs[0].Italic);

        Assert.Equal("twelve percent", runs[1].Text);
        Assert.True(runs[1].Bold);
        Assert.False(runs[1].Italic);

        Assert.Equal("stayed flat", runs[3].Text);
        Assert.False(runs[3].Bold);
        Assert.True(runs[3].Italic);
    }

    [Fact]
    public void A_run_keeps_its_underline_colour_size_and_superscript()
    {
        var paragraph = Paragraphs(Read()).First(p => p.Text == "much higher than budget");

        var run = Assert.IsType<DocxTextRun>(paragraph.Inlines[0]);

        Assert.Equal("single", run.Underline);
        Assert.Equal("C00000", run.Color);
        Assert.Equal(14.0, run.SizePoints);
        Assert.Equal(DocxScript.Superscript, run.Script);
    }

    [Fact]
    public void A_struck_run_is_struck()
    {
        var paragraph = Paragraphs(Read()).First(p => p.Text == "much higher than budget");

        var struck = paragraph.Inlines.Cast<DocxTextRun>().First(r => r.Text == " than ");

        Assert.True(struck.Strike);
    }

    [Fact]
    public void Consecutive_numbered_paragraphs_share_the_bulleted_list()
    {
        var items = Paragraphs(Read()).Where(p => p.List is not null).ToList();

        Assert.Equal(2, items.Count);

        foreach (var item in items)
        {
            Assert.NotNull(item.List);
            Assert.Equal(0, item.List!.Level);
            Assert.False(item.List.Ordered);
            Assert.Equal(1, item.List.NumberingId);
        }

        Assert.Equal(DocxFixture.BulletOne, items[0].Text);
        Assert.Equal(DocxFixture.BulletTwo, items[1].Text);
    }

    [Fact]
    public void A_table_keeps_its_columns_and_its_grid_span()
    {
        var table = Table(Read());

        Assert.Equal(3, table.Columns);
        Assert.Equal(3, table.Rows.Count);

        var first = table.Rows[0].Cells;

        Assert.Equal(2, first.Count);
        Assert.Equal(2, first[0].Colspan);
        Assert.Equal(DocxFixture.MergedCellText, Text(first[0]));
        Assert.Equal(1, first[1].Colspan);
        Assert.Equal("Third", Text(first[1]));
    }

    [Fact]
    public void A_table_keeps_its_row_span_and_drops_the_continuation_cell()
    {
        var table = Table(Read());

        var restart = table.Rows[1].Cells[0];

        Assert.Equal("Left", Text(restart));
        Assert.Equal(1, restart.Colspan);
        Assert.Equal(2, restart.Rowspan);

        // The cell that continues the merge is not a cell of its own: Typst places the remaining
        // cells of row three after the spanning one, exactly as Word does.
        Assert.Equal(2, table.Rows[2].Cells.Count);
        Assert.Equal("Middle below", Text(table.Rows[2].Cells[0]));
        Assert.Equal("Right below", Text(table.Rows[2].Cells[1]));
    }

    [Fact]
    public void An_embedded_image_is_listed_with_its_bytes()
    {
        var images = Read().Images;

        var image = Assert.Single(images);

        Assert.Equal(DocxFixture.ImagePart, image.Key);
        Assert.Equal(DocxFixture.Png, image.Value);
    }

    [Fact]
    public void An_image_run_names_the_part_it_shows()
    {
        var image = Paragraphs(Read())
            .SelectMany(p => p.Inlines)
            .OfType<DocxImageRun>()
            .Single();

        Assert.Equal(DocxFixture.ImagePart, image.Name);
        Assert.Equal("image/png", image.MimeType);
    }

    [Fact]
    public void The_page_setup_comes_from_the_section()
    {
        var page = Read().Page;

        Assert.NotNull(page);
        Assert.Equal(595.3, page!.WidthPoints);
        Assert.Equal(841.9, page.HeightPoints);
        Assert.False(page.Landscape);
        Assert.Equal(72.0, page.MarginTopPoints);
        Assert.Equal(85.05, page.MarginLeftPoints);
    }

    [Fact]
    public void Turning_images_off_drops_them_entirely()
    {
        var document = DocxReader.Read(
            DocxFixture.Build(), new DocxOptions { ExtractImages = false });

        Assert.Empty(document.Images);
        Assert.Empty(Paragraphs(document).SelectMany(p => p.Inlines).OfType<DocxImageRun>());
    }

    [Fact]
    public void Bytes_that_are_not_a_package_are_a_malformed_package()
    {
        var exception = Assert.Throws<DocxException>(() => DocxReader.Read(DocxFixture.NotAPackage()));

        Assert.Equal(DocxErrorCode.MalformedPackage, exception.Code);
    }

    [Fact]
    public void A_package_without_a_document_is_a_missing_part()
    {
        var exception = Assert.Throws<DocxException>(() => DocxReader.Read(DocxFixture.WithoutDocument()));

        Assert.Equal(DocxErrorCode.MissingPart, exception.Code);
        Assert.Contains("word/document.xml", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_document_is_an_invalid_argument()
    {
        var exception = Assert.Throws<DocxException>(() => DocxReader.Read([]));

        Assert.Equal(DocxErrorCode.InvalidArgument, exception.Code);
    }

    [Fact]
    public void A_document_over_the_limit_is_refused_before_it_is_parsed()
    {
        var options = new DocxOptions { MaxDocumentBytes = 16 };

        var exception = Assert.Throws<DocxException>(() => DocxReader.Read(DocxFixture.Build(), options));

        Assert.Equal(DocxErrorCode.Limit, exception.Code);
    }

    [Fact]
    public void A_negative_limit_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DocxReader.Read(DocxFixture.Build(), new DocxOptions { MaxDocumentBytes = 0 }));
    }

    [Fact]
    public void Every_handle_is_released()
    {
        var before = Internal.DocxNative.LiveCount();

        var document = DocxReader.Read(DocxFixture.Build());
        var again = DocxReader.Read(DocxFixture.Build());

        Assert.NotNull(document);
        Assert.NotNull(again);
        Assert.Equal(before, Internal.DocxNative.LiveCount());
    }

    [Fact]
    public void A_stream_is_read_whole()
    {
        using var stream = new MemoryStream(DocxFixture.Build());

        var document = DocxReader.Read(stream);

        Assert.Equal(DocxFixture.HeadingOne, Paragraphs(document).First().Text);
    }

    private static DocxDocument Read() => DocxReader.Read(File.ReadAllBytes(FixtureTests.Path));

    private static IEnumerable<DocxParagraph> Paragraphs(DocxDocument document)
        => document.Blocks.OfType<DocxParagraph>();

    private static DocxTableBlock Table(DocxDocument document)
        => document.Blocks.OfType<DocxTableBlock>().Single();

    private static string Text(DocxTableCell cell)
        => string.Join("\n", cell.Blocks.OfType<DocxParagraph>().Select(p => p.Text));
}
