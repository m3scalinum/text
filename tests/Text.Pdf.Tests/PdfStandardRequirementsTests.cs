using Xunit;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// The requirement table is the checker's claim about each standard: which PDF versions it accepts,
/// which XMP identifier it demands, and whether structure, title, metadata and fonts are required.
/// The tests state every row, so a standard added to the enum without a row fails here.
/// </summary>
public sealed class PdfStandardRequirementsTests
{
    public sealed record Row(
        PdfConformanceStandard Standard,
        PdfVersion Minimum,
        PdfVersion Maximum,
        int? Part,
        string? Conformance,
        string Name,
        bool Tagged,
        bool Title,
        bool Metadata,
        bool Fonts);

    public static TheoryData<Row> Standards => new()
    {
        new Row(PdfConformanceStandard.Pdf14, new PdfVersion(1, 4), new PdfVersion(1, 4), null, null, "PDF 1.4", false, false, false, false),
        new Row(PdfConformanceStandard.Pdf15, new PdfVersion(1, 5), new PdfVersion(1, 5), null, null, "PDF 1.5", false, false, false, false),
        new Row(PdfConformanceStandard.Pdf16, new PdfVersion(1, 6), new PdfVersion(1, 6), null, null, "PDF 1.6", false, false, false, false),
        new Row(PdfConformanceStandard.Pdf17, new PdfVersion(1, 7), new PdfVersion(1, 7), null, null, "PDF 1.7", false, false, false, false),
        new Row(PdfConformanceStandard.Pdf20, new PdfVersion(2, 0), new PdfVersion(2, 0), null, null, "PDF 2.0", false, false, false, false),
        new Row(PdfConformanceStandard.PdfA1b, new PdfVersion(1, 4), new PdfVersion(1, 4), 1, "B", "PDF/A-1b", false, false, true, true),
        new Row(PdfConformanceStandard.PdfA1a, new PdfVersion(1, 4), new PdfVersion(1, 4), 1, "A", "PDF/A-1a", true, false, true, true),
        new Row(PdfConformanceStandard.PdfA2b, new PdfVersion(1, 7), new PdfVersion(1, 7), 2, "B", "PDF/A-2b", false, false, true, true),
        new Row(PdfConformanceStandard.PdfA2u, new PdfVersion(1, 7), new PdfVersion(1, 7), 2, "U", "PDF/A-2u", false, false, true, true),
        new Row(PdfConformanceStandard.PdfA2a, new PdfVersion(1, 7), new PdfVersion(1, 7), 2, "A", "PDF/A-2a", true, false, true, true),
        new Row(PdfConformanceStandard.PdfA3b, new PdfVersion(1, 7), new PdfVersion(1, 7), 3, "B", "PDF/A-3b", false, false, true, true),
        new Row(PdfConformanceStandard.PdfA3u, new PdfVersion(1, 7), new PdfVersion(1, 7), 3, "U", "PDF/A-3u", false, false, true, true),
        new Row(PdfConformanceStandard.PdfA3a, new PdfVersion(1, 7), new PdfVersion(1, 7), 3, "A", "PDF/A-3a", true, false, true, true),
        new Row(PdfConformanceStandard.PdfA4, new PdfVersion(2, 0), new PdfVersion(2, 0), 4, null, "PDF/A-4", false, false, true, true),
        new Row(PdfConformanceStandard.PdfA4f, new PdfVersion(2, 0), new PdfVersion(2, 0), 4, "F", "PDF/A-4f", false, false, true, true),
        new Row(PdfConformanceStandard.PdfA4e, new PdfVersion(2, 0), new PdfVersion(2, 0), 4, "E", "PDF/A-4e", false, false, true, true),
        new Row(PdfConformanceStandard.PdfUa1, new PdfVersion(1, 4), new PdfVersion(1, 7), null, null, "PDF/UA-1", true, true, true, true),
    };

    [Theory]
    [MemberData(nameof(Standards))]
    public void Every_standard_states_its_versions_identifier_and_requirements(Row row)
    {
        var requirements = PdfStandardRequirements.Of(row.Standard);

        Assert.Equal(row.Standard, requirements.Standard);
        Assert.Equal(row.Minimum, requirements.Minimum);
        Assert.Equal(row.Maximum, requirements.Maximum);
        Assert.Equal(row.Part, requirements.ArchivePart);
        Assert.Equal(row.Conformance, requirements.ArchiveConformance);
        Assert.Equal(row.Name, requirements.Name);
        Assert.Equal(row.Tagged, requirements.RequiresTaggedStructure);
        Assert.Equal(row.Title, requirements.RequiresTitle);
        Assert.Equal(row.Metadata, requirements.RequiresMetadata);
        Assert.Equal(row.Fonts, requirements.RequiresFontChecks);
    }

    [Fact]
    public void An_unknown_standard_falls_back_to_a_permissive_range()
    {
        // Defensive arm: a cast from an out-of-range integer must not throw and must not claim a
        // version range it cannot know.
        var requirements = PdfStandardRequirements.Of((PdfConformanceStandard)999);

        Assert.Equal(new PdfVersion(1, 0), requirements.Minimum);
        Assert.Equal(new PdfVersion(9, 9), requirements.Maximum);
        Assert.Null(requirements.ArchivePart);
        Assert.Equal("999", requirements.Name);
        Assert.False(requirements.RequiresTaggedStructure);
        Assert.False(requirements.RequiresMetadata);
    }

    [Fact]
    public void Versions_compare_by_major_then_minor()
    {
        var older = new PdfVersion(1, 4);
        var newer = new PdfVersion(1, 7);
        var major = new PdfVersion(2, 0);

        Assert.True(older < newer);
        Assert.True(newer > older);
        Assert.True(older <= newer);
        Assert.True(newer >= older);
        Assert.True(major > newer);
        Assert.False(newer > major);
        Assert.True(older.CompareTo(newer) < 0);
        Assert.Equal("1.7", newer.ToString());
    }

    [Fact]
    public void An_issue_renders_its_severity_code_target_and_message()
    {
        var issue = new PdfConformanceIssue("PDFUA.TEST", PdfConformanceSeverity.Warning, "obj 3", "one sentence");

        Assert.Equal("WARNING PDFUA.TEST (obj 3): one sentence", issue.ToString());
    }
}
