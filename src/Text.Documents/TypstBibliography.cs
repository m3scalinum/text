using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace M3scalinum.Text.Documents;

/// <summary>How a citation and its bibliography entry are written.</summary>
public enum TypstBibliographyStyle
{
    /// <summary><c>[1]</c> in the flow, entries numbered in order of first appearance.</summary>
    Numeric,

    /// <summary><c>(Dupont, 2024)</c> in the flow, entries sorted by author and year, unnumbered.</summary>
    AuthorDate,
}

/// <summary>Raised when a citation names a key the bibliography does not define.</summary>
/// <remarks>
/// The message names the missing key and every key the bibliography does define, so a typo is
/// repaired from the error alone; nothing is ever written as <c>[?]</c>.
/// </remarks>
public sealed class TypstCitationException : Exception
{
    /// <summary>Creates an exception without a message.</summary>
    public TypstCitationException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    public TypstCitationException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    public TypstCitationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The key the citation asked for.</summary>
    public string? Key { get; init; }

    /// <summary>The keys the bibliography does define, in ordinal order.</summary>
    public IReadOnlyList<string> AvailableKeys { get; init; } = [];
}

/// <summary>A parsed BibTeX file, resolved by citation key.</summary>
/// <remarks>
/// Parsing is done once and the result is immutable: the same instance can be attached to several
/// documents, and each document keeps its own citation order. See
/// <see cref="TypstBibTexParser"/> for the subset read and
/// <see cref="TypstDocument.Citation"/> for how a key is resolved in the flow.
/// </remarks>
public sealed class TypstBibliography
{
    private readonly Dictionary<string, TypstBibTexEntry> _byKey;

    private TypstBibliography(IReadOnlyList<TypstBibTexEntry> entries)
    {
        Entries = entries;
        _byKey = entries.ToDictionary(entry => entry.Key, StringComparer.Ordinal);
        Keys = entries.Select(entry => entry.Key).ToArray();
    }

    /// <summary>Every entry, in file order.</summary>
    public IReadOnlyList<TypstBibTexEntry> Entries { get; }

    /// <summary>Every citation key, in file order.</summary>
    public IReadOnlyList<string> Keys { get; }

    /// <summary>Reads a BibTeX source and indexes its entries by citation key.</summary>
    /// <param name="bibtex">The contents of a <c>.bib</c> file.</param>
    /// <exception cref="TypstBibTexParseException">The source is malformed or uses an unsupported construct.</exception>
    public static TypstBibliography Parse(string bibtex)
        => new(TypstBibTexParser.Parse(bibtex));

    /// <summary>Builds a bibliography from entries another reader produced.</summary>
    /// <param name="entries">The entries; every key must be unique.</param>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is null.</exception>
    /// <exception cref="ArgumentException">Two entries share a key.</exception>
    public static TypstBibliography FromEntries(IEnumerable<TypstBibTexEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var list = entries.ToList();
        var duplicate = list
            .GroupBy(entry => entry.Key, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new ArgumentException($"the citation key '{duplicate.Key}' is defined twice", nameof(entries));
        }

        return new TypstBibliography(list);
    }

    /// <summary>Whether the bibliography defines <paramref name="key"/>.</summary>
    /// <param name="key">The citation key.</param>
    public bool Contains(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _byKey.ContainsKey(key);
    }

    /// <summary>Finds one entry by citation key.</summary>
    /// <param name="key">The citation key.</param>
    /// <exception cref="TypstCitationException">No entry has that key.</exception>
    public TypstBibTexEntry this[string key] => Resolve(key);

    /// <summary>Finds one entry by citation key.</summary>
    /// <param name="key">The citation key.</param>
    /// <exception cref="TypstCitationException">No entry has that key.</exception>
    public TypstBibTexEntry Resolve(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (_byKey.TryGetValue(key, out var entry))
        {
            return entry;
        }

        var available = _byKey.Keys.Order(StringComparer.Ordinal).ToArray();
        throw new TypstCitationException(BuildUnknownKeyMessage(key, available))
        {
            Key = key,
            AvailableKeys = available,
        };
    }

    internal static string BuildUnknownKeyMessage(string key, IReadOnlyList<string> available)
        => available.Count == 0
            ? $"the citation key '{key}' is not in the bibliography, which defines no entry."
            : $"the citation key '{key}' is not in the bibliography. Available keys: {string.Join(", ", available)}.";

    /// <summary>Escapes text for the markup writer, so a field value can be embedded as prose.</summary>
    internal static string Escape(string text)
    {
        var writer = new TypstMarkupWriter();
        writer.WriteText(text);
        return writer.ToMarkup().TrimEnd('\n');
    }

    /// <summary>Writes the citation as it appears in the flow.</summary>
    /// <param name="entry">The cited entry.</param>
    /// <param name="number">Its one-based ordinal, used by <see cref="TypstBibliographyStyle.Numeric"/>.</param>
    /// <param name="style">The document's style.</param>
    /// <param name="prose">Whether the citation is a grammatical part of the sentence.</param>
    internal static string FormatCitation(
        TypstBibTexEntry entry,
        int number,
        TypstBibliographyStyle style,
        bool prose)
    {
        if (style == TypstBibliographyStyle.Numeric)
        {
            return prose ? $"{InlineAuthors(entry)} [{number}]" : $"[{number}]";
        }

        var year = entry.Year ?? "n.d.";
        return prose ? $"{InlineAuthors(entry)} ({year})" : $"({InlineAuthors(entry)}, {year})";
    }

    /// <summary>Writes one bibliography entry, escaped and ready to emit.</summary>
    /// <param name="entry">The entry to write.</param>
    /// <param name="number">Its one-based ordinal, or null when the style does not number.</param>
    /// <param name="style">The document's style.</param>
    internal static string FormatReference(
        TypstBibTexEntry entry,
        int? number,
        TypstBibliographyStyle style)
    {
        var authors = style == TypstBibliographyStyle.Numeric
            ? Join(", ", entry.Authors.Select(NumericAuthor))
            : Join(", ", entry.Authors.Select(AuthorDateAuthor));
        var title = entry.Title is { } titleText ? $"_{Escape(titleText)}_" : null;
        var year = entry.Year ?? "n.d.";
        var source = FormatSource(entry, style);
        var segments = new List<string>();

        if (style == TypstBibliographyStyle.Numeric)
        {
            if (authors.Length > 0)
            {
                segments.Add(authors);
            }

            if (title is not null)
            {
                segments.Add(title);
            }
        }
        else
        {
            segments.Add(authors.Length > 0 ? $"{authors} ({year})" : $"({year})");

            if (title is not null)
            {
                segments.Add(title);
            }
        }

        if (source.Length > 0)
        {
            segments.Add(source);
        }

        var text = string.Join(". ", segments) + ".";

        if (entry.Doi is { } doi)
        {
            text += $" doi: {Escape(doi)}.";
        }

        return style == TypstBibliographyStyle.Numeric && number is { } ordinal
            ? $"[{ordinal}] {text}"
            : text;
    }

    private static string FormatSource(TypstBibTexEntry entry, TypstBibliographyStyle style)
    {
        var volume = VolumeText(entry);
        var head = entry.Venue is { } venue
            ? Join(" ", venue, volume)
            : null;

        return entry.EntryType switch
        {
            "article" => style == TypstBibliographyStyle.Numeric
                ? Join(", ", EscapeOrNull(head), EscapeOrNull(entry.Pages), entry.Year)
                : Join(", ", EscapeOrNull(head), EscapeOrNull(entry.Pages)),
            "inproceedings" => style == TypstBibliographyStyle.Numeric
                ? Join(", ", InVenue(entry, "In "), EscapeOrNull(entry.Pages), entry.Year)
                : Join(", ", InVenue(entry, "In "), EscapeOrNull(entry.Pages)),
            _ => style == TypstBibliographyStyle.Numeric
                ? Join(", ", EscapeOrNull(entry.Publisher), entry.Year)
                : EscapeOrNull(entry.Publisher) ?? string.Empty,
        };
    }

    private static string? InVenue(TypstBibTexEntry entry, string prefix)
        => entry.Venue is { } venue ? prefix + Escape(venue) : null;

    private static string? VolumeText(TypstBibTexEntry entry)
    {
        var text = entry.Volume is { } volume && entry.Number is { } number
            ? $"{volume}({number})"
            : entry.Volume ?? (entry.Number is { } only ? $"({only})" : null);

        return text is null ? null : Escape(text);
    }

    private static string? EscapeOrNull(string? text) => text is null ? null : Escape(text);

    private static string NumericAuthor(TypstBibAuthor author)
        => author.Initials.Length > 0 ? $"{Escape(author.Initials)} {Escape(author.Family)}" : Escape(author.Family);

    private static string AuthorDateAuthor(TypstBibAuthor author)
        => author.Initials.Length > 0 ? $"{Escape(author.Family)}, {Escape(author.Initials)}" : Escape(author.Family);

    private static string InlineAuthors(TypstBibTexEntry entry)
    {
        var authors = entry.Authors;

        return authors.Count switch
        {
            0 => Escape(entry.Key),
            1 => Escape(authors[0].Family),
            2 => $"{Escape(authors[0].Family)} and {Escape(authors[1].Family)}",
            _ => $"{Escape(authors[0].Family)} et al.",
        };
    }

    private static string Join(string separator, params string?[] parts)
        => string.Join(separator, parts.Where(part => !string.IsNullOrEmpty(part)));

    private static string Join(string separator, IEnumerable<string> parts)
        => string.Join(separator, parts.Where(part => !string.IsNullOrEmpty(part)));
}

/// <summary>
/// The citation state one document owns: the bibliography it was given, the style, and the order
/// the citations appeared in. Kept out of <see cref="TypstBibliography"/> so one parse can serve
/// several documents without sharing an order.
/// </summary>
internal sealed class TypstCitationContext
{
    private readonly List<string> _order = [];

    /// <summary>The bibliography this document cites, or null before <c>Bibliography</c> was called.</summary>
    public TypstBibliography? Bibliography { get; set; }

    /// <summary>The style the citations and the final list use.</summary>
    public TypstBibliographyStyle Style { get; set; } = TypstBibliographyStyle.Numeric;

    /// <summary>Every cited key, in order of first appearance.</summary>
    public IReadOnlyList<string> Order => _order;

    /// <summary>Records a citation, keeping the first appearance as its position.</summary>
    public void Record(string key)
    {
        if (!_order.Contains(key, StringComparer.Ordinal))
        {
            _order.Add(key);
        }
    }

    /// <summary>The one-based ordinal <paramref name="key"/> is cited at.</summary>
    public int NumberOf(string key) => _order.IndexOf(key) + 1;

    /// <summary>Resolves a key against the attached bibliography.</summary>
    /// <exception cref="InvalidOperationException">No bibliography is attached.</exception>
    /// <exception cref="TypstCitationException">The bibliography does not define the key.</exception>
    public TypstBibTexEntry Resolve(string key)
        => (Bibliography ?? throw new InvalidOperationException(
                $"the citation '{key}' needs a bibliography: call {nameof(TypstDocument.Bibliography)}(…) first"))
            .Resolve(key);
}
