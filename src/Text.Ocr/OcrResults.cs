using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace M3scalinum.Text.Ocr;

/// <summary>
/// Whether a reading was accepted or refused.
/// </summary>
public enum OcrOutcome
{
    /// <summary>The reading cleared every threshold and may be used.</summary>
    Recognized,

    /// <summary>A threshold refused the reading; there is no text to use.</summary>
    Unreadable,
}

/// <summary>
/// Which threshold refused a page.
/// </summary>
public enum OcrRefusalReason
{
    /// <summary>No line was found at all: a blank page or an unreadable scan.</summary>
    BlankPage,

    /// <summary>The page's mean line confidence is below <see cref="OcrOptions.MinimumPageConfidence"/>.</summary>
    MeanConfidence,

    /// <summary>
    /// More than <see cref="OcrOptions.MaximumRejectedWordRatio"/> of the page's words
    /// are below <see cref="OcrOptions.MinimumWordConfidence"/>.
    /// </summary>
    RejectedWords,
}

/// <summary>
/// Why a page was refused: the rule, the threshold it compares against, the value
/// observed, and how many words it concerns.
/// </summary>
public sealed class OcrRefusal
{
    internal OcrRefusal(OcrRefusalReason reason, double threshold, double observed, int wordCount)
    {
        Reason = reason;
        Threshold = threshold;
        Observed = observed;
        WordCount = wordCount;
    }

    /// <summary>Which threshold fired.</summary>
    public OcrRefusalReason Reason { get; }

    /// <summary>
    /// The threshold that rule compares against: the page minimum confidence, or the
    /// tolerated proportion of flagged words.
    /// </summary>
    public double Threshold { get; }

    /// <summary>
    /// What the page actually produced: its mean confidence for
    /// <see cref="OcrRefusalReason.MeanConfidence"/>, or the proportion of flagged words
    /// for <see cref="OcrRefusalReason.RejectedWords"/>.
    /// </summary>
    public double Observed { get; }

    /// <summary>The number of words the refusal concerns; 0 for a blank page.</summary>
    public int WordCount { get; }

    /// <summary>
    /// One sentence naming the rule, the threshold, the value observed and the word
    /// count, so a log line or an error message carries the whole story.
    /// </summary>
    public string Message
    {
        get
        {
            var rule = Reason switch
            {
                OcrRefusalReason.BlankPage => "blank page",
                OcrRefusalReason.MeanConfidence => "mean confidence below the page threshold",
                OcrRefusalReason.RejectedWords => "too many words below the word threshold",
                _ => "unnamed rule",
            };

            return string.Format(
                CultureInfo.InvariantCulture,
                "The page is unreadable ({0}): the threshold is {1:F3}, the value observed is {2:F3}, {3} word(s) concerned.",
                rule,
                Threshold,
                Observed,
                WordCount);
        }
    }

    /// <inheritdoc />
    public override string ToString() => Message;
}

/// <summary>
/// The outcome of reading one image, or one page: either a reading or a refusal.
/// </summary>
/// <remarks>
/// The two are separate types on purpose. A caller cannot reach the text of a refused
/// page by mistake, because <see cref="OcrUnreadable"/> has no text to reach.
/// </remarks>
public abstract class OcrRecognition
{
    private protected OcrRecognition(int width, int height)
    {
        Width = width;
        Height = height;
    }

    /// <summary>The image width, in pixels.</summary>
    public int Width { get; }

    /// <summary>The image height, in pixels.</summary>
    public int Height { get; }

    /// <summary>Whether this is a reading or a refusal.</summary>
    public abstract OcrOutcome Outcome { get; }
}

/// <summary>
/// The text of one image.
/// </summary>
public sealed class OcrResult : OcrRecognition
{
    internal OcrResult(
        string text,
        IReadOnlyList<OcrLine> lines,
        double meanConfidence,
        int width,
        int height)
        : base(width, height)
    {
        Text = text;
        Lines = lines;
        MeanConfidence = meanConfidence;
        RejectedWordCount = lines.Sum(line => line.Words.Count(word => word.Rejected));

        var words = lines.Sum(line => line.Words.Count);
        RejectedWordRatio = words == 0 ? 0.0 : (double)RejectedWordCount / words;
    }

    /// <summary>Every line, joined with <c>\n</c>.</summary>
    public string Text { get; }

    /// <summary>The lines, in reading order.</summary>
    public IReadOnlyList<OcrLine> Lines { get; }

    /// <summary>The mean of <see cref="OcrLine.Confidence"/> over <see cref="Lines"/>, or 0 when there are none.</summary>
    public double MeanConfidence { get; }

    /// <summary>How many words fell below <see cref="OcrOptions.MinimumWordConfidence"/>.</summary>
    public int RejectedWordCount { get; }

    /// <summary>
    /// <see cref="RejectedWordCount"/> over the total number of words, in [0, 1], or 0
    /// when the image holds no word.
    /// </summary>
    public double RejectedWordRatio { get; }

    /// <inheritdoc />
    public override OcrOutcome Outcome => OcrOutcome.Recognized;

    /// <inheritdoc />
    public override string ToString() => Text;
}

/// <summary>
/// A refused image: no text, and the reason no text was returned.
/// </summary>
public sealed class OcrUnreadable : OcrRecognition
{
    internal OcrUnreadable(int width, int height, double meanConfidence, OcrRefusal refusal)
        : base(width, height)
    {
        MeanConfidence = meanConfidence;
        Refusal = refusal;
    }

    /// <summary>The mean line confidence the image produced, below the threshold that refused it.</summary>
    public double MeanConfidence { get; }

    /// <summary>The rule that refused the image, with its threshold and the value seen.</summary>
    public OcrRefusal Refusal { get; }

    /// <inheritdoc />
    public override OcrOutcome Outcome => OcrOutcome.Unreadable;

    /// <inheritdoc />
    public override string ToString() => Refusal.Message;
}

/// <summary>
/// One word of a recognised line.
/// </summary>
public sealed class OcrWord
{
    internal OcrWord(string text, OcrBox bounds, double confidence, bool rejected = false)
    {
        Text = text;
        Bounds = bounds;
        Confidence = confidence;
        Rejected = rejected;
    }

    /// <summary>The recognised characters, with no surrounding whitespace.</summary>
    public string Text { get; }

    /// <summary>The word's box in image pixels.</summary>
    public OcrBox Bounds { get; }

    /// <summary>
    /// How strongly the text detector saw text inside <see cref="Bounds"/>, in [0, 1].
    /// See <see cref="OcrLine.Confidence"/> for what this number is, and is not.
    /// </summary>
    public double Confidence { get; }

    /// <summary>
    /// Whether <see cref="Confidence"/> fell below <see cref="OcrOptions.MinimumWordConfidence"/>.
    /// A rejected word is still returned with its box and its score so a caller can see
    /// where the doubt was, but it is not text to rely on.
    /// </summary>
    public bool Rejected { get; }

    /// <inheritdoc />
    public override string ToString() => Text;
}

/// <summary>
/// One line of recognised text, in reading order.
/// </summary>
public sealed class OcrLine
{
    internal OcrLine(string text, OcrBox bounds, double confidence, IReadOnlyList<OcrWord> words)
    {
        Text = text;
        Bounds = bounds;
        Confidence = confidence;
        Words = words;
    }

    /// <summary>The recognised line, as one string.</summary>
    public string Text { get; }

    /// <summary>The line's box in image pixels.</summary>
    public OcrBox Bounds { get; }

    /// <summary>
    /// The mean text probability the detection model assigned inside
    /// <see cref="Bounds"/>, in [0, 1].
    /// </summary>
    /// <remarks>
    /// This is a detection score, not a recognition likelihood: <c>ocrs</c> 0.14 does
    /// not expose the decoder's per-character scores, so a number invented from the
    /// characters would be a fiction. A line the detector is sure about scores near
    /// 1; a faint scan scores lower.
    /// </remarks>
    public double Confidence { get; }

    /// <summary>The words of the line, in reading order.</summary>
    public IReadOnlyList<OcrWord> Words { get; }

    /// <inheritdoc />
    public override string ToString() => Text;
}

/// <summary>
/// The outcome of reading one page of a PDF: either a reading or a refusal.
/// </summary>
public abstract class OcrPageOutcome
{
    private protected OcrPageOutcome(int pageIndex, int width, int height, int dpi)
    {
        PageIndex = pageIndex;
        Width = width;
        Height = height;
        Dpi = dpi;
    }

    /// <summary>The zero-based page index in the document.</summary>
    public int PageIndex { get; }

    /// <summary>The rasterised page width, in pixels.</summary>
    public int Width { get; }

    /// <summary>The rasterised page height, in pixels.</summary>
    public int Height { get; }

    /// <summary>The resolution the page was rasterised at.</summary>
    public int Dpi { get; }

    /// <summary>Whether this is a reading or a refusal.</summary>
    public abstract OcrOutcome Outcome { get; }
}

/// <summary>
/// The text of one page of a PDF, rendered at a given DPI.
/// </summary>
public sealed class OcrPageResult : OcrPageOutcome
{
    internal OcrPageResult(
        int pageIndex,
        string text,
        IReadOnlyList<OcrLine> lines,
        double meanConfidence,
        int width,
        int height,
        int dpi)
        : base(pageIndex, width, height, dpi)
    {
        Text = text;
        Lines = lines;
        MeanConfidence = meanConfidence;
        RejectedWordCount = lines.Sum(line => line.Words.Count(word => word.Rejected));

        var words = lines.Sum(line => line.Words.Count);
        RejectedWordRatio = words == 0 ? 0.0 : (double)RejectedWordCount / words;
    }

    /// <summary>Every line of the page, joined with <c>\n</c>.</summary>
    public string Text { get; }

    /// <summary>The lines, in reading order.</summary>
    public IReadOnlyList<OcrLine> Lines { get; }

    /// <summary>The mean of <see cref="OcrLine.Confidence"/> over <see cref="Lines"/>, or 0 when there are none.</summary>
    public double MeanConfidence { get; }

    /// <summary>How many words fell below <see cref="OcrOptions.MinimumWordConfidence"/>.</summary>
    public int RejectedWordCount { get; }

    /// <summary>
    /// <see cref="RejectedWordCount"/> over the total number of words, in [0, 1], or 0
    /// when the page holds no word.
    /// </summary>
    public double RejectedWordRatio { get; }

    /// <inheritdoc />
    public override OcrOutcome Outcome => OcrOutcome.Recognized;

    /// <inheritdoc />
    public override string ToString() => Text;
}

/// <summary>
/// A refused page: no text, and the reason no text was returned.
/// </summary>
public sealed class OcrPageUnreadable : OcrPageOutcome
{
    internal OcrPageUnreadable(
        int pageIndex, int width, int height, int dpi, double meanConfidence, OcrRefusal refusal)
        : base(pageIndex, width, height, dpi)
    {
        MeanConfidence = meanConfidence;
        Refusal = refusal;
    }

    /// <summary>The mean line confidence the page produced, below the threshold that refused it.</summary>
    public double MeanConfidence { get; }

    /// <summary>The rule that refused the page, with its threshold and the value seen.</summary>
    public OcrRefusal Refusal { get; }

    /// <inheritdoc />
    public override OcrOutcome Outcome => OcrOutcome.Unreadable;

    /// <inheritdoc />
    public override string ToString() => Refusal.Message;
}
