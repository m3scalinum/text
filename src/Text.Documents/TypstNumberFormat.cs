using System;

namespace M3scalinum.Text.Documents;

/// <summary>
/// How <see cref="TypstFormatting.FormatNumber"/> writes a number: in which language, with how many
/// fraction digits.
/// </summary>
/// <remarks>
/// The separator characters are not part of this value: they belong to the language, and a caller
/// that could override them would be able to write a number no reader of that language recognises.
/// A format is therefore language plus width only.
/// </remarks>
public readonly record struct TypstNumberFormat
{
    private TypstNumberFormat(TypstLanguage language, int fractionDigits)
    {
        if (fractionDigits is < 0 or > 28)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fractionDigits), fractionDigits, "a decimal carries at most 28 fraction digits");
        }

        Language = language;
        FractionDigits = fractionDigits;
    }

    /// <summary>The language the separators and the digit order come from.</summary>
    public TypstLanguage Language { get; }

    /// <summary>How many fraction digits are printed, zeros included.</summary>
    public int FractionDigits { get; }

    /// <summary>
    /// The everyday format: grouped thousands and two fraction digits, which is what an amount on a
    /// business document shows.
    /// </summary>
    /// <param name="language">Language of the separators.</param>
    public static TypstNumberFormat Plain(TypstLanguage language) => new(language, 2);

    /// <summary>A format with an explicit number of fraction digits.</summary>
    /// <param name="language">Language of the separators.</param>
    /// <param name="fractionDigits">Between 0 and 28; 0 prints a quantity, not an amount.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="fractionDigits"/> is outside 0…28.</exception>
    public static TypstNumberFormat WithFractionDigits(TypstLanguage language, int fractionDigits)
        => new(language, fractionDigits);
}
