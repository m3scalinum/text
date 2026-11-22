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
    /// <summary>
    /// Parses <c>{"width", "height", "status", "text", "meanConfidence", "lines"}</c>,
    /// plus <c>refusal</c> when the native side refused the page and <c>preprocess</c>
    /// when it preprocessed it.
    /// </summary>
    internal static OcrRecognition ParseImage(byte[] json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var width = root.GetProperty("width").GetInt32();
        var height = root.GetProperty("height").GetInt32();
        var meanConfidence = Confidence(root);

        if (IsUnreadable(root))
        {
            return new OcrUnreadable(width, height, meanConfidence, ParseRefusal(root));
        }

        return new OcrResult(
            Text(root),
            ParseLines(root, width, height),
            meanConfidence,
            width,
            height,
            ParsePreprocess(root));
    }

    /// <summary>Parses <c>{"pages": [...]}</c>.</summary>
    internal static IReadOnlyList<OcrPageOutcome> ParsePages(byte[] json)
    {
        using var document = JsonDocument.Parse(json);

        var pages = new List<OcrPageOutcome>();
        foreach (var page in document.RootElement.GetProperty("pages").EnumerateArray())
        {
            var pageIndex = page.GetProperty("pageIndex").GetInt32();
            var width = page.GetProperty("width").GetInt32();
            var height = page.GetProperty("height").GetInt32();
            var dpi = page.GetProperty("dpi").GetInt32();
            var meanConfidence = Confidence(page);

            if (IsUnreadable(page))
            {
                pages.Add(new OcrPageUnreadable(
                    pageIndex, width, height, dpi, meanConfidence, ParseRefusal(page)));
                continue;
            }

            pages.Add(new OcrPageResult(
                pageIndex,
                Text(page),
                ParseLines(page, width, height),
                meanConfidence,
                width,
                height,
                dpi,
                ParsePreprocess(page)));
        }

        return pages;
    }

    /// <summary>
    /// Whether the native side refused the page.
    /// </summary>
    /// <remarks>
    /// An absent <c>status</c> is a reading: a document written before the refusal
    /// existed can only ever describe one, and reading it as a refusal would turn an
    /// older native library into a page of nothing.
    /// </remarks>
    private static bool IsUnreadable(JsonElement element)
        => element.TryGetProperty("status", out var status)
            && status.ValueKind == JsonValueKind.String
            && status.GetString() == "unreadable";

    private static OcrRefusal ParseRefusal(JsonElement element)
    {
        var refusal = element.GetProperty("refusal");
        var raw = refusal.GetProperty("reason").GetString();

        var reason = raw switch
        {
            "blank_page" => OcrRefusalReason.BlankPage,
            "mean_confidence" => OcrRefusalReason.MeanConfidence,
            "rejected_words" => OcrRefusalReason.RejectedWords,
            _ => throw new JsonException($"unknown refusal reason '{raw}'"),
        };

        return new OcrRefusal(
            reason,
            refusal.GetProperty("threshold").GetDouble(),
            refusal.GetProperty("observed").GetDouble(),
            refusal.GetProperty("wordCount").GetInt32());
    }

    /// <summary>
    /// The report of what preprocessing did.
    /// </summary>
    /// <remarks>
    /// An absent <c>preprocess</c> object means nothing was done: a document written by a
    /// native library that predates preprocessing can only describe an untouched page, and
    /// reading it as anything else would invent a rotation that never happened.
    /// </remarks>
    private static OcrPreprocessReport ParsePreprocess(JsonElement element)
    {
        if (!element.TryGetProperty("preprocess", out var preprocess)
            || preprocess.ValueKind != JsonValueKind.Object)
        {
            return OcrPreprocessReport.None;
        }

        var rotation = preprocess.TryGetProperty("rotationDegrees", out var rotationElement)
            && rotationElement.ValueKind == JsonValueKind.Number
            ? rotationElement.GetInt32()
            : 0;

        var skew = preprocess.TryGetProperty("skewDegrees", out var skewElement)
            && skewElement.ValueKind == JsonValueKind.Number
            ? skewElement.GetDouble()
            : 0.0;

        var denoised = preprocess.TryGetProperty("denoised", out var denoisedElement)
            && denoisedElement.ValueKind == JsonValueKind.True;

        return new OcrPreprocessReport(rotation, skew, denoised);
    }

    private static string Text(JsonElement element)
        => element.TryGetProperty("text", out var text) ? text.GetString() ?? string.Empty : string.Empty;

    private static double Confidence(JsonElement element)
        => element.TryGetProperty("meanConfidence", out var confidence) ? confidence.GetDouble() : 0.0;

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
                    word.GetProperty("confidence").GetDouble(),
                    IsRejected(word)));
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

    /// <summary>
    /// Whether the native side flagged the word. An absent flag is a reliable word: a
    /// document without it predates the threshold, and nothing in it was flagged.
    /// </summary>
    private static bool IsRejected(JsonElement word)
        => word.TryGetProperty("rejected", out var rejected)
            && rejected.ValueKind == JsonValueKind.True;

    private static OcrBox ParseBox(JsonElement bounds) => new(
        bounds.GetProperty("x").GetInt32(),
        bounds.GetProperty("y").GetInt32(),
        bounds.GetProperty("width").GetInt32(),
        bounds.GetProperty("height").GetInt32());
}
