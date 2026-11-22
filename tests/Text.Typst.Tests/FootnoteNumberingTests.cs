using System;
using M3scalinum.Text.Documents;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

/// <summary>
/// The markup a scoped number writes: the default is untouched, and a section scope adds the rules
/// Typst needs to restart its own counters. Numbering correctness is proved by the compiled tests in
/// the integration project; this file pins the markup those tests rely on.
/// </summary>
public sealed class FootnoteNumberingTests
{
    private const string FootnoteReset =
        "#show heading.where(level: 1): it => {\n" +
        "  counter(footnote).update(0)\n" +
        "  it\n" +
        "}";

    private const string FigureReset =
        "#show heading.where(level: 1): it => {\n" +
        "  counter(figure.where(kind: image)).update(0)\n" +
        "  it\n" +
        "}";

    private const string FigureNumbering =
        "#show figure.where(kind: image): set figure(numbering: n => {\n" +
        "  let section = counter(heading).get().first()\n" +
        "  if section == 0 { numbering(\"1\", n) } else { numbering(\"1.1\", section, n) }\n" +
        "})";

    [Fact]
    public void A_document_footnote_writes_the_markup_it_always_did()
    {
        var markup = TypstDocument.Create()
            .Paragraph(c => c.Text("claim").Footnote("because"))
            .Footnote("a note")
            .ToMarkup();

        Assert.Contains("#footnote[because]", markup, StringComparison.Ordinal);
        Assert.Contains("#footnote[a note]", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("counter(", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("#show heading", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_section_footnote_adds_the_reset_rule_and_keeps_the_note_markup()
    {
        var markup = TypstDocument.Create()
            .Heading(1, "One")
            .Footnote("a note", TypstNumberingScope.Section)
            .ToMarkup();

        Assert.Contains(FootnoteReset, markup, StringComparison.Ordinal);
        Assert.Contains("#footnote[a note]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_footnote_reset_is_written_once_however_many_notes_ask_for_it()
    {
        var markup = TypstDocument.Create()
            .Footnote("one", TypstNumberingScope.Section)
            .Footnote("two", TypstNumberingScope.Section)
            .ToMarkup();

        Assert.Equal(1, Occurrences(markup, FootnoteReset));
    }

    [Fact]
    public void A_document_figure_writes_the_markup_it_always_did()
    {
        var markup = TypstDocument.Create()
            .Figure(f => f.Image("charts/revenue.png").WithCaption("Revenue"))
            .ToMarkup();

        Assert.Contains("#figure(", markup, StringComparison.Ordinal);
        Assert.Contains("caption: [Revenue]", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("#show figure", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("counter(", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_section_figure_adds_the_section_rules()
    {
        var markup = TypstDocument.Create()
            .Heading(1, "One")
            .Figure(f => f.Image("charts/revenue.png").WithCaption("Revenue"), TypstNumberingScope.Section)
            .ToMarkup();

        Assert.Contains("#set heading(numbering: \"1.1\")", markup, StringComparison.Ordinal);
        Assert.Contains(FigureReset, markup, StringComparison.Ordinal);
        Assert.Contains(FigureNumbering, markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_section_figure_keeps_the_heading_numbering_the_document_already_set()
    {
        var markup = TypstDocument.Create()
            .Heading(1, "One")
            .Label(TypstLabelKind.Section)
            .Figure(f => f.Image("charts/revenue.png"), TypstNumberingScope.Section)
            .ToMarkup();

        Assert.Equal(1, Occurrences(markup, "#set heading(numbering"));
    }

    [Fact]
    public void The_section_figure_rules_are_written_once()
    {
        var markup = TypstDocument.Create()
            .Figure(f => f.Image("a.png"), TypstNumberingScope.Section)
            .Figure(f => f.Image("b.png"), TypstNumberingScope.Section)
            .ToMarkup();

        Assert.Equal(1, Occurrences(markup, FigureReset));
        Assert.Equal(1, Occurrences(markup, FigureNumbering));
        Assert.Equal(1, Occurrences(markup, "#set heading(numbering"));
    }

    [Fact]
    public void Footnote_and_Figure_scope_names_are_the_only_accepted_ones()
    {
        var document = TypstDocument.Create();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => document.Footnote("note", (TypstNumberingScope)99));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => document.Figure(f => f.Image("a.png"), (TypstNumberingScope)99));
    }

    [Fact]
    public void Footnote_still_accepts_a_bare_string()
    {
        var markup = TypstDocument.Create().Footnote("because").ToMarkup();

        Assert.Contains("#footnote[because]", markup, StringComparison.Ordinal);
    }

    private static int Occurrences(string text, string value)
    {
        var count = 0;

        for (var index = text.IndexOf(value, StringComparison.Ordinal);
             index >= 0;
             index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
