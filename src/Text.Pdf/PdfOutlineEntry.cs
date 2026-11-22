using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// One bookmark of a PDF outline: its title, the page it opens, the entries under it, and the state
/// the file gives it.
/// </summary>
/// <remarks>
/// The same type is what <see cref="PdfOutline.Read"/> returns and what
/// <see cref="PdfOutline.Write"/> takes, so a plan read from one document can be written onto
/// another. <see cref="Title"/>, <see cref="PageNumber"/>, <see cref="Children"/>,
/// <see cref="IsOpen"/>, <see cref="IsBold"/> and <see cref="IsItalic"/> travel through a write;
/// <see cref="Destination"/> only describes what was read.
/// </remarks>
public sealed class PdfOutlineEntry
{
    private readonly List<PdfOutlineEntry> _children = [];

    /// <summary>Creates a bookmark that opens <paramref name="pageNumber"/>.</summary>
    /// <param name="title">The text the viewer shows; may be empty, as the format allows it.</param>
    /// <param name="pageNumber">
    /// The one-based page the bookmark opens, or <see langword="null"/> when the entry names no page
    /// or a page that could not be resolved — a write refuses such an entry.
    /// </param>
    public PdfOutlineEntry(string title, int? pageNumber = null)
    {
        ArgumentNullException.ThrowIfNull(title);

        Title = title;
        PageNumber = pageNumber;
    }

    /// <summary>The title the viewer shows.</summary>
    public string Title { get; }

    /// <summary>The one-based page the entry opens, or <see langword="null"/> when it could not be resolved.</summary>
    public int? PageNumber { get; }

    /// <summary>
    /// The destination the file gave the entry: the fit type, its coordinates, and the name when the
    /// entry pointed at a named destination. <see langword="null"/> when the entry names none.
    /// </summary>
    public PdfOutlineDestination? Destination { get; init; }

    /// <summary>The entries under this one, in document order.</summary>
    public IReadOnlyList<PdfOutlineEntry> Children => _children;

    /// <summary>
    /// Whether the viewer shows the children: true for an entry whose <c>/Count</c> is zero or
    /// positive, false for a negative one and for an entry with no children.
    /// </summary>
    public bool IsOpen { get; init; }

    /// <summary>Whether the entry's <c>/F</c> asks for a bold title.</summary>
    public bool IsBold { get; init; }

    /// <summary>Whether the entry's <c>/F</c> asks for an italic title.</summary>
    public bool IsItalic { get; init; }

    /// <summary>Adds <paramref name="child"/> after the entries already added, and returns this entry.</summary>
    /// <param name="child">The sub-bookmark.</param>
    /// <returns>This entry, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="child"/> is <see langword="null"/>.</exception>
    public PdfOutlineEntry Add(PdfOutlineEntry child)
    {
        ArgumentNullException.ThrowIfNull(child);

        _children.Add(child);

        return this;
    }

    /// <summary>Appends the entries a read found under this one.</summary>
    /// <param name="children">The sub-bookmarks, in document order.</param>
    internal void AddRange(IEnumerable<PdfOutlineEntry> children)
    {
        foreach (var child in children)
        {
            _children.Add(child);
        }
    }
}

/// <summary>
/// Where a bookmark goes, as the file states it: the fit type, the position on the page, and the
/// name of the named destination the entry pointed at.
/// </summary>
/// <param name="Name">The named destination's name, or <see langword="null"/> for a direct destination.</param>
/// <param name="Fit">
/// The fit type: <c>XYZ</c>, <c>Fit</c>, <c>FitH</c>, <c>FitBH</c>, <c>FitV</c>, <c>FitBV</c>,
/// <c>FitR</c> or <c>FitB</c>. Empty when the array names none.
/// </param>
/// <param name="Left">The horizontal position in points, when the fit type carries one.</param>
/// <param name="Top">The vertical position in points, when the fit type carries one.</param>
/// <param name="Zoom">The zoom factor, when the fit type is <c>XYZ</c> and the file gives one.</param>
public sealed record PdfOutlineDestination(string? Name, string Fit, double? Left, double? Top, double? Zoom);
