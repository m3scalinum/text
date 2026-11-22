using System;
using M3scalinum.Text.Documents;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

/// <summary>
/// Cross references and the generated table of contents. The model emits the markup that makes
/// Typst resolve and number; the numbers and the pages only exist once the document is compiled,
/// so nothing here asserts a number. What Typst does with the markup was checked against the real
/// compiler and is stated in <c>docs/specifications/04-document-model.md</c>.
/// </summary>
public sealed class CrossReferenceTests
{
    [Fact]
    public void A_labelled_figure_is_numbered_and_referenceable()
    {
        var markup = TypstDocument.Create()
            .Figure(f => f
                .Image("charts/revenue.png")
                .WithCaption("Revenue by month")
                .WithLabel("fig:revenue"))
            .Paragraph(c => c.Text("See ").Reference("fig:revenue"))
            .ToMarkup();

        Assert.Contains("kind: image,", markup, StringComparison.Ordinal);
        Assert.Contains("supplement: [Figure],", markup, StringComparison.Ordinal);
        Assert.Contains(") <fig:revenue>", markup, StringComparison.Ordinal);
        Assert.Contains("#ref(<fig:revenue>)", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unlabelled_figure_keeps_its_implicit_kind()
    {
        var markup = TypstDocument.Create()
            .Figure(f => f.Image("charts/revenue.png").WithCaption("Revenue by month"))
            .ToMarkup();

        Assert.DoesNotContain("kind:", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("supplement:", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_labelled_table_is_emitted_as_a_figure_of_kind_table()
    {
        var markup = TypstDocument.Create()
            .Table(t => t
                .Columns(TypstLength.Fr(1), TypstLength.Fr(2))
                .HeaderRow("Item", "Amount")
                .Row("Hosting", "1 200")
                .WithCaption("Revenue by item")
                .WithLabel("tbl:revenue"))
            .Paragraph(c => c.Text("See ").Reference("tbl:revenue"))
            .ToMarkup();

        Assert.Contains("#figure(kind: table, supplement: [Table], caption: [Revenue by item])[", markup, StringComparison.Ordinal);
        Assert.Contains("  #table(", markup, StringComparison.Ordinal);
        Assert.Contains("] <tbl:revenue>", markup, StringComparison.Ordinal);
        Assert.Contains("#ref(<tbl:revenue>)", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unlabelled_table_stays_a_bare_table()
    {
        var markup = TypstDocument.Create()
            .Table(t => t.Row("Hosting", "1 200"))
            .ToMarkup();

        Assert.Contains("#table(", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("#figure(", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_section_label_is_attached_to_its_heading_and_turns_numbering_on()
    {
        var markup = TypstDocument.Create()
            .Heading(1, "Revenue")
            .Label("sec:revenue")
            .Paragraph(c => c.Text("See ").Reference("sec:revenue"))
            .ToMarkup();

        // Typst refuses to reference a heading that is not numbered, so a section label enables it.
        Assert.Contains("#set heading(numbering: \"1.1\")", markup, StringComparison.Ordinal);
        Assert.Contains("= Revenue\n<sec:revenue>", markup, StringComparison.Ordinal);
        Assert.Contains("#ref(<sec:revenue>)", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_that_sets_its_own_heading_numbering_keeps_it()
    {
        var markup = TypstDocument.Create()
            .Set("heading(numbering: \"A.1\")")
            .Heading(1, "Revenue")
            .Label(TypstLabelKind.Section)
            .ToMarkup();

        Assert.Contains("#set heading(numbering: \"A.1\")", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("numbering: \"1.1\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_reference_may_come_before_its_target()
    {
        var markup = TypstDocument.Create()
            .Paragraph(c => c.Text("See ").Reference("fig:last"))
            .Figure(f => f.Image("charts/costs.png").WithLabel("fig:last"))
            .ToMarkup();

        var reference = markup.IndexOf("#ref(<fig:last>)", StringComparison.Ordinal);
        var label = markup.IndexOf("<fig:last>", StringComparison.Ordinal);

        Assert.True(reference >= 0, "the reference is emitted");
        Assert.True(label > reference, "the label is emitted after the reference, as written");
    }

    [Fact]
    public void An_unknown_label_is_emitted_and_left_to_the_compiler()
    {
        // Nothing on the host can know whether the label exists — it may come from a template. The
        // markup is emitted as written; Typst then fails the compilation with
        // "label `<nope>` does not exist in the document".
        var markup = TypstDocument.Create()
            .Paragraph(c => c.Text("See ").Reference("nope"))
            .ToMarkup();

        Assert.Contains("#ref(<nope>)", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<nope>", markup.Replace("#ref(<nope>)", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_labels_are_indexed()
    {
        // Typst refuses a document in which a label occurs twice, so the writer keeps the first name
        // and numbers the later occurrences. A reference still names the first.
        var markup = TypstDocument.Create()
            .Figure(f => f.Image("charts/a.png").WithLabel("fig:x"))
            .Figure(f => f.Image("charts/b.png").WithLabel("fig:x"))
            .Paragraph(c => c.Text("First: ").Reference("fig:x"))
            .ToMarkup();

        Assert.Contains(") <fig:x>", markup, StringComparison.Ordinal);
        Assert.Contains(") <fig:x-2>", markup, StringComparison.Ordinal);
        Assert.Contains("#ref(<fig:x>)", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_generated_label_does_not_collide_with_a_written_one()
    {
        var markup = TypstDocument.Create()
            .Figure(f => f.Image("charts/a.png").WithLabel("fig:1"))
            .Figure(f => f.Image("charts/b.png"))
            .Label(TypstLabelKind.Figure)
            .ToMarkup();

        Assert.Contains(") <fig:1>", markup, StringComparison.Ordinal);
        Assert.Contains(") <fig:1-2>", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_labels_count_per_kind()
    {
        var markup = TypstDocument.Create()
            .Heading(1, "One")
            .Label(TypstLabelKind.Section)
            .Heading(2, "One A")
            .Label(TypstLabelKind.Section)
            .Figure(f => f.Image("charts/a.png"))
            .Label(TypstLabelKind.Figure)
            .Table(t => t.Row("a", "b"))
            .Label(TypstLabelKind.Table)
            .Figure(f => f.Image("charts/b.png"))
            .Label(TypstLabelKind.Figure)
            .ToMarkup();

        Assert.Contains("<sec:1>", markup, StringComparison.Ordinal);
        Assert.Contains("<sec:2>", markup, StringComparison.Ordinal);
        Assert.Contains("<fig:1>", markup, StringComparison.Ordinal);
        Assert.Contains("<tbl:1>", markup, StringComparison.Ordinal);
        Assert.Contains("<fig:2>", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_labels_are_stable()
    {
        static TypstDocument Build() => TypstDocument.Create()
            .Heading(1, "One")
            .Label(TypstLabelKind.Section)
            .Figure(f => f.Image("charts/a.png"))
            .Label(TypstLabelKind.Figure)
            .Table(t => t.Row("a", "b"))
            .Label(TypstLabelKind.Table);

        var first = Build().ToMarkup();
        var second = Build().ToMarkup();

        Assert.Equal(first, second, StringComparer.Ordinal);
        Assert.Contains("<sec:1>", first, StringComparison.Ordinal);
        Assert.Contains("<fig:1>", first, StringComparison.Ordinal);
        Assert.Contains("<tbl:1>", first, StringComparison.Ordinal);
    }

    [Fact]
    public void A_reference_can_override_the_supplement()
    {
        var markup = TypstDocument.Create()
            .Figure(f => f.Image("charts/a.png").WithLabel("fig:x"))
            .Paragraph(c => c
                .Text("See ")
                .Reference("fig:x", supplement: "Image")
                .Text(" and ")
                .Reference("fig:x"))
            .ToMarkup();

        Assert.Contains("#ref(<fig:x>, supplement: [Image])", markup, StringComparison.Ordinal);
        Assert.Contains("and #ref(<fig:x>)", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void An_outline_carries_its_title_and_depth()
    {
        var markup = TypstDocument.Create()
            .Outline(TypstContent.Empty.Text("Contents"), depth: 3)
            .ToMarkup();

        Assert.Contains("#outline(title: [Contents], depth: 3)", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void An_outline_without_a_title_keeps_the_markup_it_always_emitted()
    {
        var markup = TypstDocument.Create().Outline().ToMarkup();

        Assert.Equal("#outline(depth: 2)\n", markup, StringComparer.Ordinal);
    }

    [Fact]
    public void An_outline_rejects_a_depth_outside_one_to_six()
    {
        var document = TypstDocument.Create();

        Assert.Throws<ArgumentOutOfRangeException>(() => document.Outline(depth: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.Outline(depth: 7));
    }

    [Fact]
    public void A_blank_reference_or_label_is_rejected()
    {
        var document = TypstDocument.Create();

        Assert.Throws<ArgumentException>(() => document.Reference(" "));
        Assert.Throws<ArgumentException>(() => document.Reference("fig:x", " "));
        Assert.Throws<ArgumentException>(() => document.Label(" "));
        Assert.Throws<ArgumentException>(() => new TypstTable().WithLabel(" "));
    }
}
