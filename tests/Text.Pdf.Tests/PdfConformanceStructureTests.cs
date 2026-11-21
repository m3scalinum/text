using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// The tagged-structure and XMP rules, each on the smallest document that can carry the property.
/// The positive case is the first test; every other one changes a single declaration and states the
/// issue that must follow, including the cases where the check must stay silent.
/// </summary>
public sealed class PdfConformanceStructureTests
{
    private static PdfConformanceIssue Only(PdfConformanceReport report, string code)
    {
        var matches = report.Issues.Where(issue => string.Equals(issue.Code, code, StringComparison.Ordinal)).ToList();

        Assert.True(matches.Count == 1, $"expected one {code}, found {matches.Count} in {report}");

        return matches[0];
    }

    /// <summary>A tagged document whose catalog points at an XMP packet, optionally without /Info.</summary>
    private static byte[] WithXmp(string xml, bool withInfo = true, string? metadataDictionary = null)
    {
        var bodies = PdfTestDocument.Tagged().ToList();
        bodies.Add(metadataDictionary is null
            ? $"<</Type/Metadata/Subtype/XML>>\nstream\n{xml}\nendstream"
            : $"{metadataDictionary}\nstream\n{xml}\nendstream");

        var catalog = bodies[0].Replace(
            "/StructTreeRoot 4 0 R",
            "/StructTreeRoot 4 0 R/Metadata 9 0 R",
            StringComparison.Ordinal);

        bodies[0] = catalog;

        return PdfTestDocument.Document(bodies, withInfo ? "/Info 8 0 R" : "");
    }

    private static string Xmp(string properties)
        => "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">"
            + "<rdf:Description rdf:about=\"\" xmlns:pdfaid=\"http://www.aiim.org/pdfa/ns/id/\" "
            + "xmlns:pdfuaid=\"http://www.aiim.org/pdfua/ns/id/\" "
            + "xmlns:dc=\"http://purl.org/dc/elements/1.1/\">"
            + properties
            + "</rdf:Description></rdf:RDF></x:xmpmeta>";

    [Fact]
    public void A_tagged_document_with_a_title_and_a_language_is_conformant_apart_from_its_identifier()
    {
        var report = PdfConformance.Check(PdfTestDocument.TaggedPdf(), PdfConformanceStandard.PdfUa1);

        var issue = Only(report, PdfConformanceCodes.MetadataMissing);

        Assert.Equal("catalog", issue.Target);
        Assert.False(report.IsConformant);
        Assert.Equal(new PdfVersion(1, 7), report.Version.GetValueOrDefault());
    }

    [Fact]
    public void A_structure_tree_root_without_its_type_is_reported()
    {
        var report = PdfConformance.Check(
            PdfTestDocument.TaggedPdf(bodies => bodies[3] = "<</K[5 0 R]/ParentTree<</Nums[0 7 0 R]>>>>"),
            PdfConformanceStandard.PdfUa1);

        Assert.Contains(
            "is not /Type /StructTreeRoot",
            Only(report, PdfConformanceCodes.StructTreeInconsistent).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_structure_tree_root_without_a_parent_tree_is_reported()
    {
        var report = PdfConformance.Check(
            PdfTestDocument.TaggedPdf(bodies => bodies[3] = "<</Type/StructTreeRoot/K[5 0 R]>>"),
            PdfConformanceStandard.PdfUa1);

        Assert.Contains(
            "no /ParentTree",
            Only(report, PdfConformanceCodes.StructTreeInconsistent).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_parent_tree_split_into_number_trees_is_not_guessed()
    {
        var report = PdfConformance.Check(
            PdfTestDocument.TaggedPdf(bodies => bodies[3] = "<</Type/StructTreeRoot/K[5 0 R]/ParentTree<</Kids[7 0 R]>>>>"),
            PdfConformanceStandard.PdfUa1);

        // The keys of a split parent tree are not readable from the root, so the page association
        // check is skipped rather than reported as an inconsistency.
        Assert.False(report.Has(PdfConformanceCodes.StructTreeInconsistent));
        Assert.True(report.Has(PdfConformanceCodes.MetadataMissing));
    }

    [Fact]
    public void A_parent_tree_with_neither_numbers_nor_number_trees_is_reported()
    {
        var report = PdfConformance.Check(
            PdfTestDocument.TaggedPdf(bodies => bodies[3] = "<</Type/StructTreeRoot/K[5 0 R]/ParentTree<</Key 1>>>>"),
            PdfConformanceStandard.PdfUa1);

        Assert.Contains(
            "neither /Nums nor /Kids",
            Only(report, PdfConformanceCodes.StructTreeInconsistent).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_structure_element_without_a_parent_is_reported()
    {
        var report = PdfConformance.Check(
            PdfTestDocument.TaggedPdf(bodies => bodies[4] = "<</Type/StructElem/S/Document/K[6 0 R]>>"),
            PdfConformanceStandard.PdfUa1);

        Assert.Contains(
            "has no /P",
            Only(report, PdfConformanceCodes.StructTreeInconsistent).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_structure_element_reachable_twice_is_reported()
    {
        var report = PdfConformance.Check(
            PdfTestDocument.TaggedPdf(bodies => bodies[3] = "<</Type/StructTreeRoot/K[5 0 R 5 0 R]/ParentTree<</Nums[0 7 0 R]>>>>"),
            PdfConformanceStandard.PdfUa1);

        Assert.Contains(
            "reachable more than once",
            Only(report, PdfConformanceCodes.StructTreeInconsistent).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_figure_described_by_ActualText_needs_no_Alt()
    {
        var report = PdfConformance.Check(
            PdfTestDocument.TaggedPdf(bodies =>
            {
                bodies[5] = "<</Type/StructElem/S/Figure/P 5 0 R/K[0]/ActualText(A red square)>>";
                bodies[6] = "[6 0 R]";
            }),
            PdfConformanceStandard.PdfUa1);

        Assert.False(report.Has(PdfConformanceCodes.FigureAlternateTextMissing));
    }

    [Fact]
    public void A_pdfaid_part_without_a_conformance_level_is_reported()
    {
        var pdf = WithXmp(Xmp("<pdfaid:part>2</pdfaid:part>"));
        var report = PdfConformance.Check(pdf, PdfConformanceStandard.PdfA2b);

        Assert.Contains(
            "declares none",
            Only(report, PdfConformanceCodes.XmpStandardMismatch).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_XMP_packet_without_pdfuaid_is_reported()
    {
        var pdf = WithXmp(Xmp("<pdfaid:part>1</pdfaid:part>"));
        var report = PdfConformance.Check(pdf, PdfConformanceStandard.PdfUa1);

        Assert.Contains(
            "requires pdfuaid:part 1",
            Only(report, PdfConformanceCodes.XmpStandardMismatch).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_unreadable_metadata_stream_is_a_warning_not_an_error()
    {
        var pdf = WithXmp("not read", metadataDictionary: "<</Type/Metadata/Subtype/XML/Filter/DCTDecode>>");
        var report = PdfConformance.Check(pdf, PdfConformanceStandard.PdfUa1);

        var issue = Only(report, PdfConformanceCodes.MetadataUnreadable);

        Assert.Equal(PdfConformanceSeverity.Warning, issue.Severity);
        Assert.True(report.IsConformant, report.ToString());
        Assert.True(report.Has(PdfConformanceCodes.MetadataUnreadable));
    }

    [Fact]
    public void A_PDF_A_document_without_metadata_has_no_identifier_to_compare()
    {
        var report = PdfConformance.Check(PdfTestDocument.Minimal(), PdfConformanceStandard.PdfA2b);

        Assert.True(report.Has(PdfConformanceCodes.MetadataMissing));
        Assert.False(report.Has(PdfConformanceCodes.XmpStandardMismatch));
        Assert.False(report.IsConformant);
    }

    [Fact]
    public void An_XMP_packet_that_states_its_identifier_as_an_attribute_is_read()
    {
        var xml = "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF><rdf:Description "
            + "pdfaid:part=\"2\" pdfaid:conformance=\"B\"/></rdf:RDF></x:xmpmeta>";

        var report = PdfConformance.Check(WithXmp(xml), PdfConformanceStandard.PdfA2b);

        Assert.True(report.IsConformant, report.ToString());
        Assert.Empty(report.Issues);
    }

    [Fact]
    public void An_XMP_attribute_without_a_value_is_not_read()
    {
        var xml = "<x:xmpmeta><rdf:Description pdfaid:part=2 pdfaid:conformance=B/></x:xmpmeta>";

        var report = PdfConformance.Check(WithXmp(xml), PdfConformanceStandard.PdfA2b);

        Assert.True(report.Has(PdfConformanceCodes.XmpStandardMismatch));
        Assert.False(report.IsConformant);
    }

    [Fact]
    public void A_title_in_the_XMP_is_enough_when_the_information_dictionary_has_none()
    {
        var xml = Xmp("<pdfuaid:part>1</pdfuaid:part><dc:title><rdf:Alt><rdf:li>XMP title</rdf:li></rdf:Alt></dc:title>");
        var report = PdfConformance.Check(WithXmp(xml, withInfo: false), PdfConformanceStandard.PdfUa1);

        Assert.False(report.Has(PdfConformanceCodes.TitleMissing));
        Assert.True(report.IsConformant, report.ToString());
    }

    [Fact]
    public void An_unclosed_identifier_element_is_treated_as_missing()
    {
        var xml = "<x:xmpmeta><pdfuaid:part>1</x:xmpmeta>";
        var report = PdfConformance.Check(WithXmp(xml), PdfConformanceStandard.PdfUa1);

        Assert.Contains(
            "declares none",
            Only(report, PdfConformanceCodes.XmpStandardMismatch).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_PDF_A_a_document_needs_tagged_structure()
    {
        var report = PdfConformance.Check(
            PdfTestDocument.Document(["<</Type/Catalog/Pages 2 0 R>>", "<</Type/Pages/Count 0/Kids[]>>"]),
            PdfConformanceStandard.PdfA2a);

        Assert.True(report.Has(PdfConformanceCodes.StructTreeMissing));
        Assert.True(report.Has(PdfConformanceCodes.MarkInfoMissing));
        Assert.True(report.Has(PdfConformanceCodes.LanguageMissing));
    }

    [Fact]
    public void A_catalog_mark_info_that_is_false_is_reported()
    {
        var report = PdfConformance.Check(
            PdfTestDocument.TaggedPdf(bodies => bodies[0] = bodies[0].Replace(
                "/MarkInfo<</Marked true>>",
                "/MarkInfo<</Marked 1>>",
                StringComparison.Ordinal)),
            PdfConformanceStandard.PdfUa1);

        Assert.True(report.Has(PdfConformanceCodes.MarkInfoMissing));
    }

    [Fact]
    public void A_reference_to_a_missing_structure_tree_root_is_reported()
    {
        var report = PdfConformance.Check(
            PdfTestDocument.TaggedPdf(bodies => bodies[0] = bodies[0].Replace(
                "/StructTreeRoot 4 0 R",
                "/StructTreeRoot 99 0 R",
                StringComparison.Ordinal)),
            PdfConformanceStandard.PdfUa1);

        Assert.True(report.Has(PdfConformanceCodes.StructTreeMissing));
    }

    [Fact]
    public void A_language_that_is_not_a_string_is_reported()
    {
        var report = PdfConformance.Check(
            PdfTestDocument.TaggedPdf(bodies => bodies[0] = bodies[0].Replace(
                "/Lang(en)",
                "/Lang 42",
                StringComparison.Ordinal)),
            PdfConformanceStandard.PdfUa1);

        Assert.True(report.Has(PdfConformanceCodes.LanguageMissing));
    }

    [Fact]
    public void A_title_that_is_an_empty_string_is_not_a_title()
    {
        var report = PdfConformance.Check(
            PdfTestDocument.TaggedPdf(bodies => bodies[7] = "<</Title()>>"),
            PdfConformanceStandard.PdfUa1);

        Assert.True(report.Has(PdfConformanceCodes.TitleMissing));
    }

    [Fact]
    public void A_structure_element_that_is_not_a_dictionary_does_not_stop_the_walk()
    {
        var report = PdfConformance.Check(
            PdfTestDocument.TaggedPdf(bodies => bodies[3] = "<</Type/StructTreeRoot/K[5 0 R 42]/ParentTree<</Nums[0 7 0 R]>>>>"),
            PdfConformanceStandard.PdfUa1);

        // A number in /K is a marked-content identifier, not a branch: nothing is reported for it.
        Assert.False(report.Has(PdfConformanceCodes.StructTreeInconsistent));
        Assert.True(report.Has(PdfConformanceCodes.MetadataMissing));
    }
}
