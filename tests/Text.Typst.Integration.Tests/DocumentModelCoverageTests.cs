using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using M3scalinum.Text.Typst.Compilation;
using M3scalinum.Text.Typst.Diagnostics;
using M3scalinum.Text.Documents;
using M3scalinum.Text.Typst.Export;
using M3scalinum.Text.Typst.Templates;
using Xunit;

namespace M3scalinum.Text.Typst.Integration.Tests;

/// <summary>
/// Compiles every node of the document model, so a broken <c>ToMarkup()</c> is caught here rather
/// than by a user.
/// </summary>
/// <remarks>
/// The unit tests assert the emitted markup; these assert that the compiler accepts it. The two
/// failures found while building the model — a colour name Typst does not have, and a <c>#text</c>
/// call without a body — were both of this kind.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class DocumentModelCoverageTests
{
    private const string Bibliography = """
        @article{knuth1984,
          title = {Literate Programming},
          author = {Knuth, Donald E.},
          year = {1984},
        }
        """;

    private static void CompilesCleanly(TypstDocument document, params (string Path, string Text)[] assets)
    {
        var builder = new TypstWorldBuilder().AddSource("main.typ", document.ToMarkup());

        foreach (var (path, text) in assets)
        {
            builder.AddAsset(path, Encoding.UTF8.GetBytes(text));
        }

        using var world = builder.Build();
        using var result = new TypstCompiler().Compile(world);

        var errors = result.Diagnostics
            .Where(d => d.Severity == TypstSeverity.Error)
            .Select(d => d.ToDisplayString())
            .ToList();

        Assert.Empty(errors);
        Assert.True(result.PageCount > 0);
    }

    [Fact]
    public void A_paragraph_starting_with_a_number_is_not_a_list()
    {
        // `1.` at the start of a line is an enumerated list marker in Typst. Host data must not be
        // able to change the document structure.
        var document = TypstDocument.Create()
            .Paragraph("1. This is host data, not a list");

        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", document.ToMarkup())
            .Build();

        using var result = new TypstCompiler().Compile(
            world,
            new TypstCompilationOptions { DocumentKind = TypstDocumentKind.Html });

        var html = result.ExportHtmlString();

        Assert.DoesNotContain("<ol", html, StringComparison.Ordinal);
        Assert.Contains("1. This is host data", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_paragraph_starting_with_a_decimal_is_left_alone()
    {
        var document = TypstDocument.Create().Paragraph("1.5 million units");

        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", document.ToMarkup())
            .Build();

        using var result = new TypstCompiler().Compile(
            world,
            new TypstCompilationOptions { DocumentKind = TypstDocumentKind.Html });

        Assert.Contains("1.5 million units", result.ExportHtmlString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Layout_nodes_compile()
    {
        var document = TypstDocument.Create()
            .Columns(2, TypstContent.Empty.Text("Two columns of body text."))
            .Align(TypstAlignment.Center, TypstContent.Empty.Text("Centred"))
            .Box(TypstContent.Empty.Text("Boxed"))
            .Block(TypstContent.Empty.Text("Blocked"), TypstLength.Cm(4))
            .Grid(2, TypstContent.Empty.Text("a"), TypstContent.Empty.Text("b"))
            .Stack(TypstContent.Empty.Text("stacked"))
            .Pad(TypstLength.Cm(1), TypstContent.Empty.Text("padded"))
            .Place(TypstAlignment.Right, TypstContent.Empty.Text("placed"))
            .Hide(TypstContent.Empty.Text("hidden"))
            .Rotate(15, TypstContent.Empty.Text("rotated"))
            .Scale(80, TypstContent.Empty.Text("scaled"))
            .Move(TypstLength.Cm(1), TypstLength.Cm(1), TypstContent.Empty.Text("moved"))
            .Skew(5, 5, TypstContent.Empty.Text("skewed"))
            .HSpace(TypstLength.Cm(1))
            .VSpace(TypstLength.Cm(1));

        CompilesCleanly(document);
    }

    [Fact]
    public void List_and_structure_nodes_compile()
    {
        var document = TypstDocument.Create()
            .Heading(1, "Structure")
            .BulletList("one", "two")
            .NumberedList("one", "two")
            .TermList(("term", TypstContent.Empty.Text("description")))
            .Outline()
            .Divider()
            .PageBreak()
            .ColumnBreak()
            .ParagraphBreak();

        CompilesCleanly(document);
    }

    [Fact]
    public void Inline_nodes_compile()
    {
        var document = TypstDocument.Create()
            .Heading(1, "Inline")
            .Paragraph(TypstContent.Empty
                .Text("plain ")
                .Bold("bold ")
                .Emphasis("italic ")
                .Code("code ")
                .Math("x^2 ")
                .Link("https://example.com", "link ")
                .LineBreak()
                .Text("after the break"));

        CompilesCleanly(document);
    }

    [Fact]
    public void A_reference_resolves_to_its_label()
    {
        // Typst refuses to reference a heading that has no numbering.
        var document = TypstDocument.Create()
            .Set("heading(numbering: \"1.\")")
            .Heading(1, TypstContent.Empty.Text("Target").Label("target"))
            .Paragraph(TypstContent.Empty.Text("See ").Reference("target"));

        CompilesCleanly(document);
    }

    [Fact]
    public void Metadata_and_bibliography_compile()
    {
        var document = TypstDocument.Create()
            .Metadata(m => m
                .Title("A report")
                .Author("Someone")
                .Keywords("one", "two")
                .Language("en")
                .Date(new DateOnly(2024, 1, 1)))
            .MetadataEntry("build", "42", "build-label")
            .Heading(1, "Citations")
            .Paragraph(TypstContent.Empty.Text("As shown in ").Cite("knuth1984"))
            .Bibliography("refs.bib", "ieee");

        CompilesCleanly(document, ("refs.bib", Bibliography));
    }

    [Fact]
    public void Introspection_compiles()
    {
        var document = TypstDocument.Create()
            .Heading(1, "Introspection")
            .Context(c => c.Statement(TypstQuery.Headings()))
            .Paragraph(TypstContent.Empty.Text("Measured: ").Math("1 + 1"));

        CompilesCleanly(document);
    }

    [Fact]
    public void Export_options_are_honoured()
    {
        using var pagedWorld = new TypstWorldBuilder()
            .AddSource("main.typ", "= Title\n\n#lorem(60)")
            .Build();

        using var paged = new TypstCompiler().Compile(pagedWorld);

        var svg = paged.Export(ExportFormat.Svg, new SvgExportOptions { Page = 0, Pretty = true });
        var png = paged.Export(ExportFormat.Png, new PngExportOptions { Page = 0, Ppi = 72f });

        Assert.NotEmpty(svg);
        Assert.NotEmpty(png);

        // HTML is a different layout pass, so it needs its own compilation.
        using var htmlWorld = new TypstWorldBuilder()
            .AddSource("main.typ", "= Title\n\n#lorem(60)")
            .Build();

        using var html = new TypstCompiler().Compile(
            htmlWorld,
            new TypstCompilationOptions { DocumentKind = TypstDocumentKind.Html });

        Assert.NotEmpty(html.Export(ExportFormat.Html, new HtmlExportOptions { Pretty = false }));
    }

    [Fact]
    public void A_bundle_round_trips()
    {
        // The bundle target only accepts content inside `#document("…")[…]`.
        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", "#document(\"index.html\")[\n  = Bundled\n\n  A bundled paragraph.\n]")
            .Build();

        var bundle = new TypstCompiler().CompileBundle(world);

        Assert.NotEmpty(bundle.Html);
        Assert.NotEmpty(bundle.Files);

        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "sharp-typst-bundle-" + Guid.NewGuid().ToString("N"));

        try
        {
            bundle.WriteTo(directory);
            Assert.True(System.IO.Directory.EnumerateFiles(directory, "*", System.IO.SearchOption.AllDirectories).Any());
        }
        finally
        {
            if (System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void A_fixed_clock_is_deterministic()
    {
        var instant = new DateTimeOffset(2020, 6, 15, 12, 0, 0, TimeSpan.Zero);

        static byte[] Render(DateTimeOffset instant)
        {
            using var world = new TypstWorldBuilder()
                .WithFixedClock(instant)
                .AddSource("main.typ", "= Clock\n\n#datetime.today().display()")
                .Build();

            using var result = new TypstCompiler().Compile(world);
            return result.ExportPdf();
        }

        Assert.Equal(Render(instant), Render(instant));
        Assert.NotEqual(Render(instant), Render(instant.AddDays(1)));
    }

    [Fact]
    public void An_embedded_template_renders()
    {
        var template = TypstTemplate.LoadEmbedded(
            typeof(DocumentModelCoverageTests).Assembly,
            "M3scalinum.Text.Typst.Integration.Tests.templates.minimal");

        var pdf = template.Render(
            new TypstCompiler(),
            new Dictionary<string, object?> { ["title"] = "Embedded" });

        Assert.NotEmpty(pdf);
    }
}
