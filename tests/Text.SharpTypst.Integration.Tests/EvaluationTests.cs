using M3scalinum.Text.SharpTypst.Compilation;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Integration.Tests;

/// <summary>Introspection against the real compiler.</summary>
[Trait("Category", "Integration")]
public sealed class EvaluationTests
{
    private const string Document = """
        #set page(paper: "a4")

        #metadata((id: 42, kind: "report")) <meta>

        = Introduction <intro>

        Some text.

        == Details <details>

        More text.

        #pagebreak()

        = Conclusion <conclusion>

        The end.
        """;

    private static TypstEvaluation Evaluate(string markup)
    {
        using var world = new TypstWorldBuilder().AddSource("main.typ", markup).Build();
        using var result = new TypstCompiler().Compile(world);

        Assert.Empty(result.Diagnostics);
        return result.Evaluate();
    }

    [Fact]
    public void Reports_the_page_count()
    {
        Assert.Equal(2, Evaluate(Document).PageCount);
    }

    [Fact]
    public void Reports_the_outline_in_document_order()
    {
        var outline = Evaluate(Document).Outline;

        Assert.Equal(3, outline.Count);

        Assert.Equal(1, outline[0].Level);
        Assert.Equal("Introduction", outline[0].Title);
        Assert.Equal(1, outline[0].Page);
        Assert.Equal("intro", outline[0].Label);

        Assert.Equal(2, outline[1].Level);
        Assert.Equal("Details", outline[1].Title);
        Assert.Equal(1, outline[1].Page);

        Assert.Equal(1, outline[2].Level);
        Assert.Equal("Conclusion", outline[2].Title);
        Assert.Equal(2, outline[2].Page);
    }

    [Fact]
    public void Reports_the_labels_with_their_page()
    {
        var labels = Evaluate(Document).Labels;

        Assert.Contains(labels, l => l.Name == "intro" && l.Page == 1);
        Assert.Contains(labels, l => l.Name == "details" && l.Page == 1);
        Assert.Contains(labels, l => l.Name == "conclusion" && l.Page == 2);
        Assert.Contains(labels, l => l.Name == "meta" && l.Page == 1);
    }

    [Fact]
    public void Reports_the_metadata_values()
    {
        var metadata = Evaluate(Document).Metadata;

        var entry = Assert.Single(metadata);
        Assert.Equal(1, entry.Page);
        Assert.Equal(42, entry.Value.GetProperty("id").GetInt32());
        Assert.Equal("report", entry.Value.GetProperty("kind").GetString());
    }

    [Fact]
    public void An_empty_document_has_an_empty_evaluation()
    {
        var evaluation = Evaluate("Nothing to see.");

        Assert.Equal(1, evaluation.PageCount);
        Assert.Empty(evaluation.Outline);
        Assert.Empty(evaluation.Labels);
        Assert.Empty(evaluation.Metadata);
    }

    [Fact]
    public void Heading_positions_are_ordered_down_the_page()
    {
        var outline = Evaluate(Document).Outline;

        Assert.True(outline[0].Y < outline[1].Y);
        Assert.All(outline, entry => Assert.True(entry.X >= 0));
    }

    [Fact]
    public void Evaluation_requires_a_paged_document()
    {
        using var world = new TypstWorldBuilder().AddSource("main.typ", "= Title").Build();
        using var result = new TypstCompiler().Compile(
            world,
            new TypstCompilationOptions { DocumentKind = TypstDocumentKind.Html });

        Assert.Throws<TypstNativeException>(() => result.Evaluate());
    }
}
