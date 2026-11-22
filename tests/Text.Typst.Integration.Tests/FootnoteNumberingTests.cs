using System;
using M3scalinum.Text.Documents;
using M3scalinum.Text.Typst.Compilation;
using Xunit;

namespace M3scalinum.Text.Typst.Integration.Tests;

/// <summary>
/// Section-scoped numbering, laid out by the real compiler: the numbers asserted here are the ones
/// Typst printed, not the ones the model wrote.
/// </summary>
[Trait("Category", "Integration")]
public sealed class FootnoteNumberingTests
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

    [Fact]
    public void A_document_without_sections_numbers_its_notes_one_two_three()
    {
        var text = Compile(TypstDocument.Create()
            .Paragraph(c => c.Text("Alpha").Footnote("one"))
            .Footnote("two")
            .Footnote("three"));

        Assert.Contains("Alpha1", text, StringComparison.Ordinal);
        Assert.Contains("1one", text, StringComparison.Ordinal);
        Assert.Contains("2two", text, StringComparison.Ordinal);
        Assert.Contains("3three", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_section_scope_restarts_the_notes_at_each_section()
    {
        var text = Compile(TypstDocument.Create()
            .Heading(1, "One")
            .Footnote("first note one", TypstNumberingScope.Section)
            .Footnote("first note two", TypstNumberingScope.Section)
            .Heading(1, "Two")
            .Footnote("second note one", TypstNumberingScope.Section)
            .Footnote("second note two", TypstNumberingScope.Section));

        Assert.Contains("1first note one", text, StringComparison.Ordinal);
        Assert.Contains("2first note two", text, StringComparison.Ordinal);
        Assert.Contains("1second note one", text, StringComparison.Ordinal);
        Assert.Contains("2second note two", text, StringComparison.Ordinal);
        Assert.DoesNotContain("3second note one", text, StringComparison.Ordinal);
        Assert.DoesNotContain("4second note two", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_sections_number_their_figures_one_one_and_two_one()
    {
        var text = Compile(TypstDocument.Create()
            .Heading(1, "One")
            .Figure(f => f.Image("pixel.png").WithCaption("one"), TypstNumberingScope.Section)
            .Figure(f => f.Image("pixel.png").WithCaption("two"), TypstNumberingScope.Section)
            .Heading(1, "Two")
            .Figure(f => f.Image("pixel.png").WithCaption("three"), TypstNumberingScope.Section));

        Assert.Contains("Figure 1.1: one", text, StringComparison.Ordinal);
        Assert.Contains("Figure 1.2: two", text, StringComparison.Ordinal);
        Assert.Contains("Figure 2.1: three", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_section_figure_before_the_first_section_keeps_a_plain_number()
    {
        var text = Compile(TypstDocument.Create()
            .Figure(f => f.Image("pixel.png").WithCaption("standalone"), TypstNumberingScope.Section));

        Assert.Contains("Figure 1: standalone", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Figure 0.1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_default_scope_keeps_figures_numbered_across_the_document()
    {
        var text = Compile(TypstDocument.Create()
            .Heading(1, "One")
            .Figure(f => f.Image("pixel.png").WithCaption("one"))
            .Heading(1, "Two")
            .Figure(f => f.Image("pixel.png").WithCaption("two")));

        Assert.Contains("Figure 1: one", text, StringComparison.Ordinal);
        Assert.Contains("Figure 2: two", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_reference_to_a_section_numbered_figure_prints_the_section_number()
    {
        var text = Compile(TypstDocument.Create()
            .Heading(1, "One")
            .Figure(f => f.Image("pixel.png").WithCaption("one").WithLabel("fig:one"), TypstNumberingScope.Section)
            .Paragraph(c => c.Text("See ").Reference("fig:one")));

        Assert.Contains("Figure 1.1: one", text, StringComparison.Ordinal);
        Assert.Contains("See Figure 1.1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_outline_does_not_disturb_the_section_numbering()
    {
        // The section number comes from counter(heading), which Typst owns; a document the outline
        // re-walks must not see its figures renumbered, which is what a hand-stepped counter did
        // while it was measured.
        var text = Compile(TypstDocument.Create()
            .Outline(title: "Contents")
            .Heading(1, "One")
            .Figure(f => f.Image("pixel.png").WithCaption("one"), TypstNumberingScope.Section)
            .Heading(1, "Two")
            .Figure(f => f.Image("pixel.png").WithCaption("two"), TypstNumberingScope.Section));

        Assert.Contains("Figure 1.1: one", text, StringComparison.Ordinal);
        Assert.Contains("Figure 2.1: two", text, StringComparison.Ordinal);
    }

    private static string Compile(TypstDocument document)
    {
        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", document.ToMarkup())
            .AddAsset("pixel.png", Pixel)
            .Build();

        using var result = new TypstCompiler().Compile(world);

        Assert.Empty(result.Diagnostics);
        return string.Join(" | ", result.ExtractTextPages())
            .Replace('\u00A0', ' ')
            .Replace('\u202F', ' ');
    }
}
