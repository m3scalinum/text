using System;
using System.Collections.Generic;
using System.Linq;
using M3scalinum.Text.Documents;
using M3scalinum.Text.Typst.Compilation;
using Xunit;

namespace M3scalinum.Text.Typst.Integration.Tests;

/// <summary>
/// Sections against the real compiler: three parts with three footers, a cover without a page
/// number, a watermark drawn behind the text, and a header that is re-drawn on every page of its
/// section rather than once at its top.
/// </summary>
/// <remarks>
/// The page text a <see cref="CompilationResult"/> exposes is the reading of the produced PDF, so
/// every assertion here is about what Typst actually laid out, not about the markup the model wrote.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class SectionHeaderIntegrationTests
{
    private static TypstDocumentStyle ReportStyle() => TypstDocumentStyle.Create()
        .Page(page => page.Paper("a4").Margin(TypstLength.Cm(2)).Numbering("1"))
        .Body(body => body.Size(TypstLength.Pt(11)))
        .Footer("Rapport — page {page}")
        .Build();

    private static CompilationResult Compile(TypstDocument document)
    {
        var world = new TypstWorldBuilder().AddSource("main.typ", document.ToMarkup()).Build();
        return new TypstCompiler().Compile(world, new TypstCompilationOptions());
    }

    private static string Paragraphs(int count) => string.Join(
        "\n\n",
        Enumerable.Repeat(
            "Le paragraphe de remplissage occupe la page sans rien dire de particulier.",
            count));

    [Fact]
    public void Three_sections_carry_three_footers_and_the_cover_has_no_page_number()
    {
        var document = TypstDocument.Create()
            .Apply(ReportStyle())
            .Section(
                section => section.NoPageNumber().NoHeader().Footer("COUVERTURE"),
                body => body.Heading(1, "Rapport annuel").Paragraph("Page de garde"))
            .Section(
                section => section
                    .StartsOnNewPage()
                    .Header("PARTIE UN")
                    .Footer("PARTIE UN — page {page}"),
                body => body.Heading(1, "Contexte").Paragraph("Le contrat commence ici."))
            .Section(
                section => section
                    .StartsOnNewPage()
                    .Header("PARTIE DEUX")
                    .Footer("PARTIE DEUX — page {page}"),
                body => body.Heading(1, "Annexes").Paragraph("Les annexes suivent."));

        using var result = Compile(document);

        Assert.Equal(3, result.PageCount);

        var pages = result.ExtractTextPages();

        Assert.Contains("COUVERTURE", pages[0], StringComparison.Ordinal);
        Assert.DoesNotContain("page 1", pages[0], StringComparison.Ordinal);
        Assert.DoesNotContain("PARTIE UN", pages[0], StringComparison.Ordinal);

        Assert.Contains("PARTIE UN — page 2", pages[1], StringComparison.Ordinal);
        Assert.Contains("PARTIE DEUX — page 3", pages[2], StringComparison.Ordinal);
        Assert.DoesNotContain("COUVERTURE", pages[1], StringComparison.Ordinal);
        Assert.DoesNotContain("PARTIE UN", pages[2], StringComparison.Ordinal);
    }

    [Fact]
    public void A_header_is_drawn_on_every_page_of_its_section_and_stops_at_the_next()
    {
        var document = TypstDocument.Create()
            .Apply(ReportStyle())
            .Section(
                section => section.Header("PARTIE LONGUE").Footer("page {page}"),
                body => body.Paragraph(Paragraphs(120)))
            .Section(
                section => section
                    .StartsOnNewPage()
                    .Header("PARTIE COURTE")
                    .Footer("page {page}"),
                body => body.Paragraph("Fin."));

        using var result = Compile(document);

        Assert.True(result.PageCount >= 3, "the long section spans at least two pages");

        var pages = result.ExtractTextPages();
        var last = pages.Count - 1;

        for (var page = 0; page < last; page++)
        {
            Assert.StartsWith("PARTIE LONGUE", pages[page], StringComparison.Ordinal);
        }

        Assert.StartsWith("PARTIE COURTE", pages[last], StringComparison.Ordinal);
    }

    [Fact]
    public void The_watermark_is_drawn_on_the_page_and_the_body_text_stays_intact()
    {
        var document = TypstDocument.Create()
            .Apply(ReportStyle())
            .Section(
                section => section.Watermark("BROUILLON"),
                body => body.Paragraph("Le texte du contrat reste lisible."));

        using var result = Compile(document);

        var page = result.ExtractText(0);

        Assert.Contains("BROUILLON", page, StringComparison.Ordinal);
        Assert.Contains("Le texte du contrat reste lisible.", page, StringComparison.Ordinal);
        Assert.True(
            page.IndexOf("BROUILLON", StringComparison.Ordinal)
                < page.IndexOf("Le texte du contrat", StringComparison.Ordinal),
            "the background is read before the body it is drawn behind");
    }

    [Fact]
    public void A_later_section_can_drop_the_watermark()
    {
        var document = TypstDocument.Create()
            .Apply(ReportStyle())
            .Section(
                section => section.Watermark("CONFIDENTIEL"),
                body => body.Paragraph("Première partie."))
            .Section(
                section => section.StartsOnNewPage().NoWatermark(),
                body => body.Paragraph("Seconde partie."));

        using var result = Compile(document);

        Assert.Equal(2, result.PageCount);

        var pages = result.ExtractTextPages();

        Assert.Contains("CONFIDENTIEL", pages[0], StringComparison.Ordinal);
        Assert.DoesNotContain("CONFIDENTIEL", pages[1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_section_page_numbering_format_reaches_the_pages()
    {
        var document = TypstDocument.Create()
            .Apply(ReportStyle())
            .Section(
                section => section.PageNumbering("i").Footer("Préliminaires — {page}"),
                body => body.Paragraph("Sommaire."))
            .Section(
                section => section.StartsOnNewPage().NoHeader().Footer("Corps — page {page}"),
                body => body.Paragraph("Chapitre premier."));

        using var result = Compile(document);

        var pages = result.ExtractTextPages();

        Assert.Contains("Préliminaires — i", pages[0], StringComparison.Ordinal);
        Assert.Contains("Corps — page 2", pages[1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_section_keeps_the_document_footer_it_does_not_override()
    {
        var document = TypstDocument.Create()
            .Apply(ReportStyle())
            .Section(
                section => section.Header("SECTION SANS PIED"),
                body => body.Paragraph("Le pied du document reste."));

        using var result = Compile(document);

        Assert.Contains("Rapport — page 1", result.ExtractText(0), StringComparison.Ordinal);
    }

    [Fact]
    public void A_change_of_page_setup_starts_a_new_page_so_two_sections_never_share_one()
    {
        var document = TypstDocument.Create()
            .Apply(ReportStyle())
            .Paragraph("Texte avant la première section.")
            .Section(
                section => section.Footer("PREMIÈRE"),
                body => body.Paragraph("Corps de la première partie."))
            .Section(
                section => section.Footer("SECONDE"),
                body => body.Paragraph("Corps de la seconde partie."));

        using var result = Compile(document);

        Assert.Equal(3, result.PageCount);

        var pages = result.ExtractTextPages();

        Assert.DoesNotContain("PREMIÈRE", pages[0], StringComparison.Ordinal);
        Assert.Contains("PREMIÈRE", pages[1], StringComparison.Ordinal);
        Assert.Contains("SECONDE", pages[2], StringComparison.Ordinal);
        Assert.DoesNotContain("SECONDE", pages[1], StringComparison.Ordinal);
    }

    [Fact]
    public void The_document_footer_comes_back_after_the_section_closes()
    {
        var document = TypstDocument.Create()
            .Apply(ReportStyle())
            .Section(
                section => section.Footer("DANS LA SECTION"),
                body => body.Paragraph("Le corps de la section."))
            .Paragraph("Après la section.");

        using var result = Compile(document);

        var pages = result.ExtractTextPages();

        Assert.Equal(2, result.PageCount);
        Assert.Contains("DANS LA SECTION", pages[0], StringComparison.Ordinal);
        Assert.Contains("Rapport — page 2", pages[1], StringComparison.Ordinal);
        Assert.DoesNotContain("DANS LA SECTION", pages[1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_without_a_section_still_renders_its_own_footer()
    {
        var document = TypstDocument.Create()
            .Apply(ReportStyle())
            .Paragraph("Un document d'un seul tenant.");

        using var result = Compile(document);

        Assert.Equal(1, result.PageCount);
        Assert.Contains("Rapport — page 1", result.ExtractText(0), StringComparison.Ordinal);
    }
}
