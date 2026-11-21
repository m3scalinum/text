using System;
using System.Collections.Generic;
using System.Linq;

namespace M3scalinum.Text.Ocr.Internal;

/// <summary>
/// The arithmetic the result documents need and the native side does not do: a box
/// clipped to the page it was found on, the union of a line's word boxes, and the
/// reading order of the words inside a line.
/// </summary>
/// <remarks>
/// The native library reports what the detector saw. A detector can report a box that
/// runs off the page it was given, a line box of no area where it found words, and
/// words in the order the recogniser emitted them rather than the order a reader reads
/// them. The public surface promises boxes in image pixels and words in reading order,
/// so the promise is kept here, once, where it can be tested without a model.
/// </remarks>
internal static class BoxMath
{
    /// <summary>A box of no area at the origin.</summary>
    internal static OcrBox Empty { get; } = new(0, 0, 0, 0);

    /// <summary>
    /// Clips <paramref name="box"/> to a page of the given size. A box that lies
    /// entirely outside the page becomes empty rather than negative, and a page of no
    /// size clips everything to <see cref="Empty"/>.
    /// </summary>
    internal static OcrBox Clip(OcrBox box, int pageWidth, int pageHeight)
    {
        var width = Math.Max(0, pageWidth);
        var height = Math.Max(0, pageHeight);

        var left = Math.Clamp(box.X, 0, width);
        var top = Math.Clamp(box.Y, 0, height);
        var right = Math.Clamp((long)box.X + box.Width, 0, width);
        var bottom = Math.Clamp((long)box.Y + box.Height, 0, height);

        return new OcrBox(left, top, (int)Math.Max(0, right - left), (int)Math.Max(0, bottom - top));
    }

    /// <summary>
    /// The smallest box containing every box in <paramref name="boxes"/>, or
    /// <see cref="Empty"/> when there are none.
    /// </summary>
    internal static OcrBox Union(IReadOnlyList<OcrBox> boxes)
    {
        if (boxes.Count == 0)
        {
            return Empty;
        }

        var left = int.MaxValue;
        var top = int.MaxValue;
        var right = (long)int.MinValue;
        var bottom = (long)int.MinValue;

        foreach (var box in boxes)
        {
            left = Math.Min(left, box.X);
            top = Math.Min(top, box.Y);
            right = Math.Max(right, (long)box.X + box.Width);
            bottom = Math.Max(bottom, (long)box.Y + box.Height);
        }

        return new OcrBox(left, top, (int)Math.Max(0, right - left), (int)Math.Max(0, bottom - top));
    }

    /// <summary>
    /// The words of a line in reading order: left to right, and top to bottom between
    /// two words that start in the same column. The sort is stable, so words the
    /// detector already placed in order keep their relative order.
    /// </summary>
    internal static IReadOnlyList<OcrWord> InReadingOrder(IReadOnlyList<OcrWord> words)
        => words.OrderBy(word => word.Bounds.X).ThenBy(word => word.Bounds.Y).ToList();
}
