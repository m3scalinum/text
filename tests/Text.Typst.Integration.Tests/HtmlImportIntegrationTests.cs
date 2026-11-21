using System;
using M3scalinum.Text.Typst.Compilation;
using M3scalinum.Text.Documents;
using Xunit;

namespace M3scalinum.Text.Typst.Integration.Tests;

/// <summary>An imported HTML document, compiled by the real compiler.</summary>
[Trait("Category", "Integration")]
public sealed class HtmlImportIntegrationTests
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

    private const string Page = """
        <!DOCTYPE html>
        <html>
          <head>
            <title>Quarterly report</title>
            <style>body { font-family: serif; }</style>
          </head>
          <body>
            <h1>Quarterly report</h1>
            <p>Revenue grew by <strong>12%</strong> and costs <em>fell</em>.</p>
            <ul>
              <li>Hosting</li>
              <li>Support</li>
            </ul>
            <table>
              <thead><tr><th>Item</th><th>Amount</th></tr></thead>
              <tbody>
                <tr><td colspan="2">Total</td></tr>
                <tr><td>Hosting</td><td>1 200 EUR</td></tr>
              </tbody>
            </table>
            <pre><code class="language-rust">fn main() {}</code></pre>
            <blockquote><p>Numbers are provisional.</p></blockquote>
          </body>
        </html>
        """;

    [Fact]
    public void Compiles_an_imported_page()
    {
        var markup = TypstDocument.FromHtml(Page).ToMarkup();
        using var world = new TypstWorldBuilder().AddSource("main.typ", markup).Build();

        using var result = new TypstCompiler().Compile(world);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, result.PageCount);

        // Plain text is derived from the HTML document, so it needs an HTML-kind compilation.
        var text = TypstCompiler.CompileToText(markup);

        Assert.Contains("Revenue grew by 12% and costs fell.", text, StringComparison.Ordinal);
        Assert.Contains("Hosting", text, StringComparison.Ordinal);
        Assert.Contains("Numbers are provisional.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Keeps_the_heading_and_the_table_in_the_markdown_export()
    {
        var markdown = TypstCompiler.CompileToMarkdown(TypstDocument.FromHtml(Page).ToMarkup());

        Assert.Contains("Quarterly report", markdown, StringComparison.Ordinal);
        Assert.Contains("Total", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Compiles_an_imported_image_against_a_registered_asset()
    {
        var options = new TypstHtmlImportOptions
        {
            AssetResolver = source => source == "charts/revenue.png" ? "pixel.png" : null,
        };

        var document = TypstDocument.FromHtml(
            "<h2>Chart</h2><p>Below.</p><img src=\"charts/revenue.png\" alt=\"Revenue\">",
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
    public void Compiles_text_that_is_typst_markup_in_the_source_html()
    {
        const string html = """
            <p>Cost: 5 * 3 = 15, not a heading.</p>
            <p># not code, @ not a reference, [not a block], &lt;not a label&gt;, $not math$.</p>
            <p><a href="https://example.com/a?x=*_#$&amp;y=[1]">a link with markup in its URL</a></p>
            """;

        var markup = TypstDocument.FromHtml(html).ToMarkup();
        using var world = new TypstWorldBuilder().AddSource("main.typ", markup).Build();

        using var result = new TypstCompiler().Compile(world);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, result.PageCount);

        var text = TypstCompiler.CompileToText(markup);

        Assert.Contains("Cost: 5 * 3 = 15, not a heading.", text, StringComparison.Ordinal);
        Assert.Contains("# not code, @ not a reference", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Compiles_a_page_that_only_lists_nested_items()
    {
        const string html = """
            <ol>
              <li>First<ol><li>Nested one</li><li>Nested two</li></ol></li>
              <li>Second</li>
            </ol>
            """;

        var markup = TypstDocument.FromHtml(html).ToMarkup();
        using var world = new TypstWorldBuilder().AddSource("main.typ", markup).Build();

        using var result = new TypstCompiler().Compile(world);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, result.PageCount);
        Assert.Contains("Nested two", TypstCompiler.CompileToText(markup), StringComparison.Ordinal);
    }
}
