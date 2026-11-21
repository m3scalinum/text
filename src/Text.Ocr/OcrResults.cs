using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Ocr;

/// <summary>
/// One word of a recognised line.
/// </summary>
public sealed class OcrWord
{
    internal OcrWord(string text, OcrBox bounds, double confidence)
    {
        Text = text;
        Bounds = bounds;
        Confidence = confidence;
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
/// The text of one image.
/// </summary>
public sealed class OcrResult
{
    internal OcrResult(string text, IReadOnlyList<OcrLine> lines, double meanConfidence, int width, int height)
    {
        Text = text;
        Lines = lines;
        MeanConfidence = meanConfidence;
        Width = width;
        Height = height;
    }

    /// <summary>Every line, joined with <c>\n</c>.</summary>
    public string Text { get; }

    /// <summary>The lines, in reading order.</summary>
    public IReadOnlyList<OcrLine> Lines { get; }

    /// <summary>The mean of <see cref="OcrLine.Confidence"/> over <see cref="Lines"/>, or 0 when there are none.</summary>
    public double MeanConfidence { get; }

    /// <summary>The image width, in pixels.</summary>
    public int Width { get; }

    /// <summary>The image height, in pixels.</summary>
    public int Height { get; }

    /// <inheritdoc />
    public override string ToString() => Text;
}

/// <summary>
/// The text of one page of a PDF, rendered at a given DPI.
/// </summary>
public sealed class OcrPageResult
{
    internal OcrPageResult(int pageIndex, string text, IReadOnlyList<OcrLine> lines, double meanConfidence, int width, int height, int dpi)
    {
        PageIndex = pageIndex;
        Text = text;
        Lines = lines;
        MeanConfidence = meanConfidence;
        Width = width;
        Height = height;
        Dpi = dpi;
    }

    /// <summary>The zero-based page index in the document.</summary>
    public int PageIndex { get; }

    /// <summary>Every line of the page, joined with <c>\n</c>.</summary>
    public string Text { get; }

    /// <summary>The lines, in reading order.</summary>
    public IReadOnlyList<OcrLine> Lines { get; }

    /// <summary>The mean of <see cref="OcrLine.Confidence"/> over <see cref="Lines"/>, or 0 when there are none.</summary>
    public double MeanConfidence { get; }

    /// <summary>The rasterised page width, in pixels.</summary>
    public int Width { get; }

    /// <summary>The rasterised page height, in pixels.</summary>
    public int Height { get; }

    /// <summary>The resolution the page was rasterised at.</summary>
    public int Dpi { get; }

    /// <inheritdoc />
    public override string ToString() => Text;
}
