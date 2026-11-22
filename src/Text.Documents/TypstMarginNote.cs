using System;

namespace M3scalinum.Text.Documents;

/// <summary>
/// A margin note: a marker in the flow and a small text placed in the outer margin, on the line of
/// its marker.
/// </summary>
/// <remarks>
/// <para>
/// The note is emitted as an inline <c>#box(place(…))</c> at the marker, so the vertical position is
/// the marker's line, not the top or bottom of the page: Typst's floating placement
/// (<c>place(float: true)</c>) only stacks at the top or the bottom of a page and would lose the
/// alignment, and its parent-scoped float form accepts no other alignment.
/// </para>
/// <para>
/// The horizontal geometry is the caller's: <see cref="Offset"/> is the distance from the marker to
/// the note's left edge, and <see cref="Width"/> the note's width. The model knows neither the page
/// text width nor the margin width in Typst units when the markup is written (a margin may be a
/// percentage, and a two-column page splits the text area again), so it does not guess them. A note
/// in the margin of a single-column A4 page with 2 cm margins starts about
/// <c>15.5cm - marker x</c> to the right of its marker; pass <see cref="TypstLength"/> values that
/// fit the caller's page.
/// </para>
/// <para>
/// A placed note takes no space in the flow, so it cannot be broken across pages and two notes whose
/// markers are closer than the note is tall overlap. Keep notes short, or keep their markers apart.
/// </para>
/// </remarks>
public sealed class TypstMarginNote
{
    /// <summary>Creates a margin note.</summary>
    /// <param name="marker">The text printed in the flow at the anchor, for example <c>1</c> or <c>*</c>.</param>
    /// <param name="content">The note's text, placed in the margin.</param>
    /// <param name="offset">The distance from the marker to the note's left edge.</param>
    /// <param name="width">The width the note wraps at; null lets the text run on one line.</param>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    public TypstMarginNote(string marker, TypstContent content, TypstLength offset, TypstLength? width = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(marker);
        ArgumentNullException.ThrowIfNull(content);
        Marker = marker;
        Content = content;
        Offset = offset;
        Width = width;
    }

    /// <summary>The text printed in the flow at the anchor.</summary>
    public string Marker { get; }

    /// <summary>The note's text.</summary>
    public TypstContent Content { get; }

    /// <summary>The distance from the marker to the note's left edge.</summary>
    public TypstLength Offset { get; }

    /// <summary>The width the note wraps at, or <see langword="null"/> for a single line.</summary>
    public TypstLength? Width { get; }
}
