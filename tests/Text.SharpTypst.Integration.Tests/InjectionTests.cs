using System;
using M3scalinum.Text.SharpTypst.Compilation;
using M3scalinum.Text.SharpTypst.Documents;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Integration.Tests;

/// <summary>
/// Hostile input must stay text. Every case here would change the document — or fail the
/// compilation — if the writer let it through unescaped.
/// </summary>
[Trait("Category", "Integration")]
public sealed class InjectionTests
{
    private static string RenderText(TypstDocument document)
    {
        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", document.ToMarkup())
            .Build();

        using var result = new TypstCompiler().Compile(
            world,
            new TypstCompilationOptions { DocumentKind = TypstDocumentKind.Html });

        // HTML export emits an "under development" warning; only errors would matter here, and a
        // compilation error throws before this point.
        // Typst applies typographic quotes to plain text, so `"` comes back as `“`/`”`.
        return result.ExportText()
            .Replace('\u201c', '"')
            .Replace('\u201d', '"');
    }

    [Theory]
    [InlineData("#panic(\"boom\")")]
    [InlineData("#let x = 1")]
    [InlineData("#set text(red)")]
    [InlineData("#import \"@preview/cetz:0.3.1\": canvas")]
    [InlineData("$x^2$")]
    [InlineData("*bold*")]
    [InlineData("_italic_")]
    [InlineData("`code`")]
    [InlineData("@reference")]
    [InlineData("<label>")]
    [InlineData("// line comment")]
    [InlineData("/* block comment */")]
    [InlineData("= heading")]
    [InlineData("- list item")]
    [InlineData("+ numbered item")]
    [InlineData("/ term")]
    [InlineData("~non-breaking")]
    [InlineData("\\ backslash")]
    public void Paragraph_text_is_never_interpreted(string hostile)
    {
        var text = RenderText(TypstDocument.Create().Paragraph(hostile));

        Assert.Contains(hostile, text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_heading_title_is_never_interpreted()
    {
        var text = RenderText(TypstDocument.Create().Heading(1, "#panic(\"boom\")"));

        Assert.Contains("#panic(\"boom\")", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_table_cell_is_never_interpreted()
    {
        var document = TypstDocument.Create().Table(table =>
        {
            table.HeaderRow("#panic(\"boom\")");
            table.Row("#let x = 1");
        });

        var text = RenderText(document);

        Assert.Contains("#panic(\"boom\")", text, StringComparison.Ordinal);
        Assert.Contains("#let x = 1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_list_item_is_never_interpreted()
    {
        var text = RenderText(TypstDocument.Create().BulletList("#panic(\"boom\")", "- nested"));

        Assert.Contains("#panic(\"boom\")", text, StringComparison.Ordinal);
        Assert.Contains("- nested", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_code_block_keeps_its_content_verbatim()
    {
        var text = RenderText(TypstDocument.Create().Code("typst", "#panic(\"boom\")"));

        Assert.Contains("#panic(\"boom\")", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_quote_is_never_interpreted()
    {
        var text = RenderText(TypstDocument.Create().Quote("#panic(\"boom\")"));

        Assert.Contains("#panic(\"boom\")", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_link_target_cannot_break_out_of_its_string()
    {
        var document = TypstDocument.Create().Paragraph(content =>
            content.Link("https://example.com/\") + #panic(\"boom\") + (\"", "click"));

        var text = RenderText(document);

        Assert.Contains("click", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_metadata_value_cannot_break_out_of_its_string()
    {
        var document = TypstDocument.Create()
            .MetadataEntry("id", "\") + #panic(\"boom\") + (\"")
            .Paragraph("body");

        var text = RenderText(document);

        Assert.Contains("body", text, StringComparison.Ordinal);
    }
}
