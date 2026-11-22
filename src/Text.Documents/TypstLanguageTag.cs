using System;

namespace M3scalinum.Text.Documents;

/// <summary>
/// The language Typst reads a piece of text in — the value of <c>text(lang: …)</c>, which picks the
/// hyphenation dictionary, the line-breaking rules and the language the PDF declares.
/// </summary>
/// <remarks>
/// <para>
/// This is not <see cref="TypstLanguage"/>. That enum is a closed table of number and date patterns:
/// it answers "how does this language write 1 234,56", so it only carries the languages
/// <see cref="TypstFormatting"/> has a row for. A tag answers "how does this language break a line",
/// and Typst keys that on the ISO 639 code, through a dictionary it carries for some sixty
/// languages. Chinese and Japanese have no hyphenation at all — their lines break between
/// characters, under the kinsoku rules — but they still need the tag, because it selects those
/// rules and the spacing between a CJK glyph and a Latin one.
/// </para>
/// <para>
/// The well-known tags are properties, so a caller writes <see cref="Japanese"/> rather than a
/// string; anything else is <see cref="Of"/>, which accepts a BCP 47 tag such as <c>zh-Hans</c> or
/// <c>de-CH</c> and refuses a string that is not one. The tag is not validated against a list of
/// languages: a tag Typst has no dictionary for hyphenates nothing, which is what a document in that
/// language does anyway.
/// </para>
/// </remarks>
public readonly struct TypstLanguageTag : IEquatable<TypstLanguageTag>
{
    private readonly string? _code;

    private TypstLanguageTag(string code) => _code = code;

    /// <summary>English, <c>en</c>.</summary>
    public static TypstLanguageTag English { get; } = new("en");

    /// <summary>French, <c>fr</c>.</summary>
    public static TypstLanguageTag French { get; } = new("fr");

    /// <summary>German, <c>de</c>.</summary>
    public static TypstLanguageTag German { get; } = new("de");

    /// <summary>Japanese, <c>ja</c>: no hyphenation, kinsoku line breaking.</summary>
    public static TypstLanguageTag Japanese { get; } = new("ja");

    /// <summary>Chinese, <c>zh</c>: no hyphenation, kinsoku line breaking.</summary>
    public static TypstLanguageTag Chinese { get; } = new("zh");

    /// <summary>Arabic, <c>ar</c>: right to left.</summary>
    public static TypstLanguageTag Arabic { get; } = new("ar");

    /// <summary>Hebrew, <c>he</c>: right to left.</summary>
    public static TypstLanguageTag Hebrew { get; } = new("he");

    /// <summary>The tag itself, for example <c>ja</c>. An uninitialised tag has no code.</summary>
    public string Code => _code ?? string.Empty;

    /// <summary>Reads a BCP 47 language tag.</summary>
    /// <param name="code">
    /// The tag: a primary subtag of two or three ASCII letters, then any number of subtags of one to
    /// eight ASCII letters or digits, separated by hyphens. Case is kept as written; Typst does not
    /// care, and neither does equality, which is case-insensitive.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="code"/> is not a well-formed tag.</exception>
    public static TypstLanguageTag Of(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var parts = code.Split('-');

        if (parts.Length == 0 || parts[0].Length is < 2 or > 3 || !AllLetters(parts[0]))
        {
            throw new ArgumentException(
                "a language tag starts with two or three letters, as in 'ja' or 'zh'",
                nameof(code));
        }

        for (var index = 1; index < parts.Length; index++)
        {
            if (parts[index].Length is < 1 or > 8 || !AllLettersOrDigits(parts[index]))
            {
                throw new ArgumentException(
                    "every subtag after the first is one to eight letters or digits, as in 'zh-Hans'",
                    nameof(code));
            }
        }

        return new TypstLanguageTag(code);
    }

    /// <summary>Whether two tags name the same language, ignoring case and an empty tag.</summary>
    /// <param name="other">The tag to compare with.</param>
    public bool Equals(TypstLanguageTag other)
        => string.Equals(Code, other.Code, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is TypstLanguageTag other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Code);

    /// <inheritdoc/>
    public override string ToString() => Code;

    /// <summary>Whether two tags name the same language.</summary>
    public static bool operator ==(TypstLanguageTag left, TypstLanguageTag right) => left.Equals(right);

    /// <summary>Whether two tags name different languages.</summary>
    public static bool operator !=(TypstLanguageTag left, TypstLanguageTag right) => !left.Equals(right);

    private static bool AllLetters(string subtag)
    {
        foreach (var c in subtag)
        {
            if (!char.IsAsciiLetter(c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AllLettersOrDigits(string subtag)
    {
        foreach (var c in subtag)
        {
            if (!char.IsAsciiLetterOrDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
