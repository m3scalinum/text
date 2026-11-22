using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// The accessibility checklist of <c>docs/specifications/15-pdf-conformance.md</c> §6, one test per
/// line, each on a real document: the compiler's PDF/UA-1 fixture, or a document built by hand in
/// this file whose single property is stated. <see cref="PdfConformance.CheckAccessibility"/> is a
/// separate pass from <see cref="PdfConformance.Check"/>, and these tests are its contract.
/// </summary>
/// <remarks>
/// The last test writes the demo document to the test output directory so a person can open it in
/// a screen reader. No test here drives NVDA or VoiceOver: the specification lists what still needs
/// that manual pass, and this file does not claim it happened.
/// </remarks>
public sealed class PdfConformanceAccessibilityTests
{
    private static byte[] Fixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "pdfua", name);

        Assert.True(File.Exists(path), $"the fixture {name} is missing from {path}");

        return File.ReadAllBytes(path);
    }

    private static byte[] Ua1() => Fixture("ua1.pdf");

    private static PdfConformanceIssue Only(PdfConformanceReport report, string code)
    {
        var matches = report.Issues.Where(issue => string.Equals(issue.Code, code, StringComparison.Ordinal)).ToList();

        Assert.True(matches.Count == 1, $"expected one {code}, found {matches.Count} in {report}");

        return matches[0];
    }

    private static string Describe(PdfConformanceReport report)
        => string.Join("; ", report.Issues.Select(issue => issue.ToString()));

    [Fact]
    public void Image_Alternate_Text_Is_Reported_When_The_Figure_Loses_It()
    {
        // The compiler's fixture binds its image to a /Figure that carries /Alt. The same bytes with
        // the key renamed keep the figure and lose its description, which is the failure a reader
        // would announce as "graphic" with no name.
        Assert.False(PdfConformance.CheckAccessibility(Ua1()).Has(PdfConformanceCodes.FigureAlternateTextMissing));

        var report = PdfConformance.CheckAccessibility(Replace(Ua1(), "/S/Figure/P 13 0 R/Alt<", "/S/Figure/P 13 0 R/Alx<"));

        var issue = Only(report, PdfConformanceCodes.FigureAlternateTextMissing);

        Assert.Equal("obj 12", issue.Target);
    }

    [Fact]
    public void Table_Headers_Are_Reported_Missing_On_The_Compiler_Output()
    {
        // Typst 0.15 writes table rows as /TR of /TD cells and never a /TH: the fixture's table has
        // twelve data cells and no header. That is the finding the checklist must state honestly.
        var report = PdfConformance.CheckAccessibility(Ua1());

        var issue = Only(report, PdfConformanceCodes.TableHeaderMissing);

        Assert.Equal("obj 31", issue.Target);
        Assert.Equal(PdfConformanceSeverity.Error, issue.Severity);
        Assert.False(report.IsConformant);
    }

    [Fact]
    public void A_Table_Header_Without_A_Scope_Is_Reported()
    {
        // The demo document's header cell declares /A /Scope /Column; removing the attribute leaves
        // a /TH a reader cannot place.
        Assert.False(PdfConformance.CheckAccessibility(Demo()).Has(PdfConformanceCodes.TableHeaderScopeMissing));

        var report = PdfConformance.CheckAccessibility(
            Demo(bodies => bodies[9] = "<</Type/StructElem/S/TH/P 9 0 R/K[2]/Pg 3 0 R>>"));

        var issue = Only(report, PdfConformanceCodes.TableHeaderScopeMissing);

        Assert.Equal("obj 10", issue.Target);
    }

    [Fact]
    public void A_Table_With_A_Header_Row_Is_Not_Reported()
    {
        var report = PdfConformance.CheckAccessibility(Demo());

        Assert.False(report.Has(PdfConformanceCodes.TableHeaderMissing));
        Assert.True(report.IsConformant, Describe(report));
    }

    [Fact]
    public void The_Document_Language_Is_Reported_Missing()
    {
        Assert.False(PdfConformance.CheckAccessibility(Ua1()).Has(PdfConformanceCodes.LanguageMissing));

        var report = PdfConformance.CheckAccessibility(Replace(Ua1(), "/Lang(fr)", "/Lanx(fr)"));

        Assert.Equal("catalog", Only(report, PdfConformanceCodes.LanguageMissing).Target);
    }

    [Fact]
    public void Reading_Order_Is_Reported_When_A_Page_Does_Not_Follow_The_Structure()
    {
        // Both pages of the fixture declare /Tabs /S, so the structure order is the reading order.
        Assert.False(PdfConformance.CheckAccessibility(Ua1()).Has(PdfConformanceCodes.TabOrderNotStructure));

        var report = PdfConformance.CheckAccessibility(Replace(Ua1(), "/StructParents 0/Tabs/S", "/StructParents 0/Tabs/A"));

        var issue = Only(report, PdfConformanceCodes.TabOrderNotStructure);

        Assert.Contains("page 46", issue.Target, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Structure_Without_A_Tree_Is_Reported_On_The_Accessibility_Path_Too()
    {
        var report = PdfConformance.CheckAccessibility(Replace(Ua1(), "/StructTreeRoot 5 0 R", new string(' ', 21)));

        Assert.True(report.Has(PdfConformanceCodes.StructTreeMissing));
        Assert.False(report.IsConformant);
    }

    [Fact]
    public void A_Skipped_Heading_Level_Is_Reported()
    {
        // The fixture goes H1 then H2 in document order, which is the positive case; H1 then H3 is
        // the outline a reader announces with a missing level.
        Assert.False(PdfConformance.CheckAccessibility(Ua1()).Has(PdfConformanceCodes.HeadingLevelSkipped));

        var report = PdfConformance.CheckAccessibility(Replace(Ua1(), "/S/H2", "/S/H3"));

        var issue = Only(report, PdfConformanceCodes.HeadingLevelSkipped);

        Assert.Equal("obj 14", issue.Target);
        Assert.Contains("skips level 2", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Structure_Role_Outside_The_RoleMap_Is_Reported()
    {
        // The fixture maps its custom roles (Datetime, Terms, Title, Strong, Em) through /RoleMap,
        // so its own output is silent. /Dix is a role neither standard nor mapped.
        Assert.False(PdfConformance.CheckAccessibility(Ua1()).Has(PdfConformanceCodes.RoleNotMapped));

        var report = PdfConformance.CheckAccessibility(Replace(Ua1(), "/S/Div/P 33 0 R", "/S/Dix/P 33 0 R"));

        var issue = Only(report, PdfConformanceCodes.RoleNotMapped);

        Assert.Contains("/Dix", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Custom_Role_Mapped_By_The_RoleMap_Is_Accepted()
    {
        // The mirror of the previous test: /Subtitle is not a standard role, but /RoleMap says it
        // means /H2, so a reader still announces a heading and nothing is reported.
        var report = PdfConformance.CheckAccessibility(
            Demo(bodies =>
            {
                bodies[3] = "<</Type/StructTreeRoot/K[5 0 R]/ParentTree<</Nums[0 16 0 R]>>/RoleMap<</Subtitle/H2>>>>";
                bodies[6] = "<</Type/StructElem/S/Subtitle/P 5 0 R/K[1]/Pg 3 0 R>>";
            }));

        Assert.False(report.Has(PdfConformanceCodes.RoleNotMapped));
        Assert.True(report.IsConformant, Describe(report));
    }

    [Fact]
    public void A_Document_That_Does_Not_Display_Its_Title_Is_Reported()
    {
        Assert.False(PdfConformance.CheckAccessibility(Ua1()).Has(PdfConformanceCodes.DisplayDocTitleMissing));

        var report = PdfConformance.CheckAccessibility(
            Replace(Ua1(), "/DisplayDocTitle true", "/DisplayDocTitlx true"));

        Assert.Equal("catalog", Only(report, PdfConformanceCodes.DisplayDocTitleMissing).Target);
    }

    [Fact]
    public void A_Document_With_No_Title_Anywhere_Is_Reported()
    {
        var pdf = Replace(Replace(Ua1(), "/Title(", "/Titlx("), "<dc:title>", "<dx:title>");

        Assert.True(PdfConformance.CheckAccessibility(pdf).Has(PdfConformanceCodes.TitleMissing));
    }

    [Fact]
    public void A_PDF_UA_2_Claim_Is_Reported_Because_Only_UA_1_Exists()
    {
        // Typst 0.15 has one accessibility flavour. A document that declares part 2 while claiming
        // UA-1 is reported rather than accepted, so the limit of the producer is visible.
        var report = PdfConformance.CheckAccessibility(Replace(Ua1(), "<pdfuaid:part>1", "<pdfuaid:part>2"));

        Assert.Equal("xmp", Only(report, PdfConformanceCodes.XmpStandardMismatch).Target);
        Assert.DoesNotContain(
            Enum.GetNames<PdfConformanceStandard>(),
            name => name.Contains("Ua2", StringComparison.Ordinal));
    }

    [Fact]
    public void Contrast_And_Font_Size_Are_Not_Checked_Because_They_Belong_To_The_Model()
    {
        // The demo sets its body text at 2 pt: no PDF structure rule is broken, and the checker says
        // nothing about size or colour, because those are decisions of the document model, not the
        // PDF file. This test states the boundary rather than implying a check that does not exist.
        var small = Demo(bodies => bodies[18] = Stream("BT /F1 2 Tf (x) Tj ET"));
        var report = PdfConformance.CheckAccessibility(small);

        Assert.True(report.IsConformant, Describe(report));
        Assert.DoesNotContain(
            report.Issues,
            issue => issue.Message.Contains("contrast", StringComparison.OrdinalIgnoreCase)
                || issue.Message.Contains("font size", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_Demo_Document_Passes_The_Checklist_And_Is_Written_For_A_Manual_Check()
    {
        // The one document on which every automatic line passes at once. It is written next to the
        // test assembly so the manual screen-reader pass named in the specification has a file to
        // open; the test asserts the file, not that a reader was run.
        var pdf = Demo();
        var report = PdfConformance.CheckAccessibility(pdf);

        Assert.True(report.IsConformant, Describe(report));
        Assert.Empty(report.Issues);
        Assert.Equal(PdfConformanceStandard.PdfUa1, report.Standard);

        var path = Path.Combine(AppContext.BaseDirectory, "demo-accessible.pdf");
        File.WriteAllBytes(path, pdf);

        Assert.True(new FileInfo(path).Length > 0);
    }

    [Fact]
    public void CheckAccessibility_Never_Throws_On_Bytes_That_Are_Not_A_Pdf()
    {
        var report = PdfConformance.CheckAccessibility(Encoding.ASCII.GetBytes("not a pdf"));

        Assert.Equal("header", Only(report, PdfConformanceCodes.NotAPdf).Target);
    }

    [Fact]
    public void Tables_Nested_In_A_Head_Wrapper_Are_Still_Read()
    {
        var report = PdfConformance.CheckAccessibility(
            Demo(bodies =>
            {
                bodies[7] = "<</Type/StructElem/S/Table/P 5 0 R/K[22 0 R]>>";
                bodies[8] = "<</Type/StructElem/S/TR/P 22 0 R/K[10 0 R 11 0 R]>>";
                bodies[21] = "<</Type/StructElem/S/THead/P 8 0 R/K[9 0 R]>>";
            }));

        Assert.True(report.IsConformant, Describe(report));
    }

    /// <summary>
    /// A minimal document that satisfies every line of the checklist: language, title and its
    /// display, tab order, a figure with alternate text, a table with a scoped header and nested
    /// headings — built by hand so a single property can be changed per test.
    /// </summary>
    /// <remarks>
    /// Object numbers: 1 catalog, 2 page tree, 3 page, 4 structure tree root, 5 Document, 6 H1,
    /// 7 H2, 8 Table, 9 TR, 10 TH, 11 TD, 12 Figure, 13 image, 14 font, 15 ToUnicode, 16 parent
    /// tree numbers, 17 document information, 18 XMP, 19 contents, 20 font descriptor, 21 font
    /// program, 22 table head wrapper.
    /// </remarks>
    private static byte[] Demo(Action<string[]>? change = null)
    {
        var bodies = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R/Metadata 18 0 R/Lang(fr)/StructTreeRoot 4 0 R/MarkInfo<</Marked true>>/ViewerPreferences<</DisplayDocTitle true>>>>",
            "<</Type/Pages/Count 1/Kids[3 0 R]>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]/Resources<</Font<</F1 14 0 R>>/XObject<</Im1 13 0 R>>>>/StructParents 0/Tabs/S/Contents 19 0 R>>",
            "<</Type/StructTreeRoot/K[5 0 R]/ParentTree<</Nums[0 16 0 R]>>>>",
            "<</Type/StructElem/S/Document/P 4 0 R/K[6 0 R 7 0 R 8 0 R 12 0 R]>>",
            "<</Type/StructElem/S/H1/P 5 0 R/K[0]/Pg 3 0 R>>",
            "<</Type/StructElem/S/H2/P 5 0 R/K[1]/Pg 3 0 R>>",
            "<</Type/StructElem/S/Table/P 5 0 R/K[9 0 R]>>",
            "<</Type/StructElem/S/TR/P 8 0 R/K[10 0 R 11 0 R]>>",
            "<</Type/StructElem/S/TH/P 9 0 R/A[<</O/Table/Scope/Column>>]/K[2]/Pg 3 0 R>>",
            "<</Type/StructElem/S/TD/P 9 0 R/K[3]/Pg 3 0 R>>",
            "<</Type/StructElem/S/Figure/P 5 0 R/Alt(Un carre rouge)/K[4]/Pg 3 0 R>>",
            "<</Type/XObject/Subtype/Image/Width 1/Height 1/ColorSpace/DeviceRGB/BitsPerComponent 8/Length 3>>\nstream\nabc\nendstream",
            "<</Type/Font/Subtype/TrueType/BaseFont/Demo/FontDescriptor 20 0 R/ToUnicode 15 0 R>>",
            Stream(string.Empty),
            "[6 0 R]",
            "<</Title(Demonstration accessible)>>",
            Xmp(),
            Stream("BT /F1 12 Tf (x) Tj ET"),
            "<</Type/FontDescriptor/FontName/Demo/Flags 4/FontBBox[0 0 1 1]/ItalicAngle 0/Ascent 1/Descent 0/CapHeight 1/StemV 1/FontFile2 21 0 R>>",
            Stream(string.Empty),
            "<</Type/StructElem/S/THead/P 8 0 R/K[9 0 R]>>",
        };

        var values = bodies.ToArray();
        change?.Invoke(values);

        return PdfTestDocument.Document(values, "/Info 17 0 R");
    }

    private static string Stream(string content)
        => $"<</Length {content.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)}>>\nstream\n{content}\nendstream";

    private static string Xmp()
    {
        var xml = "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">"
            + "<rdf:Description rdf:about=\"\" xmlns:pdfuaid=\"http://www.aiim.org/pdfua/ns/id/\" "
            + "xmlns:dc=\"http://purl.org/dc/elements/1.1/\">"
            + "<pdfuaid:part>1</pdfuaid:part>"
            + "<dc:title><rdf:Alt><rdf:li>Demonstration accessible</rdf:li></rdf:Alt></dc:title>"
            + "</rdf:Description></rdf:RDF></x:xmpmeta>";

        return $"<</Type/Metadata/Subtype/XML/Length {xml.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)}>>\nstream\n{xml}\nendstream";
    }

    /// <summary>Replaces one occurrence, asserting that the fixture holds exactly one.</summary>
    private static byte[] Replace(byte[] pdf, string from, string to)
    {
        Assert.Equal(from.Length, to.Length);

        var text = Encoding.Latin1.GetString(pdf);
        var first = text.IndexOf(from, StringComparison.Ordinal);

        Assert.True(first >= 0, $"the fixture no longer contains '{from}'");
        Assert.Equal(first, text.LastIndexOf(from, StringComparison.Ordinal));

        return Encoding.Latin1.GetBytes(string.Concat(text.AsSpan(0, first), to, text.AsSpan(first + from.Length)));
    }
}
