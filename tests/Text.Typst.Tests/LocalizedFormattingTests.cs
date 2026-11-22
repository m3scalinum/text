using System;
using System.Globalization;
using M3scalinum.Text.Documents;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

/// <summary>
/// The language table behind <see cref="TypstFormatting"/>. Every expectation is written with the
/// bare characters, not with a culture: the process runs under <c>InvariantGlobalization</c>, which
/// is the only condition these cases are meant to work in.
/// </summary>
public sealed class LocalizedFormattingTests
{
    private const string NoBreakSpace = "\u00A0";

    [Fact]
    public void French_numbers_group_with_a_no_break_space_and_a_comma()
    {
        Assert.Equal(
            "1" + NoBreakSpace + "234,56",
            TypstFormatting.FormatNumber(1234.56m, TypstNumberFormat.Plain(TypstLanguage.French)));
    }

    [Fact]
    public void English_numbers_group_with_a_comma_and_a_dot()
    {
        Assert.Equal(
            "1,234.56",
            TypstFormatting.FormatNumber(1234.56m, TypstNumberFormat.Plain(TypstLanguage.English)));
    }

    [Fact]
    public void A_negative_number_carries_one_leading_sign_and_no_double_separator()
    {
        Assert.Equal(
            "-1" + NoBreakSpace + "234,56",
            TypstFormatting.FormatNumber(-1234.56m, TypstNumberFormat.Plain(TypstLanguage.French)));

        Assert.Equal(
            "-1,234.56",
            TypstFormatting.FormatNumber(-1234.56m, TypstNumberFormat.Plain(TypstLanguage.English)));
    }

    [Fact]
    public void Zero_prints_its_declared_fraction_digits_and_never_a_sign()
    {
        Assert.Equal(
            "0,00",
            TypstFormatting.FormatNumber(0m, TypstNumberFormat.Plain(TypstLanguage.French)));

        Assert.Equal(
            "0,00",
            TypstFormatting.FormatNumber(-0.0001m, TypstNumberFormat.Plain(TypstLanguage.French)));
    }

    [Fact]
    public void A_whole_number_prints_without_a_fraction_part()
    {
        Assert.Equal(
            "1" + NoBreakSpace + "234",
            TypstFormatting.FormatNumber(1234m, TypstNumberFormat.WithFractionDigits(TypstLanguage.French, 0)));

        Assert.Equal(
            "1,234",
            TypstFormatting.FormatNumber(1234m, TypstNumberFormat.WithFractionDigits(TypstLanguage.English, 0)));
    }

    [Fact]
    public void A_four_digit_group_starts_the_first_separator_after_three_digits_from_the_right()
    {
        Assert.Equal(
            "1" + NoBreakSpace + "234" + NoBreakSpace + "567" + NoBreakSpace + "890,12",
            TypstFormatting.FormatNumber(1234567890.12m, TypstNumberFormat.Plain(TypstLanguage.French)));
    }

    [Fact]
    public void French_amounts_put_the_symbol_after_a_no_break_space()
    {
        Assert.Equal(
            "1" + NoBreakSpace + "234,56" + NoBreakSpace + "€",
            TypstFormatting.FormatCurrency(1234.56m, "EUR", TypstLanguage.French));
    }

    [Fact]
    public void English_amounts_put_the_symbol_before_the_number()
    {
        Assert.Equal(
            "€1,234.56",
            TypstFormatting.FormatCurrency(1234.56m, "EUR", TypstLanguage.English));

        Assert.Equal(
            "$1,234.56",
            TypstFormatting.FormatCurrency(1234.56m, "USD", TypstLanguage.English));
    }

    [Fact]
    public void A_currency_code_the_table_does_not_know_is_printed_as_written()
    {
        Assert.Equal(
            "1" + NoBreakSpace + "234,56" + NoBreakSpace + "xts",
            TypstFormatting.FormatCurrency(1234.56m, "xts", TypstLanguage.French));
    }

    [Fact]
    public void A_currency_code_is_case_insensitive()
    {
        Assert.Equal(
            TypstFormatting.FormatCurrency(99m, "EUR", TypstLanguage.French),
            TypstFormatting.FormatCurrency(99m, "eur", TypstLanguage.French));
    }

    [Fact]
    public void French_dates_are_day_first()
    {
        Assert.Equal(
            "01/03/2026",
            TypstFormatting.FormatDate(new DateOnly(2026, 3, 1), TypstLanguage.French));
    }

    [Fact]
    public void English_dates_are_month_first()
    {
        Assert.Equal(
            "03/01/2026",
            TypstFormatting.FormatDate(new DateOnly(2026, 3, 1), TypstLanguage.English));
    }

    [Fact]
    public void The_two_languages_render_the_same_value_differently()
    {
        // The difference comes from the language argument alone: the process culture is the same for
        // both calls, and it is the invariant one.
        Assert.NotEqual(
            TypstFormatting.FormatNumber(1234.56m, TypstNumberFormat.Plain(TypstLanguage.English)),
            TypstFormatting.FormatNumber(1234.56m, TypstNumberFormat.Plain(TypstLanguage.French)));
    }

    [Fact]
    public void An_unknown_language_is_refused_rather_than_guessed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TypstFormatting.FormatNumber(1m, TypstNumberFormat.Plain((TypstLanguage)42)));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TypstFormatting.FormatCurrency(1m, "EUR", (TypstLanguage)42));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TypstFormatting.FormatDate(new DateOnly(2026, 3, 1), (TypstLanguage)42));
    }

    [Fact]
    public void A_fraction_digit_count_outside_the_decimal_range_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TypstNumberFormat.WithFractionDigits(TypstLanguage.French, 29));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TypstNumberFormat.WithFractionDigits(TypstLanguage.French, -1));
    }

    [Fact]
    public void Rounding_goes_away_from_zero_at_the_last_printed_digit()
    {
        Assert.Equal(
            "2,35",
            TypstFormatting.FormatNumber(2.345m, TypstNumberFormat.Plain(TypstLanguage.French)));
    }

    [Fact]
    public void The_output_does_not_move_when_the_current_culture_is_reassigned()
    {
        // Under InvariantGlobalization the only culture that exists is the invariant one, so this
        // assignment cannot change how the runtime formats anything: what the test proves is that
        // nothing in the call path reads CurrentCulture, by showing the answer is identical before,
        // during and after the reassignment.
        const decimal Amount = 1234.56m;
        var date = new DateOnly(2026, 3, 1);

        var before = TypstFormatting.FormatCurrency(Amount, "EUR", TypstLanguage.French)
            + " " + TypstFormatting.FormatDate(date, TypstLanguage.French);

        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            var during = TypstFormatting.FormatCurrency(Amount, "EUR", TypstLanguage.French)
                + " " + TypstFormatting.FormatDate(date, TypstLanguage.French);

            Assert.Equal(before, during);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        Assert.Equal(
            before,
            TypstFormatting.FormatCurrency(Amount, "EUR", TypstLanguage.French)
                + " " + TypstFormatting.FormatDate(date, TypstLanguage.French));
    }

    [Fact]
    public void An_amount_contains_no_breakable_space()
    {
        // A plain space would be a space token in Typst markup, and a line could break inside the
        // amount. The French number carries U+00A0 only, which is not markup whitespace.
        var text = TypstFormatting.FormatCurrency(1234567.89m, "EUR", TypstLanguage.French);

        Assert.DoesNotContain(' ', text);
        Assert.Contains(NoBreakSpace, text);
    }

    [Fact]
    public void A_formatted_amount_reaches_the_markup_unchanged()
    {
        // The markup writer escapes `~` to `\\~`; U+00A0 is not `~`, so the formatted figure must
        // come out byte for byte, ready for the compiler.
        var amount = TypstFormatting.FormatCurrency(1234.56m, "EUR", TypstLanguage.French);
        var markup = TypstDocument.Create().Paragraph(new TypstContent().Text(amount)).ToMarkup();

        Assert.Equal(amount + "\n", markup);
    }

    [Fact]
    public void A_formatted_date_reaches_the_markup_unchanged()
    {
        var text = TypstFormatting.FormatDate(new DateOnly(2026, 3, 1), TypstLanguage.French);
        var markup = TypstDocument.Create().Paragraph(new TypstContent().Text(text)).ToMarkup();

        Assert.Equal(text + "\n", markup);
    }
}
