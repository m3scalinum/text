using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using M3scalinum.Text.Pdf;
using M3scalinum.Text.Typst.Compilation;
using M3scalinum.Text.Typst.Export;
using Xunit;

namespace M3scalinum.Text.Typst.Integration.Tests;

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

        var pdf = result.ExportPdf(new PdfExportOptions { Creator = "M3scalinum.Text.Typst tests" });

        Assert.Contains("M3scalinum.Text.Typst tests", Encoding.Latin1.GetString(pdf), StringComparison.Ordinal);
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

    [Fact]
    public void Pdf_a_4f_with_an_embedded_file_is_conformant_and_keeps_the_attachment()
    {
        var payload = Encoding.UTF8.GetBytes("reading,value\noxygen,21\n");

        var world = new TypstWorldBuilder()
            .AddSource("main.typ", """
                #set document(title: "Archived with its data")

                #pdf.attach(
                  "readings.csv",
                  relationship: "supplement",
                  mime-type: "text/csv",
                  description: "Raw readings",
                )

                = One
                """)
            .AddAsset("readings.csv", payload)
            .Build();

        using var result = new TypstCompiler().Compile(world);

        var pdf = result.ExportPdf(new PdfExportOptions
        {
            Standards = [PdfStandard.PdfA4f],
            Timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        });

        Assert.StartsWith("%PDF-2.0", Encoding.Latin1.GetString(pdf), StringComparison.Ordinal);

        var report = PdfConformance.Check(pdf, PdfConformanceStandard.PdfA4f);
        Assert.True(
            report.IsConformant,
            string.Join("; ", report.Errors.Select(issue => $"{issue.Code} at {issue.Target}: {issue.Message}")));

        var attachment = Assert.Single(PdfAttachments.Read(pdf));
        Assert.Equal("readings.csv", attachment.Name);
        Assert.Equal(PdfAttachmentRole.Supplement, attachment.Role);
        Assert.Equal(payload, attachment.Content);
    }

    [Fact]
    public void Pdf_a_4f_without_an_embedded_file_claims_the_profile_the_checker_rejects()
    {
        using var result = Compile(ThreePages);

        // Typst writes an empty /EmbeddedFiles tree for 4f rather than refusing, so the bytes claim a
        // profile the document does not meet. The checker is what says so.
        var pdf = result.ExportPdf(new PdfExportOptions
        {
            Standards = [PdfStandard.PdfA4f],
            Timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        });

        var report = PdfConformance.Check(pdf, PdfConformanceStandard.PdfA4f);

        Assert.False(report.IsConformant);
        Assert.Contains(report.Errors, issue => issue.Code == "PDFA4F.EMBEDDED_FILES_MISSING");
    }

    [Fact]
    public void Pdf_a_4e_exports_a_pdf_2_0_with_archival_metadata()
    {
        using var result = Compile(ThreePages);

        var pdf = result.ExportPdf(new PdfExportOptions
        {
            Standards = [PdfStandard.PdfA4e],
            Timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        });

        var text = Encoding.Latin1.GetString(pdf);
        Assert.StartsWith("%PDF-2.0", text, StringComparison.Ordinal);
        Assert.Contains("pdfaid", text, StringComparison.Ordinal);
    }
}
