using System;
using M3scalinum.Text.Documents;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

/// <summary>
/// The model has to be able to state what an accessible PDF requires: an alternative description
/// per figure, header cells with their scope, and the natural language of the document. These tests
/// prove the Typst source carries it; the renderer's own tagging is covered where the native
/// library is available.
/// </summary>
public sealed class ModelAccessibilityTests
{
    [Fact]
    public void Emits_the_alternative_text_as_the_alt_argument_of_the_figure()
    {
        var markup = TypstDocument.Create()
            .Figure(f => f
                .Image("charts/revenue.png")
                .Alt("Revenue grew by 12% between January and June."))
            .ToMarkup();

        Assert.Contains("#figure(", markup, StringComparison.Ordinal);
        Assert.Contains("alt: \"Revenue grew by 12% between January and June.\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_a_figure_without_alt_when_none_is_set()
    {
        var markup = TypstDocument.Create()
            .Figure(f => f.Image("charts/revenue.png").WithCaption("Revenue by month"))
            .ToMarkup();

        Assert.DoesNotContain("alt:", markup, StringComparison.Ordinal);
        Assert.Contains("caption: [Revenue by month]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Escapes_a_quote_in_the_alternative_text()
    {
        var markup = TypstDocument.Create()
            .Figure(f => f.Image("logo.png").Alt("The \"M3\" wordmark"))
            .ToMarkup();

        Assert.Contains("alt: \"The \\\"M3\\\" wordmark\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_blank_alternative_text()
    {
        var figure = new TypstFigure().Image("logo.png");

        Assert.Throws<ArgumentException>(() => figure.Alt(" "));
    }

    [Fact]
    public void Marks_a_header_row_as_a_table_header()
    {
        var markup = TypstDocument.Create()
            .Table(t => t
                .Columns(TypstLength.Fr(1), TypstLength.Fr(1))
                .HeaderRow("Item", "Amount")
                .Row("Hosting", "1 200"))
            .ToMarkup();

        Assert.Contains("table.header(repeat: true, [Item], [Amount]),", markup, StringComparison.Ordinal);
        Assert.Contains(
            "[Hosting], [1 200],",
            markup,
            StringComparison.Ordinal);
        Assert.DoesNotContain("pdf.header-cell", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Marks_a_body_cell_as_a_row_header()
    {
        var markup = TypstDocument.Create()
            .Table(t => t
                .Columns(TypstLength.Fr(1), TypstLength.Fr(1))
                .HeaderRow("Item", "Amount")
                .Row(
                    Cell("Hosting", TypstHeaderScope.Row),
                    new TypstTableCell("1 200")))
            .ToMarkup();

        Assert.Contains("pdf.header-cell(scope: \"row\")[Hosting]", markup, StringComparison.Ordinal);
        Assert.Contains("[1 200]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Marks_the_corner_of_a_header_row_as_labelling_both_tracks()
    {
        var markup = TypstDocument.Create()
            .Table(t => t
                .Columns(TypstLength.Fr(1), TypstLength.Fr(1), TypstLength.Fr(1))
                .HeaderRow(
                    Cell("Nutrient", TypstHeaderScope.Both),
                    new TypstTableCell("Per 100 g"),
                    new TypstTableCell("Per serving"))
                .Row("Protein", "8 g", "12 g"))
            .ToMarkup();

        Assert.Contains(
            "table.header(repeat: true, pdf.header-cell(scope: \"both\")[Nutrient], [Per 100 g], [Per serving]),",
            markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void States_an_explicit_column_scope_on_a_body_cell()
    {
        var markup = TypstDocument.Create()
            .Table(t => t
                .Columns(TypstLength.Fr(1), TypstLength.Fr(1))
                .Row(Cell("Total", TypstHeaderScope.Column), new TypstTableCell("2 000")))
            .ToMarkup();

        Assert.Contains("pdf.header-cell(scope: \"column\")[Total]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Carries_a_header_scope_on_a_cell_that_spans_columns()
    {
        var markup = TypstDocument.Create()
            .Table(t => t
                .Columns(TypstLength.Fr(1), TypstLength.Fr(1))
                .Row(
                    new TypstTableCell("Users", colspan: 2) { HeaderScope = TypstHeaderScope.Row })
                .Row("North", "40"))
            .ToMarkup();

        Assert.Contains(
            "pdf.header-cell(table.cell(colspan: 2)[Users], scope: \"row\")",
            markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Leaves_a_table_without_a_header_row_as_data_cells()
    {
        // The markup a table without headers emitted before the accessibility work must not change.
        var markup = TypstDocument.Create()
            .Table(t => t
                .Columns(TypstLength.Fr(1), TypstLength.Fr(1))
                .Row("Hosting", "1 200")
                .Row(new TypstTableCell("Support", rowspan: 2), new TypstTableCell("800")))
            .ToMarkup();

        Assert.DoesNotContain("table.header", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("pdf.header-cell", markup, StringComparison.Ordinal);
        Assert.Contains("[Hosting], [1 200],", markup, StringComparison.Ordinal);
        Assert.Contains("table.cell(rowspan: 2)[Support], [800],", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void States_the_document_language_for_the_catalog()
    {
        var markup = TypstDocument.Create()
            .Metadata(m => m.Language("fr"))
            .Paragraph("Bonjour")
            .ToMarkup();

        Assert.Contains("#set text(lang: \"fr\")", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_built_by_the_model_carries_every_structure_the_checks_read()
    {
        var markup = TypstDocument.Create()
            .Metadata(m => m.Title("Q3 report").Language("en"))
            .Heading(1, "Revenue")
            .Figure(f => f
                .Image("charts/revenue.png")
                .Alt("Bar chart: revenue rises every quarter of 2026.")
                .WithCaption("Revenue by quarter"))
            .Table(t => t
                .Columns(TypstLength.Fr(1), TypstLength.Fr(2))
                .HeaderRow(Cell("Quarter", TypstHeaderScope.Both), new TypstTableCell("Revenue"))
                .Row(Cell("Q1", TypstHeaderScope.Row), new TypstTableCell("1 200")))
            .ToMarkup();

        Assert.Contains("#set text(lang: \"en\")", markup, StringComparison.Ordinal);
        Assert.Contains("alt: \"Bar chart: revenue rises every quarter of 2026.\"", markup, StringComparison.Ordinal);
        Assert.Contains(
            "table.header(repeat: true, pdf.header-cell(scope: \"both\")[Quarter], [Revenue]),",
            markup,
            StringComparison.Ordinal);
        Assert.Contains("pdf.header-cell(scope: \"row\")[Q1], [1 200],", markup, StringComparison.Ordinal);

        // The header block holds the header cells only: the data row sits outside it.
        var header = markup.IndexOf("table.header(", StringComparison.Ordinal);
        var data = markup.IndexOf("pdf.header-cell(scope: \"row\")[Q1]", StringComparison.Ordinal);
        Assert.True(header < data);
        Assert.Contains("),", markup[header..data], StringComparison.Ordinal);
    }

    private static TypstTableCell Cell(string text, TypstHeaderScope scope)
        => new(text) { HeaderScope = scope };
}
