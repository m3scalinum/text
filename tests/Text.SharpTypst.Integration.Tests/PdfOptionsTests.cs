using System;
using System.Text;
using System.Text.RegularExpressions;
using M3scalinum.Text.SharpTypst.Compilation;
using M3scalinum.Text.SharpTypst.Export;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Integration.Tests;

/// <summary>PDF export options: standards, page ranges, metadata.</summary>
[Trait("Category", "Integration")]
public sealed partial class PdfOptionsTests
{
    private const string ThreePages = """
        = One

        #pagebreak()

        = Two

        #pagebreak()

        = Three
        """;

    private static CompilationResult Compile(string markup)
    {
        var world = new TypstWorldBuilder().AddSource("main.typ", markup).Build();
        return new TypstCompiler().Compile(world);
    }

    private static int CountPages(byte[] pdf)
        => PageObject().Count(Encoding.Latin1.GetString(pdf));

    [GeneratedRegex(@"/Type\s*/Page(?![A-Za-z])")]
    private static partial Regex PageObject();

    [Fact]
    public void The_default_export_keeps_every_page()
    {
        using var result = Compile(ThreePages);

        Assert.Equal(3, CountPages(result.ExportPdf()));
    }

    [Fact]
    public void A_page_range_exports_only_that_page()
    {
        using var result = Compile(ThreePages);

        var pdf = result.ExportPdf(new PdfExportOptions { PageRanges = "2", Tagged = false });

        Assert.Equal(1, CountPages(pdf));
    }

    [Fact]
    public void A_page_range_accepts_a_span_and_a_list()
    {
        using var result = Compile(ThreePages);

        Assert.Equal(2, CountPages(result.ExportPdf(new PdfExportOptions { PageRanges = "1-2", Tagged = false })));
        Assert.Equal(2, CountPages(result.ExportPdf(new PdfExportOptions { PageRanges = "1,3", Tagged = false })));
        Assert.Equal(2, CountPages(result.ExportPdf(new PdfExportOptions { PageRanges = "2-", Tagged = false })));
    }

    [Fact]
    public void Pdf_a_2b_writes_archival_metadata()
    {
        using var result = Compile(ThreePages);

        var pdf = result.ExportPdf(new PdfExportOptions
        {
            Standards = [PdfStandard.PdfA2b],
            Timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        });

        Assert.Contains("pdfaid", Encoding.Latin1.GetString(pdf), StringComparison.Ordinal);
    }

    [Fact]
    public void Pdf_ua_1_writes_accessibility_metadata()
    {
        // PDF/UA requires a document title, so the markup has to declare one.
        using var result = Compile("""
            #set document(title: "Accessible report")

            = One
            """);

        var pdf = result.ExportPdf(new PdfExportOptions { Standards = [PdfStandard.PdfUa1] });

        Assert.Contains("pdfuaid", Encoding.Latin1.GetString(pdf), StringComparison.Ordinal);
    }

    [Fact]
    public void Pdf_a_without_a_date_is_rejected()
    {
        using var result = Compile(ThreePages);

        Assert.Throws<TypstExportException>(
            () => result.ExportPdf(new PdfExportOptions { Standards = [PdfStandard.PdfA2b] }));
    }

    [Fact]
    public void A_page_range_with_tagging_is_rejected()
    {
        using var result = Compile(ThreePages);

        Assert.Throws<TypstExportException>(
            () => result.ExportPdf(new PdfExportOptions { PageRanges = "1", Tagged = true }));
    }

    [Fact]
    public void An_untagged_export_is_smaller_than_a_tagged_one()
    {
        using var result = Compile(ThreePages);

        var tagged = result.ExportPdf(new PdfExportOptions { Tagged = true });
        var untagged = result.ExportPdf(new PdfExportOptions { Tagged = false });

        Assert.True(untagged.Length < tagged.Length, $"untagged {untagged.Length} >= tagged {tagged.Length}");
    }

    [Fact]
    public void A_creator_is_written_into_the_metadata()
    {
        using var result = Compile(ThreePages);

        var pdf = result.ExportPdf(new PdfExportOptions { Creator = "M3scalinum.Text.SharpTypst tests" });

        Assert.Contains("M3scalinum.Text.SharpTypst tests", Encoding.Latin1.GetString(pdf), StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_page_range_is_rejected()
    {
        using var result = Compile(ThreePages);

        Assert.Throws<TypstNativeException>(
            () => result.ExportPdf(new PdfExportOptions { PageRanges = "zero" }));
    }

    [Fact]
    public void An_incompatible_standard_combination_is_rejected()
    {
        using var result = Compile(ThreePages);

        // PDF/A-1b is PDF 1.4; asking for PDF 2.0 as well cannot be satisfied.
        Assert.Throws<TypstNativeException>(
            () => result.ExportPdf(new PdfExportOptions
            {
                Standards = [PdfStandard.PdfA1b, PdfStandard.Pdf20],
            }));
    }
}
