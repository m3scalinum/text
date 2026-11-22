using System;
using System.Linq;
using M3scalinum.Text.Documents;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

/// <summary>
/// The HTML export contract: one fixture per element of the mapping, the escaping rules, and the
/// nodes the model can hold but HTML cannot express.
/// </summary>
public sealed class HtmlExportTests
{
    [Theory]
    [InlineData(1, "<h1>Title</h1>\n")]
    [InlineData(2, "<h2>Title</h2>\n")]
    [InlineData(3, "<h3>Title</h3>\n")]
    [InlineData(4, "<h4>Title</h4>\n")]
    [InlineData(5, "<h5>Title</h5>\n")]
    [InlineData(6, "<h6>Title</h6>\n")]
    public void ToHtml_With_Heading_Emits_H1_Through_H6(int level, string expected)
    {
        var result = TypstDocument.Create().Heading(level, "Title").ToHtml();

        Assert.Equal(expected, result.Html);
        Assert.Equal(0, result.UnrepresentableCount);
    }

    [Fact]
    public void ToHtml_With_Paragraph_Emits_P()
    {
        var result = TypstDocument.Create().Paragraph("Hello world.").ToHtml();

        Assert.Equal("<p>Hello world.</p>\n", result.Html);
    }

    [Fact]
    public void ToHtml_With_Bold_And_Emphasis_Emits_Strong_And_Em()
    {
        var document = TypstDocument.Create()
            .Paragraph(c => c.Text("a ").Bold("b").Text(" ").Emphasis("c").Text("."));

        Assert.Equal("<p>a <strong>b</strong> <em>c</em>.</p>\n", document.ToHtml().Html);
    }

    [Fact]
    public void ToHtml_With_Inline_Code_Emits_Code()
    {
        var document = TypstDocument.Create().Paragraph(c => c.Text("call ").Code("a < b").Text(" now"));

        Assert.Equal("<p>call <code>a &lt; b</code> now</p>\n", document.ToHtml().Html);
    }

    [Fact]
    public void ToHtml_With_Bullet_List_Emits_Ul_And_Li()
    {
        var result = TypstDocument.Create().BulletList("one", "two").ToHtml();

        Assert.Equal("<ul>\n<li>one</li>\n<li>two</li>\n</ul>\n", result.Html);
    }

    [Fact]
    public void ToHtml_With_Numbered_List_Emits_Ol_And_Li()
    {
        var result = TypstDocument.Create().NumberedList("first", "second").ToHtml();

        Assert.Equal("<ol>\n<li>first</li>\n<li>second</li>\n</ol>\n", result.Html);
    }

    [Fact]
    public void ToHtml_With_Table_Emits_Th_And_Td()
    {
        var document = TypstDocument.Create()
            .Table(t => t
                .Columns(TypstLength.Fr(1), TypstLength.Fr(1))
                .HeaderRow("Item", "Amount")
                .Row("Hosting", "1 200 EUR"));

        const string Expected =
            "<table>\n" +
            "<thead>\n" +
            "<tr><th>Item</th><th>Amount</th></tr>\n" +
            "</thead>\n" +
            "<tbody>\n" +
            "<tr><td>Hosting</td><td>1 200 EUR</td></tr>\n" +
            "</tbody>\n" +
            "</table>\n";

        Assert.Equal(Expected, document.ToHtml().Html);
    }

    [Fact]
    public void ToHtml_With_Cell_Alignment_Emits_The_Align_Attribute()
    {
        var document = TypstDocument.Create()
            .Table(t => t
                .HeaderRow(
                    new TypstTableCell("Item") { Align = TypstAlignment.Left },
                    new TypstTableCell("Amount") { Align = TypstAlignment.Right })
                .Row("Hosting", "1 200"));

        var html = document.ToHtml().Html;

        Assert.Contains("<th align=\"left\">Item</th>", html, StringComparison.Ordinal);
        Assert.Contains("<th align=\"right\">Amount</th>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ToHtml_With_Cell_Spans_Emits_Colspan_And_Rowspan()
    {
        var document = TypstDocument.Create()
            .Table(t => t
                .HeaderRow("a", "b")
                .Row(new TypstTableCell("wide", colspan: 2))
                .Row(new TypstTableCell[] { new("tall", rowspan: 2), new("c") }));

        var html = document.ToHtml().Html;

        Assert.Contains("<td colspan=\"2\">wide</td>", html, StringComparison.Ordinal);
        Assert.Contains("<td rowspan=\"2\">tall</td>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ToHtml_With_Code_Block_Emits_Pre_And_The_Language_Class()
    {
        var document = TypstDocument.Create().Code("csharp", "var x = 1 < 2;\nvar y = 3;");

        Assert.Equal(
            "<pre><code class=\"language-csharp\">var x = 1 &lt; 2;\nvar y = 3;</code></pre>\n",
            document.ToHtml().Html);
    }

    [Fact]
    public void ToHtml_With_Quote_Emits_Blockquote()
    {
        var document = TypstDocument.Create().Quote(new TypstContent().Text("quoted"));

        Assert.Equal("<blockquote>\n<p>quoted</p>\n</blockquote>\n", document.ToHtml().Html);
    }

    [Fact]
    public void ToHtml_With_Link_Emits_Anchor_With_Href()
    {
        var document = TypstDocument.Create()
            .Paragraph(c => c.Text("see ").Link("https://example.com/docs", "the docs"));

        Assert.Equal(
            "<p>see <a href=\"https://example.com/docs\">the docs</a></p>\n",
            document.ToHtml().Html);
    }

    [Fact]
    public void ToHtml_With_A_Link_Without_Text_Uses_The_Url()
    {
        var document = TypstDocument.Create().Paragraph(c => c.Link("https://example.com"));

        Assert.Equal(
            "<p><a href=\"https://example.com\">https://example.com</a></p>\n",
            document.ToHtml().Html);
    }

    [Fact]
    public void ToHtml_With_Line_Break_And_Divider_Emits_Br_And_Hr()
    {
        var document = TypstDocument.Create()
            .Paragraph(c => c.Text("a").LineBreak().Text("b"))
            .Divider();

        Assert.Equal("<p>a<br />b</p>\n<hr />\n", document.ToHtml().Html);
    }

    [Fact]
    public void ToHtml_With_Figure_And_Caption_Emits_Img_And_Figcaption()
    {
        var document = TypstDocument.Create()
            .Figure(f => f.Image("assets/logo.png").WithCaption("The logo"));

        Assert.Equal(
            "<figure>\n<img src=\"assets/logo.png\" alt=\"\" />\n<figcaption>The logo</figcaption>\n</figure>\n",
            document.ToHtml().Html);
    }

    [Fact]
    public void ToHtml_With_A_Data_Url_Image_Keeps_The_Data_Url()
    {
        var document = TypstDocument.Create()
            .Figure(f => f.Image("data:image/png;base64,iVBORw0KGgo="));

        Assert.Equal(
            "<figure>\n<img src=\"data:image/png;base64,iVBORw0KGgo=\" alt=\"\" />\n</figure>\n",
            document.ToHtml().Html);
    }

    [Fact]
    public void ToHtml_With_A_Figure_Label_Emits_An_Id()
    {
        var document = TypstDocument.Create()
            .Figure(f => f.Image("assets/logo.png").WithLabel("fig:logo"));

        Assert.Contains("<figure id=\"fig:logo\">", document.ToHtml().Html, StringComparison.Ordinal);
    }

    [Fact]
    public void ToHtml_With_Term_List_Emits_Dt_And_Dd()
    {
        var document = TypstDocument.Create()
            .TermList(("API", "Application Programming Interface"));

        Assert.Equal(
            "<dl>\n<dt>API</dt>\n<dd>Application Programming Interface</dd>\n</dl>\n",
            document.ToHtml().Html);
    }

    [Fact]
    public void ToHtml_With_Display_Math_Emits_A_Math_Div()
    {
        var document = TypstDocument.Create().Math("x^2 + y^2 = z^2");

        Assert.Equal("<div class=\"math\">x^2 + y^2 = z^2</div>\n", document.ToHtml().Html);
    }

    [Fact]
    public void ToHtml_With_Inline_Math_Emits_A_Math_Span()
    {
        var document = TypstDocument.Create().Paragraph(c => c.Text("where ").Math("x > 0"));

        Assert.Equal("<p>where <span class=\"math\">x &gt; 0</span></p>\n", document.ToHtml().Html);
    }

    [Fact]
    public void ToHtml_With_A_Footnote_Emits_A_Footnote_Span()
    {
        var document = TypstDocument.Create()
            .Paragraph(c => c.Text("claim").Footnote(new TypstContent().Text("because")));

        Assert.Equal(
            "<p>claim<span class=\"footnote\">because</span></p>\n",
            document.ToHtml().Html);
    }

    [Fact]
    public void ToHtml_With_A_Label_And_A_Reference_Emits_An_Anchor()
    {
        var document = TypstDocument.Create()
            .Paragraph(c => c.Label("fig:x").Text(" ").Reference("fig:x"));

        Assert.Equal(
            "<p><span id=\"fig:x\"></span> <a href=\"#fig:x\">@fig:x</a></p>\n",
            document.ToHtml().Html);
    }

    [Fact]
    public void ToHtml_With_Special_Characters_Escapes_The_Text()
    {
        var document = TypstDocument.Create().Paragraph("a < b & c > d");

        Assert.Equal("<p>a &lt; b &amp; c &gt; d</p>\n", document.ToHtml().Html);
    }

    [Fact]
    public void ToHtml_With_Special_Characters_Escapes_The_Attributes()
    {
        var document = TypstDocument.Create()
            .Paragraph(c => c.Link("https://example.com/?a=1&b=\"two\"", "q"));

        Assert.Equal(
            "<p><a href=\"https://example.com/?a=1&amp;b=&quot;two&quot;\">q</a></p>\n",
            document.ToHtml().Html);
    }

    [Fact]
    public void ToHtml_With_Added_Content_Emits_A_Paragraph()
    {
        var document = TypstDocument.Create().Add(new TypstContent().Text("bare run"));

        Assert.Equal("<p>bare run</p>\n", document.ToHtml().Html);
    }

    [Fact]
    public void ToHtml_With_Underline_Strike_And_Superscript_Emits_Their_Tags()
    {
        var document = TypstDocument.Create()
            .Paragraph(c => c
                .Underline("u")
                .Text(" ")
                .Strikethrough("s")
                .Text(" x")
                .Superscript("2"));

        Assert.Equal(
            "<p><u>u</u> <s>s</s> x<sup>2</sup></p>\n",
            document.ToHtml().Html);
    }

    [Fact]
    public void ToHtml_With_Styled_Text_Emits_A_Span_And_Keeps_The_Text()
    {
        var document = TypstDocument.Create()
            .Paragraph(c => c.Styled("big", new TypstTextStyle().Size(TypstLength.Pt(20))));

        Assert.Equal("<p><span>big</span></p>\n", document.ToHtml().Html);
        Assert.Equal(0, document.ToHtml().UnrepresentableCount);
    }

    [Fact]
    public void ToHtml_Of_An_Imported_Nested_List_Round_Trips()
    {
        const string Html = "<ul>\n<li>one\n<ul>\n<li>one-a</li>\n</ul>\n</li>\n<li>two</li>\n</ul>";

        var first = TypstDocument.FromHtml(Html).ToHtml();

        Assert.Equal(Html + "\n", first.Html);
        Assert.Equal(first.Html, TypstDocument.FromHtml(first.Html).ToHtml().Html);
    }

    [Fact]
    public void ToHtml_With_An_Empty_Document_Returns_An_Empty_String()
    {
        var result = TypstDocument.Create().ToHtml();

        Assert.Equal(string.Empty, result.Html);
        Assert.Empty(result.Unrepresentable);
        Assert.Equal(0, result.UnrepresentableCount);
    }

    [Fact]
    public void ToHtml_Lists_Raw_Page_Breaks_And_Placement_As_Unrepresentable()
    {
        var document = TypstDocument.Create()
            .Raw("#let x = 1")
            .Raw("#let y = 2")
            .PageBreak()
            .Align(TypstAlignment.Center, "centred")
            .Place(TypstAlignment.Left, "floating");

        var result = document.ToHtml();

        Assert.Equal(5, result.UnrepresentableCount);
        Assert.Equal(2, result.CountOf("raw"));
        Assert.Equal(1, result.CountOf("pagebreak"));
        Assert.Equal(1, result.CountOf("align"));
        Assert.Equal(1, result.CountOf("place"));
        Assert.Equal(0, result.CountOf("paragraph"));
        Assert.Equal(
            ["align", "pagebreak", "place", "raw"],
            result.Unrepresentable.Select(element => element.ElementName).ToArray());
    }

    [Fact]
    public void ToHtml_Counts_Outline_Bibliography_Lorem_And_Metadata()
    {
        var document = TypstDocument.Create()
            .Outline()
            .Bibliography("references.bib")
            .Lorem(10)
            .MetadataEntry("status", "draft");

        var result = document.ToHtml();

        Assert.Equal(4, result.UnrepresentableCount);
        Assert.Equal(1, result.CountOf("outline"));
        Assert.Equal(1, result.CountOf("bibliography"));
        Assert.Equal(1, result.CountOf("lorem"));
        Assert.Equal(1, result.CountOf("metadata"));
    }

    [Fact]
    public void ToHtml_Is_Deterministic()
    {
        var document = BuildFixture();

        var first = document.ToHtml();
        var second = document.ToHtml();

        Assert.Equal(first.Html, second.Html);
        Assert.Equal(first.UnrepresentableCount, second.UnrepresentableCount);
    }

    [Theory]
    [InlineData("<h1>Title</h1>\n<p>Hello <strong>world</strong> and <em>more</em>.</p>")]
    [InlineData("<ul>\n<li>one</li>\n<li>two</li>\n</ul>")]
    [InlineData("<ol>\n<li>first</li>\n<li>second</li>\n</ol>")]
    [InlineData("<blockquote>\n<p>quoted</p>\n</blockquote>")]
    [InlineData("<pre><code class=\"language-rust\">fn main() {}</code></pre>")]
    [InlineData("<table>\n<thead>\n<tr><th>a</th><th>b</th></tr>\n</thead>\n<tbody>\n<tr><td>1</td><td>2</td></tr>\n</tbody>\n</table>")]
    [InlineData("<p>see <a href=\"https://example.com/?a=1&amp;b=2\">the site</a></p>")]
    [InlineData("<p>a <code>x &lt; y</code> b</p>")]
    [InlineData("<dl>\n<dt>API</dt>\n<dd>Application Programming Interface</dd>\n</dl>")]
    public void ToHtml_Of_Imported_Html_Round_Trips_the_Fixture(string html)
    {
        var first = TypstDocument.FromHtml(html).ToHtml();
        var second = TypstDocument.FromHtml(first.Html).ToHtml();

        Assert.Equal(html + "\n", first.Html);
        Assert.Equal(first.Html, second.Html);
    }

    [Fact]
    public void ToHtml_Of_An_Imported_Figure_Resolves_The_Asset_On_The_Way_In()
    {
        const string Html = "<figure>\n<img src=\"logo.png\" alt=\"\" />\n</figure>";

        var options = new TypstHtmlImportOptions
        {
            AssetResolver = source => source == "logo.png" ? "assets/logo.png" : source,
        };

        var first = TypstDocument.FromHtml(Html, options).ToHtml();

        Assert.Equal(
            "<figure>\n<img src=\"assets/logo.png\" alt=\"\" />\n</figure>\n",
            first.Html);
        Assert.Equal(first.Html, TypstDocument.FromHtml(first.Html, options).ToHtml().Html);
    }

    private static TypstDocument BuildFixture()
        => TypstDocument.Create()
            .Heading(1, "Report")
            .Paragraph(c => c.Text("Revenue grew by ").Bold("12%").Text("."))
            .BulletList("one", "two")
            .Table(t => t.HeaderRow("Item").Row("Hosting"))
            .Code("rust", "fn main() {}")
            .Raw("#pagebreak()");
}
