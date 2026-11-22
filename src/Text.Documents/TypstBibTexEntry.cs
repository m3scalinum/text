using System;
using System.Collections.Generic;
using System.Linq;

namespace M3scalinum.Text.Documents;

/// <summary>One author of a BibTeX entry, split into the parts a citation needs.</summary>
/// <remarks>
/// <see cref="TypstBibTexParser"/> splits a name on its comma: <c>Dupont, Jean</c> gives the family
/// name <c>Dupont</c>; a name without a comma keeps its last word as the family name. A third
/// comma-separated part (the <c>Jr</c> suffix) is not read — see the specification.
/// </remarks>
public sealed class TypstBibAuthor
{
    internal TypstBibAuthor(string family, string given)
    {
        Family = family;
        Given = given;
    }

    /// <summary>The family name, as written.</summary>
    public string Family { get; }

    /// <summary>The given names, as written, or an empty string when the source had none.</summary>
    public string Given { get; }

    /// <summary>
    /// The given names reduced to initials, each followed by a period and separated by a space:
    /// <c>Jean-Pierre</c> becomes <c>J. P.</c>.
    /// </summary>
    public string Initials => TypstBibTexParser.Initials(Given);
}

/// <summary>One entry of a BibTeX file, as the parser read it.</summary>
/// <remarks>
/// The reader covers <c>@article</c>, <c>@book</c> and <c>@inproceedings</c> and formats them
/// differently; any other entry type is read structurally and formatted generically. Fields are
/// kept with their names lowercased and their values whitespace-collapsed; a field the model has no
/// accessor for is still in <see cref="Fields"/>.
/// </remarks>
public sealed class TypstBibTexEntry
{
    private readonly IReadOnlyDictionary<string, string> _fields;
    private IReadOnlyList<TypstBibAuthor>? _authors;

    internal TypstBibTexEntry(
        string key,
        string entryType,
        IReadOnlyDictionary<string, string> fields)
    {
        Key = key;
        EntryType = entryType;
        _fields = fields;
    }

    /// <summary>The citation key, exactly as the entry declared it.</summary>
    public string Key { get; }

    /// <summary>The entry type, lowercased: <c>article</c>, <c>book</c>, <c>inproceedings</c>, …</summary>
    public string EntryType { get; }

    /// <summary>Every field the entry declared, keyed by its lowercased name.</summary>
    public IReadOnlyDictionary<string, string> Fields => _fields;

    /// <summary>The <c>title</c> field, or null.</summary>
    public string? Title => Field("title");

    /// <summary>The <c>author</c> field split on <c>and</c>, in the entry's order. Empty when absent.</summary>
    public IReadOnlyList<TypstBibAuthor> Authors
        => _authors ??= TypstBibTexParser.SplitAuthors(Field("author"));

    /// <summary>The <c>year</c> field, or null.</summary>
    public string? Year => Field("year");

    /// <summary>The <c>journal</c> field, then <c>booktitle</c>, whichever the entry declared first.</summary>
    public string? Venue => Field("journal") ?? Field("booktitle");

    /// <summary>The <c>publisher</c> field, or null.</summary>
    public string? Publisher => Field("publisher");

    /// <summary>The <c>volume</c> field, or null.</summary>
    public string? Volume => Field("volume");

    /// <summary>The <c>number</c> field, or null.</summary>
    public string? Number => Field("number");

    /// <summary>The <c>pages</c> field with its <c>--</c> range marker written as an en dash, or null.</summary>
    public string? Pages => Field("pages")?.Replace("--", "\u2013", StringComparison.Ordinal);

    /// <summary>The <c>doi</c> field, or null.</summary>
    public string? Doi => Field("doi");

    /// <summary>One field value by its lowercased name, or null when the entry has no such field.</summary>
    /// <param name="name">The field name, matched case-insensitively.</param>
    public string? Field(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _fields.TryGetValue(name.ToLowerInvariant(), out var value) ? value : null;
    }
}
