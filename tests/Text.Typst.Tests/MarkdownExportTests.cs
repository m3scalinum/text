using System;
using M3scalinum.Text.Documents;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

/// <summary>
/// The fidelity contract of the Markdown exporter: one case per row of the mapping tables of
/// <c>docs/specifications/06-markdown-and-text-export.md</c> §3 and §3.2, the round trip through the
/// importer, and the report of the constructs Markdown cannot spell.
/// </summary>
public sealed class MarkdownExportTests
{
    private static TypstMarkdownExportResult Export(string markdown)
        => TypstDocument.FromMarkdown(markdown, Options()).ToMarkdown();

    private static TypstMarkdownImportOptions Options()
        => new() { AssetResolver = _ => "logo.png" };

    private static string Write(Action<TypstDocument> build)
    {
        var document = TypstDocument.Create();
        build(document);
        return document.ToMarkdown().Markdown;
    }

    // ---- §3 headings and paragraphs ------------------------------------------------------------

    [Theory]
    [InlineData(1, "# Title")]
    [InlineData(2, "## Title")]
    [InlineData(3, "### Title")]
    [InlineData(4, "#### Title")]
    [InlineData(5, "##### Title")]
    [InlineData(6, "###### Title")]
    public void ToMarkdown_With_Heading_Emits_One_Hash_Per_Level(int level, string expected)
    {
        Assert.Equal(expected + "\n", Write(document => document.Heading(level, "Title")));
    }

    [Fact]
    public void ToMarkdown_With_Paragraph_Emits_Its_Inline_Content()
    {
        Assert.Equal("A paragraph.\n", Write(document => document.Paragraph("A paragraph.")));
    }

    [Fact]
    public void ToMarkdown_Separates_Blocks_With_A_Blank_Line()
    {
        var markdown = Write(document => document
            .Heading(1, "Title")
            .Paragraph("Body")
            .Code("rust", "let x = 1;"));

        Assert.Equal("# Title\n\nBody\n\n```rust\nlet x = 1;\n```\n", markdown);
    }

    [Fact]
    public void ToMarkdown_On_An_Empty_Document_Emits_No_Markdown_And_Reports_Nothing()
    {
        var result = TypstDocument.Create().ToMarkdown();

        Assert.Equal(string.Empty, result.Markdown);
        Assert.Empty(result.Unrepresentable);
        Assert.Equal(0, result.UnrepresentableCount);
    }

    // ---- §3 inline emphasis, code, links -------------------------------------------------------

    [Fact]
    public void ToMarkdown_With_Strong_Emphasis_Emits_Double_Asterisks()
    {
        Assert.Equal("**bold**\n", Write(document => document.Paragraph(content => content.Bold("bold"))));
    }

    [Fact]
    public void ToMarkdown_With_Emphasis_Emits_Single_Asterisks()
    {
        Assert.Equal("*light*\n", Write(document => document.Paragraph(content => content.Emphasis("light"))));
    }

    [Fact]
    public void ToMarkdown_With_Inline_Code_Grows_The_Fence_Past_The_Longest_Backtick_Run()
    {
        Assert.Equal("``a ` b``\n", Write(document => document.Paragraph(content => content.Code("a ` b"))));
    }

    [Fact]
    public void ToMarkdown_With_Inline_Code_That_Starts_Or_Ends_With_A_Backtick_Pads_The_Code()
    {
        Assert.Equal("`` `x ``\n", Write(document => document.Paragraph(content => content.Code("`x"))));
    }

    [Fact]
    public void ToMarkdown_With_Code_Block_Emits_A_Fenced_Block_With_The_Language()
    {
        Assert.Equal("```csharp\nvar x = 1;\n```\n", Write(document => document.Code("csharp", "var x = 1;")));
    }

    [Fact]
    public void ToMarkdown_With_Code_Block_Containing_A_Fence_Emits_A_Longer_Fence()
    {
        var markdown = Write(document => document.Code("md", "```\ninner\n```"));

        Assert.StartsWith("````md\n", markdown, StringComparison.Ordinal);
        Assert.EndsWith("\n````\n", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void ToMarkdown_With_Link_Emits_Its_Text_And_Destination()
    {
        var markdown = Write(document => document.Paragraph(content => content.Link("https://example.com", "the site")));

        Assert.Equal("[the site](https://example.com)\n", markdown);
    }

    [Fact]
    public void ToMarkdown_With_Link_Whose_Text_Is_Its_Destination_Emits_A_Bare_Url()
    {
        var markdown = Write(document => document.Paragraph(content => content.Link("https://example.com")));

        Assert.Equal("https://example.com\n", markdown);
    }

    [Fact]
    public void ToMarkdown_With_Line_Break_Emits_Two_Trailing_Spaces()
    {
        var markdown = Write(document => document.Paragraph(content => content.Text("one").LineBreak().Text("two")));

        Assert.Equal("one  \ntwo\n", markdown);
    }

    [Fact]
    public void ToMarkdown_With_Math_Emits_Dollar_Delimiters()
    {
        Assert.Equal("$x^2$\n", Write(document => document.Paragraph(content => content.Math("x^2"))));
        Assert.Equal("$$x^2$$\n", Write(document => document.Math("x^2")));
    }

    // ---- §3 lists, quotes, tables --------------------------------------------------------------

    [Fact]
    public void ToMarkdown_With_Bullet_List_Emits_Dash_Markers()
    {
        Assert.Equal("- one\n- two\n", Write(document => document.BulletList("one", "two")));
    }

    [Fact]
    public void ToMarkdown_With_Numbered_List_Emits_One_Markers()
    {
        Assert.Equal("1. one\n1. two\n", Write(document => document.NumberedList("one", "two")));
    }

    [Fact]
    public void ToMarkdown_With_Nested_List_Indents_Two_Spaces_Per_Level()
    {
        Assert.Equal("- one\n  - nested\n", Export("- one\n  - nested\n").Markdown);
    }

    [Fact]
    public void ToMarkdown_With_Quote_Prefixes_Every_Line()
    {
        Assert.Equal("> quoted\n", Write(document => document.Quote("quoted")));
    }

    [Fact]
    public void ToMarkdown_With_Quote_Holding_Several_Blocks_Separates_Them()
    {
        Assert.Equal("> first\n>\n> second\n", Export("> first\n>\n> second\n").Markdown);
    }

    [Fact]
    public void ToMarkdown_With_Table_Emits_A_Pipe_Table_With_Its_Alignments()
    {
        var markdown = Write(document => document.Table(table => table
            .Columns(TypstLength.Fr(1), TypstLength.Fr(1), TypstLength.Fr(1))
            .HeaderRow(
                new TypstTableCell("left") { Align = TypstAlignment.Left },
                new TypstTableCell("centre") { Align = TypstAlignment.Center },
                new TypstTableCell("right") { Align = TypstAlignment.Right })
            .Row("a", "b", "c")));

        Assert.Equal("| left | centre | right |\n| :--- | :---: | ---: |\n| a | b | c |\n", markdown);
    }

    [Fact]
    public void ToMarkdown_With_Table_Cell_Containing_A_Pipe_Escapes_It()
    {
        var markdown = Write(document => document.Table(table => table
            .Columns(TypstLength.Fr(1))
            .HeaderRow("a|b")
            .Row("c")));

        Assert.Equal("| a\\|b |\n| --- |\n| c |\n", markdown);
    }

    // ---- §3 figures and rules ------------------------------------------------------------------

    [Fact]
    public void ToMarkdown_With_Figure_Emits_An_Image_Whose_Alt_Is_The_Caption()
    {
        var markdown = Write(document => document.Figure(figure => figure
            .Image("logo.png")
            .WithCaption("The logo")));

        Assert.Equal("![The logo](logo.png)\n", markdown);
    }

    [Fact]
    public void ToMarkdown_With_Figure_Carrying_Markup_In_Its_Caption_Keeps_Only_The_Text()
    {
        var caption = new TypstContent().Text("A ").Bold("bold").Text(" caption");
        var markdown = Write(document => document.Figure(figure => figure.Image("logo.png").WithCaption(caption)));

        Assert.Equal("![A bold caption](logo.png)\n", markdown);
    }

    [Fact]
    public void ToMarkdown_With_Divider_Emits_A_Thematic_Break()
    {
        Assert.Equal("---\n", Write(document => document.Divider()));
    }

    // ---- §3.2 escaping -------------------------------------------------------------------------

    [Theory]
    [InlineData("\\", "\\\\")]
    [InlineData("`", "\\`")]
    [InlineData("*", "\\*")]
    [InlineData("_", "\\_")]
    [InlineData("[", "\\[")]
    [InlineData("]", "\\]")]
    [InlineData("<", "\\<")]
    [InlineData(">", "\\>")]
    public void ToMarkdown_Escapes_Every_Markdown_Special_Character(string text, string expected)
    {
        Assert.Equal(expected + "\n", Write(document => document.Paragraph(text)));
    }

    [Theory]
    [InlineData("- a", "\\- a")]
    [InlineData("+ a", "\\+ a")]
    [InlineData("# a", "\\# a")]
    [InlineData("1. a", "1\\. a")]
    public void ToMarkdown_Escapes_A_Block_Marker_At_The_Start_Of_A_Line(string text, string expected)
    {
        Assert.Equal(expected + "\n", Write(document => document.Paragraph(text)));
    }

    [Fact]
    public void ToMarkdown_Does_Not_Escape_A_Block_Marker_Inside_A_Line()
    {
        Assert.Equal("a - b # c 1. d\n", Write(document => document.Paragraph("a - b # c 1. d")));
    }

    // ---- §5 the round trip ---------------------------------------------------------------------

    [Theory]
    [InlineData("# Title\n")]
    [InlineData("## Section\n")]
    [InlineData("###### Deep\n")]
    [InlineData("A paragraph of text.\n")]
    [InlineData("Text with **strong** and *emphasis* and `code` and a [link](https://example.com).\n")]
    [InlineData("- one\n- two\n  - nested\n  - deeper\n")]
    [InlineData("1. one\n1. two\n")]
    [InlineData("- one\n\n- loose\n")]
    [InlineData("> quoted text\n")]
    [InlineData("> a\n>\n> > nested\n")]
    [InlineData("| Name | Value |\n| :--- | ---: |\n| x | 1 |\n")]
    [InlineData("| only |\n| --- |\n| cell |\n")]
    [InlineData("```csharp\nvar x = 1;\n```\n")]
    [InlineData("---\n")]
    [InlineData("A line  \nand the next\n")]
    [InlineData("![Logo](logo.png)\n")]
    [InlineData("Escaped \\* \\_ \\` \\[ \\] \\< \\> text\n")]
    [InlineData("# Title\n\nA paragraph.\n\n- a list\n\n| h |\n| --- |\n| c |\n")]
    public void ToMarkdown_Round_Trips_A_Markdown_Document_Stably(string markdown)
    {
        Assert.Equal(markdown, Export(markdown).Markdown);
    }

    [Theory]
    [InlineData("# Title\n")]
    [InlineData("- one\n- two\n  - nested\n")]
    [InlineData("| Name | Value |\n| :--- | ---: |\n| x | 1 |\n")]
    [InlineData("> a\n>\n> > nested\n")]
    [InlineData("A line  \nand the next\n")]
    public void ToMarkdown_Is_Idempotent_On_Its_Own_Output(string markdown)
    {
        var once = Export(markdown).Markdown;

        Assert.Equal(once, Export(once).Markdown);
    }

    [Fact]
    public void ToMarkdown_Of_An_Imported_Document_Reports_Nothing_Unrepresentable()
    {
        var result = Export("# Title\n\nA paragraph with a [link](https://example.com).\n\n- one\n- two\n");

        Assert.Empty(result.Unrepresentable);
        Assert.Equal(0, result.UnrepresentableCount);
    }

    // ---- §5 what Markdown cannot spell ---------------------------------------------------------

    [Fact]
    public void ToMarkdown_Counts_Each_Construct_That_Has_No_Markdown_Mapping()
    {
        var cases = new (string Name, TypstDocument Document)[]
        {
            ("raw", TypstDocument.Create().Raw("#rect(width: 1cm, height: 1cm)")),
            ("call", TypstDocument.Create().Align(TypstAlignment.Center, "centred")),
            ("outline", TypstDocument.Create().Outline()),
            ("footnote", TypstDocument.Create().Footnote("a note")),
            ("cite", TypstDocument.Create().Cite("key")),
            ("metadata", TypstDocument.Create().MetadataEntry("key", "value")),
            ("lorem", TypstDocument.Create().Lorem(5)),
            ("bibliography", TypstDocument.Create().Bibliography("refs.bib")),
            ("term-list", TypstDocument.Create().TermList(("term", "description"))),
            ("label", TypstDocument.Create().Paragraph(content => content.Label("intro"))),
            ("reference", TypstDocument.Create().Paragraph(content => content.Reference("intro"))),
            ("strike", TypstDocument.Create().Paragraph(content => content.Strikethrough("gone"))),
            ("underline", TypstDocument.Create().Paragraph(content => content.Underline("underlined"))),
            ("highlight", TypstDocument.Create().Paragraph(content => content.Highlight("lit"))),
            ("preamble", TypstDocument.Create().Set("text(size: 10pt)")),
            ("page-setup", TypstDocument.Create().Page(page => page.Paper("a4"))),
            ("document-metadata", TypstDocument.Create().Metadata(metadata => metadata.Title("T"))),
        };

        foreach (var (name, document) in cases)
        {
            var result = document.ToMarkdown();

            Assert.Equal(1, result.CountOf(name));
            Assert.Equal(1, result.UnrepresentableCount);
        }
    }

    [Fact]
    public void ToMarkdown_With_Table_Span_Counts_The_Cell_And_Keeps_Its_Text()
    {
        var result = TypstDocument.Create()
            .Table(table => table
                .Columns(TypstLength.Fr(1), TypstLength.Fr(1))
                .HeaderRow(new TypstTableCell("spans", colspan: 2))
                .Row("a", "b"))
            .ToMarkdown();

        Assert.Equal(1, result.CountOf("table-span"));
        Assert.Contains("spans", result.Markdown, StringComparison.Ordinal);
        Assert.Contains("| a | b |", result.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void ToMarkdown_Keeps_The_Text_Of_A_Construct_It_Cannot_Spell()
    {
        var result = TypstDocument.Create()
            .Paragraph(content => content.Text("a ").Strikethrough("gone").Text(" b"))
            .ToMarkdown();

        Assert.Equal("a gone b\n", result.Markdown);
        Assert.Single(result.Unrepresentable);
    }

    [Fact]
    public void ToMarkdown_Reports_The_Constructs_Sorted_By_Name()
    {
        var result = TypstDocument.Create()
            .Raw("#rect(width: 1cm, height: 1cm)")
            .Cite("key")
            .Outline()
            .ToMarkdown();

        Assert.Equal(["cite", "outline", "raw"], Names(result));
    }

    [Fact]
    public void ToMarkdown_Counts_Every_Occurrence_Of_A_Construct()
    {
        var result = TypstDocument.Create()
            .Raw("#rect(width: 1cm, height: 1cm)")
            .Raw("#circle(radius: 1cm)")
            .ToMarkdown();

        Assert.Equal(2, result.CountOf("raw"));
        Assert.Equal(2, result.UnrepresentableCount);
    }

    [Fact]
    public void ToMarkdown_On_A_Document_Built_In_C_Sharp_Writes_Markdown_Without_Compiling()
    {
        var result = TypstDocument.Create()
            .Metadata(metadata => metadata.Title("Report"))
            .Heading(1, "Report")
            .Paragraph(content => content.Text("Built with ").Link("https://example.com", "the model").Text("."))
            .BulletList("one", "two")
            .Table(table => table
                .Columns(TypstLength.Fr(1), TypstLength.Fr(1))
                .HeaderRow("Name", "Value")
                .Row("x", "1"))
            .ToMarkdown();

        var expected = "# Report\n\n"
            + "Built with [the model](https://example.com).\n\n"
            + "- one\n- two\n\n"
            + "| Name | Value |\n| --- | --- |\n| x | 1 |\n";

        Assert.Equal(expected, result.Markdown);
        Assert.Equal(1, result.CountOf("document-metadata"));
    }

    private static string[] Names(TypstMarkdownExportResult result)
    {
        var names = new string[result.Unrepresentable.Count];

        for (var index = 0; index < names.Length; index++)
        {
            names[index] = result.Unrepresentable[index].ConstructName;
        }

        return names;
    }
}
