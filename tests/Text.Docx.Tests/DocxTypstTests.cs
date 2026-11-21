using System.IO;
using M3scalinum.Text.Docx;
using M3scalinum.Text.SharpTypst.Compilation;
using M3scalinum.Text.SharpTypst.Documents;
using Xunit;

namespace M3scalinum.Text.Docx.Tests;

/// <summary>From a Word file to Typst source, and from Typst source to text.</summary>
/// <remarks>
/// The last test drives the real compiler: it reads the committed package, converts it, registers
/// its images as world assets, compiles, and reads the text back. That is the whole claim of the
/// package in one test — a Word document renders through the suite's own model.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class DocxTypstTests
{
    [Fact]
    public void Headings_become_typst_headings()
    {
        var markup = Read().ToTypstDocument().ToMarkup();

        Assert.Contains($"= {DocxFixture.HeadingOne}", markup, System.StringComparison.Ordinal);
        Assert.Contains($"== {DocxFixture.HeadingTwo}", markup, System.StringComparison.Ordinal);
    }

    [Fact]
    public void A_spanning_cell_becomes_a_spanning_typst_cell()
    {
        var markup = Read().ToTypstDocument().ToMarkup();

        Assert.Contains("colspan: 2", markup, System.StringComparison.Ordinal);
        Assert.Contains("rowspan: 2", markup, System.StringComparison.Ordinal);
    }

    [Fact]
    public void The_page_setup_becomes_a_page_rule()
    {
        var markup = Read().ToTypstDocument().ToMarkup();

        Assert.Contains("#set page(", markup, System.StringComparison.Ordinal);
        Assert.Contains("595.3pt", markup, System.StringComparison.Ordinal);
        Assert.Contains("72pt", markup, System.StringComparison.Ordinal);
    }

    [Fact]
    public void An_image_becomes_a_figure_of_its_part()
    {
        var markup = Read().ToTypstDocument().ToMarkup();

        Assert.Contains($"image(\"{DocxFixture.ImagePart}\")", markup, System.StringComparison.Ordinal);
    }

    [Fact]
    public void A_word_document_compiles_and_its_text_exports()
    {
        var document = Read();

        using var result = Compile(document);

        var text = result.ExportText();

        Assert.Contains(DocxFixture.HeadingOne, text, System.StringComparison.Ordinal);
        Assert.Contains(DocxFixture.HeadingTwo, text, System.StringComparison.Ordinal);
        Assert.Contains("twelve percent", text, System.StringComparison.Ordinal);
        Assert.Contains(DocxFixture.StyledText, text, System.StringComparison.Ordinal);
        Assert.Contains(DocxFixture.BulletOne, text, System.StringComparison.Ordinal);
        Assert.Contains(DocxFixture.BulletTwo, text, System.StringComparison.Ordinal);
        Assert.Contains(DocxFixture.MergedCellText, text, System.StringComparison.Ordinal);
        Assert.Contains("Middle below", text, System.StringComparison.Ordinal);
    }

    private static DocxDocument Read() => DocxReader.Read(File.ReadAllBytes(FixtureTests.Path));

    /// <summary>
    /// Compiles the document with its images registered as assets: an <c>image("…")</c> call whose
    /// file the world does not hold is a compile error, so this is also what proves the part names
    /// line up between the AST and the media table.
    /// </summary>
    /// <remarks>
    /// The HTML layout is the one that yields text and Markdown; the paged layout is what produces
    /// PDF, SVG and PNG. Both run from the same source.
    /// </remarks>
    private static CompilationResult Compile(DocxDocument document)
    {
        var builder = new TypstWorldBuilder()
            .AddSource("main.typ", document.ToTypstDocument().ToMarkup());

        foreach (var (name, bytes) in document.Images)
        {
            builder.AddAsset(name, bytes);
        }

        var world = builder.Build();

        try
        {
            return new TypstCompiler().Compile(
                world, new TypstCompilationOptions { DocumentKind = TypstDocumentKind.Html });
        }
        finally
        {
            world.Dispose();
        }
    }
}
