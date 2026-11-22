using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Xunit;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// PDF/A-4f: the archival level that carries embedded files. The documents here are written by hand
/// so the name tree, the file specification and the association array are visible, which is what the
/// profile talks about; the assertions are on the produced bytes, read back through
/// <see cref="PdfAttachments"/> and <see cref="PdfConformance"/> rather than on the converter's word.
/// </summary>
public sealed class PdfA4fTests
{
    private const string InvoiceXml = "<?xml version=\"1.0\"?><Invoice>F-2026-0042</Invoice>";

    private static readonly byte[] Invoice = Encoding.UTF8.GetBytes(InvoiceXml);

    private static PdfAConversionOptions To(PdfConformanceStandard target) => new() { Target = target };

    /// <summary>
    /// A one-page document carrying one embedded file: <c>/Names /EmbeddedFiles</c>, a file
    /// specification pointing at the stream, and — when asked — the catalog's <c>/AF</c> array.
    /// </summary>
    /// <param name="relationship">The <c>/AFRelationship</c> to write, or <see langword="null"/> for none.</param>
    /// <param name="associated">Whether the catalog lists the file in <c>/AF</c>.</param>
    /// <param name="named">Whether the name tree exists at all.</param>
    /// <param name="streamRelationship">
    /// A relationship to write on the embedded file stream instead of, or as well as, on the file
    /// specification: a producer that put it there made the same statement in another place.
    /// </param>
    private static byte[] WithEmbeddedFile(
        string? relationship = "Data",
        bool associated = true,
        bool named = true,
        string? streamRelationship = null)
    {
        var catalog = new StringBuilder("<</Type/Catalog/Pages 2 0 R");
        if (named)
        {
            catalog.Append("/Names<</EmbeddedFiles 6 0 R>>");
        }

        if (associated)
        {
            catalog.Append("/AF[5 0 R]");
        }

        catalog.Append(">>");

        var specification = new StringBuilder("<</Type/Filespec/F(invoice.xml)/UF(invoice.xml)/EF<</F 4 0 R>>");
        if (relationship is not null)
        {
            specification.Append("/AFRelationship/").Append(relationship);
        }

        specification.Append(">>");

        var stream = new StringBuilder("<</Type/EmbeddedFile/Subtype/text#2Fxml");
        if (streamRelationship is not null)
        {
            stream.Append("/AFRelationship/").Append(streamRelationship);
        }

        stream.Append("/Length ").Append(Invoice.Length.ToString(CultureInfo.InvariantCulture))
            .Append(">>\nstream\n").Append(Encoding.Latin1.GetString(Invoice)).Append("\nendstream");

        var bodies = new List<string>
        {
            catalog.ToString(),
            "<</Type/Pages/Count 1/Kids[3 0 R]>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]>>",
            stream.ToString(),
            specification.ToString(),
            "<</Names[(invoice.xml) 5 0 R]>>",
        };

        return PdfTestDocument.Document(bodies);
    }

    private static string Refusals(PdfAConversionResult result)
        => string.Join(" | ", result.Refusals.Select(refusal => refusal.ToString()));

    private static string Errors(PdfAConversionResult result)
        => string.Join(" | ", result.Errors.Select(error => error.ToString()));

    [Fact]
    public void A_document_with_a_related_attachment_becomes_a_conformant_PDF_A_4f()
    {
        var result = PdfAConverter.Convert(WithEmbeddedFile(), To(PdfConformanceStandard.PdfA4f));

        Assert.True(result.HasBytes, Refusals(result));
        Assert.True(result.IsConformant, Errors(result));
        Assert.Equal(PdfConformanceStandard.PdfA4f, result.Report!.Standard);

        Assert.Contains(result.Changes, change => change.Code == PdfAConversionCodes.A4fAssociatedFiles);
        Assert.Contains(result.Changes, change => change.Code == PdfAConversionCodes.A4fEmbeddedFilesTree);

        var text = Encoding.Latin1.GetString(result.Bytes);

        // The header is PDF 2.0 and the packet declares part 4, level F: the profile, not base 4.
        Assert.StartsWith("%PDF-2.0", text, StringComparison.Ordinal);
        Assert.Contains("pdfaid:part=\"4\"", text, StringComparison.Ordinal);
        Assert.Contains("pdfaid:conformance=\"F\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_catalog_exposes_both_the_AF_array_and_the_EmbeddedFiles_name_tree()
    {
        var result = PdfAConverter.Convert(WithEmbeddedFile(), To(PdfConformanceStandard.PdfA4f));

        Assert.True(result.HasBytes, Refusals(result));
        Assert.True(PdfFile.TryOpen(result.Bytes, out var file));
        Assert.NotNull(file.Catalog);

        var associated = file.ResolveArray(file.Catalog["AF"]);
        Assert.Single(associated);

        var names = file.ResolveDictionary(file.Catalog["Names"]);
        Assert.NotNull(names);

        var tree = file.ResolveDictionary(names["EmbeddedFiles"]);
        Assert.NotNull(tree);

        // The same file is reachable through both routes: one specification, listed twice.
        var entries = file.ResolveArray(tree["Names"]);
        Assert.Equal(2, entries.Count);
        Assert.Equal(
            Assert.IsType<PdfReference>(associated[0]).Number,
            Assert.IsType<PdfReference>(entries[1]).Number);
    }

    [Fact]
    public void The_attachment_bytes_and_name_survive_the_rewrite()
    {
        var result = PdfAConverter.Convert(WithEmbeddedFile(), To(PdfConformanceStandard.PdfA4f));

        Assert.True(result.HasBytes, Refusals(result));

        var attachment = Assert.Single(PdfAttachments.Read(result.Bytes));

        Assert.Equal("invoice.xml", attachment.Name);
        Assert.Equal(PdfAttachmentRole.Data, attachment.Role);
        Assert.Equal(Invoice, attachment.Content);
        Assert.Equal(InvoiceXml.Length, attachment.Size);
    }

    [Fact]
    public void An_attachment_without_a_relationship_is_refused_by_name()
    {
        var result = PdfAConverter.Convert(WithEmbeddedFile(relationship: null), To(PdfConformanceStandard.PdfA4f));

        Assert.False(result.HasBytes);
        Assert.Null(result.Report);

        var refusal = Assert.Single(result.Refusals);

        Assert.Equal(PdfAConversionCodes.A4fAttachmentWithoutRelationship, refusal.Code);
        Assert.Contains("invoice.xml", refusal.Target, StringComparison.Ordinal);
        Assert.Contains("/AFRelationship", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_relationship_is_refused()
    {
        var result = PdfAConverter.Convert(WithEmbeddedFile(relationship: "Attached"), To(PdfConformanceStandard.PdfA4f));

        Assert.False(result.HasBytes);

        var refusal = Assert.Single(result.Refusals);

        Assert.Equal(PdfAConversionCodes.A4fRelationshipUnknown, refusal.Code);
        Assert.Contains("invoice.xml", refusal.Target, StringComparison.Ordinal);
        Assert.Contains("/Attached", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_second_class_relationship_is_accepted()
    {
        // ISO 32000-2 lets a producer register its own relationship name; C2PA's manifest one is the
        // example the PDF Association publishes. It is a name with an underscore, and not one this
        // package could enumerate, so it is accepted rather than refused.
        var result = PdfAConverter.Convert(
            WithEmbeddedFile(relationship: "C2PA_Manifest"),
            To(PdfConformanceStandard.PdfA4f));

        Assert.True(result.HasBytes, Refusals(result));
        Assert.True(result.IsConformant, Errors(result));
        Assert.Equal(PdfAttachmentRole.Unspecified, Assert.Single(PdfAttachments.Read(result.Bytes)).Role);
    }

    [Fact]
    public void A_4f_target_without_an_embedded_file_says_why()
    {
        var result = PdfAConverter.Convert(PdfTestDocument.Minimal(), To(PdfConformanceStandard.PdfA4f));

        Assert.False(result.HasBytes);

        var refusal = Assert.Single(result.Refusals);

        Assert.Equal(PdfAConversionCodes.A4fWithoutEmbeddedFile, refusal.Code);
        Assert.Contains("at least one", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("PDF/A-4", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_without_attachments_still_converts_to_base_PDF_A_4()
    {
        // Non-regression: 4f needs a file, 4 does not, and the same document must still convert.
        var result = PdfAConverter.Convert(PdfTestDocument.Minimal(), To(PdfConformanceStandard.PdfA4));

        Assert.True(result.HasBytes, Refusals(result));
        Assert.True(result.IsConformant, Errors(result));
        Assert.StartsWith("%PDF-2.0", Encoding.Latin1.GetString(result.Bytes), StringComparison.Ordinal);
        Assert.Contains("pdfaid:part=\"4\"", Encoding.Latin1.GetString(result.Bytes), StringComparison.Ordinal);
        Assert.Empty(PdfAttachments.Read(result.Bytes));
    }

    [Fact]
    public void An_embedded_file_under_base_PDF_A_4_is_refused()
    {
        // Base PDF/A-4 allows only embedded files that are themselves PDF/A. That is a conformance
        // run inside a conformance run, which this converter cannot perform, so it refuses.
        var result = PdfAConverter.Convert(WithEmbeddedFile(), To(PdfConformanceStandard.PdfA4));

        Assert.False(result.HasBytes);

        var refusal = Assert.Single(result.Refusals);

        Assert.Equal(PdfAConversionCodes.A4WithEmbeddedFiles, refusal.Code);
        Assert.Contains("invoice.xml", refusal.Target, StringComparison.Ordinal);
        Assert.Contains("PDF/A-4f", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_attachment_named_only_in_AF_gains_the_EmbeddedFiles_tree()
    {
        var result = PdfAConverter.Convert(
            WithEmbeddedFile(named: false),
            To(PdfConformanceStandard.PdfA4f));

        Assert.True(result.HasBytes, Refusals(result));
        Assert.True(result.IsConformant, Errors(result));

        Assert.True(PdfFile.TryOpen(result.Bytes, out var file));
        Assert.NotNull(file.ResolveDictionary(file.Catalog?["Names"])?["EmbeddedFiles"]);

        var attachment = Assert.Single(PdfAttachments.Read(result.Bytes));
        Assert.Equal("invoice.xml", attachment.Name);
        Assert.Equal(Invoice, attachment.Content);
    }

    [Fact]
    public void A_relationship_stated_on_the_stream_is_restated_on_the_specification()
    {
        // ISO 32000-2 puts /AFRelationship on the file specification. A producer that wrote it on
        // the embedded file stream has stated the same thing in another place, and the conversion
        // moves it to where the profile reads it rather than dropping it.
        var result = PdfAConverter.Convert(
            WithEmbeddedFile(relationship: null, streamRelationship: "Data"),
            To(PdfConformanceStandard.PdfA4f));

        Assert.True(result.HasBytes, Refusals(result));
        Assert.True(result.IsConformant, Errors(result));

        Assert.True(PdfFile.TryOpen(result.Bytes, out var file));
        var specification = file.ResolveDictionary(file.ResolveArray(file.Catalog?["AF"])[0]);

        Assert.NotNull(specification);
        Assert.Equal("Data", (file.Resolve(specification["AFRelationship"]) as PdfName)?.Value);
        Assert.Equal(PdfAttachmentRole.Data, Assert.Single(PdfAttachments.Read(result.Bytes)).Role);
    }

    [Fact]
    public void The_checker_reports_a_name_tree_entry_with_no_embedded_file_stream()
    {
        // A file specification with no /EF names an attachment that is not there: a reader offers a
        // paperclip with nothing behind it.
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R/Names<</EmbeddedFiles 4 0 R>>/AF[3 0 R]>>",
            "<</Type/Pages/Count 1/Kids[5 0 R]>>",
            "<</Type/Filespec/F(invoice.xml)/AFRelationship/Data>>",
            "<</Names[(invoice.xml) 3 0 R]>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]>>",
        ]);

        var report = PdfConformance.Check(pdf, PdfConformanceStandard.PdfA4f);

        Assert.Contains(
            report.Issues,
            issue => issue.Code == PdfConformanceCodes.AssociatedFilesInconsistent
                && issue.Message.Contains("embedded file stream", StringComparison.Ordinal));
        Assert.False(report.IsConformant);
    }

    [Fact]
    public void A_name_tree_with_branches_is_read_and_written_as_one_flat_tree()
    {
        // A producer with many attachments writes a balanced tree of /Kids nodes. The profile reads
        // the entries wherever they sit, and the conversion writes them back as one ordered tree.
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R/Names<</EmbeddedFiles 5 0 R>>/AF[3 0 R]>>",
            "<</Type/Pages/Count 1/Kids[6 0 R]>>",
            "<</Type/Filespec/F(invoice.xml)/UF(invoice.xml)/EF<</F 4 0 R>>/AFRelationship/Data>>",
            "<</Type/EmbeddedFile/Subtype/text#2Fxml/Length " + Invoice.Length.ToString(CultureInfo.InvariantCulture)
                + ">>\nstream\n" + Encoding.Latin1.GetString(Invoice) + "\nendstream",
            "<</Kids[7 0 R]>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]>>",
            "<</Names[(invoice.xml) 3 0 R]/Limits[(invoice.xml) (invoice.xml)]>>",
        ]);

        var result = PdfAConverter.Convert(pdf, To(PdfConformanceStandard.PdfA4f));

        Assert.True(result.HasBytes, Refusals(result));
        Assert.True(result.IsConformant, Errors(result));
        Assert.Equal(Invoice, Assert.Single(PdfAttachments.Read(result.Bytes)).Content);
    }

    [Fact]
    public void The_checker_reports_an_AF_entry_that_is_not_a_file_specification()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R/Names<</EmbeddedFiles 5 0 R>>/AF[3 0 R 42]>>",
            "<</Type/Pages/Count 1/Kids[6 0 R]>>",
            "<</Type/Filespec/F(invoice.xml)/UF(invoice.xml)/EF<</F 4 0 R>>/AFRelationship/Data>>",
            "<</Type/EmbeddedFile/Subtype/text#2Fxml/Length " + Invoice.Length.ToString(CultureInfo.InvariantCulture)
                + ">>\nstream\n" + Encoding.Latin1.GetString(Invoice) + "\nendstream",
            "<</Names[(invoice.xml) 3 0 R]>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]>>",
        ]);

        var report = PdfConformance.Check(pdf, PdfConformanceStandard.PdfA4f);

        Assert.Contains(
            report.Issues,
            issue => issue.Code == PdfConformanceCodes.AssociatedFilesInconsistent
                && issue.Message.Contains("does not resolve to a file specification", StringComparison.Ordinal));
    }

    [Fact]
    public void The_checker_reports_a_claimed_4f_with_no_embedded_file()
    {
        // The profile exists because the file carries an embedded file; without one, the claim is
        // false even though every other PDF/A-4 rule may hold.
        var report = PdfConformance.Check(PdfTestDocument.Minimal(), PdfConformanceStandard.PdfA4f);

        Assert.False(report.IsConformant);
        Assert.Contains(report.Issues, issue => issue.Code == PdfConformanceCodes.EmbeddedFilesMissing);
        Assert.Contains(report.Issues, issue => issue.Code == PdfConformanceCodes.AssociatedFilesMissing);
    }

    [Fact]
    public void The_checker_reports_a_named_file_that_is_not_an_associated_file()
    {
        var report = PdfConformance.Check(WithEmbeddedFile(associated: false), PdfConformanceStandard.PdfA4f);

        Assert.Contains(report.Issues, issue => issue.Code == PdfConformanceCodes.AssociatedFilesMissing);
        Assert.Contains(
            report.Issues,
            issue => issue.Code == PdfConformanceCodes.AssociatedFilesInconsistent
                && issue.Target.Contains("invoice.xml", StringComparison.Ordinal));
    }

    [Fact]
    public void The_checker_reports_a_missing_relationship_even_when_the_file_is_associated()
    {
        var report = PdfConformance.Check(WithEmbeddedFile(relationship: null), PdfConformanceStandard.PdfA4f);

        var issue = Assert.Single(
            report.Issues,
            finding => finding.Code == PdfConformanceCodes.EmbeddedFileRelationshipMissing);

        Assert.Contains("invoice.xml", issue.Target, StringComparison.Ordinal);
        Assert.False(report.IsConformant);
    }

    [Fact]
    public void The_checker_reports_an_unknown_relationship()
    {
        var report = PdfConformance.Check(WithEmbeddedFile(relationship: "Attached"), PdfConformanceStandard.PdfA4f);

        var issue = Assert.Single(
            report.Issues,
            finding => finding.Code == PdfConformanceCodes.EmbeddedFileRelationshipUnknown);

        Assert.Contains("/Attached", issue.Message, StringComparison.Ordinal);
        Assert.False(report.IsConformant);
    }
}
