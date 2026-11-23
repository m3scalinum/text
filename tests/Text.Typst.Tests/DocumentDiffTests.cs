using System;
using M3scalinum.Text.Documents;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

/// <summary>
/// Two versions of a document compared as models rather than as files: what a reader would call a
/// change, located in the tree, with no compilation and no PDF in the way.
/// </summary>
public sealed class DocumentDiffTests
{
    [Fact]
    public void Two_documents_that_differ_by_one_price_produce_exactly_one_change()
    {
        var before = Invoice("12,00 €");
        var after = Invoice("13,00 €");

        var change = Assert.Single(TypstDocumentDiff.Between(before, after));

        var text = Assert.IsType<TypstTextChanged>(change);
        Assert.Equal(TypstDocumentChangeKind.TextChanged, text.Kind);
        Assert.Equal("Total : 12,00 €", text.OldText);
        Assert.Equal("Total : 13,00 €", text.NewText);
        Assert.Equal("body[3].inline[0]", text.Path.Text);
        Assert.Equal(["body[3]", "inline[0]"], text.Path.Segments);
    }

    [Fact]
    public void An_inserted_paragraph_is_one_addition_and_not_a_shift_of_every_paragraph_after_it()
    {
        var before = Paragraphs("Un premier paragraphe.", "Un deuxième paragraphe.", "Un troisième paragraphe.");
        var after = Paragraphs(
            "Un premier paragraphe.",
            "Un deuxième paragraphe.",
            "Un paragraphe inséré au milieu.",
            "Un troisième paragraphe.");

        var change = Assert.Single(TypstDocumentDiff.Between(before, after));

        var added = Assert.IsType<TypstNodeAdded>(change);
        Assert.Equal("paragraph", added.Node);
        Assert.Equal("body[2]", added.Path.Text);
    }

    [Fact]
    public void A_changed_font_size_is_a_style_change_with_the_old_and_the_new_value()
    {
        var before = Styled(10);
        var after = Styled(12);

        var change = Assert.Single(TypstDocumentDiff.Between(before, after));

        var style = Assert.IsType<TypstStyleChanged>(change);
        Assert.Equal(TypstDocumentChangeKind.StyleChanged, style.Kind);
        Assert.Equal("size", style.Property);
        Assert.Equal("10pt", style.OldValue);
        Assert.Equal("12pt", style.NewValue);
        Assert.Equal("body[0].inline[0]", style.Path.Text);
    }

    [Fact]
    public void Two_identical_documents_produce_no_change()
    {
        var document = Invoice("12,00 €");

        Assert.Empty(TypstDocumentDiff.Between(document, document));
        Assert.Empty(TypstDocumentDiff.Between(Invoice("12,00 €"), Invoice("12,00 €")));
    }

    [Fact]
    public void A_changed_table_cell_is_one_change_located_in_that_cell()
    {
        var before = PriceTable("1 200,00 €", "800,00 €");
        var after = PriceTable("1 200,00 €", "850,00 €");

        var change = Assert.Single(TypstDocumentDiff.Between(before, after));

        var text = Assert.IsType<TypstTextChanged>(change);
        Assert.Equal("800,00 €", text.OldText);
        Assert.Equal("850,00 €", text.NewText);
        Assert.Equal("body[0].row[1].cell[1].inline[0]", text.Path.Text);
    }

    [Fact]
    public void A_removed_paragraph_is_one_removal()
    {
        var before = Paragraphs("Un premier paragraphe.", "Un deuxième paragraphe.", "Un troisième paragraphe.");
        var after = Paragraphs("Un premier paragraphe.", "Un troisième paragraphe.");

        var change = Assert.Single(TypstDocumentDiff.Between(before, after));

        var removed = Assert.IsType<TypstNodeRemoved>(change);
        Assert.Equal("paragraph", removed.Node);
        Assert.Equal("body[1]", removed.Path.Text);
    }

    [Fact]
    public void A_paragraph_that_kept_its_text_and_changed_its_place_is_one_move()
    {
        var before = Paragraphs("Un premier paragraphe.", "Un deuxième paragraphe.", "Un troisième paragraphe.");
        var after = Paragraphs("Un deuxième paragraphe.", "Un troisième paragraphe.", "Un premier paragraphe.");

        var change = Assert.Single(TypstDocumentDiff.Between(before, after));

        var moved = Assert.IsType<TypstNodeMoved>(change);
        Assert.Equal("body[0]", moved.From.Text);
        Assert.Equal("body[2]", moved.Path.Text);
    }

    [Fact]
    public void A_changed_heading_level_is_a_style_change_named_after_its_property()
    {
        var before = TypstDocument.Create().Heading(1, "Conditions");
        var after = TypstDocument.Create().Heading(2, "Conditions");

        var change = Assert.Single(TypstDocumentDiff.Between(before, after));

        var style = Assert.IsType<TypstStyleChanged>(change);
        Assert.Equal("level", style.Property);
        Assert.Equal("1", style.OldValue);
        Assert.Equal("2", style.NewValue);
        Assert.Equal("body[0]", style.Path.Text);
    }

    [Fact]
    public void A_change_inside_a_labelled_table_carries_the_label_in_its_path()
    {
        var before = TypstDocument.Create()
            .Table(t => t.HeaderRow("Désignation", "Montant").Row("Conseil", "1 200,00 €"))
            .Label("tbl:prices");
        var after = TypstDocument.Create()
            .Table(t => t.HeaderRow("Désignation", "Montant").Row("Conseil", "1 250,00 €"))
            .Label("tbl:prices");

        var change = Assert.Single(TypstDocumentDiff.Between(before, after));

        Assert.Equal("tbl:prices", change.Path.Label);
    }

    [Fact]
    public void The_diff_reads_a_document_that_the_compiler_could_not_compile()
    {
        // The asset does not exist and no font is resolved: a compilation of either document would
        // fail, and the diff still answers, because it never leaves the model.
        var before = TypstDocument.Create()
            .Figure(f => f.Image("assets/revenue.png").Alt("Revenue by month"))
            .Paragraph("Chiffre d'affaires : 1 200,00 €");
        var after = TypstDocument.Create()
            .Figure(f => f.Image("assets/revenue.png").Alt("Revenue by month"))
            .Paragraph("Chiffre d'affaires : 1 300,00 €");

        var change = Assert.Single(TypstDocumentDiff.Between(before, after));

        Assert.Equal("body[1].inline[0]", change.Path.Text);
    }

    [Fact]
    public void A_section_is_compared_through_its_body_and_its_running_text()
    {
        var before = TypstDocument.Create()
            .Section(
                section => section.Footer("Page 1"),
                body => body.Paragraph("Le présent contrat prend effet le 1er janvier 2026."));
        var after = TypstDocument.Create()
            .Section(
                section => section.Footer("Page 1"),
                body => body.Paragraph("Le présent contrat prend effet le 1er février 2026."));

        var change = Assert.Single(TypstDocumentDiff.Between(before, after));

        Assert.Equal("body[0].section[0].inline[0]", change.Path.Text);
    }

    [Fact]
    public void A_change_in_a_caption_is_located_in_the_caption()
    {
        var before = TypstDocument.Create()
            .Figure(f => f.Image("assets/revenue.png").Alt("Revenue by month").WithCaption("Revenue, 2025"));
        var after = TypstDocument.Create()
            .Figure(f => f.Image("assets/revenue.png").Alt("Revenue by month").WithCaption("Revenue, 2026"));

        var change = Assert.Single(TypstDocumentDiff.Between(before, after));

        Assert.Equal("body[0].caption[0].inline[0]", change.Path.Text);
    }

    [Fact]
    public void A_document_compared_with_null_is_refused()
    {
        var document = TypstDocument.Create();

        Assert.Throws<ArgumentNullException>(() => TypstDocumentDiff.Between(null!, document));
        Assert.Throws<ArgumentNullException>(() => TypstDocumentDiff.Between(document, null!));
    }

    private static TypstDocument Invoice(string total) => TypstDocument.Create()
        .Heading(1, "Facture 2026-0042")
        .Paragraph("Client : ACME Industrie")
        .Table(t => t
            .HeaderRow("Désignation", "Montant")
            .Row("Conseil", "1 200,00 €")
            .Row("Formation", "800,00 €"))
        .Paragraph("Total : " + total);

    private static TypstDocument PriceTable(string consulting, string training) => TypstDocument.Create()
        .Table(t => t
            .HeaderRow("Désignation", "Montant")
            .Row("Conseil", consulting)
            .Row("Formation", training));

    private static TypstDocument Paragraphs(params string[] paragraphs)
    {
        var document = TypstDocument.Create();

        foreach (var paragraph in paragraphs)
        {
            document.Paragraph(paragraph);
        }

        return document;
    }

    private static TypstDocument Styled(double points) => TypstDocument.Create()
        .Paragraph(c => c.Styled("Avertissement", new TypstTextStyle().Size(TypstLength.Pt(points))));
}
