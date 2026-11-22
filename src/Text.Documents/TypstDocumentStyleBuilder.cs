using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace M3scalinum.Text.Documents;

/// <summary>
/// The fluent form of a <see cref="TypstDocumentStyle"/>. Every method returns the builder, so a style
/// reads as one chain; <see cref="Build"/> freezes it.
/// </summary>
/// <remarks>
/// A builder holds no state in the style: <see cref="Build"/> renders the <c>#set</c> and
/// <c>#show</c> lines once and hands them to the style, so the style a document carries cannot be
/// changed afterwards by reusing the builder.
/// </remarks>
public sealed class TypstDocumentStyleBuilder
{
    private const int HighestHeadingLevel = 3;

    private readonly TypstBodyStyle _body = new();
    private readonly TypstHeadingStyle?[] _headings = new TypstHeadingStyle?[HighestHeadingLevel];
    private TypstPageSetup? _page;
    private TypstLanguage? _language;
    private TypstLanguageTag? _languageTag;
    private bool _headingNumbering;
    private string? _header;
    private string? _footer;

    /// <summary>Configures the page: format, margins, orientation and page numbering.</summary>
    public TypstDocumentStyleBuilder Page(Action<TypstPageSetup> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _page ??= new TypstPageSetup();
        configure(_page);
        return this;
    }

    /// <summary>Configures the body text: family, size, line height and justification.</summary>
    public TypstDocumentStyleBuilder Body(Action<TypstBodyStyle> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_body);
        return this;
    }

    /// <summary>Configures one heading level.</summary>
    /// <param name="level">The heading level, 1 to 3. Deeper levels are not part of a style.</param>
    /// <param name="configure">Configures the level's size, weight and spacing.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="level"/> is outside 1 to 3.</exception>
    public TypstDocumentStyleBuilder Heading(int level, Action<TypstHeadingStyle> configure)
    {
        if (level is < 1 or > HighestHeadingLevel)
        {
            throw new ArgumentOutOfRangeException(
                nameof(level),
                level,
                "a document style covers heading levels 1 to 3");
        }

        ArgumentNullException.ThrowIfNull(configure);
        _headings[level - 1] ??= new TypstHeadingStyle();
        configure(_headings[level - 1]!);
        return this;
    }

    /// <summary>Numbers the headings <c>1</c>, <c>1.1</c>, <c>1.1.1</c>, or turns numbering back off.</summary>
    public TypstDocumentStyleBuilder HeadingNumbering(bool enabled = true)
    {
        _headingNumbering = enabled;
        return this;
    }

    /// <summary>Sets the language the document is written in.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="language"/> has no code: the style refuses to guess.
    /// </exception>
    public TypstDocumentStyleBuilder Language(TypstLanguage language)
    {
        _ = LanguageCode(language);
        _language = language;
        _languageTag = null;
        return this;
    }

    /// <summary>
    /// Sets the language the document is written in, with any BCP 47 tag — <c>ja</c>, <c>zh</c>,
    /// <c>ar</c> — so that Typst hyphenates and breaks lines by that language's rules.
    /// </summary>
    /// <param name="language">The tag, for example <see cref="TypstLanguageTag.Japanese"/>.</param>
    /// <remarks>
    /// The tag replaces the two-valued <see cref="Language(TypstLanguage)"/> form: a style carries
    /// one language, and the last call wins. A style that sets neither writes no <c>lang</c> at all.
    /// </remarks>
    public TypstDocumentStyleBuilder Language(TypstLanguageTag language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language.Code);
        _languageTag = language;
        _language = null;
        return this;
    }

    /// <summary>
    /// Sets the running header. <c>{page}</c> is replaced by the page number, <c>{pages}</c> by the page
    /// count and <c>{title}</c> by the document title; every other character is literal text.
    /// </summary>
    public TypstDocumentStyleBuilder Header(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        _header = text;
        return this;
    }

    /// <summary>
    /// Sets the running footer, with the same <c>{page}</c>, <c>{pages}</c> and <c>{title}</c>
    /// placeholders as <see cref="Header"/>.
    /// </summary>
    public TypstDocumentStyleBuilder Footer(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        _footer = text;
        return this;
    }

    /// <summary>Freezes the configuration into a style that no longer changes.</summary>
    public TypstDocumentStyle Build()
    {
        var rules = new List<string>();

        PageRule(rules);
        TextRule(rules);
        ParagraphRule(rules);
        HeadingRules(rules);

        return TypstDocumentStyle.FromRules(rules);
    }

    private void PageRule(List<string> rules)
    {
        var arguments = new List<string>();

        if (_page is not null)
        {
            arguments.AddRange(TypstDocument.PageArguments(
                _page,
                includeHeader: _header is null,
                includeFooter: _footer is null));
        }

        if (_header is not null)
        {
            arguments.Add("header: " + RunningText(_header));
        }

        if (_footer is not null)
        {
            arguments.Add("footer: " + RunningText(_footer));
        }

        if (arguments.Count > 0)
        {
            rules.Add("#set page(" + string.Join(", ", arguments) + ")");
        }
    }

    private void TextRule(List<string> rules)
    {
        var arguments = new List<string>();

        if (_body.FontValue is { } font)
        {
            arguments.Add("font: " + LinkNode.Quote(font));
        }

        if (_body.SizeValue is { } size)
        {
            arguments.Add("size: " + size);
        }

        if (_languageTag is { } tag)
        {
            arguments.Add("lang: " + LinkNode.Quote(tag.Code));
        }
        else if (_language is { } language)
        {
            arguments.Add("lang: " + LinkNode.Quote(LanguageCode(language)));
        }

        if (arguments.Count > 0)
        {
            rules.Add("#set text(" + string.Join(", ", arguments) + ")");
        }
    }

    private void ParagraphRule(List<string> rules)
    {
        var arguments = new List<string>();

        if (_body.LeadingValue is { } leading)
        {
            arguments.Add("leading: " + Number(leading) + "em");
        }

        if (_body.Justified)
        {
            arguments.Add("justify: true");
        }

        if (arguments.Count > 0)
        {
            rules.Add("#set par(" + string.Join(", ", arguments) + ")");
        }
    }

    private void HeadingRules(List<string> rules)
    {
        if (_headingNumbering)
        {
            rules.Add("#set heading(numbering: \"1.1.1\")");
        }

        for (var level = 1; level <= HighestHeadingLevel; level++)
        {
            if (_headings[level - 1] is { } heading)
            {
                rules.Add(HeadingRule(level, heading));
            }
        }
    }

    private static string HeadingRule(int level, TypstHeadingStyle heading)
    {
        var statements = new List<string>();
        var text = new List<string>();

        if (heading.SizeValue is { } size)
        {
            text.Add("size: " + size);
        }

        if (heading.WeightValue is { } weight)
        {
            text.Add("weight: " + LinkNode.Quote(weight));
        }

        if (text.Count > 0)
        {
            statements.Add("set text(" + string.Join(", ", text) + ")");
        }

        var block = new List<string>();

        if (heading.AboveValue is { } above)
        {
            block.Add("above: " + above);
        }

        if (heading.BelowValue is { } below)
        {
            block.Add("below: " + below);
        }

        if (block.Count > 0)
        {
            statements.Add("set block(" + string.Join(", ", block) + ")");
        }

        statements.Add("it");

        return $"#show heading.where(level: {level}): it => {{ "
            + string.Join("; ", statements)
            + " }";
    }

    /// <summary>
    /// Turns a header or footer text into a context block, so the page counter is available where the
    /// running text is laid out. The section model writes the same expression.
    /// </summary>
    private static string RunningText(string text) => TypstRunningText.Markup(text);

    private static string LanguageCode(TypstLanguage language) => language switch
    {
        TypstLanguage.English => "en",
        TypstLanguage.French => "fr",
        _ => throw new ArgumentOutOfRangeException(
            nameof(language),
            language,
            "no language code for this language: the style covers English and French"),
    };

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
