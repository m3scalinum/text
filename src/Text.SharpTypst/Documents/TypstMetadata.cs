using System;
using System.Collections.Generic;

namespace M3scalinum.Text.SharpTypst.Documents;

/// <summary>Document metadata, configured through <see cref="TypstDocument.Metadata"/>.</summary>
public sealed class TypstMetadata
{
    internal string? TitleValue { get; private set; }

    internal string? AuthorValue { get; private set; }

    internal IReadOnlyList<string> KeywordsValue { get; private set; } = [];

    internal string? LanguageValue { get; private set; }

    internal DateOnly? DateValue { get; private set; }

    /// <summary>Sets the document title.</summary>
    public TypstMetadata Title(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        TitleValue = title;
        return this;
    }

    /// <summary>Sets the author.</summary>
    public TypstMetadata Author(string author)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(author);
        AuthorValue = author;
        return this;
    }

    /// <summary>Sets the keywords.</summary>
    public TypstMetadata Keywords(params string[] keywords)
    {
        ArgumentNullException.ThrowIfNull(keywords);
        KeywordsValue = keywords;
        return this;
    }

    /// <summary>Sets the document language, for example <c>en</c>.</summary>
    public TypstMetadata Language(string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        LanguageValue = language;
        return this;
    }

    /// <summary>Sets the document date. Omitted means the world's clock decides.</summary>
    public TypstMetadata Date(DateOnly date)
    {
        DateValue = date;
        return this;
    }
}