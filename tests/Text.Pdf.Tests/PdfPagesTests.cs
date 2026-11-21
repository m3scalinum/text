using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// Page operations, each verified on files the compiler produced. A hand-written fixture would prove
/// the arithmetic and nothing else: these documents carry compressed content streams, subset fonts
/// with <c>/ToUnicode</c> maps, a structure tree and inherited page attributes, which is what a
/// rebuild has to get right.
/// </summary>
public sealed class PdfPagesTests
{
    private static byte[] Fixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "pdfua", name);

        Assert.True(File.Exists(path), $"the fixture {name} is missing from {path}");

        return File.ReadAllBytes(path);
    }

    private static byte[] Ua1() => Fixture("ua1.pdf");

    private static PdfFile Open(byte[] pdf)
    {
        Assert.True(PdfFile.TryOpen(pdf, out var file), "the produced bytes were not read as a PDF");
        Assert.Empty(file!.Problems);

        return file;
    }

    private static int Rotation(PdfPage page)
        => page.Dictionary["Rotate"] is PdfNumber number ? (int)number.AsInteger : 0;

    private static double[] Box(PdfFile file, PdfPage page, string key)
    {
        var items = file.ResolveArray(page.Dictionary[key]);

        return items.Select(item => ((PdfNumber)file.Resolve(item)!).Value).ToArray();
    }

    [Fact]
    public void Merging_two_compiler_documents_keeps_every_page_in_order()
    {
        // One page of each input is rotated, so the order of the documents is visible in the file
        // rather than only in the bytes of two pages that look the same.
        var first = PdfPages.Rotate(Ua1(), 90, "1");
        var second = PdfPages.Rotate(Fixture("a2b.pdf"), 270, "2");

        var merged = PdfPages.Merge(first, second);
        var file = Open(merged);
        var pages = file.Pages();

        Assert.Equal(4, pages.Count);
        Assert.Equal(new[] { 90, 0, 0, 270 }, pages.Select(Rotation));

        var box = Box(file, pages[0], "MediaBox");
        Assert.Equal(595.28, box[2], 1);
        Assert.Equal(841.89, box[3], 1);

        var text = PdfTextProbe.Pages(merged);
        Assert.Contains("Rapport", text[0], StringComparison.Ordinal);
        Assert.Contains("Page deux", text[1], StringComparison.Ordinal);
        Assert.Contains("Rapport", text[2], StringComparison.Ordinal);
        Assert.Contains("Page deux", text[3], StringComparison.Ordinal);
    }

    [Fact]
    public void A_merged_document_is_structurally_sound_and_keeps_its_fonts()
    {
        var merged = PdfPages.Merge(Ua1(), Fixture("a2b.pdf"), Fixture("a4.pdf"));
        var file = Open(merged);

        Assert.Equal(6, file.Pages().Count);
        Assert.Equal(2, file.MajorVersion);
        Assert.Equal(0, file.MinorVersion);

        // The merged file drops the document-level tagging, which a page rebuild cannot merge, but
        // everything the page needs -- resources, embedded fonts, their /ToUnicode maps -- must pass
        // the checker's page-level rules.
        var report = PdfConformance.Check(merged, PdfConformanceStandard.PdfUa1);

        foreach (var code in new[]
                 {
                     PdfConformanceCodes.NotAPdf,
                     PdfConformanceCodes.XrefInvalid,
                     PdfConformanceCodes.TrailerInvalid,
                     PdfConformanceCodes.CatalogMissing,
                     PdfConformanceCodes.CatalogInvalid,
                     PdfConformanceCodes.PagesInvalid,
                     PdfConformanceCodes.ObjectUnreadable,
                     PdfConformanceCodes.FontNotEmbedded,
                     PdfConformanceCodes.FontWithoutToUnicode,
                 })
        {
            Assert.False(report.Has(code), $"{code} on a merged document: {report}");
        }
    }

    [Fact]
    public void The_stream_overload_merges_the_same_way()
    {
        using var first = new MemoryStream(Ua1());
        using var second = new MemoryStream(Fixture("a2b.pdf"));

        var merged = PdfPages.Merge(first, second);

        Assert.Equal(4, Open(merged).Pages().Count);
    }

    [Fact]
    public void Extracting_a_range_keeps_the_named_pages_in_document_order()
    {
        var extracted = PdfPages.Extract(Ua1(), "2-");
        var file = Open(extracted);

        Assert.Single(file.Pages());
        Assert.Contains("Page deux", PdfTextProbe.Page(extracted, 0), StringComparison.Ordinal);

        var both = PdfPages.Extract(Ua1(), "2,1");
        Assert.Equal(2, Open(both).Pages().Count);
        Assert.Contains("Rapport", PdfTextProbe.Page(both, 0), StringComparison.Ordinal);
    }

    [Fact]
    public void Extract_refuses_a_page_the_document_does_not_have()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfPages.Extract(Ua1(), "3"));
        Assert.Throws<ArgumentException>(() => PdfPages.Extract(Ua1(), "five"));
    }

    [Fact]
    public void Splitting_produces_one_document_per_part()
    {
        var parts = PdfPages.Split(Ua1(), 1);

        Assert.Equal(2, parts.Count);
        Assert.Single(Open(parts[0]).Pages());
        Assert.Single(Open(parts[1]).Pages());
        Assert.Contains("Rapport", PdfTextProbe.Page(parts[0], 0), StringComparison.Ordinal);
        Assert.Contains("Page deux", PdfTextProbe.Page(parts[1], 0), StringComparison.Ordinal);

        Assert.Single(PdfPages.Split(Ua1(), 5));
    }

    [Fact]
    public void Rotating_sets_the_value_a_reader_reads()
    {
        var rotated = PdfPages.Rotate(Ua1(), 90);
        var pages = Open(rotated).Pages();

        Assert.All(pages, page => Assert.Equal(90, Rotation(page)));

        var one = Open(PdfPages.Rotate(Ua1(), 270, "2")).Pages();
        Assert.Equal(0, Rotation(one[0]));
        Assert.Equal(270, Rotation(one[1]));

        var twice = Open(PdfPages.Rotate(PdfPages.Rotate(Ua1(), 90, "1"), 90, "1")).Pages();
        Assert.Equal(180, Rotation(twice[0]));
    }

    [Fact]
    public void Rotating_keeps_the_text_and_refuses_an_angle_that_is_not_a_quarter_turn()
    {
        var rotated = PdfPages.Rotate(Ua1(), 180);
        var before = PdfTextProbe.Pages(Ua1());
        var after = PdfTextProbe.Pages(rotated);

        Assert.Equal(before.Count, after.Count);

        for (var index = 0; index < before.Count; index++)
        {
            Assert.Equal(before[index], after[index]);
        }

        Assert.Throws<ArgumentException>(() => PdfPages.Rotate(Ua1(), 45));
    }

    [Fact]
    public void Removing_a_page_keeps_the_other_one()
    {
        var kept = Open(PdfPages.Remove(Ua1(), "1")).Pages();

        Assert.Single(kept);
        Assert.Contains("Page deux", PdfTextProbe.Page(PdfPages.Remove(Ua1(), "1"), 0), StringComparison.Ordinal);
    }

    [Fact]
    public void Removing_every_page_is_refused()
    {
        Assert.Throws<PdfOperationException>(() => PdfPages.Remove(Ua1(), "1-2"));
    }

    [Fact]
    public void An_encrypted_document_is_refused_by_every_operation()
    {
        var encrypted = PdfTestDocument.Document(
            [
                "<</Type/Catalog/Pages 2 0 R>>",
                "<</Type/Pages/Count 1/Kids[3 0 R]>>",
                "<</Type/Page/Parent 2 0 R/MediaBox[0 0 100 100]>>",
            ],
            "/Encrypt<</Filter/Standard/V 1/R 2/O<00>/U<00>/P -1>>");

        var operation = Assert.Throws<PdfOperationException>(() => PdfPages.Extract(encrypted, "1"));
        Assert.Contains("encrypted", operation.Message, StringComparison.Ordinal);
        Assert.Throws<PdfOperationException>(() => PdfPages.Merge(encrypted));
        Assert.Throws<PdfOperationException>(() => PdfPages.Rotate(encrypted, 90));
    }

    [Fact]
    public void Bytes_that_are_not_a_PDF_are_refused()
    {
        Assert.Throws<PdfOperationException>(() => PdfPages.Merge(Encoding.ASCII.GetBytes("not a pdf")));
        Assert.Throws<PdfOperationException>(() => PdfPages.Extract(Encoding.ASCII.GetBytes("not a pdf"), "1"));
        Assert.Throws<ArgumentException>(() => PdfPages.Merge(Array.Empty<byte[]>()));
    }
}
