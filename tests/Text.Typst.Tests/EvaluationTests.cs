using M3scalinum.Text.Typst.Compilation;
using M3scalinum.Text.Typst.Native;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

/// <summary>Parsing of the evaluation payload into the public model.</summary>
public sealed class EvaluationTests
{
    private static CompilationResult Compile(string evaluationJson)
    {
        var native = new FakeTypstNative { EvaluationJson = evaluationJson };
        var world = new TypstWorldBuilder(native).AddSource("main.typ", "= Title").Build();
        return new TypstCompiler(native).Compile(world);
    }

    [Fact]
    public void Reads_the_page_count()
    {
        using var result = Compile("""{"pageCount":7,"outline":[],"labels":[],"metadata":[]}""");

        Assert.Equal(7, result.Evaluate().PageCount);
    }

    [Fact]
    public void Reads_the_outline()
    {
        using var result = Compile("""
            {
              "pageCount": 2,
              "outline": [
                { "level": 1, "title": "Introduction", "page": 1, "x": 0, "y": 72.5, "label": "intro" },
                { "level": 2, "title": "Details", "page": 2, "x": 0, "y": 100, "label": null }
              ],
              "labels": [],
              "metadata": []
            }
            """);

        var outline = result.Evaluate().Outline;

        Assert.Equal(2, outline.Count);
        Assert.Equal(1, outline[0].Level);
        Assert.Equal("Introduction", outline[0].Title);
        Assert.Equal(1, outline[0].Page);
        Assert.Equal(72.5, outline[0].Y);
        Assert.Equal("intro", outline[0].Label);
        Assert.Null(outline[1].Label);
    }

    [Fact]
    public void Reads_the_labels()
    {
        using var result = Compile("""
            {
              "pageCount": 1,
              "outline": [],
              "labels": [ { "name": "figure-1", "page": 1, "x": 10, "y": 20 } ],
              "metadata": []
            }
            """);

        var labels = result.Evaluate().Labels;

        Assert.Single(labels);
        Assert.Equal("figure-1", labels[0].Name);
        Assert.Equal(10, labels[0].X);
        Assert.Equal(20, labels[0].Y);
    }

    [Fact]
    public void Reads_the_metadata()
    {
        using var result = Compile("""
            {
              "pageCount": 1,
              "outline": [],
              "labels": [],
              "metadata": [ { "value": { "id": 42, "kind": "report" }, "page": 1 } ]
            }
            """);

        var metadata = result.Evaluate().Metadata;

        Assert.Single(metadata);
        Assert.Equal(1, metadata[0].Page);
        Assert.Equal(42, metadata[0].Value.GetProperty("id").GetInt32());
        Assert.Equal("report", metadata[0].Value.GetProperty("kind").GetString());
    }

    [Fact]
    public void Tolerates_an_empty_payload()
    {
        using var result = Compile("{}");

        var evaluation = result.Evaluate();

        Assert.Equal(0, evaluation.PageCount);
        Assert.Empty(evaluation.Outline);
        Assert.Empty(evaluation.Labels);
        Assert.Empty(evaluation.Metadata);
    }

    [Fact]
    public void Surfaces_a_native_failure()
    {
        var native = new FakeTypstNative
        {
            ExportFailure = new NativeCallException(
                NativeStatus.InvalidArgument,
                """{"code":"invalid-argument","message":"evaluation requires a paged document"}""",
                "evaluate"),
        };

        var world = new TypstWorldBuilder(native).AddSource("main.typ", "= Title").Build();
        using var result = new TypstCompiler(native).Compile(world);

        Assert.Throws<TypstNativeException>(() => result.Evaluate());
    }
}
