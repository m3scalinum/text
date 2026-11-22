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

    /// <summary>
    /// Whether a page turned by 90°, 180° or 270° is detected and turned back before it
    /// is read. Off by default.
    /// </summary>
    /// <remarks>
    /// The detection is a heuristic on the page's ink: the direction a page's lines run
    /// gives the quarter turns, and the imbalance between ascenders and descenders gives
    /// the 180° case, because printed text has more of the former. A page whose lines are
    /// vertically symmetric — all capitals, digits, a table rule — gives no signal, and is
    /// then left as it is rather than guessed at. <see cref="OcrPreprocessReport.RotationDegrees"/>
    /// says what was applied.
    /// </remarks>
    public bool DetectRotation { get; init; }

    /// <summary>
    /// Whether a fine skew of at most 10° either way is estimated and removed before the
    /// page is read. Off by default.
    /// </summary>
    /// <remarks>
    /// The estimate searches a quarter of a degree at a time and is applied by bilinear
    /// interpolation, so the page is resampled once. A page that is level to within a
    /// third of a degree is not touched. <see cref="OcrPreprocessReport.SkewDegrees"/> says
    /// what was applied.
    /// </remarks>
    public bool Deskew { get; init; }

    /// <summary>
    /// Whether a 3×3 median filter is applied to the page's luma before it is read. Off by
    /// default.
    /// </summary>
    /// <remarks>
    /// The filter removes isolated specks without softening stroke edges the way a mean
    /// filter would. A stroke one pixel wide is a minority in its own 3×3 window and is
    /// removed with the noise it sits in; a 300 DPI scan's strokes are 2–4 pixels.
    /// </remarks>
    public bool Denoise { get; init; }

    /// <summary>
    /// Whether any preprocessing stage was asked for. The native call with no stage is the
    /// one revision 1 and 2 of the ABI exported, and is used unchanged when this is false.
    /// </summary>
    internal bool RequiresPreprocessing => DetectRotation || Deskew || Denoise;

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
