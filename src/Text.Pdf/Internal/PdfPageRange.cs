using System;
using System.Collections.Generic;
using System.Globalization;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// Parses a page selection the way the compiler's <c>PageRanges</c> export option does: one-indexed,
/// comma-separated, <c>1-3,5</c> for a closed range and <c>2-</c> for an open one.
/// </summary>
/// <remarks>
/// A selection is a set, not a sequence: <c>3,1</c> selects pages one and three, and the output of
/// an extraction keeps the document's own page order. Repeating a page in the selection therefore
/// does not repeat it in the result.
/// </remarks>
internal static class PdfPageRange
{
    /// <summary>The zero-based indexes <paramref name="ranges"/> selects, ascending.</summary>
    /// <param name="ranges">The selection, for example <c>1-3,5</c> or <c>2-</c>.</param>
    /// <param name="pageCount">How many pages the document has.</param>
    /// <exception cref="ArgumentException">The text is not a page selection.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The selection names a page outside the document.</exception>
    public static IReadOnlyList<int> Parse(string ranges, int pageCount)
    {
        if (string.IsNullOrWhiteSpace(ranges))
        {
            throw new ArgumentException("the page selection is empty", nameof(ranges));
        }

        var selected = new bool[pageCount];

        foreach (var part in ranges.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var (first, last) = Bounds(part, ranges, pageCount);

            for (var page = first; page <= last; page++)
            {
                selected[page - 1] = true;
            }
        }

        var indexes = new List<int>();
        for (var index = 0; index < pageCount; index++)
        {
            if (selected[index])
            {
                indexes.Add(index);
            }
        }

        if (indexes.Count == 0)
        {
            throw new ArgumentException($"the page selection '{ranges}' selects no page", nameof(ranges));
        }

        return indexes;
    }

    private static (int First, int Last) Bounds(string part, string ranges, int pageCount)
    {
        var dash = part.IndexOf('-', StringComparison.Ordinal);

        if (dash < 0)
        {
            var page = Number(part, ranges, pageCount);

            return (page, page);
        }

        if (part.IndexOf('-', dash + 1) >= 0)
        {
            throw new ArgumentException($"'{part}' is not a page range in '{ranges}'", nameof(ranges));
        }

        var start = part[..dash].Trim();
        var end = part[(dash + 1)..].Trim();

        var first = start.Length == 0 ? 1 : Number(start, ranges, pageCount);
        var last = end.Length == 0 ? pageCount : Number(end, ranges, pageCount);

        if (first > last)
        {
            throw new ArgumentException($"the page range '{part}' runs backwards", nameof(ranges));
        }

        return (first, last);
    }

    private static int Number(string text, string ranges, int pageCount)
    {
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var page) || page < 1)
        {
            throw new ArgumentException($"'{text}' is not a page number in '{ranges}'", nameof(ranges));
        }

        if (page > pageCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ranges),
                page,
                $"the selection names page {page} but the document has {pageCount} page(s)");
        }

        return page;
    }
}
