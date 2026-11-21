using System;
using M3scalinum.Text.Documents;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

public sealed class TextDecorationTests
{
    private static string Render(Action<TypstContent> build)
    {
        var content = new TypstContent();
        build(content);
        return TypstDocument.Create().Paragraph(content).ToMarkup();
    }

    [Fact]
    public void Emits_each_decoration_through_its_own_method()
    {
        var markup = Render(c => c
            .Upper("a")
            .Lower("b")
            .SmallCaps("c")
            .Highlight("d")
            .Underline("e")
            .Overline("f")
            .Strikethrough("g")
            .Subscript("h")
            .Superscript("i"));

        Assert.Contains("#upper[a]", markup, StringComparison.Ordinal);
        Assert.Contains("#lower[b]", markup, StringComparison.Ordinal);
        Assert.Contains("#smallcaps[c]", markup, StringComparison.Ordinal);
        Assert.Contains("#highlight[d]", markup, StringComparison.Ordinal);
        Assert.Contains("#underline[e]", markup, StringComparison.Ordinal);
        Assert.Contains("#overline[f]", markup, StringComparison.Ordinal);
        Assert.Contains("#strike[g]", markup, StringComparison.Ordinal);
        Assert.Contains("#sub[h]", markup, StringComparison.Ordinal);
        Assert.Contains("#super[i]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_lorem_footnote_cite_and_metadata()
    {
        var markup = Render(c => c
            .Lorem(20)
            .Footnote("note")
            .Cite("key")
            .Cite("key", prose: true)
            .Metadata("source", "erp", "meta:source"));

        Assert.Contains("#lorem(20)", markup, StringComparison.Ordinal);
        Assert.Contains("#footnote[note]", markup, StringComparison.Ordinal);
        Assert.Contains("@key", markup, StringComparison.Ordinal);
        Assert.Contains("#cite(<key>, form: \"prose\")", markup, StringComparison.Ordinal);
        Assert.Contains("#metadata((source: \"erp\")) <meta:source>", markup, StringComparison.Ordinal);
    }
}