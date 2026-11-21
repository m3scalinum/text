using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using M3scalinum.Text.Docx;
using M3scalinum.Text.Docx.Internal;
using Xunit;

namespace M3scalinum.Text.Docx.Tests;

/// <summary>
/// The mapping from the AST the native reader writes to the public tree.
/// </summary>
/// <remarks>
/// These read the same JSON the native side produces (<c>ast.rs</c> serialises a run as
/// <c>{"type":"break"}</c>, a subscript run as <c>{"type":"text","script":"subscript"}</c>) rather than
/// building the DTOs in code: what is pinned here is the wire shape as well as the mapping, so a
/// rename on either side fails a test instead of silently dropping an inline.
/// </remarks>
public sealed class DocxModelTests
{
    [Fact]
    public void A_break_run_becomes_a_line_break_and_a_newline_in_the_text()
    {
        var document = Build(
            """
            {"blocks":[{"type":"paragraph","runs":[
              {"type":"text","text":"before"},
              {"type":"break"},
              {"type":"text","text":"after"}]}]}
            """);

        var paragraph = Assert.IsType<DocxParagraph>(Assert.Single(document.Blocks));

        Assert.Equal(3, paragraph.Inlines.Count);
        Assert.IsType<DocxLineBreak>(paragraph.Inlines[1]);
        Assert.Equal("before\nafter", paragraph.Text);
    }

    [Fact]
    public void A_subscript_run_keeps_its_script()
    {
        var document = Build(
            """
            {"blocks":[{"type":"paragraph","runs":[
              {"type":"text","text":"H2O","script":"subscript"}]}]}
            """);

        var run = Assert.IsType<DocxTextRun>(
            Assert.IsType<DocxParagraph>(Assert.Single(document.Blocks)).Inlines[0]);

        Assert.Equal(DocxScript.Subscript, run.Script);
    }

    [Fact]
    public void An_ast_with_no_blocks_is_an_empty_document()
    {
        var document = Build("""{"blocks":null}""");

        Assert.Empty(document.Blocks);
        Assert.Null(document.Page);
    }

    [Fact]
    public void A_block_of_an_unknown_type_is_skipped_rather_than_failing_the_document()
    {
        var document = Build(
            """
            {"blocks":[
              {"type":"footnote","runs":[{"type":"text","text":"ignored"}]},
              {"type":"paragraph","runs":[{"type":"text","text":"kept"}]}]}
            """);

        var paragraph = Assert.IsType<DocxParagraph>(Assert.Single(document.Blocks));

        Assert.Equal("kept", paragraph.Text);
    }

    private static DocxDocument Build(string json)
    {
        var ast = JsonSerializer.Deserialize(json, DocxJsonContext.Default.DocxAstDto)!;

        return DocxModel.Build(ast, new Dictionary<string, byte[]>());
    }
}
