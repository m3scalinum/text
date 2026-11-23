using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Ocr;

/// <summary>
/// One recognised word, placed on the page in points: what a text layer needs to draw the word where
/// it was read.
/// </summary>
/// <remarks>
/// The coordinates are the page's own — points, origin at the bottom-left corner of the page's
/// <c>/MediaBox</c> — and not the pixels the recogniser worked in. The field names match the ones
/// <c>PdfTextLayerWord</c> of <c>M3scalinum.Text.Pdf</c> takes, so a caller projects one onto the
/// other without arithmetic; this package does not reference that one, and says so where it matters.
/// <see cref="PageIndex"/> is zero-based, as everywhere in the suite.
/// </remarks>
/// <param name="PageIndex">The zero-based page the word was read from.</param>
/// <param name="Text">The recognised characters, without surrounding whitespace.</param>
/// <param name="Left">The left edge of the word, in points.</param>
/// <param name="Bottom">The bottom edge of the word, in points.</param>
/// <param name="Right">The right edge of the word, in points.</param>
/// <param name="Top">The top edge of the word, in points.</param>
public readonly record struct OcrTextLayerWord(
    int PageIndex,
    string Text,
    double Left,
    double Bottom,
    double Right,
    double Top);

/// <summary>
/// Turns recognised pages into positioned words: the bridge from what an engine read to what a
/// searchable text layer needs.
/// </summary>
/// <remarks>
/// <para>
/// The recogniser reports boxes in the pixels of the rasterised page, with the origin at the top-left
/// corner; a PDF places text in points, with the origin at the bottom-left corner of the page. The
/// conversion is the page's raster width divided by its width in points, which is the resolution it
/// was rendered at, and a flip of the vertical axis.
/// </para>
/// <para>
/// A page's raster is a whole number of pixels at the resolution asked for, so the page in points is
/// the pixel size scaled by 72/DPI: a 300 DPI A4 page is 2480×3508 pixels and 595×842 points. The
/// words that come out therefore sit in the page's user space as the caller's PDF has it, and a
/// caller that rendered at a different resolution than the document's own gets the same points.
/// </para>
/// <para>
/// What is left out, deliberately: a word the engine flagged as rejected below the confidence
/// threshold, because a text layer states what the page says and a word the engine doubts is not a
/// statement; and a word whose box has no area, because a text layer cannot place one and the box is
/// what told us nothing. The reading order is the engine's: lines in the order it returned them,
/// words in the order it returned them within a line.
/// </para>
/// </remarks>
public static class OcrTextLayer
{
    /// <summary>The points in an inch, which is what turns a raster resolution into page points.</summary>
    private const double PointsPerInch = 72.0;

    /// <summary>The words of one page, placed in points, in the engine's reading order.</summary>
    /// <param name="page">The outcome of reading one page of a PDF.</param>
    /// <returns>
    /// One entry per accepted word, or an empty list for a page the engine refused or a page that
    /// holds no word.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="page"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<OcrTextLayerWord> Words(OcrPageOutcome page)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (page is not OcrPageResult result || result.Dpi <= 0)
        {
            return [];
        }

        var scale = PointsPerInch / result.Dpi;
        var words = new List<OcrTextLayerWord>();

        foreach (var line in result.Lines)
        {
            foreach (var word in line.Words)
            {
                if (word.Rejected || word.Text.Length == 0 || word.Bounds.Width <= 0 || word.Bounds.Height <= 0)
                {
                    continue;
                }

                var right = (word.Bounds.X + word.Bounds.Width) * scale;
                var top = (result.Height - word.Bounds.Y) * scale;

                words.Add(new OcrTextLayerWord(
                    result.PageIndex,
                    word.Text,
                    word.Bounds.X * scale,
                    top - (word.Bounds.Height * scale),
                    right,
                    top));
            }
        }

        return words;
    }

    /// <summary>The words of several pages, placed in points, page by page in the order given.</summary>
    /// <param name="pages">The outcomes of reading the pages of a PDF.</param>
    /// <returns>One entry per accepted word, with the page each came from.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pages"/> or one of its elements is <see langword="null"/>.</exception>
    public static IReadOnlyList<OcrTextLayerWord> Words(IEnumerable<OcrPageOutcome> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        var words = new List<OcrTextLayerWord>();

        foreach (var page in pages)
        {
            words.AddRange(Words(page));
        }

        return words;
    }

    /// <summary>The size of the page the boxes were read from, in points.</summary>
    /// <param name="page">The outcome of reading one page of a PDF.</param>
    /// <returns>The width and the height of the page in points.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="page"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The page was read at a resolution of zero, which no render does.</exception>
    /// <remarks>
    /// A caller that resizes or crops a page checks its boxes against this, because the layer it
    /// builds is placed in the page's own coordinates and a box outside the page is a word the
    /// recogniser placed outside it.
    /// </remarks>
    public static (double Width, double Height) PageSize(OcrPageOutcome page)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (page.Dpi <= 0)
        {
            throw new ArgumentException("the page names no resolution, so its size in points is undefined", nameof(page));
        }

        var scale = PointsPerInch / page.Dpi;

        return (page.Width * scale, page.Height * scale);
    }
}
