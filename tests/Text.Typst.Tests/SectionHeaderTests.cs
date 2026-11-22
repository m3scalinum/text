using System;
using M3scalinum.Text.Documents;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

/// <summary>
/// <see cref="TypstSection"/>: a header, a footer, a watermark and a page numbering that belong to
/// one part of a document and stop at the end of it.
/// </summary>
/// <remarks>
/// These tests pin the markup, not the layout: that the emitted block is what Typst scopes, and that
/// a section writes nothing the document already had. The rendering itself — three footers on the
/// pages of three sections, a cover with no number — is measured in
/// <c>SectionHeaderIntegrationTests</c> against the real compiler.
/// </remarks>
public sealed class SectionHeaderTests
{
    private static string Markup(TypstDocument document) => document.ToMarkup();

    private static string SectionMarkup(TypstSection section)
        => Markup(TypstDocument.Create().Section(section));

    [Fact]
    public void A_section_writes_its_footer_inside_a_content_block()
    {
        var markup = SectionMarkup(TypstSection.Create().Footer("Partie I — page {page}"));

        Assert.Contains(
            "#[\n#set page(footer: context [ Partie I — page #counter(page).display() ])\n",
            markup,
            StringComparison.Ordinal);
        Assert.EndsWith("]\n", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_section_that_declares_no_header_leaves_the_documents_header_alone()
    {
        var style = TypstDocumentStyle.Create().Header("Rapport").Build();

        var markup = Markup(TypstDocument.Create()
            .Apply(style)
            .Section(TypstSection.Create().Footer("Partie I")));

        Assert.Contains("header: context [ Rapport ]", markup, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(markup, "header:"));
    }

    [Fact]
    public void A_section_can_remove_the_header_with_none()
    {
        var markup = SectionMarkup(TypstSection.Create().NoHeader());

        Assert.Contains("#set page(header: none)", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_section_can_remove_the_footer_with_none()
    {
        var markup = SectionMarkup(TypstSection.Create().NoFooter());

        Assert.Contains("#set page(footer: none)", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_section_can_remove_the_documents_watermark()
    {
        var markup = SectionMarkup(TypstSection.Create().NoWatermark());

        Assert.Contains("background: none", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_watermark_is_a_page_background_centred_and_slanted()
    {
        var markup = SectionMarkup(TypstSection.Create().Watermark("BROUILLON"));

        Assert.Contains(
            "background: place(center + horizon, rotate(-30deg, text(size: 72pt, "
                + "fill: luma(0.7).transparentize(85%), \"BROUILLON\")))",
            markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_watermark_with_full_opacity_is_drawn_in_its_own_colour()
    {
        var markup = SectionMarkup(TypstSection.Create().Watermark(
            "INTERNE",
            watermark => watermark
                .Angle(-45)
                .Size(TypstLength.Pt(48))
                .Color(TypstColor.Rgb(0xCC, 0xCC, 0xCC))
                .Opacity(1)));

        Assert.Contains(
            "rotate(-45deg, text(size: 48pt, fill: rgb(204, 204, 204), \"INTERNE\"))",
            markup,
            StringComparison.Ordinal);
        Assert.DoesNotContain("transparentize", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_watermark_opacity_is_written_as_a_transparency()
    {
        var markup = SectionMarkup(TypstSection.Create().Watermark(
            "INTERNE",
            watermark => watermark.Opacity(0.25)));

        Assert.Contains("transparentize(75%)", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_cover_page_is_numbered_none_and_the_counter_keeps_counting()
    {
        var markup = SectionMarkup(TypstSection.Create().NoPageNumber());

        Assert.Contains("numbering: none", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("counter(page).update", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_section_can_number_its_pages_with_its_own_format()
    {
        var markup = SectionMarkup(TypstSection.Create().PageNumbering("i"));

        Assert.Contains("numbering: \"i\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hidden_page_number_is_undone_by_a_later_format()
    {
        var markup = SectionMarkup(TypstSection.Create().NoPageNumber().PageNumbering("1"));

        Assert.Contains("numbering: \"1\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("numbering: none", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_section_that_starts_on_a_new_page_writes_the_break_before_its_block()
    {
        var markup = SectionMarkup(TypstSection.Create().StartsOnNewPage().Footer("Partie II"));

        Assert.Contains("#pagebreak()\n\n#[", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_section_that_does_not_ask_for_a_break_writes_none()
    {
        var markup = SectionMarkup(TypstSection.Create().Footer("Partie II"));

        Assert.DoesNotContain("#pagebreak()", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_section_only_overrides_what_it_declares()
    {
        var markup = SectionMarkup(TypstSection.Create().Footer("Partie I"));

        Assert.Contains("#set page(footer: context [ Partie I ])", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("header:", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("background:", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("numbering:", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_section_that_declares_nothing_writes_an_empty_block()
    {
        var markup = SectionMarkup(TypstSection.Create());

        Assert.Contains("#[\n]", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("#set page(", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_section_footer_uses_the_same_three_placeholders_as_a_style()
    {
        var section = SectionMarkup(TypstSection.Create().Footer("{title} {page}/{pages}"));
        var style = Markup(TypstDocument.Create()
            .Apply(TypstDocumentStyle.Create().Footer("{title} {page}/{pages}").Build()));

        const string expected =
            "context [ #document.title #counter(page).display()/#counter(page).final().first() ]";

        Assert.Contains(expected, section, StringComparison.Ordinal);
        Assert.Contains(expected, style, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_placeholder_in_a_section_footer_stays_literal()
    {
        var markup = SectionMarkup(TypstSection.Create().Footer("{reference}"));

        Assert.Contains("context [ {reference} ]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_blank_section_header_is_refused()
    {
        Assert.Throws<ArgumentException>(() => TypstSection.Create().Header(" "));
    }

    [Fact]
    public void A_blank_section_footer_is_refused()
    {
        Assert.Throws<ArgumentException>(() => TypstSection.Create().Footer(""));
    }

    [Fact]
    public void A_blank_watermark_is_refused()
    {
        Assert.Throws<ArgumentException>(() => TypstSection.Create().Watermark("\t"));
    }

    [Fact]
    public void An_opacity_outside_zero_to_one_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TypstWatermark.Create("DRAFT").Opacity(1.5));
    }

    [Fact]
    public void A_null_section_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => TypstDocument.Create().Section(null!));
    }

    [Fact]
    public void A_section_body_keeps_the_document_api_inside_the_block()
    {
        var markup = Markup(TypstDocument.Create()
            .Section(
                section => section.Footer("Partie I"),
                body => body.Heading(1, "Contexte").Paragraph("Le contrat")));

        Assert.Contains("= Contexte\n\nLe contrat\n]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_prepared_section_and_a_configured_section_write_the_same_markup()
    {
        var prepared = TypstSection.Create().Footer("Partie I");
        prepared.Body.Paragraph("Le contrat");

        var inline = Markup(TypstDocument.Create().Section(
            section => section.Footer("Partie I"),
            body => body.Paragraph("Le contrat")));

        Assert.Equal(inline, Markup(TypstDocument.Create().Section(prepared)));
    }

    [Fact]
    public void Three_sections_write_three_page_rules()
    {
        var markup = Markup(TypstDocument.Create()
            .Section(s => s.Footer("Partie I"), b => b.Paragraph("Un"))
            .Section(s => s.Footer("Partie II"), b => b.Paragraph("Deux"))
            .Section(s => s.Footer("Partie III"), b => b.Paragraph("Trois")));

        Assert.Equal(3, Occurrences(markup, "#set page("));
        Assert.Contains("Partie I", markup, StringComparison.Ordinal);
        Assert.Contains("Partie II", markup, StringComparison.Ordinal);
        Assert.Contains("Partie III", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_without_a_section_writes_no_block()
    {
        var markup = Markup(TypstDocument.Create().Paragraph("Un paragraphe"));

        Assert.Equal("Un paragraphe" + "\n", markup);
        Assert.DoesNotContain("#[", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_set_rule_inside_a_section_body_stays_inside_the_block()
    {
        var markup = Markup(TypstDocument.Create().Section(
            section => section.Footer("Partie I"),
            body => body.Page(page => page.Margin(TypstLength.Cm(1))).Paragraph("Le contrat")));

        var rule = markup.IndexOf("#set page(margin: 1cm)", StringComparison.Ordinal);
        var close = markup.LastIndexOf(']');

        Assert.True(rule > 0, "the body's own rule is written");
        Assert.True(rule < close, "the body's own rule is written inside the section's block");
    }

    private static int Occurrences(string text, string value)
        => text.Split(value, StringSplitOptions.None).Length - 1;
}
