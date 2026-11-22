using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using M3scalinum.Text.Documents;
using M3scalinum.Text.Typst.Compilation;
using Xunit;

namespace M3scalinum.Text.Typst.Integration.Tests;

/// <summary>Columns and margin notes, laid out by the real compiler.</summary>
/// <remarks>
/// Markup cannot say whether the first column fills before the second, nor whether a note lands level
/// with its marker: that is the layout's decision. These tests read the text of every page and the
/// position of every glyph run the SVG export draws, and ask them.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class ColumnAndMarginNoteIntegrationTests
{
    private const double PageWidth = 595.275590551;

    /// <summary>2 cm, the margin of the column documents.</summary>
    private const double TwoCentimetres = 56.692913386;

    /// <summary>3.5 cm, the margin of the margin note documents.</summary>
    private const double ThreeAndAHalfCentimetres = 99.212598425;

    private const double NoteRight = PageWidth - ThreeAndAHalfCentimetres;

    private static CompilationResult Compile(TypstDocument document)
        => new TypstCompiler().Compile(
            new TypstWorldBuilder().AddSource("main.typ", document.ToMarkup()).Build());

    /// <summary>
    /// The page position of every glyph run the SVG export draws, in the order it draws them. Typst
    /// writes each run as a <c>&lt;use&gt;</c> inside a <c>&lt;g transform&gt;</c> chain, so the position
    /// is the product of the chain.
    /// </summary>
    private static List<(double X, double Y)> GlyphRuns(byte[] svg)
    {
        var document = XDocument.Parse(Encoding.UTF8.GetString(svg));
        var runs = new List<(double X, double Y)>();
        Walk(document.Root!, (1, 0, 0, 1, 0, 0), runs);
        return runs;

        static void Walk(XElement element, (double A, double B, double C, double D, double E, double F) matrix, List<(double, double)> runs)
        {
            if (element.Attribute("transform") is { } transform)
            {
                matrix = Multiply(matrix, Parse(transform.Value));
            }

            if (element.Name.LocalName == "use")
            {
                runs.Add((matrix.E, matrix.F));
            }

            foreach (var child in element.Elements())
            {
                Walk(child, matrix, runs);
            }
        }
    }

    private static (double A, double B, double C, double D, double E, double F) Parse(string transform)
    {
        var numbers = Regex.Matches(transform, @"-?[\d.]+(?:[eE][-+]?\d+)?")
            .Select(match => double.Parse(match.Value, CultureInfo.InvariantCulture))
            .ToArray();

        if (transform.StartsWith("translate", StringComparison.Ordinal))
        {
            return (1, 0, 0, 1, numbers[0], numbers.Length > 1 ? numbers[1] : 0);
        }

        if (transform.StartsWith("matrix", StringComparison.Ordinal) && numbers.Length >= 6)
        {
            return (numbers[0], numbers[1], numbers[2], numbers[3], numbers[4], numbers[5]);
        }

        return (1, 0, 0, 1, 0, 0);
    }

    private static (double A, double B, double C, double D, double E, double F) Multiply(
        (double A, double B, double C, double D, double E, double F) left,
        (double A, double B, double C, double D, double E, double F) right)
        => (
            left.A * right.A + left.C * right.B,
            left.B * right.A + left.D * right.B,
            left.A * right.C + left.C * right.D,
            left.B * right.C + left.D * right.D,
            left.A * right.E + left.C * right.F + left.E,
            left.B * right.E + left.D * right.F + left.F);

    private static TypstDocument SixtyParagraphs()
    {
        var document = TypstDocument.Create();
        for (var index = 0; index < 60; index++)
        {
            document.Paragraph($"PARA{index:D3} lorem ipsum dolor sit amet consectetur adipiscing elit sed do eiusmod tempor.");
        }

        return document;
    }

    private static int[] Paragraphs(string page)
        => Regex.Matches(page, @"PARA(\d{3})")
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToArray();

    [Fact]
    public void A_two_column_document_fills_the_first_column_before_the_second()
    {
        var document = SixtyParagraphs().Page(page => page
            .Paper("a4")
            .Margin(TypstLength.Cm(2))
            .Columns(2, TypstLength.Em(1)));

        using var result = Compile(document);
        var pages = result.ExtractTextPages();

        Assert.True(pages.Count > 1, $"two columns of sixty paragraphs should span pages, got {pages.Count}");

        var first = Paragraphs(pages[0]);
        Assert.Equal(Enumerable.Range(0, first.Length), first);

        // Every run of the first page, in drawing order: the left column must be complete before the
        // right one starts. A row-first layout would interleave them.
        var runs = GlyphRuns(result.ExportSvg(0)).Where(run => run.X < PageWidth - 1).ToArray();
        var columns = runs.Select(run => run.X < TwoCentimetres + 10 ? 0 : run.X > TwoCentimetres + 200 ? 1 : -1).ToArray();

        Assert.DoesNotContain(-1, columns);
        Assert.Contains(0, columns);
        Assert.Contains(1, columns);
        Assert.True(
            columns.SequenceEqual(columns.Order()),
            "the columns are drawn column after column, not row after row");
    }

    [Fact]
    public void The_second_page_continues_where_the_first_page_stopped()
    {
        var document = SixtyParagraphs().Page(page => page
            .Paper("a4")
            .Margin(TypstLength.Cm(2))
            .Columns(2, TypstLength.Em(1)));

        using var result = Compile(document);
        var pages = result.ExtractTextPages();

        var first = Paragraphs(pages[0]);
        var second = Paragraphs(pages[1]);

        Assert.Equal(first.Max() + 1, second.Min());
        Assert.Equal(Enumerable.Range(second.Min(), second.Length), second);
    }

    [Fact]
    public void A_single_column_document_keeps_one_column()
    {
        var document = SixtyParagraphs().Page(page => page.Paper("a4").Margin(TypstLength.Cm(2)));

        using var result = Compile(document);
        var runs = GlyphRuns(result.ExportSvg(0)).Where(run => run.X < PageWidth - 1).ToArray();

        Assert.NotEmpty(runs);
        Assert.All(runs, run => Assert.True(run.X <= TwoCentimetres + 10, $"a run starts at x={run.X}"));
    }

    [Fact]
    public void A_margin_note_sits_in_the_margin_level_with_its_marker()
    {
        // A4 is 21 cm wide and the margins take 3.5 cm on each side, so the text area is 14 cm wide:
        // 14.3 cm puts a 3 cm note in the outer margin, clear of the text.
        var document = TypstDocument.Create()
            .Page(page => page.Paper("a4").Margin(TypstLength.Cm(3.5)))
            .Paragraph("Body line")
            .MarginNote("1", "MARGINNOTE", TypstLength.Cm(14.3), TypstLength.Cm(3));

        using var result = Compile(document);
        var runs = GlyphRuns(result.ExportSvg(0)).Where(run => run.X < PageWidth - 1).ToArray();

        var body = runs.Where(run => run.X < ThreeAndAHalfCentimetres + 5).ToArray();
        var note = runs.Where(run => run.X > NoteRight - 2).ToArray();

        Assert.NotEmpty(body);
        Assert.NotEmpty(note);

        // The marker is a superscript on the line it opens, so the last body line is the marker's
        // line; the note must sit on it, not at the top or the bottom of the page.
        var markerLine = body.Max(run => run.Y);
        Assert.True(
            Math.Abs(note.Min(run => run.Y) - markerLine) < 14,
            $"the note starts at y={note.Min(run => run.Y)}, its marker line at y={markerLine}");
        Assert.True(
            note.Min(run => run.Y) > body.Min(run => run.Y) + 14,
            $"the note must follow its marker, not the first line of the page");
    }

    [Fact]
    public void Two_margin_notes_on_one_page_do_not_overlap()
    {
        // Two different offsets give each note its own x band, so the margin runs can be told apart
        // without reading glyphs: the first note is further right than the second.
        var document = TypstDocument.Create()
            .Page(page => page.Paper("a4").Margin(TypstLength.Cm(3.5)))
            .Paragraph("First body line")
            .MarginNote("1", "FIRSTNOTE one two three four five six seven eight.", TypstLength.Cm(14.3), TypstLength.Cm(1.5))
            .Paragraph(string.Join(' ', Enumerable.Repeat("Filler body text that keeps the notes apart.", 12)))
            .Paragraph("Second body line")
            .MarginNote("2", "SECONDNOTE one two three four five six seven eight.", TypstLength.Cm(15.9), TypstLength.Cm(1.5));

        using var result = Compile(document);
        Assert.Equal(1, result.PageCount);

        var margin = GlyphRuns(result.ExportSvg(0))
            .Where(run => run.X > NoteRight - 2 && run.X < PageWidth - 1)
            .ToArray();

        var second = margin.Where(run => run.X > ThreeAndAHalfCentimetres + 430).ToArray();
        var first = margin.Where(run => run.X <= ThreeAndAHalfCentimetres + 430).ToArray();

        Assert.NotEmpty(first);
        Assert.NotEmpty(second);
        Assert.True(
            first.Max(run => run.Y) < second.Min(run => run.Y),
            $"the first note ends at y={first.Max(run => run.Y)}, the second starts at y={second.Min(run => run.Y)}");
    }

    /// <summary>
    /// The documented limit: a placed note takes no space in the flow, so the layout does not push
    /// the next note down. Two notes whose markers are closer than one note is tall overlap.
    /// </summary>
    [Fact]
    public void Two_notes_closer_than_one_note_tall_overlap()
    {
        var document = TypstDocument.Create()
            .Page(page => page.Paper("a4").Margin(TypstLength.Cm(3.5)))
            .Paragraph("First body line")
            .MarginNote("1", "FIRSTNOTE one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen.", TypstLength.Cm(14.3), TypstLength.Cm(1.5))
            .Paragraph("Second body line, one line lower")
            .MarginNote("2", "SECONDNOTE one two three four five six seven eight nine ten eleven twelve.", TypstLength.Cm(15.9), TypstLength.Cm(1.5));

        using var result = Compile(document);
        var margin = GlyphRuns(result.ExportSvg(0))
            .Where(run => run.X > NoteRight - 2 && run.X < PageWidth - 1)
            .ToArray();

        var second = margin.Where(run => run.X > ThreeAndAHalfCentimetres + 430).ToArray();
        var first = margin.Where(run => run.X <= ThreeAndAHalfCentimetres + 430).ToArray();

        Assert.NotEmpty(first);
        Assert.NotEmpty(second);
        Assert.True(
            first.Max(run => run.Y) > second.Min(run => run.Y),
            "the notes are expected to overlap here: placed content does not displace the next note");
    }
}
