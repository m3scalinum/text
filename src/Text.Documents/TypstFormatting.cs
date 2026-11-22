using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace M3scalinum.Text.Documents;

/// <summary>
/// Writes dates, numbers and amounts the way one language writes them, as a pure function of the
/// language, the pattern and the value.
/// </summary>
/// <remarks>
/// <para>
/// The suite builds and ships with <c>InvariantGlobalization=true</c>, and most of the containers it
/// runs in do too: <c>CultureInfo.GetCultureInfo("fr-FR")</c> throws there, and every
/// <c>ToString</c> format that takes a culture silently produces the invariant result when the
/// culture is missing. Instead of asking the runtime for a language, this type carries a table of
/// patterns — one row per language, written in the code, like
/// <c>M3scalinum.Text.Templates.TemplateLabels</c> — and applies it by transforming separators. The
/// only culture object it touches is <see cref="CultureInfo.InvariantCulture"/>, which exists
/// whether or not globalization is invariant, and which it uses solely as the number-to-digits
/// conversion: the separators afterwards are the table's.
/// </para>
/// <para>
/// The output is deterministic: same language, same format, same decimal give the same string on
/// every machine, in every process, whatever <see cref="CultureInfo.CurrentCulture"/> says.
/// </para>
/// </remarks>
public static class TypstFormatting
{
    /// <summary>
    /// The no-break space U+00A0. The table uses it for the French thousands separator and between an
    /// amount and its symbol.
    /// </summary>
    /// <remarks>
    /// U+202F (narrow no-break space) is the character French typography prefers, but the compiler
    /// does not know it: in Typst 0.15.1 only U+00A0 is special-cased in shaping — its advance is
    /// normalised to the width of a normal space and it is treated as a space for justification —
    /// while U+202F is left to the font, and draws as a missing glyph in a face that lacks it. U+00A0
    /// is also what <c>~</c> compiles to in Typst markup. See 32-localized-formatting.md §4.
    /// </remarks>
    public const char NoBreakSpace = '\u00A0';

    private static readonly Dictionary<string, string> CurrencySymbols =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["EUR"] = "€",
            ["USD"] = "$",
            ["GBP"] = "£",
            ["CHF"] = "CHF",
            ["JPY"] = "¥",
            ["CAD"] = "CA$",
        };

    /// <summary>Writes a number in a language: grouped integer part, then the fraction digits.</summary>
    /// <param name="value">The value; it is rounded away from zero to the format's fraction digits.</param>
    /// <param name="format">Language and number of fraction digits.</param>
    /// <returns>For example <c>1&#160;234,56</c> in French and <c>1,234.56</c> in English.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> names no known language.</exception>
    public static string FormatNumber(decimal value, TypstNumberFormat format)
        => Number(value, format.FractionDigits, Pattern(format.Language));

    /// <summary>
    /// Writes an amount: a number with two fraction digits, and the currency symbol on the side the
    /// language puts it.
    /// </summary>
    /// <param name="value">The amount; it is rounded away from zero to two fraction digits.</param>
    /// <param name="isoCode">
    /// ISO 4217 code. A code the table does not know is printed as the caller wrote it, so the output
    /// stays readable instead of guessing a symbol.
    /// </param>
    /// <param name="language">Language of the separators and of the symbol position.</param>
    /// <returns>For example <c>1&#160;234,56&#160;€</c> in French and <c>€1,234.56</c> in English.</returns>
    /// <exception cref="ArgumentException"><paramref name="isoCode"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="language"/> is not a known language.</exception>
    public static string FormatCurrency(decimal value, string isoCode, TypstLanguage language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(isoCode);

        var pattern = Pattern(language);
        var number = Number(value, 2, pattern);

        return pattern.SymbolComesFirst
            ? Symbol(isoCode) + number
            : number + pattern.SymbolSeparator + Symbol(isoCode);
    }

    /// <summary>Writes a date with the default pattern of a language.</summary>
    /// <param name="date">The date; its calendar is already Gregorian, as <see cref="DateOnly"/> is.</param>
    /// <param name="language">Language whose day/month order is used.</param>
    /// <returns>For example <c>01/03/2026</c> in French and <c>03/01/2026</c> in English.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="language"/> is not a known language.</exception>
    public static string FormatDate(DateOnly date, TypstLanguage language)
    {
        var pattern = Pattern(language);
        var day = TwoDigits(date.Day);
        var month = TwoDigits(date.Month);
        var year = date.Year.ToString("D4", CultureInfo.InvariantCulture);

        return pattern.DayComesFirst
            ? $"{day}{pattern.DateSeparator}{month}{pattern.DateSeparator}{year}"
            : $"{month}{pattern.DateSeparator}{day}{pattern.DateSeparator}{year}";
    }

    private static string Number(decimal value, int fractionDigits, LanguagePattern pattern)
    {
        var rounded = decimal.Round(value, fractionDigits, MidpointRounding.AwayFromZero);
        var text = Math.Abs(rounded).ToString(
            "F" + fractionDigits.ToString(CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture);

        var point = text.IndexOf('.', StringComparison.Ordinal);
        var whole = point < 0 ? text : text[..point];
        var fraction = point < 0 ? string.Empty : text[(point + 1)..];

        var builder = new StringBuilder(text.Length + (whole.Length / 3) + 1);

        if (rounded < 0m)
        {
            builder.Append('-');
        }

        AppendGrouped(builder, whole, pattern.ThousandsSeparator);

        if (fraction.Length > 0)
        {
            builder.Append(pattern.DecimalSeparator).Append(fraction);
        }

        return builder.ToString();
    }

    private static void AppendGrouped(StringBuilder builder, string digits, string separator)
    {
        for (var index = 0; index < digits.Length; index++)
        {
            if (index > 0 && (digits.Length - index) % 3 == 0)
            {
                builder.Append(separator);
            }

            builder.Append(digits[index]);
        }
    }

    private static string TwoDigits(int value) => value.ToString("D2", CultureInfo.InvariantCulture);

    private static string Symbol(string isoCode)
        => CurrencySymbols.TryGetValue(isoCode, out var symbol) ? symbol : isoCode;

    private static LanguagePattern Pattern(TypstLanguage language) => language switch
    {
        TypstLanguage.English => new LanguagePattern(
            ThousandsSeparator: ",",
            DecimalSeparator: ".",
            DateSeparator: "/",
            DayComesFirst: false,
            SymbolComesFirst: true,
            SymbolSeparator: string.Empty),

        TypstLanguage.French => new LanguagePattern(
            ThousandsSeparator: NoBreakSpace.ToString(),
            DecimalSeparator: ",",
            DateSeparator: "/",
            DayComesFirst: true,
            SymbolComesFirst: false,
            SymbolSeparator: NoBreakSpace.ToString()),

        _ => throw new ArgumentOutOfRangeException(
            nameof(language),
            language,
            "no pattern for this language: the table has a row for English and French"),
    };

    private readonly record struct LanguagePattern(
        string ThousandsSeparator,
        string DecimalSeparator,
        string DateSeparator,
        bool DayComesFirst,
        bool SymbolComesFirst,
        string SymbolSeparator);
}
