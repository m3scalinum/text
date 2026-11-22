using System;
using M3scalinum.Text.Documents;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

/// <summary>
/// <see cref="TypstDocumentStyle"/>: one reusable style for page, body, headings, language, header and
/// footer, applied to any document, writing nothing where it is not configured.
/// </summary>
public sealed class DocumentStyleTests
{
    private static TypstDocumentStyle ReportStyle() => TypstDocumentStyle.Create()
        .Page(p => p.Paper("a4").Margin(TypstLength.Cm(2.5)))
        .Body(b => b.Font("Libertinus Serif").Size(TypstLength.Pt(11)).Leading(1.4).Justify())
        .Heading(1, h => h.Size(TypstLength.Pt(18)).Weight("bold").Spacing(TypstLength.Pt(18), TypstLength.Pt(8)))
        .Heading(2, h => h.Size(TypstLength.Pt(14)))
        .Heading(3, h => h.Size(TypstLength.Pt(12)))
        .HeadingNumbering()
        .Language(TypstLanguage.French)
        .Header("Compte rendu")
        .Footer("{title} — page {page} / {pages}")
        .Build();

    private static string Markup(TypstDocumentStyle style)
        => TypstDocument.Create().Apply(style).Paragraph("x").ToMarkup();

    [Fact]
    public void A_page_format_and_a_margin_are_written_once()
    {
        var markup = Markup(TypstDocumentStyle.Create()
            .Page(p => p.Paper("a4").Margin(TypstLength.Cm(2.5)))
            .Build());

        Assert.Contains("#set page(paper: \"a4\", margin: 2.5cm)", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_landscape_page_sets_the_flipped_flag()
    {
        var markup = Markup(TypstDocumentStyle.Create()
            .Page(p => p.Paper("a4").Landscape().Margin(TypstLength.Cm(2)))
            .Build());

        Assert.Contains("paper: \"a4\"", markup, StringComparison.Ordinal);
        Assert.Contains("flipped: true", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_custom_page_size_writes_a_width_and_a_height()
    {
        var markup = Markup(TypstDocumentStyle.Create()
            .Page(p => p.Size(TypstLength.Mm(210), TypstLength.Mm(297)))
            .Build());

        Assert.Contains("width: 210mm, height: 297mm", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_body_family_size_leading_and_justification_are_written()
    {
        var markup = Markup(TypstDocumentStyle.Create()
            .Body(b => b.Font("Libertinus Serif").Size(TypstLength.Pt(11)).Leading(1.4).Justify())
            .Build());

        Assert.Contains(
            "#set text(font: \"Libertinus Serif\", size: 11pt)",
            markup,
            StringComparison.Ordinal);
        Assert.Contains("#set par(leading: 1.4em, justify: true)", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_body_without_justification_leaves_the_paragraph_alignment_alone()
    {
        var markup = Markup(TypstDocumentStyle.Create()
            .Body(b => b.Size(TypstLength.Pt(10)).Justify(false))
            .Build());

        Assert.DoesNotContain("justify", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("#set par(", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_heading_level_is_styled_by_its_own_show_rule()
    {
        var markup = Markup(ReportStyle());

        Assert.Contains(
            "#show heading.where(level: 1): it => { set text(size: 18pt, weight: \"bold\"); "
            + "set block(above: 18pt, below: 8pt); it }",
            markup,
            StringComparison.Ordinal);
        Assert.Contains(
            "#show heading.where(level: 2): it => { set text(size: 14pt); it }",
            markup,
            StringComparison.Ordinal);
        Assert.Contains(
            "#show heading.where(level: 3): it => { set text(size: 12pt); it }",
            markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_heading_level_that_is_not_configured_emits_no_rule()
    {
        var markup = Markup(TypstDocumentStyle.Create()
            .Heading(2, h => h.Size(TypstLength.Pt(14)))
            .Build());

        Assert.Contains("heading.where(level: 2)", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("level: 1", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("level: 3", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Heading_numbering_is_set_on_the_heading_element()
    {
        var markup = Markup(TypstDocumentStyle.Create().HeadingNumbering().Build());

        Assert.Contains("#set heading(numbering: \"1.1.1\")", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Heading_numbering_can_be_turned_back_off()
    {
        var markup = Markup(TypstDocumentStyle.Create()
            .HeadingNumbering()
            .HeadingNumbering(false)
            .Build());

        Assert.DoesNotContain("numbering", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_heading_level_outside_one_to_three_is_refused()
    {
        var builder = TypstDocumentStyle.Create();

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Heading(0, heading => heading.Size(TypstLength.Pt(9))));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Heading(4, heading => heading.Size(TypstLength.Pt(9))));
    }

    [Fact]
    public void The_language_becomes_a_text_language_code()
    {
        var french = Markup(TypstDocumentStyle.Create().Language(TypstLanguage.French).Build());
        var english = Markup(TypstDocumentStyle.Create().Language(TypstLanguage.English).Build());

        Assert.Contains("#set text(lang: \"fr\")", french, StringComparison.Ordinal);
        Assert.Contains("#set text(lang: \"en\")", english, StringComparison.Ordinal);
    }

    [Fact]
    public void An_undefined_language_is_refused_rather_than_guessed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TypstDocumentStyle.Create().Language((TypstLanguage)42));
    }

    [Fact]
    public void The_body_family_and_the_language_share_one_text_rule()
    {
        var markup = Markup(TypstDocumentStyle.Create()
            .Body(b => b.Font("Libertinus Serif"))
            .Language(TypstLanguage.French)
            .Build());

        Assert.Contains(
            "#set text(font: \"Libertinus Serif\", lang: \"fr\")",
            markup,
            StringComparison.Ordinal);
        Assert.Equal(1, Count(markup, "#set text("));
    }

    [Fact]
    public void The_header_is_written_as_literal_text()
    {
        var markup = Markup(TypstDocumentStyle.Create().Header("Compte rendu").Build());

        Assert.Contains("header: context [ Compte rendu ]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_footer_replaces_the_page_pages_and_title_placeholders()
    {
        var markup = Markup(TypstDocumentStyle.Create()
            .Footer("{title} — page {page} / {pages}")
            .Build());

        Assert.Contains(
            "footer: context [ #document.title — page #counter(page).display() / "
            + "#counter(page).final().first() ]",
            markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_placeholder_stays_literal_text()
    {
        var markup = Markup(TypstDocumentStyle.Create().Header("{reference}").Build());

        Assert.Contains("context [ {reference} ]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Markup_characters_in_the_running_text_are_escaped()
    {
        var markup = Markup(TypstDocumentStyle.Create().Header("A * B").Build());

        Assert.Contains("context [ A \\* B ]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_blank_running_text_is_refused()
    {
        var builder = TypstDocumentStyle.Create();

        Assert.Throws<ArgumentException>(() => builder.Header(" "));
        Assert.Throws<ArgumentException>(() => builder.Footer(string.Empty));
    }

    [Fact]
    public void A_document_without_a_style_keeps_exactly_its_markup()
    {
        var without = TypstDocument.Create()
            .Page(page => page.Paper("a4").Margin(TypstLength.Cm(2)))
            .Metadata(metadata => metadata.Title("Rapport"))
            .Heading(1, "Titre")
            .Paragraph("Corps")
            .ToMarkup();

        Assert.Equal(
            "#set document(title: \"Rapport\")\n"
            + "#set page(paper: \"a4\", margin: 2cm)\n"
            + "\n"
            + "= Titre\n"
            + "\n"
            + "Corps\n",
            without);
    }

    [Fact]
    public void Applying_the_same_style_twice_does_not_duplicate_its_rules()
    {
        var style = ReportStyle();
        var once = TypstDocument.Create().Apply(style).Paragraph("x").ToMarkup();
        var twice = TypstDocument.Create().Apply(style).Apply(style).Paragraph("x").ToMarkup();

        Assert.Equal(once, twice);
        Assert.Equal(1, Count(twice, "#set page("));
        Assert.Equal(1, Count(twice, "heading.where(level: 1)"));
    }

    [Fact]
    public void A_style_and_the_documents_own_page_setup_coexist()
    {
        var markup = TypstDocument.Create()
            .Page(page => page.Fill(TypstColor.White).WithHeader("Maison"))
            .Apply(TypstDocumentStyle.Create()
                .Page(p => p.Margin(TypstLength.Cm(2)))
                .Build())
            .Paragraph("x")
            .ToMarkup();

        // The document's fill and header survive: the style only declares a margin.
        Assert.Contains("fill: white", markup, StringComparison.Ordinal);
        Assert.Contains("header: [Maison]", markup, StringComparison.Ordinal);
        Assert.Contains("#set page(margin: 2cm)", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_style_reaches_an_imported_document()
    {
        var markup = TypstDocument
            .FromMarkdown("# Title\n\nBody")
            .Apply(TypstDocumentStyle.Create()
                .Body(b => b.Size(TypstLength.Pt(11)))
                .Heading(1, h => h.Size(TypstLength.Pt(18)))
                .Build())
            .ToMarkup();

        Assert.Contains("#set text(size: 11pt)", markup, StringComparison.Ordinal);
        Assert.Contains("heading.where(level: 1)", markup, StringComparison.Ordinal);
        Assert.Contains("= Title", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_style_rules_come_before_the_body_and_after_the_documents_own_rules()
    {
        var markup = TypstDocument.Create()
            .Metadata(metadata => metadata.Title("Rapport"))
            .Apply(TypstDocumentStyle.Create().Language(TypstLanguage.English).Build())
            .Paragraph("x")
            .ToMarkup();

        Assert.True(
            markup.IndexOf("#set document(", StringComparison.Ordinal)
            < markup.IndexOf("#set text(lang: \"en\")", StringComparison.Ordinal));
        Assert.True(
            markup.IndexOf("#set text(lang: \"en\")", StringComparison.Ordinal)
            < markup.IndexOf('x'));
    }

    [Fact]
    public void An_empty_style_writes_nothing()
    {
        var markup = TypstDocument.Create()
            .Apply(TypstDocumentStyle.Create().Build())
            .Paragraph("x")
            .ToMarkup();

        Assert.Equal("x\n", markup);
    }

    [Fact]
    public void Applying_a_null_style_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => TypstDocument.Create().Apply(null!));
    }

    [Fact]
    public void A_builder_can_build_twice_and_both_styles_are_independent()
    {
        var builder = TypstDocumentStyle.Create().Body(b => b.Size(TypstLength.Pt(10)));
        var first = builder.Build();
        builder.Body(b => b.Size(TypstLength.Pt(12)));
        var second = builder.Build();

        Assert.Contains("size: 10pt", Markup(first), StringComparison.Ordinal);
        Assert.Contains("size: 12pt", Markup(second), StringComparison.Ordinal);
        Assert.Contains("size: 10pt", Markup(first), StringComparison.Ordinal);
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        var index = 0;

        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
