using System;
using M3scalinum.Text.Documents;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

/// <summary>
/// CJK and bidirectional text in the document model: the language tag a style or a run writes, and
/// the direction a run or a block writes. The markup is asserted here; what the compiler does with
/// it is <c>CjkAndBidiIntegrationTests</c>, which needs the native library.
/// </summary>
public sealed class CjkAndBidiTests
{
    private static string Markup(TypstDocument document) => document.ToMarkup();

    // ---- Language -------------------------------------------------------------------------

    [Fact]
    public void A_japanese_style_writes_its_language_tag()
    {
        var markup = Markup(TypstDocument.Create()
            .Apply(TypstDocumentStyle.Create().Language(TypstLanguageTag.Japanese).Build())
            .Paragraph("東京は日本の首都です。"));

        Assert.Contains("#set text(lang: \"ja\")", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_style_that_names_no_language_writes_no_lang_rule()
    {
        var markup = Markup(TypstDocument.Create()
            .Apply(TypstDocumentStyle.Create().Body(b => b.Size(TypstLength.Pt(11))).Build())
            .Paragraph("x"));

        Assert.DoesNotContain("lang:", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_tag_form_and_the_two_valued_form_do_not_both_write_a_rule()
    {
        var markup = Markup(TypstDocument.Create()
            .Apply(TypstDocumentStyle.Create()
                .Language(TypstLanguage.French)
                .Language(TypstLanguageTag.Japanese)
                .Build())
            .Paragraph("x"));

        Assert.Contains("lang: \"ja\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("lang: \"fr\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_two_valued_form_wins_when_it_comes_last()
    {
        var markup = Markup(TypstDocument.Create()
            .Apply(TypstDocumentStyle.Create()
                .Language(TypstLanguageTag.Japanese)
                .Language(TypstLanguage.French)
                .Build())
            .Paragraph("x"));

        Assert.Contains("lang: \"fr\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("lang: \"ja\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_language_comes_after_the_style_rules()
    {
        var markup = Markup(TypstDocument.Create()
            .Apply(TypstDocumentStyle.Create().Language(TypstLanguage.French).Build())
            .Language(TypstLanguageTag.Arabic)
            .Paragraph("مرحبا بالعالم"));

        var style = markup.IndexOf("lang: \"fr\"", StringComparison.Ordinal);
        var document = markup.IndexOf("#set text(lang: \"ar\")", StringComparison.Ordinal);

        Assert.True(style >= 0, markup);
        Assert.True(document > style, markup);
    }

    [Fact]
    public void A_run_carries_its_own_language()
    {
        var markup = Markup(TypstDocument.Create()
            .Paragraph(c => c
                .Text("The quotation reads ")
                .Language(TypstLanguageTag.Japanese, new TypstContent().Text("東京は日本の首都です。"))
                .Text(" in the original.")));

        Assert.Contains("#text(lang: \"ja\")[東京は日本の首都です。]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_default_tag_writes_no_language_and_is_refused_by_a_style()
    {
        Assert.Equal(string.Empty, default(TypstLanguageTag).Code);
        Assert.Throws<ArgumentException>(
            () => TypstDocumentStyle.Create().Language(default(TypstLanguageTag)));
    }

    [Fact]
    public void A_language_tag_with_a_region_is_read_as_written()
    {
        Assert.Equal("zh-Hans", TypstLanguageTag.Of("zh-Hans").Code);
        Assert.Equal("de-CH-1901", TypstLanguageTag.Of("de-CH-1901").Code);
    }

    [Fact]
    public void A_language_tag_that_is_not_one_is_refused()
    {
        Assert.Throws<ArgumentException>(() => TypstLanguageTag.Of(string.Empty));
        Assert.Throws<ArgumentException>(() => TypstLanguageTag.Of("   "));
        Assert.Throws<ArgumentException>(() => TypstLanguageTag.Of("j"));
        Assert.Throws<ArgumentException>(() => TypstLanguageTag.Of("ja_JP"));
        Assert.Throws<ArgumentException>(() => TypstLanguageTag.Of("ja-"));
        Assert.Throws<ArgumentException>(() => TypstLanguageTag.Of("123"));
        Assert.Throws<ArgumentException>(() => TypstLanguageTag.Of("japanese"));
    }

    [Fact]
    public void Two_tags_naming_the_same_language_are_equal_whatever_their_case()
    {
        Assert.Equal(TypstLanguageTag.Of("JA"), TypstLanguageTag.Japanese);
        Assert.True(TypstLanguageTag.Japanese == TypstLanguageTag.Of("ja"));
        Assert.False(TypstLanguageTag.Japanese != TypstLanguageTag.Of("ja"));
        Assert.NotEqual(TypstLanguageTag.Japanese, TypstLanguageTag.Chinese);
        Assert.Equal(TypstLanguageTag.Japanese.GetHashCode(), TypstLanguageTag.Of("ja").GetHashCode());
    }

    // ---- Direction ------------------------------------------------------------------------

    [Fact]
    public void A_run_carries_its_own_direction()
    {
        var markup = Markup(TypstDocument.Create()
            .Paragraph(c => c
                .Text("The Hebrew word is ")
                .Direction(TypstDirection.RightToLeft, new TypstContent().Text("שלום"))
                .Text(" in this sentence.")));

        Assert.Contains("#text(dir: rtl)[שלום]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_directed_block_holds_the_set_rule_that_turns_its_paragraphs_around()
    {
        var markup = Markup(TypstDocument.Create()
            .Direction(TypstDirection.RightToLeft, new TypstContent().Text("مرحبا بالعالم")));

        Assert.Contains("#block[#set text(dir: rtl)", markup, StringComparison.Ordinal);
        Assert.Contains("مرحبا بالعالم]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_that_asks_for_neither_language_nor_direction_emits_none()
    {
        var markup = Markup(TypstDocument.Create()
            .Paragraph("Plain, left to right, no language declared."));

        Assert.DoesNotContain("dir:", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("lang:", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_directed_block_without_content_is_refused()
    {
        Assert.Throws<ArgumentNullException>(
            () => TypstDocument.Create().Direction(TypstDirection.RightToLeft, null!));
        Assert.Throws<ArgumentNullException>(
            () => TypstContent.Empty.Direction(TypstDirection.RightToLeft, null!));
        Assert.Throws<ArgumentNullException>(
            () => TypstContent.Empty.Language(TypstLanguageTag.Arabic, null!));
    }

    [Fact]
    public void An_unknown_direction_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TypstDocument.Create().Direction((TypstDirection)42, new TypstContent().Text("x")));
    }

    [Fact]
    public void A_vertical_direction_is_refused_as_a_text_property()
    {
        // text(dir:) is horizontal; the compiler answers "text direction must be horizontal" to
        // ttb and btt, so the model refuses them instead of writing a document that cannot compile.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TypstDocument.Create().Direction(TypstDirection.TopToBottom, new TypstContent().Text("x")));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TypstDocument.Create()
                .Paragraph(c => c.Direction(TypstDirection.BottomToTop, new TypstContent().Text("x"))));
    }

    // ---- The characters themselves ---------------------------------------------------------

    [Fact]
    public void Full_width_punctuation_reaches_the_markup_unescaped()
    {
        const string Japanese = "東京は、日本の首都です。人口は約1400万人，増え続けています。";

        var markup = Markup(TypstDocument.Create().Paragraph(Japanese));

        Assert.Contains(Japanese, markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Arabic_and_hebrew_reach_the_markup_in_logical_order()
    {
        const string Arabic = "العدد 42 يساوي XLII حرفًا.";
        const string Hebrew = "שלום עולם, 2026";

        var markup = Markup(TypstDocument.Create()
            .Paragraph(Arabic)
            .Paragraph(Hebrew));

        Assert.Contains(Arabic, markup, StringComparison.Ordinal);
        Assert.Contains(Hebrew, markup, StringComparison.Ordinal);
    }
}
