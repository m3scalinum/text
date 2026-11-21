using System.Collections.Generic;
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

        return new OcrResult(
            Text(root),
            ParseLines(root),
            Confidence(root),
            root.GetProperty("width").GetInt32(),
            root.GetProperty("height").GetInt32());
    }

    /// <summary>Parses <c>{"pages": [...]}</c>.</summary>
    internal static IReadOnlyList<OcrPageResult> ParsePages(byte[] json)
    {
        using var document = JsonDocument.Parse(json);

        var pages = new List<OcrPageResult>();
        foreach (var page in document.RootElement.GetProperty("pages").EnumerateArray())
        {
            pages.Add(new OcrPageResult(
                page.GetProperty("pageIndex").GetInt32(),
                Text(page),
                ParseLines(page),
                Confidence(page),
                page.GetProperty("width").GetInt32(),
                page.GetProperty("height").GetInt32(),
                page.GetProperty("dpi").GetInt32()));
        }

        return pages;
    }

    private static string Text(JsonElement element)
        => element.GetProperty("text").GetString() ?? string.Empty;

    private static double Confidence(JsonElement element)
        => element.GetProperty("meanConfidence").GetDouble();

    private static List<OcrLine> ParseLines(JsonElement element)
    {
        var lines = new List<OcrLine>();

        foreach (var line in element.GetProperty("lines").EnumerateArray())
        {
            var words = new List<OcrWord>();
            foreach (var word in line.GetProperty("words").EnumerateArray())
            {
                words.Add(new OcrWord(
                    word.GetProperty("text").GetString() ?? string.Empty,
                    ParseBox(word.GetProperty("bounds")),
                    word.GetProperty("confidence").GetDouble()));
            }

            lines.Add(new OcrLine(
                line.GetProperty("text").GetString() ?? string.Empty,
                ParseBox(line.GetProperty("bounds")),
                line.GetProperty("confidence").GetDouble(),
                words));
        }

        return lines;
    }

    private static OcrBox ParseBox(JsonElement bounds) => new(
        bounds.GetProperty("x").GetInt32(),
        bounds.GetProperty("y").GetInt32(),
        bounds.GetProperty("width").GetInt32(),
        bounds.GetProperty("height").GetInt32());
}
