using M3scalinum.Text.Documents;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

/// <summary>
/// Fails when the HTML exporter stops carrying the figure's alternative text.
/// </summary>
/// <remarks>
/// The model gained <see cref="TypstFigure.Alt"/> for the PDF accessibility checklist, and the same
/// description is what an <c>img</c> element needs: writing an empty <c>alt</c> would declare a
/// described image decorative, which is a defect a reader of the HTML cannot see.
/// </remarks>
public sealed class HtmlExportAltTests
{
    [Fact]
    public void ToHtml_With_Figure_Alt_Writes_It_On_The_Image()
    {
        var document = TypstDocument.Create().Figure(figure => figure
            .Image("logo.png")
            .Alt("Le logo de la societe"));

        var result = document.ToHtml();

        Assert.Contains("alt=\"Le logo de la societe\"", result.Html);
    }

    [Fact]
    public void ToHtml_Without_Figure_Alt_Writes_An_Empty_Alt()
    {
        var document = TypstDocument.Create().Figure(figure => figure.Image("logo.png"));

        var result = document.ToHtml();

        Assert.Contains("alt=\"\"", result.Html);
        Assert.DoesNotContain("alt=\"null\"", result.Html);
    }

    [Fact]
    public void ToHtml_With_Figure_Alt_Escapes_The_Attribute()
    {
        var document = TypstDocument.Create().Figure(figure => figure
            .Image("logo.png")
            .Alt("Un \"carre\" & un <triangle>"));

        var result = document.ToHtml();

        Assert.Contains("alt=\"Un &quot;carre&quot; &amp; un &lt;triangle&gt;\"", result.Html);
    }
}
