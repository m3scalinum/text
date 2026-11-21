using System;
using M3scalinum.Text.Typst.Compilation;
using Xunit;

namespace M3scalinum.Text.Typst.Integration.Tests;

/// <summary>
/// Markdown and plain text, derived from the semantic HTML tree.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SemanticExportTests
{
    private const string Markup = """
        = Report

        A paragraph with *strong*, _emphasis_ and `code`.

        == Details

        - first item
        - second item with a #link("https://example.com")[link]

        + one
        + two

        #table(
          columns: 2,
          [Name], [Value],
          [alpha], [1],
          [beta], [2],
        )

        ```rust
        fn main() {}
        ```

        > A quote.
        """;

    private static CompilationResult Compile()
    {
        var world = new TypstWorldBuilder().AddSource("main.typ", Markup).Build();
        var result = new TypstCompiler().Compile(
            world, new TypstCompilationOptions { DocumentKind = TypstDocumentKind.Html });
        world.Dispose();
        return result;
    }

    [Fact]
    public void Markdown_has_headings_emphasis_and_code()
    {
        using var result = Compile();

        var markdown = result.ExportMarkdown();

        // Typst reserves h1 for the document title, so `= Report` becomes h2.
        Assert.Contains("## Report", markdown, StringComparison.Ordinal);
        Assert.Contains("### Details", markdown, StringComparison.Ordinal);
        Assert.Contains("**strong**", markdown, StringComparison.Ordinal);
        Assert.Contains("*emphasis*", markdown, StringComparison.Ordinal);
        Assert.Contains("`code`", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_has_lists_tables_and_links()
    {
        using var result = Compile();

        var markdown = result.ExportMarkdown();

        Assert.Contains("- first item", markdown, StringComparison.Ordinal);
        Assert.Contains("1. one", markdown, StringComparison.Ordinal);
        Assert.Contains("| Name | Value |", markdown, StringComparison.Ordinal);
        Assert.Contains("| --- | --- |", markdown, StringComparison.Ordinal);
        Assert.Contains("](https://example.com)", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_has_a_fenced_code_block_with_its_language()
    {
        using var result = Compile();

        var markdown = result.ExportMarkdown();

        Assert.Contains("```rust", markdown, StringComparison.Ordinal);
        Assert.Contains("fn main() {}", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_quotes_are_prefixed()
    {
        using var result = Compile();

        var markdown = result.ExportMarkdown();

        Assert.Contains("> A quote.", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_has_underlined_headings_and_no_markup()
    {
        using var result = Compile();

        var text = result.ExportText();

        Assert.Contains("Report\n------", text, StringComparison.Ordinal);
        Assert.Contains("Details", text, StringComparison.Ordinal);
        Assert.DoesNotContain("**", text, StringComparison.Ordinal);
        Assert.DoesNotContain("```", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_keeps_list_markers_and_table_cells()
    {
        using var result = Compile();

        var text = result.ExportText();

        Assert.Contains("- first item", text, StringComparison.Ordinal);
        Assert.Contains("Name | Value", text, StringComparison.Ordinal);
        Assert.Contains("alpha | 1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_shows_link_targets()
    {
        using var result = Compile();

        var text = result.ExportText();

        Assert.Contains("link (https://example.com)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Semantic_export_requires_an_html_document()
    {
        using var world = new TypstWorldBuilder().AddSource("main.typ", "= Title").Build();
        using var paged = new TypstCompiler().Compile(world);

        var exception = Assert.Throws<TypstNativeException>(() => paged.ExportMarkdown());

        Assert.Equal(TypstErrorCode.InvalidArgument, exception.Code);
    }

    [Fact]
    public void Markdown_is_deterministic()
    {
        using var first = Compile();
        using var second = Compile();

        Assert.Equal(first.ExportMarkdown(), second.ExportMarkdown());
    }
}
