using System;
using System.Text;
using Xunit;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// The bounds a received PDF meets that the reader tests do not already pin: a page the tree reaches
/// twice, and a form XObject chain deeper than the walk allows.
/// </summary>
/// <remarks>
/// These are the PDF rows of <c>docs/specifications/29-threat-model.md</c> that had no test. Each one
/// fails if its bound is removed — the page tree would count a page twice, and the form walk would
/// reach a form it is supposed to stop before — rather than asserting the value of a constant.
/// </remarks>
public sealed class ThreatModelLimitsTests
{
    private static string Form(string name, string next, string content, int fontObject = 14)
    {
        var bytes = Encoding.Latin1.GetByteCount(content);
        var xobject = next.Length == 0 ? string.Empty : $"/XObject<</{next} {NextObject(next)} 0 R>>";

        return $"<</Type/XObject/Subtype/Form/BBox[0 0 300 300]"
            + $"/Resources<</Font<</F1 {fontObject} 0 R>>{xobject}>>/Length {bytes}>>\n"
            + $"stream\n{content}\nendstream";
    }

    /// <summary>The object number a form named <c>FmN</c> is written at, matching the fixture below.</summary>
    private static int NextObject(string name)
        => name switch
        {
            "Fm2" => 6,
            "Fm3" => 7,
            "Fm4" => 8,
            "Fm5" => 9,
            "Fm6" => 10,
            "Fm7" => 11,
            "Fm8" => 12,
            "Fm9" => 13,
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "no such form"),
        };

    [Fact]
    public void A_page_the_tree_reaches_twice_is_counted_once()
    {
        // The page tree is a graph the producer writes, not a tree: two intermediate nodes name the
        // same page. The walk keeps a visited set, so the page is read once; a walk that trusted the
        // producer would hand the caller the same page twice, and a cycle would never end.
        var pdf = PdfTestDocument.Document(
            [
                "<</Type/Catalog/Pages 2 0 R>>",
                "<</Type/Pages/Count 1/Kids[3 0 R 4 0 R]>>",
                "<</Type/Pages/Count 1/Kids[5 0 R]>>",
                "<</Type/Pages/Count 1/Kids[5 0 R]>>",
                "<</Type/Page/Parent 3 0 R/MediaBox[0 0 300 300]>>",
            ]);

        var result = PdfText.Extract(pdf);

        Assert.Equal(1, result.PageCount);
    }

    [Fact]
    public void A_form_nested_past_the_depth_cap_draws_no_text()
    {
        // Nine forms, each drawing the next. The page reaches the eighth through the chain, which is
        // the deepest walk the reader performs; the ninth is past MaxFormDepth and its text is not
        // read. If the cap is removed or raised, "DepthNine" appears in the page's text.
        var pdf = PdfTestDocument.Document(
            [
                "<</Type/Catalog/Pages 2 0 R>>",
                "<</Type/Pages/Count 1/Kids[3 0 R]>>",
                "<</Type/Page/Parent 2 0 R/MediaBox[0 0 300 300]"
                    + "/Resources<</Font<</F1 14 0 R>>/XObject<</Fm1 5 0 R>>>>/Contents 4 0 R>>",
                Stream("/Fm1 Do"),
                Form("Fm1", "Fm2", "/Fm2 Do"),
                Form("Fm2", "Fm3", "/Fm3 Do"),
                Form("Fm3", "Fm4", "/Fm4 Do"),
                Form("Fm4", "Fm5", "/Fm5 Do"),
                Form("Fm5", "Fm6", "/Fm6 Do"),
                Form("Fm6", "Fm7", "/Fm7 Do"),
                Form("Fm7", "Fm8", "BT /F1 12 Tf 10 10 Td (DepthEight) Tj ET\n/Fm8 Do"),
                Form("Fm8", "Fm9", "BT /F1 12 Tf 10 10 Td (DepthNine) Tj ET\n/Fm9 Do"),
                Form("Fm9", "", "BT /F1 12 Tf 10 10 Td (DepthTen) Tj ET"),
                "<</Type/Font/Subtype/Type1/BaseFont/Helvetica/Encoding/WinAnsiEncoding>>",
            ]);

        var page = PdfText.Extract(pdf).Pages[0];

        Assert.Contains("DepthEight", page.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("DepthTen", page.Text, StringComparison.Ordinal);
    }

    private static string Stream(string content)
    {
        var bytes = Encoding.Latin1.GetByteCount(content);

        return $"<</Length {bytes}>>\nstream\n{content}\nendstream";
    }
}
