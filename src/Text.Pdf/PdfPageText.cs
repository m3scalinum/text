using System.Collections.Generic;

namespace M3scalinum.Text.Pdf;

/// <summary>The text of one page, and what the reader could not map.</summary>
/// <remarks>
/// The text is the glyphs the content stream shows, in the order it shows them. Words are separated
/// by a space where the layout leaves a horizontal gap; lines are separated by a newline; a page with
/// text ends with one newline, as the compiler's page text does. Nothing else is inferred: a table is
/// a sequence of cells in the order they were drawn, not a grid, and a two-column page reads across
/// both columns in stream order rather than down the first one.
/// <para>
/// The page also says what it could not do faithfully. A font with no <c>/ToUnicode</c> CMap has its
/// codes read through the encoding's differences or, failing that, as raw code values, and is named
/// in <see cref="FallbackFonts"/>. A content stream whose filter this reader does not implement is
/// counted in <see cref="SkippedContentStreams"/>. Both are per page, so a caller indexing a
/// document can mark the pages whose text it cannot trust instead of silently storing mojibake.
/// </para>
/// </remarks>
public sealed class PdfPageText
{
    internal PdfPageText(
        int pageNumber,
        string text,
        IReadOnlyList<string> fallbackFonts,
        int skippedContentStreams)
    {
        PageNumber = pageNumber;
        Text = text;
        FallbackFonts = fallbackFonts;
        SkippedContentStreams = skippedContentStreams;
    }

    /// <summary>The page's number in the document, one-based, as a reader counts pages.</summary>
    public int PageNumber { get; }

    /// <summary>The text the page draws, or an empty string for a page that draws none.</summary>
    public string Text { get; }

    /// <summary>
    /// The resource names of the fonts whose codes were read without a <c>/ToUnicode</c> CMap, in the
    /// order the page first used them. Empty when every glyph was mapped through its font's CMap.
    /// </summary>
    public IReadOnlyList<string> FallbackFonts { get; }

    /// <summary>
    /// Whether at least one glyph on the page was decoded without a <c>/ToUnicode</c> map, so the
    /// characters may not be the ones the producer meant.
    /// </summary>
    public bool HasFallback => FallbackFonts.Count > 0;

    /// <summary>
    /// How many of the page's content streams used a filter this reader does not implement, such as
    /// LZW, and were therefore not read. Zero on a page whose text is complete.
    /// </summary>
    public int SkippedContentStreams { get; }
}
