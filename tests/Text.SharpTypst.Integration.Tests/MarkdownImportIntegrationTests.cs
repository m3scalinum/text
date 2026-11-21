using System;
using M3scalinum.Text.SharpTypst.Compilation;
using M3scalinum.Text.SharpTypst.Documents;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Integration.Tests;

/// <summary>A Markdown document imported into the model, compiled by the real compiler.</summary>
[Trait("Category", "Integration")]
public sealed class MarkdownImportIntegrationTests
{
    /// <summary>A 1×1 transparent PNG.</summary>
    private static readonly byte[] Pixel =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];

    private const string Readme = """
        # Quarterly report

        Revenue grew by **12%** and costs *fell*.<sup>1</sup>

        ## Items

        - Hosting
        - Support
          - Priority support
        - Travel

        1. First
        2. Second

        > Numbers are provisional.

        | Item | Amount |
        | :--- | ---: |
        | Hosting | 1 200 EUR |
        | Support | 800 EUR |

        ```rust
        fn main() {
            let total = 2000;
        }
        ```

        Read the [full report](https://example.com/report) for the method.[^method]

        ---

        [^method]: The method is described in appendix B.
        """;

    [Fact]
    public void Compiles_an_imported_readme()
    {
        var markup = TypstDocument.FromMarkdown(Readme).ToMarkup();
        using var world = new TypstWorldBuilder().AddSource("main.typ", markup).Build();

        using var result = new TypstCompiler().Compile(world);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, result.PageCount);

        var text = TypstCompiler.CompileToText(markup);

        Assert.Contains("Revenue grew by 12% and costs fell.", text, StringComparison.Ordinal);
        Assert.Contains("Priority support", text, StringComparison.Ordinal);
        Assert.Contains("Numbers are provisional.", text, StringComparison.Ordinal);
        Assert.Contains("800 EUR", text, StringComparison.Ordinal);
        Assert.Contains("The method is described in appendix B.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Compiles_a_table_inside_a_list_inside_a_quote()
    {
        const string markdown = """
            > 1. item
            >
            >    | a | b |
            >    | - | - |
            >    | 1 | 2 |
            """;

        var markup = TypstDocument.FromMarkdown(markdown).ToMarkup();
        using var world = new TypstWorldBuilder().AddSource("main.typ", markup).Build();

        using var result = new TypstCompiler().Compile(world);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, result.PageCount);
        Assert.Contains("item", TypstCompiler.CompileToText(markup), StringComparison.Ordinal);
    }

    [Fact]
    public void Compiles_an_imported_image_against_a_registered_asset()
    {
        var options = new TypstMarkdownImportOptions
        {
            AssetResolver = source => source == "charts/revenue.png" ? "pixel.png" : null,
        };

        var document = TypstDocument.FromMarkdown(
            "## Chart\n\nBelow.\n\n![Revenue](charts/revenue.png)\n",
            options);

        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", document.ToMarkup())
            .AddAsset("pixel.png", Pixel)
            .Build();

        using var result = new TypstCompiler().Compile(world);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, result.PageCount);
    }

    [Fact]
    public void Compiles_text_that_is_typst_markup_in_the_source_markdown()
    {
        const string markdown = """
            Cost: 5 * 3 = 15, not emphasis.

            A hash # is not a heading here, @ is not a reference, [brackets] are text, <angle> too.

            A [link](https://example.com/a?x=*_#&y=[1]) with markup in its URL.
            """;

        var markup = TypstDocument.FromMarkdown(markdown).ToMarkup();
        using var world = new TypstWorldBuilder().AddSource("main.typ", markup).Build();

        using var result = new TypstCompiler().Compile(world);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, result.PageCount);

        var text = TypstCompiler.CompileToText(markup);

        Assert.Contains("Cost: 5 * 3 = 15, not emphasis.", text, StringComparison.Ordinal);
        Assert.Contains("A hash # is not a heading here, @ is not a reference", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Compiles_a_document_of_several_pages()
    {
        var document = TypstDocument.FromMarkdown("# One\n\nFirst page.\n").PageBreak().Paragraph("Second page.");

        using var world = new TypstWorldBuilder().AddSource("main.typ", document.ToMarkup()).Build();

        using var result = new TypstCompiler().Compile(world);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(2, result.PageCount);
    }
}
