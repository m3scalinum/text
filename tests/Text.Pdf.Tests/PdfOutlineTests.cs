using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// The document outline: what <c>/Outlines</c> states when a document is read, and what an
/// incremental update writes back. The fixtures are built with <see cref="PdfTestDocument"/> where
/// the shape is the point, and read from a document the compiler produced where a real producer is.
/// </summary>
public sealed class PdfOutlineTests
{
    /// <summary>A title as the file writes it: UTF-16BE with a byte-order mark.</summary>
    private static string Text(string value) => "<FEFF" + Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(value)) + ">";

    /// <summary>Two A4 pages, a catalog, and whatever outline the test adds.</summary>
    private static List<string> TwoPages() =>
    [
        "<</Type/Catalog/Pages 2 0 R/Outlines 5 0 R>>",
        "<</Type/Pages/Count 2/Kids[3 0 R 4 0 R]>>",
        "<</Type/Page/Parent 2 0 R/MediaBox[0 0 100 100]>>",
        "<</Type/Page/Parent 2 0 R/MediaBox[0 0 100 100]>>",
    ];

    private static PdfFile Open(byte[] pdf)
    {
        Assert.True(PdfFile.TryOpen(pdf, out var file), "the bytes were not read as a PDF");
        Assert.Empty(file!.Problems);

        return file;
    }

    private static byte[] Fixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", name);

        Assert.True(File.Exists(path), $"the fixture {name} is missing from {path}");

        return File.ReadAllBytes(path);
    }

    private static void AssertSamePlan(IReadOnlyList<PdfOutlineEntry> expected, IReadOnlyList<PdfOutlineEntry> actual)
    {
        Assert.Equal(expected.Count, actual.Count);

        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index].Title, actual[index].Title);
            Assert.Equal(expected[index].PageNumber, actual[index].PageNumber);
            Assert.Equal(expected[index].IsOpen, actual[index].IsOpen);
            Assert.Equal(expected[index].IsBold, actual[index].IsBold);
            Assert.Equal(expected[index].IsItalic, actual[index].IsItalic);
            AssertSamePlan(expected[index].Children, actual[index].Children);
        }
    }

    [Fact]
    public void A_document_without_an_outline_reads_as_an_empty_outline()
    {
        var outline = PdfOutline.Read(PdfTestDocument.Minimal());

        Assert.Empty(outline.Entries);
    }

    [Fact]
    public void A_flat_outline_reads_titles_pages_and_destinations()
    {
        var bodies = TwoPages();
        bodies.AddRange(
        [
            "<</Type/Outlines/First 6 0 R/Last 7 0 R/Count 2>>",
            "<</Title" + Text("Introduction") + "/Parent 5 0 R/Next 7 0 R/Dest[3 0 R/XYZ 72 700 0]>>",
            "<</Title" + Text("Conclusion") + "/Parent 5 0 R/Prev 6 0 R/Dest[4 0 R/Fit]>>",
        ]);

        var outline = PdfOutline.Read(PdfTestDocument.Document(bodies));

        Assert.Equal(["Introduction", "Conclusion"], outline.Entries.Select(entry => entry.Title));
        Assert.Equal([1, 2], outline.Entries.Select(entry => entry.PageNumber));
        Assert.Empty(outline.Entries[0].Children);

        var introduction = outline.Entries[0].Destination;

        Assert.NotNull(introduction);
        Assert.Null(introduction.Name);
        Assert.Equal("XYZ", introduction.Fit);
        Assert.Equal(72, introduction.Left);
        Assert.Equal(700, introduction.Top);
        Assert.Equal(0, introduction.Zoom);

        var conclusion = outline.Entries[1].Destination;

        Assert.NotNull(conclusion);
        Assert.Equal("Fit", conclusion.Fit);
        Assert.Null(conclusion.Top);

        // A leaf has no /Count, so nothing collapses it: a viewer shows it and nothing more.
        Assert.All(outline.Entries, entry => Assert.False(entry.IsOpen));
    }

    [Fact]
    public void A_nested_outline_reads_three_levels_and_their_state()
    {
        var bodies = TwoPages();
        bodies.AddRange(
        [
            "<</Type/Outlines/First 6 0 R/Last 8 0 R/Count 2>>",
            // Part one is open (positive /Count) and bold.
            "<</Title(Part one)/Parent 5 0 R/Next 8 0 R/First 7 0 R/Last 7 0 R/Count 1/F 2/Dest[3 0 R/Fit]>>",
            // Chapter one is closed (negative /Count) and italic, and holds one leaf.
            "<</Title(Chapter one)/Parent 6 0 R/First 9 0 R/Last 9 0 R/Count -1/F 1/Dest[3 0 R/FitH 640]>>",
            "<</Title(Part two)/Parent 5 0 R/Prev 6 0 R/Dest[4 0 R/Fit]>>",
            "<</Title(Section one)/Parent 7 0 R/Dest[4 0 R/FitV 40]>>",
        ]);

        var outline = PdfOutline.Read(PdfTestDocument.Document(bodies));

        var partOne = outline.Entries[0];
        Assert.Equal("Part one", partOne.Title);
        Assert.True(partOne.IsOpen);
        Assert.True(partOne.IsBold);
        Assert.False(partOne.IsItalic);

        var chapter = Assert.Single(partOne.Children);
        Assert.Equal("Chapter one", chapter.Title);
        Assert.False(chapter.IsOpen);
        Assert.Equal(1, chapter.PageNumber);
        Assert.True(chapter.IsItalic);
        Assert.False(chapter.IsBold);
        Assert.Equal("FitH", chapter.Destination!.Fit);
        Assert.Equal(640, chapter.Destination.Top);

        var section = Assert.Single(chapter.Children);
        Assert.Equal("Section one", section.Title);
        Assert.Equal(2, section.PageNumber);
        Assert.Equal("FitV", section.Destination!.Fit);
        Assert.Equal(40, section.Destination.Left);
        Assert.Empty(section.Children);

        Assert.Equal("Part two", outline.Entries[1].Title);
        Assert.Equal(2, outline.Entries[1].PageNumber);
    }

    [Fact]
    public void A_named_destination_in_the_name_tree_resolves_its_page()
    {
        var document = PdfTestDocument.Document(
        [
            // The catalog's /Names dictionary leads to a name tree whose second level holds the
            // entries, which is how a producer with many destinations writes them.
            "<</Type/Catalog/Pages 2 0 R/Outlines 5 0 R/Names 8 0 R>>",
            "<</Type/Pages/Count 2/Kids[3 0 R 4 0 R]>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 100 100]>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 100 100]>>",
            "<</Type/Outlines/First 6 0 R/Last 7 0 R/Count 2>>",
            "<</Title(Start)/Parent 5 0 R/Next 7 0 R/Dest(Intro)>>",
            "<</Title(Missing)/Parent 5 0 R/Prev 6 0 R/Dest(Nowhere)>>",
            "<</Dests 9 0 R>>",
            "<</Kids[10 0 R]>>",
            "<</Names[(Intro)[4 0 R/XYZ 10 20 1.5] (Nowhere)[99 0 R/Fit]]>>",
        ]);

        var outline = PdfOutline.Read(document);

        Assert.Equal(2, outline.Entries[0].PageNumber);
        var start = outline.Entries[0].Destination!;
        Assert.Equal("Intro", start.Name);
        Assert.Equal("XYZ", start.Fit);
        Assert.Equal(1.5, start.Zoom);

        Assert.Null(outline.Entries[1].PageNumber);
        var missing = outline.Entries[1].Destination!;
        Assert.Equal("Nowhere", missing.Name);
        Assert.Equal("Fit", missing.Fit);
    }

    [Fact]
    public void A_named_destination_in_the_dests_dictionary_resolves_its_page()
    {
        var document = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R/Outlines 5 0 R/Dests 7 0 R>>",
            "<</Type/Pages/Count 2/Kids[3 0 R 4 0 R]>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 100 100]>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 100 100]>>",
            "<</Type/Outlines/First 6 0 R/Last 6 0 R/Count 1>>",
            "<</Title(Legacy)/Parent 5 0 R/Dest/legacy>>",
            "<</legacy 8 0 R>>",
            "[3 0 R/FitBH 120]",
        ]);

        var outline = PdfOutline.Read(document);

        var entry = Assert.Single(outline.Entries);

        Assert.Equal("Legacy", entry.Title);
        Assert.Equal(1, entry.PageNumber);
        Assert.Equal("legacy", entry.Destination!.Name);
        Assert.Equal("FitBH", entry.Destination.Fit);
        Assert.Equal(120, entry.Destination.Top);
    }

    [Fact]
    public void An_entry_with_a_goto_action_reads_its_destination()
    {
        var bodies = TwoPages();
        bodies.AddRange(
        [
            "<</Type/Outlines/First 6 0 R/Last 6 0 R/Count 1>>",
            "<</Title(Jump)/Parent 5 0 R/A<</S/GoTo/D[4 0 R/XYZ 5 6 2]>>>>",
        ]);

        var entry = Assert.Single(PdfOutline.Read(PdfTestDocument.Document(bodies)).Entries);

        Assert.Equal(2, entry.PageNumber);
        Assert.Equal("XYZ", entry.Destination!.Fit);
        Assert.Equal(5, entry.Destination.Left);
        Assert.Equal(2, entry.Destination.Zoom);
    }

    [Fact]
    public void An_entry_whose_page_cannot_be_resolved_still_reads()
    {
        var bodies = TwoPages();
        bodies.AddRange(
        [
            "<</Type/Outlines/First 6 0 R/Last 8 0 R/Count 3>>",
            "<</Title(Gone)/Parent 5 0 R/Next 7 0 R/Dest[99 0 R/Fit]>>",
            "<</Title(Index)/Parent 5 0 R/Prev 6 0 R/Next 8 0 R/Dest[1/Fit]>>",
            "<</Title(None)/Parent 5 0 R/Prev 7 0 R>>",
        ]);

        var entries = PdfOutline.Read(PdfTestDocument.Document(bodies)).Entries;

        Assert.Null(entries[0].PageNumber);
        Assert.Equal("Fit", entries[0].Destination!.Fit);

        // A destination may name a page by its zero-based index instead of a reference.
        Assert.Equal(2, entries[1].PageNumber);
        Assert.Null(entries[1].Destination!.Left);

        Assert.Null(entries[2].PageNumber);
        Assert.Null(entries[2].Destination);
    }

    [Fact]
    public void A_broken_outline_reads_the_entries_it_can()
    {
        var bodies = TwoPages();
        bodies.AddRange(
        [
            "<</Type/Outlines/First 6 0 R>>",
            // /Next points back at the item itself, and the named destination names nothing.
            "<</Title(Twisted)/Parent 5 0 R/Next 6 0 R/Dest(Twisted)>>",
        ]);

        var entry = Assert.Single(PdfOutline.Read(PdfTestDocument.Document(bodies)).Entries);

        Assert.Equal("Twisted", entry.Title);
        Assert.Null(entry.PageNumber);

        var destination = entry.Destination!;
        Assert.Equal("Twisted", destination.Name);
        Assert.Equal(string.Empty, destination.Fit);

        // An /Outlines dictionary whose /First is not a dictionary at all is a document without a
        // plan, not a failure.
        var empty = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R/Outlines 4 0 R>>",
            "<</Type/Pages/Count 1/Kids[3 0 R]>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 100 100]>>",
            "<</Type/Outlines/First 5 0 R/Count 1>>",
            "42",
        ]);

        Assert.Empty(PdfOutline.Read(empty).Entries);
    }

    [Fact]
    public void Every_shape_of_destination_reads_as_what_it_states()
    {
        var bodies = TwoPages();
        bodies.AddRange(
        [
            "<</Type/Outlines/First 6 0 R/Last 11 0 R/Count 6>>",
            "<</Title(Dictionary)/Parent 5 0 R/Next 7 0 R/Dest<</D[3 0 R/FitV 55]>>>>",
            "<</Title(Empty)/Parent 5 0 R/Prev 6 0 R/Next 8 0 R/Dest[]>>",
            "<</Title(Absent)/Parent 5 0 R/Prev 7 0 R/Next 9 0 R/Dest/absent>>",
            "<</Title(Odd)/Parent 5 0 R/Prev 8 0 R/Next 10 0 R/Dest 5>>",
            "<</Title(WithoutAD)/Parent 5 0 R/Prev 9 0 R/Next 11 0 R/Dest<</X 1>>>>",
            "<</Title(NoPage)/Parent 5 0 R/Prev 10 0 R/Dest[/Fit]>>",
        ]);

        var entries = PdfOutline.Read(PdfTestDocument.Document(bodies)).Entries;

        Assert.Equal(1, entries[0].PageNumber);
        var dictionary = entries[0].Destination!;
        Assert.Equal("FitV", dictionary.Fit);
        Assert.Equal(55, dictionary.Left);

        Assert.Null(entries[1].PageNumber);
        var empty = entries[1].Destination!;
        Assert.Equal(string.Empty, empty.Fit);

        Assert.Null(entries[2].PageNumber);
        Assert.Equal("absent", entries[2].Destination!.Name);

        Assert.Null(entries[3].Destination);
        Assert.Null(entries[4].Destination);
        Assert.Null(entries[5].PageNumber);
    }

    [Fact]
    public void An_outline_deeper_than_the_reader_can_trust_is_cut()
    {
        const int Levels = 34;
        var bodies = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R/Outlines 5 0 R>>",
            "<</Type/Pages/Count 1/Kids[3 0 R]>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 100 100]>>",
            "null",
            "<</Type/Outlines/First 6 0 R/Last 6 0 R/Count 1>>",
        };

        for (var level = 0; level < Levels; level++)
        {
            var number = 6 + level;
            var parent = level == 0 ? 5 : number - 1;

            bodies.Add(level + 1 < Levels
                ? $"<</Title(L{level})/Parent {parent} 0 R/First {number + 1} 0 R/Last {number + 1} 0 R/Count 1>>"
                : $"<</Title(L{level})/Parent {parent} 0 R/Dest[3 0 R/Fit]>>");
        }

        var entries = PdfOutline.Read(PdfTestDocument.Document(bodies)).Entries;

        var depth = 0;
        var current = entries;
        while (current.Count > 0)
        {
            depth++;
            current = current[0].Children;
        }

        Assert.Equal(33, depth);
    }

    [Fact]
    public void A_written_plan_keeps_the_fit_type_and_the_page_it_targets()
    {
        var plan = new List<PdfOutlineEntry>
        {
            new PdfOutlineEntry("Sideways", 1) { Destination = new PdfOutlineDestination(null, "FitV", 12, null, null) },
            new PdfOutlineEntry("Plain", 1),
        };

        var entries = PdfOutline.Read(PdfOutline.Write(PdfTestDocument.Minimal(), plan)).Entries;

        var sideways = entries[0].Destination!;
        Assert.Equal("FitV", sideways.Fit);
        Assert.Equal(12, sideways.Left);
        Assert.Null(sideways.Top);

        var plain = entries[1].Destination!;
        Assert.Equal("XYZ", plain.Fit);
        Assert.Null(plain.Left);
        Assert.Null(plain.Top);
        Assert.Null(plain.Zoom);
    }

    [Fact]
    public void An_outline_written_as_an_update_reads_back_identically()
    {
        var source = PdfTestDocument.Minimal();
        var plan = new List<PdfOutlineEntry>
        {
            new PdfOutlineEntry("Chapter one", 1)
            {
                IsOpen = true,
                IsBold = true,
                Destination = new PdfOutlineDestination(null, "XYZ", 0, 800, 1),
            }
                .Add(new PdfOutlineEntry("Section one", 1)
                {
                    Destination = new PdfOutlineDestination(null, "FitH", null, 500, null),
                })
                .Add(new PdfOutlineEntry("Section two", 1) { IsItalic = true }),
            new PdfOutlineEntry("Chapter two", 1)
                .Add(new PdfOutlineEntry("Section three", 1)),
        };

        var written = PdfOutline.Write(source, plan);

        // The update is appended: no byte of the document moved, which is what keeps a signature
        // over the original revision inside its own byte range.
        Assert.True(written.Length > source.Length);
        Assert.Equal(source, written.Take(source.Length).ToArray());

        var file = Open(written);
        Assert.Contains("UseOutlines", file.Catalog!["PageMode"]!.ToString());

        var read = PdfOutline.Read(written);

        AssertSamePlan(plan, read.Entries);

        var chapter = read.Entries[0].Destination!;
        Assert.Equal("XYZ", chapter.Fit);
        Assert.Equal(800, chapter.Top);

        var section = read.Entries[0].Children[0].Destination!;
        Assert.Equal("FitH", section.Fit);
        Assert.Equal(500, section.Top);
    }

    [Fact]
    public void An_outline_can_be_written_twice_over_itself()
    {
        var first = PdfOutline.Write(PdfTestDocument.Minimal(), [new PdfOutlineEntry("First plan", 1)]);
        var second = PdfOutline.Write(first, [new PdfOutlineEntry("Second plan", 1)]);

        var outline = PdfOutline.Read(second);

        var entry = Assert.Single(outline.Entries);
        Assert.Equal("Second plan", entry.Title);
        Assert.Equal(1, entry.PageNumber);
    }

    [Fact]
    public void An_empty_plan_removes_the_outline_it_replaces()
    {
        var withOutline = PdfOutline.Write(PdfTestDocument.Minimal(), [new PdfOutlineEntry("Gone", 1)]);
        var without = PdfOutline.Write(withOutline, []);

        Assert.Empty(PdfOutline.Read(without).Entries);
        Assert.NotNull(Open(without).Catalog!["Outlines"]);
    }

    [Fact]
    public void Writing_an_entry_without_a_page_is_refused()
    {
        var exception = Assert.Throws<PdfOperationException>(
            () => PdfOutline.Write(PdfTestDocument.Minimal(), [new PdfOutlineEntry("Nowhere")]));

        Assert.Contains("names no page", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Writing_an_entry_past_the_last_page_is_refused()
    {
        var exception = Assert.Throws<PdfOperationException>(
            () => PdfOutline.Write(PdfTestDocument.Minimal(), [new PdfOutlineEntry("Nowhere", 2)]));

        Assert.Contains("page 2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Bytes_that_are_not_a_pdf_carry_no_outline()
    {
        Assert.Throws<PdfOperationException>(() => PdfOutline.Read("not a document"u8));
        Assert.Throws<PdfOperationException>(() => PdfOutline.Write("not a document"u8, []));
    }

    [Fact]
    public void An_encrypted_document_is_refused_until_it_is_decrypted()
    {
        var encrypted = Fixture(Path.Combine("encryption", "qpdf-r6-user-password.pdf"));

        var read = Assert.Throws<PdfOperationException>(() => PdfOutline.Read(encrypted));
        Assert.Contains("PdfEncryption.Open", read.Message, StringComparison.Ordinal);

        Assert.Throws<PdfOperationException>(() => PdfOutline.Write(encrypted, []));
    }

    [Fact]
    public void A_document_whose_cross_reference_is_a_stream_takes_a_plan()
    {
        var source = Fixture(Path.Combine("pdfua", "xref-stream.pdf"));

        var written = PdfOutline.Write(source, [new PdfOutlineEntry("Added", 1)]);

        Assert.Equal(source, written.Take(source.Length).ToArray());
        Assert.Empty(Open(written).Problems);

        var entry = Assert.Single(PdfOutline.Read(written).Entries);
        Assert.Equal("Added", entry.Title);
        Assert.Equal(1, entry.PageNumber);
    }

    [Fact]
    public void A_document_the_compiler_produced_reads_the_plan_of_its_headings()
    {
        // a4.pdf is compiled by M3scalinum.Text.Typst from fixtures/pdfua/document.typ: one heading
        // "Rapport de conformité" with a subheading "Résultats", the parent closed. Typst writes the
        // destination of each heading as a direct /XYZ array at the heading's baseline.
        var outline = PdfOutline.Read(Fixture(Path.Combine("pdfua", "a4.pdf")));

        var chapter = Assert.Single(outline.Entries);
        Assert.Equal("Rapport de conformité", chapter.Title);
        Assert.Equal(1, chapter.PageNumber);
        Assert.False(chapter.IsOpen);
        Assert.Equal("XYZ", chapter.Destination!.Fit);
        Assert.Equal(795.19684, chapter.Destination.Top!.Value, 3);

        var section = Assert.Single(chapter.Children);
        Assert.Equal("Résultats", section.Title);
        Assert.Equal(1, section.PageNumber);
        Assert.True(section.Destination!.Top < chapter.Destination.Top);

        // The plan of the compiled document is what a plan written back is: reading it, writing it
        // and reading the result gives the same titles, pages and states.
        var rewritten = PdfOutline.Read(PdfOutline.Write(Fixture(Path.Combine("pdfua", "a4.pdf")), outline.Entries));

        AssertSamePlan(outline.Entries, rewritten.Entries);

        // a4.pdf is PDF/A-4 (header 2.0); the update adds a catalog entry and an outline and must
        // leave it as conformant as it found it.
        var report = PdfConformance.Check(
            PdfOutline.Write(Fixture(Path.Combine("pdfua", "a4.pdf")), outline.Entries),
            PdfConformanceStandard.PdfA4);

        Assert.True(
            report.IsConformant,
            string.Join("; ", report.Errors.Select(issue => $"{issue.Code} {issue.Target}: {issue.Message}")));
    }
}
