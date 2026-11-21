using System;

namespace M3scalinum.Text.Epub;

/// <summary>
/// The Dublin Core metadata an EPUB package must carry: a unique identifier, a title and a language,
/// plus the optional creator, publisher and dates.
/// </summary>
/// <remarks>
/// <see cref="Identifier"/> and <see cref="Title"/> are what an e-reader's library lists and what
/// <c>unique-identifier</c> in the package document points at, so both are required. A
/// <c>urn:uuid:</c> value is the convention when the book has no ISBN, but any non-empty string
/// works — the reader only stores it.
/// </remarks>
public sealed record EpubMetadata
{
    /// <summary>The unique identifier of this publication, for example <c>urn:isbn:978...</c>.</summary>
    public required string Identifier { get; init; }

    /// <summary>The title a reader shows in its library.</summary>
    public required string Title { get; init; }

    /// <summary>The BCP 47 language tag of the content, <c>en</c> by default.</summary>
    public string Language { get; init; } = "en";

    /// <summary>The author, when the book has one.</summary>
    public string? Author { get; init; }

    /// <summary>The publisher, when the book has one.</summary>
    public string? Publisher { get; init; }

    /// <summary>The publication date, when the book has one.</summary>
    public DateOnly? Date { get; init; }

    /// <summary>
    /// The modification timestamp the package must declare (<c>dcterms:modified</c>). It also stamps
    /// every archive entry, so two writes of the same book produce the same bytes; when it is unset
    /// the date is used, and failing that the current time.
    /// </summary>
    public DateTimeOffset? Modified { get; init; }
}
