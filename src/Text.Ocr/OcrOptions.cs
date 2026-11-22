using System;

namespace M3scalinum.Text.Ocr;

/// <summary>
/// The confidence thresholds that decide whether a page is readable.
/// </summary>
/// <remarks>
/// <para>
/// The numbers are on the scale the result already reports — the mean detection
/// probability the detector assigned inside a box, in [0, 1] — so a caller compares
/// like with like and there is no second scale to convert between.
/// </para>
/// <para>
/// A page that fails a threshold is not returned as text. It comes back as
/// <see cref="OcrUnreadable"/>, or as an <see cref="OcrPageUnreadable"/> inside the
/// list <see cref="OcrEngine.RecognizePdf"/> returns, naming the rule that fired, the
/// threshold, the value observed and the number of words concerned.
/// </para>
/// <para>
/// The threshold does not improve recognition. It decides what to do with a reading
/// that is already poor, and the answer is to refuse it rather than to hand back
/// approximate text the caller has no way to distrust.
/// </para>
/// </remarks>
public class OcrOptions
{
    /// <summary>
    /// The default page threshold: a page whose mean line confidence is below this is
    /// refused. A crisp 300 DPI scan scores well above it.
    /// </summary>
    public const double DefaultMinimumPageConfidence = 0.6;

    /// <summary>
    /// The default word threshold: a word below this is flagged
    /// (<see cref="OcrWord.Rejected"/>) and is not reliable text.
    /// </summary>
    public const double DefaultMinimumWordConfidence = 0.5;

    /// <summary>
    /// The default tolerated proportion of flagged words: above it, the page is refused
    /// even when its mean confidence is high enough.
    /// </summary>
    public const double DefaultMaximumRejectedWordRatio = 0.5;

    /// <summary>
    /// The lowest mean line confidence a page may have and still be returned as text,
    /// in [0, 1]. Defaults to <see cref="DefaultMinimumPageConfidence"/>.
    /// </summary>
    public double MinimumPageConfidence { get; init; } = DefaultMinimumPageConfidence;

    /// <summary>
    /// The lowest confidence a word may have and still be reliable text, in [0, 1].
    /// A word strictly below it is returned with <see cref="OcrWord.Rejected"/> set.
    /// Defaults to <see cref="DefaultMinimumWordConfidence"/>.
    /// </summary>
    public double MinimumWordConfidence { get; init; } = DefaultMinimumWordConfidence;

    /// <summary>
    /// The largest proportion of a page's words that may be flagged before the page is
    /// refused, in [0, 1]. Defaults to <see cref="DefaultMaximumRejectedWordRatio"/>.
    /// </summary>
    public double MaximumRejectedWordRatio { get; init; } = DefaultMaximumRejectedWordRatio;

    internal virtual void Validate()
    {
        ValidateThreshold(nameof(MinimumPageConfidence), MinimumPageConfidence);
        ValidateThreshold(nameof(MinimumWordConfidence), MinimumWordConfidence);
        ValidateThreshold(nameof(MaximumRejectedWordRatio), MaximumRejectedWordRatio);
    }

    private static void ValidateThreshold(string name, double value)
    {
        // NaN fails every comparison, so it must be named as out of range here: a NaN
        // threshold would silently turn the refusal off.
        if (double.IsNaN(value) || value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                name, value, "The threshold must be a number between 0 and 1.");
        }
    }
}
