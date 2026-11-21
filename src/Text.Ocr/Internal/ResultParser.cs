using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace M3scalinum.Text.Ocr.Internal;

/// <summary>
/// Parses the result documents the native library returns.
/// </summary>
/// <remarks>
/// This is the only place that knows the JSON shape, and it is separated from the
/// call so the shape can be tested without a native library and without models:
/// a parser test feeds the exact document <c>native/text-ocr</c> produces.
/// </remarks>
internal static class ResultParser
{
    /// <summary>Parses <c>{"width", "height", "text", "meanConfidence", "lines"}</c>.</summary>
    internal static OcrResult ParseImage(byte[] json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var width = root.GetProperty("width").GetInt32();
        var height = root.GetProperty("height").GetInt32();

        return new OcrResult(
            Text(root),
            ParseLines(root, width, height),
            Confidence(root),
            width,
            height);
    }

    /// <summary>Parses <c>{"pages": [...]}</c>.</summary>
    internal static IReadOnlyList<OcrPageResult> ParsePages(byte[] json)
    {
        using var document = JsonDocument.Parse(json);

        var pages = new List<OcrPageResult>();
        foreach (var page in document.RootElement.GetProperty("pages").EnumerateArray())
        {
            var width = page.GetProperty("width").GetInt32();
            var height = page.GetProperty("height").GetInt32();

            pages.Add(new OcrPageResult(
                page.GetProperty("pageIndex").GetInt32(),
                Text(page),
                ParseLines(page, width, height),
                Confidence(page),
                width,
                height,
                page.GetProperty("dpi").GetInt32()));
        }

        return pages;
    }

    private static string Text(JsonElement element)
        => element.GetProperty("text").GetString() ?? string.Empty;

    private static double Confidence(JsonElement element)
        => element.GetProperty("meanConfidence").GetDouble();

    /// <summary>
    /// Reads the lines of one image or page and normalises them against the page: every
    /// box is clipped to it, the words of a line are put in reading order, and a line
    /// box the detector reported with no area is widened to the boxes of the words it
    /// found rather than reported as a zero-size line that holds text.
    /// </summary>
    private static List<OcrLine> ParseLines(JsonElement element, int pageWidth, int pageHeight)
    {
        var lines = new List<OcrLine>();

        foreach (var line in element.GetProperty("lines").EnumerateArray())
        {
            var words = new List<OcrWord>();
            foreach (var word in line.GetProperty("words").EnumerateArray())
            {
                words.Add(new OcrWord(
                    word.GetProperty("text").GetString() ?? string.Empty,
                    BoxMath.Clip(ParseBox(word.GetProperty("bounds")), pageWidth, pageHeight),
                    word.GetProperty("confidence").GetDouble()));
            }

            var ordered = BoxMath.InReadingOrder(words);
            var bounds = BoxMath.Clip(ParseBox(line.GetProperty("bounds")), pageWidth, pageHeight);

            if (bounds.Width == 0 || bounds.Height == 0)
            {
                var union = BoxMath.Union(ordered.Select(word => word.Bounds).ToList());
                if (union.Width > 0 && union.Height > 0)
                {
                    bounds = union;
                }
            }

            lines.Add(new OcrLine(
                line.GetProperty("text").GetString() ?? string.Empty,
                bounds,
                line.GetProperty("confidence").GetDouble(),
                ordered));
        }

        return lines;
    }

    private static OcrBox ParseBox(JsonElement bounds) => new(
        bounds.GetProperty("x").GetInt32(),
        bounds.GetProperty("y").GetInt32(),
        bounds.GetProperty("width").GetInt32(),
        bounds.GetProperty("height").GetInt32());
}
