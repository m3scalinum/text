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
        var models = ParseModels(root);
        var language = ParseLanguage(root);

        if (IsUnreadable(root))
        {
            return new OcrUnreadable(
                width, height, meanConfidence, ParseRefusal(root), models, language);
        }

        return new OcrResult(
            Text(root),
            ParseLines(root, width, height),
            meanConfidence,
            width,
            height,
            ParsePreprocess(root),
            models,
            language);
    }

    /// <summary>Parses <c>{"pages": [...]}</c>.</summary>
    internal static IReadOnlyList<OcrPageOutcome> ParsePages(byte[] json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var models = ParseModels(root);

        var pages = new List<OcrPageOutcome>();
        foreach (var page in root.GetProperty("pages").EnumerateArray())
        {
            var pageIndex = page.GetProperty("pageIndex").GetInt32();
            var width = page.GetProperty("width").GetInt32();
            var height = page.GetProperty("height").GetInt32();
            var dpi = page.GetProperty("dpi").GetInt32();
            var meanConfidence = Confidence(page);

            // A page of a language-set read carries its own identity, because two
            // pages of one document can have been read by two pairs of weights. A
            // document with one set carries it at the root only.
            var pageModels = ParseModels(page) ?? models;
            var language = ParseLanguage(page);

            if (IsUnreadable(page))
            {
                pages.Add(new OcrPageUnreadable(
                    pageIndex, width, height, dpi, meanConfidence, ParseRefusal(page),
                    pageModels, language));
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
                ParsePreprocess(page),
                pageModels,
                language));
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

    /// <summary>
    /// The language verdict a page carries, or <see langword="null"/> when it carries
    /// none.
    /// </summary>
    /// <remarks>
    /// An absent object means the engine held one model set: there was no choice to
    /// report, and inventing a tag would claim the page asked for one.
    /// </remarks>
    private static OcrLanguage? ParseLanguage(JsonElement element)
    {
        if (!element.TryGetProperty("language", out var language)
            || language.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var requested = language.TryGetProperty("requested", out var requestedElement)
            && requestedElement.ValueKind == JsonValueKind.String
            ? requestedElement.GetString()
            : null;

        var used = language.TryGetProperty("used", out var usedElement)
            && usedElement.ValueKind == JsonValueKind.String
            ? usedElement.GetString() ?? string.Empty
            : string.Empty;

        var fellBack = language.TryGetProperty("fallback", out var fallbackElement)
            && fallbackElement.ValueKind == JsonValueKind.True;

        var reason = OcrLanguageFallbackReason.Unspecified;

        if (fellBack
            && language.TryGetProperty("reason", out var reasonElement)
            && reasonElement.ValueKind == JsonValueKind.String)
        {
            reason = reasonElement.GetString() switch
            {
                "missing_language" => OcrLanguageFallbackReason.MissingLanguage,
                _ => OcrLanguageFallbackReason.Unspecified,
            };
        }

        var message = language.TryGetProperty("message", out var messageElement)
            && messageElement.ValueKind == JsonValueKind.String
            ? messageElement.GetString()
            : null;

        return new OcrLanguage(requested, used, fellBack, reason, message);
    }

    /// <summary>Reads the page count document: <c>{"pages":N}</c>.</summary>
    internal static uint ParsePageCount(byte[] json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("pages").GetUInt32();
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

    /// <summary>
    /// Reads the <c>models</c> object a result document carries, or
    /// <see langword="null"/> when it carries none.
    /// </summary>
    /// <remarks>
    /// An absent object means the native library predates model identity: it only
    /// ever loaded the bytes the caller passed, so there is no identity to invent
    /// and a result that has none reports none.
    /// </remarks>
    internal static OcrModelIdentity? ParseModels(byte[] json)
    {
        using var document = JsonDocument.Parse(json);
        return ParseModels(document.RootElement);
    }

    private static OcrModelIdentity? ParseModels(JsonElement element)
        => element.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Object
            ? ParseIdentity(models)
            : null;

    /// <summary>
    /// Reads the identity document the native side writes:
    /// <c>{"detection":{…},"recognition":{…},"set":"…"}</c>.
    /// </summary>
    internal static OcrModelIdentity ParseIdentity(JsonElement element) => new(
        ParseFingerprint(element.GetProperty("detection")),
        ParseFingerprint(element.GetProperty("recognition")),
        element.GetProperty("set").GetString() ?? string.Empty);

    /// <summary>
    /// Reads the preflight document of a model directory:
    /// <c>{"directory":"…","detection":{…},"recognition":{…},"set":"…"}</c>.
    /// </summary>
    internal static (string Directory, OcrModelIdentity Identity) ParseModelSet(byte[] json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var directory = root.TryGetProperty("directory", out var directoryElement)
            && directoryElement.ValueKind == JsonValueKind.String
            ? directoryElement.GetString() ?? string.Empty
            : string.Empty;

        return (directory, ParseIdentity(root));
    }

    /// <summary>Reads an identity document that is the whole document.</summary>
    internal static OcrModelIdentity ParseIdentity(byte[] json)
    {
        using var document = JsonDocument.Parse(json);
        return ParseIdentity(document.RootElement);
    }

    private static OcrModelFingerprint ParseFingerprint(JsonElement element)
    {
        var role = element.TryGetProperty("role", out var roleElement)
            && roleElement.ValueKind == JsonValueKind.String
            ? roleElement.GetString() switch
            {
                "detection" => OcrModelRole.Detection,
                "recognition" => OcrModelRole.Recognition,
                _ => OcrModelRole.Unspecified,
            }
            : OcrModelRole.Unspecified;

        var fileName = element.TryGetProperty("file", out var fileElement)
            && fileElement.ValueKind == JsonValueKind.String
            ? fileElement.GetString()
            : null;

        return new OcrModelFingerprint(
            role,
            fileName,
            element.GetProperty("format").GetString() ?? "unknown",
            element.GetProperty("length").GetInt64(),
            element.GetProperty("sha256").GetString() ?? string.Empty);
    }
}
