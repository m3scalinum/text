using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Xunit;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// Every check is exercised twice: on a document the real compiler produced, and on that same
/// document with one property mutated. The fixtures are real Typst output, not hand-written PDFs, so
/// the parser meets compressed streams, object graphs and the structure tree a producer really
/// writes. Mutations keep the byte length, so the cross-reference stays valid and the failure can
/// only come from the rule under test.
/// </summary>
public sealed class PdfConformanceTests
{
    private static byte[] Fixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "pdfua", name);

        Assert.True(File.Exists(path), $"the fixture {name} is missing from {path}");

        return File.ReadAllBytes(path);
    }

    private static byte[] Ua1() => Fixture("ua1.pdf");

    private static PdfConformanceIssue TheOnlyIssue(PdfConformanceReport report, string code)
    {
        var issue = Assert.Single(report.Issues);
        Assert.Equal(code, issue.Code);

        return issue;
    }

    private static PdfConformanceIssue IssueWith(PdfConformanceReport report, string code)
    {
        var issues = report.Issues.Where(issue => string.Equals(issue.Code, code, StringComparison.Ordinal)).ToList();

        Assert.True(issues.Count == 1, $"expected one {code}, found {issues.Count} in: {report}");

        return issues[0];
    }

    [Fact]
    public void A_PDF_UA_1_document_produced_by_the_compiler_conforms()
    {
        var report = PdfConformance.Check(Ua1(), PdfConformanceStandard.PdfUa1);

        Assert.True(report.IsConformant, report.ToString());
        Assert.Empty(report.Issues);
        Assert.True(report.Version.HasValue);
        Assert.Equal(new PdfVersion(1, 7), report.Version.GetValueOrDefault());
        Assert.Equal(PdfConformanceStandard.PdfUa1, report.Standard);
    }

    [Fact]
    public void A_PDF_A_2b_document_produced_by_the_compiler_conforms()
    {
        var report = PdfConformance.Check(Fixture("a2b.pdf"), PdfConformanceStandard.PdfA2b);

        Assert.True(report.IsConformant, report.ToString());
        Assert.Empty(report.Issues);
        Assert.True(report.Version.HasValue);
        Assert.Equal(new PdfVersion(1, 7), report.Version.GetValueOrDefault());
    }

    [Fact]
    public void A_PDF_A_4_document_produced_by_the_compiler_conforms()
    {
        var report = PdfConformance.Check(Fixture("a4.pdf"), PdfConformanceStandard.PdfA4);

        Assert.True(report.IsConformant, report.ToString());
        Assert.Empty(report.Issues);
        Assert.True(report.Version.HasValue);
        Assert.Equal(new PdfVersion(2, 0), report.Version.GetValueOrDefault());
    }

    [Fact]
    public void A_plain_PDF_claim_is_satisfied_without_accessibility_structure()
    {
        // The same bytes, claimed as PDF 1.7: no structure, language or alt text is required.
        var report = PdfConformance.Check(Fixture("a2b.pdf"), PdfConformanceStandard.Pdf17);

        Assert.True(report.IsConformant, report.ToString());
    }

    [Fact]
    public void A_signed_document_still_conforms()
    {
        // Signing appends an incremental update: a second trailer, /Prev, and a rewritten catalog.
        // The conformance check follows the cross-reference chain, so this guards that too.
        var signed = PdfSigner.Sign(Ua1(), Certificate());
        var report = PdfConformance.Check(signed, PdfConformanceStandard.PdfUa1);

        Assert.True(report.IsConformant, report.ToString());
        Assert.Empty(report.Issues);
    }

    [Fact]
    public void A_PDF_2_0_whose_cross_reference_is_a_stream_is_read()
    {
        // Not a conformance fixture: the smallest PDF 2.0 file that puts its entries in a
        // cross-reference stream, so the reader path a PDF 2.0 producer may take is exercised.
        var report = PdfConformance.Check(Fixture("xref-stream.pdf"), PdfConformanceStandard.Pdf20);

        Assert.True(report.IsConformant, report.ToString());
        Assert.Equal(new PdfVersion(2, 0), report.Version.GetValueOrDefault());
    }

    [Fact]
    public void A_PDF_2_0_whose_objects_live_in_an_object_stream_is_read()
    {
        var report = PdfConformance.Check(Fixture("objstm.pdf"), PdfConformanceStandard.Pdf20);

        Assert.True(report.IsConformant, report.ToString());
        Assert.Empty(report.Issues);
    }

    [Fact]
    public void A_minimal_PDF_2_0_is_not_PDF_UA_1()
    {
        // The same bytes claimed as PDF/UA-1: the parser read the file, but nothing the standard
        // requires is declared, which is the point of not accepting a minimal PDF as a positive.
        var report = PdfConformance.Check(Fixture("xref-stream.pdf"), PdfConformanceStandard.PdfUa1);

        Assert.False(report.IsConformant);
        Assert.True(report.Has(PdfConformanceCodes.MarkInfoMissing));
        Assert.True(report.Has(PdfConformanceCodes.StructTreeMissing));
        Assert.True(report.Has(PdfConformanceCodes.LanguageMissing));
    }

    [Fact]
    public void A_PDF_UA_1_document_claimed_as_PDF_A_2b_is_reported_without_an_XMP_identifier()
    {
        var report = PdfConformance.Check(Ua1(), PdfConformanceStandard.PdfA2b);

        var issue = TheOnlyIssue(report, PdfConformanceCodes.XmpStandardMismatch);

        Assert.Equal(PdfConformanceSeverity.Error, issue.Severity);
        Assert.Equal("xmp", issue.Target);
        Assert.False(report.IsConformant);
    }

    [Fact]
    public void Empty_input_is_reported_as_not_a_pdf()
    {
        var issue = TheOnlyIssue(PdfConformance.Check([], PdfConformanceStandard.PdfUa1), PdfConformanceCodes.NotAPdf);

        Assert.Equal("bytes", issue.Target);
        Assert.Null(PdfConformance.Check([], PdfConformanceStandard.PdfUa1).Version);
    }

    [Fact]
    public void Text_input_is_reported_as_not_a_pdf()
    {
        var report = PdfConformance.Check(Encoding.ASCII.GetBytes("hello, this is not a PDF"), PdfConformanceStandard.PdfUa1);

        TheOnlyIssue(report, PdfConformanceCodes.NotAPdf);
        Assert.False(report.IsConformant);
    }

    [Fact]
    public void A_startxref_that_points_one_byte_off_is_a_cross_reference_error()
    {
        var report = PdfConformance.Check(WithStartXrefOffset(Ua1(), 1), PdfConformanceStandard.PdfUa1);

        IssueWith(report, PdfConformanceCodes.XrefInvalid);
        Assert.False(report.IsConformant);
    }

    [Fact]
    public void A_cross_reference_entry_that_points_one_byte_off_is_a_cross_reference_error()
    {
        // The first in-use entry of the table, found by its fixed-width layout.
        var text = Encoding.Latin1.GetString(Ua1());
        var index = text.IndexOf("00000 n", StringComparison.Ordinal);
        Assert.True(index > 10);

        var offsetStart = index - 10;
        var offset = int.Parse(text.AsSpan(offsetStart, 10), CultureInfo.InvariantCulture) + 1;
        var shifted = offset.ToString("D10", CultureInfo.InvariantCulture);
        var mutated = string.Concat(text.AsSpan(0, offsetStart), shifted, text.AsSpan(offsetStart + 10));

        var report = PdfConformance.Check(Encoding.Latin1.GetBytes(mutated), PdfConformanceStandard.PdfUa1);

        IssueWith(report, PdfConformanceCodes.XrefInvalid);
    }

    [Fact]
    public void A_size_that_does_not_cover_the_objects_is_a_trailer_error()
    {
        var issue = TheOnlyIssue(
            PdfConformance.Check(Replace(Ua1(), "/Size 62", "/Size 10"), PdfConformanceStandard.PdfUa1),
            PdfConformanceCodes.TrailerInvalid);

        Assert.Equal("trailer", issue.Target);
    }

    [Fact]
    public void A_trailer_without_a_root_is_a_missing_catalog()
    {
        var issue = TheOnlyIssue(
            PdfConformance.Check(Replace(Ua1(), "/Root 61 0 R", "/RooX 61 0 R"), PdfConformanceStandard.PdfUa1),
            PdfConformanceCodes.CatalogMissing);

        Assert.Equal("trailer", issue.Target);
    }

    [Fact]
    public void A_root_that_is_not_a_catalog_is_reported()
    {
        var issue = TheOnlyIssue(
            PdfConformance.Check(Replace(Ua1(), "/Type/Catalog", "/Type/Catalox"), PdfConformanceStandard.PdfUa1),
            PdfConformanceCodes.CatalogInvalid);

        Assert.Equal("catalog", issue.Target);
    }

    [Fact]
    public void A_page_tree_whose_count_disagrees_with_its_pages_is_reported()
    {
        var issue = TheOnlyIssue(
            PdfConformance.Check(Replace(Ua1(), "/Type/Pages/Count 2", "/Type/Pages/Count 0"), PdfConformanceStandard.PdfUa1),
            PdfConformanceCodes.PagesInvalid);

        Assert.Equal("page tree", issue.Target);
    }

    [Fact]
    public void An_encrypted_trailer_is_reported_and_stops_the_checks()
    {
        var issue = TheOnlyIssue(
            PdfConformance.Check(Replace(Ua1(), "/Info 59 0 R", "/Encrypt /Ey"), PdfConformanceStandard.PdfUa1),
            PdfConformanceCodes.Encrypted);

        Assert.Equal("trailer", issue.Target);
    }

    [Fact]
    public void A_version_below_the_claimed_standard_is_reported()
    {
        var issue = TheOnlyIssue(
            PdfConformance.Check(Replace(Ua1(), "%PDF-1.7", "%PDF-1.3"), PdfConformanceStandard.PdfUa1),
            PdfConformanceCodes.VersionMismatch);

        Assert.Equal("header", issue.Target);
        Assert.Contains("1.3", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_PDF_A_2b_document_claimed_as_PDF_A_1b_is_reported_for_its_version_and_identifier()
    {
        var report = PdfConformance.Check(Fixture("a2b.pdf"), PdfConformanceStandard.PdfA1b);

        IssueWith(report, PdfConformanceCodes.VersionMismatch);
        IssueWith(report, PdfConformanceCodes.XmpStandardMismatch);
        Assert.False(report.IsConformant);
    }

    [Fact]
    public void A_missing_mark_info_is_reported()
    {
        var issue = TheOnlyIssue(
            PdfConformance.Check(
                Replace(Ua1(), "/MarkInfo<</Marked true", "/MarkInfx<</Marked true"),
                PdfConformanceStandard.PdfUa1),
            PdfConformanceCodes.MarkInfoMissing);

        Assert.Equal("catalog", issue.Target);
    }

    [Fact]
    public void A_missing_structure_tree_is_reported()
    {
        var issue = TheOnlyIssue(
            PdfConformance.Check(
                Replace(Ua1(), "/StructTreeRoot 5 0 R", new string(' ', 21)),
                PdfConformanceStandard.PdfUa1),
            PdfConformanceCodes.StructTreeMissing);

        Assert.Equal("catalog", issue.Target);
    }

    [Fact]
    public void A_structure_element_whose_parent_disagrees_with_the_tree_is_reported()
    {
        var issue = TheOnlyIssue(
            PdfConformance.Check(
                Replace(Ua1(), "/S/Document/P 5 0 R", "/S/Document/P 6 0 R"),
                PdfConformanceStandard.PdfUa1),
            PdfConformanceCodes.StructTreeInconsistent);

        Assert.Equal("obj 33", issue.Target);
    }

    [Fact]
    public void A_page_whose_struct_parents_the_root_does_not_index_is_reported()
    {
        var issue = TheOnlyIssue(
            PdfConformance.Check(Replace(Ua1(), "/StructParents 1", "/StructParents 3"), PdfConformanceStandard.PdfUa1),
            PdfConformanceCodes.StructTreeInconsistent);

        Assert.Equal("page 47", issue.Target);
    }

    [Fact]
    public void A_missing_language_is_reported()
    {
        var issue = TheOnlyIssue(
            PdfConformance.Check(Replace(Ua1(), "/Lang(fr)", "/Lanx(fr)"), PdfConformanceStandard.PdfUa1),
            PdfConformanceCodes.LanguageMissing);

        Assert.Equal("catalog", issue.Target);
    }

    [Fact]
    public void A_missing_title_is_reported()
    {
        var pdf = Replace(Replace(Ua1(), "/Title(", "/Titlx("), "<dc:title>", "<dx:title>");
        var issue = TheOnlyIssue(PdfConformance.Check(pdf, PdfConformanceStandard.PdfUa1), PdfConformanceCodes.TitleMissing);

        Assert.Equal("document information", issue.Target);
    }

    [Fact]
    public void A_figure_without_alternate_text_is_reported()
    {
        var issue = TheOnlyIssue(
            PdfConformance.Check(
                Replace(Ua1(), "/S/Figure/P 13 0 R/Alt<", "/S/Figure/P 13 0 R/Alx<"),
                PdfConformanceStandard.PdfUa1),
            PdfConformanceCodes.FigureAlternateTextMissing);

        Assert.Equal("obj 12", issue.Target);
    }

    [Fact]
    public void An_image_without_a_figure_is_a_warning_not_an_error()
    {
        var report = PdfConformance.Check(Replace(Ua1(), "/S/Figure", "/S/Divvvv"), PdfConformanceStandard.PdfUa1);

        var issue = TheOnlyIssue(report, PdfConformanceCodes.ImageNotStructured);

        Assert.Equal(PdfConformanceSeverity.Warning, issue.Severity);
        Assert.True(report.IsConformant, report.ToString());
        Assert.Single(report.Warnings);
        Assert.Empty(report.Errors);
    }

    [Fact]
    public void A_font_without_an_embedded_program_is_reported()
    {
        var report = PdfConformance.Check(
            ReplaceAll(Ua1(), "/FontFile3", "/FontFilx3"),
            PdfConformanceStandard.PdfUa1);

        var issues = report.Issues.Where(issue => issue.Code == PdfConformanceCodes.FontNotEmbedded).ToList();

        Assert.Equal(2, issues.Count);
        Assert.All(issues, issue => Assert.StartsWith("obj ", issue.Target, StringComparison.Ordinal));
        Assert.False(report.IsConformant);
    }

    [Fact]
    public void A_font_without_a_to_unicode_cmap_is_reported()
    {
        var report = PdfConformance.Check(
            ReplaceAll(Ua1(), "/ToUnicode", "/ToUnicodx"),
            PdfConformanceStandard.PdfUa1);

        var issues = report.Issues.Where(issue => issue.Code == PdfConformanceCodes.FontWithoutToUnicode).ToList();

        Assert.Equal(2, issues.Count);
        Assert.False(report.IsConformant);
    }

    [Fact]
    public void A_wrong_pdfuaid_part_is_reported()
    {
        var issue = TheOnlyIssue(
            PdfConformance.Check(Replace(Ua1(), "<pdfuaid:part>1", "<pdfuaid:part>2"), PdfConformanceStandard.PdfUa1),
            PdfConformanceCodes.XmpStandardMismatch);

        Assert.Equal("xmp", issue.Target);
    }

    [Fact]
    public void A_missing_metadata_stream_is_reported()
    {
        var issue = TheOnlyIssue(
            PdfConformance.Check(Replace(Ua1(), "/Metadata 60 0 R", new string(' ', 16)), PdfConformanceStandard.PdfUa1),
            PdfConformanceCodes.MetadataMissing);

        Assert.Equal("catalog", issue.Target);
    }

    [Fact]
    public void A_wrong_pdfaid_conformance_is_reported()
    {
        var report = PdfConformance.Check(
            Replace(Fixture("a2b.pdf"), ">B</pdfaid:conformance>", ">U</pdfaid:conformance>"),
            PdfConformanceStandard.PdfA2b);

        TheOnlyIssue(report, PdfConformanceCodes.XmpStandardMismatch);
    }

    [Fact]
    public void Assert_throws_with_the_report_when_the_document_does_not_conform()
    {
        var exception = Assert.Throws<PdfConformanceException>(
            () => PdfConformance.Assert(Replace(Ua1(), "/Lang(fr)", "/Lanx(fr)"), PdfConformanceStandard.PdfUa1));

        Assert.Contains(PdfConformanceCodes.LanguageMissing, exception.Report.Issues.Select(issue => issue.Code));
        Assert.Contains("catalog", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Assert_returns_when_the_document_conforms()
    {
        PdfConformance.Assert(Ua1(), PdfConformanceStandard.PdfUa1);
    }

    [Fact]
    public void Every_issue_carries_a_code_a_target_and_a_sentence()
    {
        var report = PdfConformance.Check(Replace(Ua1(), "/Type/Catalog", "/Type/Catalox"), PdfConformanceStandard.PdfUa1);

        Assert.NotEmpty(report.Issues);
        Assert.All(report.Issues, issue =>
        {
            Assert.False(string.IsNullOrWhiteSpace(issue.Code));
            Assert.False(string.IsNullOrWhiteSpace(issue.Target));
            Assert.False(string.IsNullOrWhiteSpace(issue.Message));
            Assert.True(issue.Severity is PdfConformanceSeverity.Error or PdfConformanceSeverity.Warning);
        });
    }

    /// <summary>Replaces one occurrence, asserting that the fixture still holds exactly one.</summary>
    private static byte[] Replace(byte[] pdf, string from, string to)
    {
        Assert.Equal(from.Length, to.Length);

        var text = Encoding.Latin1.GetString(pdf);
        var first = text.IndexOf(from, StringComparison.Ordinal);
        Assert.True(first >= 0, $"the fixture no longer contains '{from}'");
        Assert.Equal(first, text.LastIndexOf(from, StringComparison.Ordinal));

        return Encoding.Latin1.GetBytes(string.Concat(text.AsSpan(0, first), to, text.AsSpan(first + from.Length)));
    }

    /// <summary>Replaces every occurrence, for a property each font carries.</summary>
    private static byte[] ReplaceAll(byte[] pdf, string from, string to)
    {
        Assert.Equal(from.Length, to.Length);

        var text = Encoding.Latin1.GetString(pdf);
        Assert.Contains(from, text, StringComparison.Ordinal);

        return Encoding.Latin1.GetBytes(text.Replace(from, to, StringComparison.Ordinal));
    }

    private static byte[] WithStartXrefOffset(byte[] pdf, int delta)
    {
        var text = Encoding.Latin1.GetString(pdf);
        var start = text.LastIndexOf("startxref", StringComparison.Ordinal) + "startxref".Length;

        while (start < text.Length && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        var end = start;
        while (end < text.Length && char.IsAsciiDigit(text[end]))
        {
            end++;
        }

        var value = int.Parse(text[start..end], CultureInfo.InvariantCulture) + delta;
        var digits = value.ToString(CultureInfo.InvariantCulture).PadLeft(end - start, '0');

        return Encoding.Latin1.GetBytes(string.Concat(text.AsSpan(0, start), digits, text.AsSpan(end)));
    }

    private static X509Certificate2 Certificate()
    {
        using var key = RSA.Create(2048);

        var request = new CertificateRequest(
            "CN=M3scalinum.Text.Pdf Conformance Test, O=M3scalinum.Text.Pdf, C=FR",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        using var created = request.CreateSelfSigned(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2036, 1, 1, 0, 0, 0, TimeSpan.Zero));

        return X509CertificateLoader.LoadPkcs12(
            created.Export(X509ContentType.Pkcs12),
            password: null,
            X509KeyStorageFlags.Exportable);
    }
}
