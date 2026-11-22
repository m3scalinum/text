using System;
using System.Globalization;

namespace M3scalinum.Text.Documents;

/// <summary>
/// A diagonal text drawn behind the content of every page of a <see cref="TypstSection"/>: a draft
/// mark, a confidentiality mark, a specimen.
/// </summary>
/// <remarks>
/// <para>
/// The watermark is the page's <c>background</c>, not a run of content: Typst draws it before the
/// page body on every page the section covers, so it never pushes the text down and a page break
/// does not repeat it as a paragraph. Its own defaults — 30° counter-clockwise, 72 pt, a light grey
/// at 15 % — are chosen so a reader sees it without it competing with the body text.
/// </para>
/// <para>
/// It is drawn as text, so a PDF reader that extracts a page's text finds the watermark's words in
/// the page's content stream like any other glyphs. A caller that extracts text and wants the mark
/// kept apart filters the watermark's own words.
/// </para>
/// </remarks>
public sealed class TypstWatermark
{
    private const double DefaultAngle = -30;
    private const double DefaultOpacity = 0.15;

    private TypstWatermark(string text) => TextValue = text;

    /// <summary>Starts a watermark. <see cref="TypstSection.Watermark(string, Action{TypstWatermark}?)"/> does it in one call.</summary>
    /// <param name="text">The word or phrase drawn across the page.</param>
    /// <exception cref="ArgumentException"><paramref name="text"/> is blank.</exception>
    public static TypstWatermark Create(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new TypstWatermark(text);
    }

    /// <summary>The text drawn across the page.</summary>
    internal string TextValue { get; }

    /// <summary>The rotation in degrees, counter-clockwise for a positive value.</summary>
    internal double AngleValue { get; private set; } = DefaultAngle;

    /// <summary>The type size of the mark.</summary>
    internal TypstLength SizeValue { get; private set; } = TypstLength.Pt(72);

    /// <summary>The colour the mark is drawn in, before its opacity.</summary>
    internal TypstColor ColorValue { get; private set; } = TypstColor.Luma(0.7);

    /// <summary>How opaque the mark is, 0 for invisible and 1 for solid.</summary>
    internal double OpacityValue { get; private set; } = DefaultOpacity;

    /// <summary>Rotates the mark. A negative angle reads bottom-left to top-right, the usual draft slant.</summary>
    /// <param name="degrees">The angle in degrees.</param>
    public TypstWatermark Angle(double degrees)
    {
        AngleValue = degrees;
        return this;
    }

    /// <summary>Sets the type size. A larger size covers more of the page.</summary>
    /// <param name="size">The type size.</param>
    public TypstWatermark Size(TypstLength size)
    {
        SizeValue = size;
        return this;
    }

    /// <summary>Sets the colour the mark is drawn in. The opacity applies on top of it.</summary>
    /// <param name="color">The colour.</param>
    public TypstWatermark Color(TypstColor color)
    {
        ColorValue = color;
        return this;
    }

    /// <summary>Sets the opacity of the mark.</summary>
    /// <param name="opacity">A value from 0 to 1, where 1 is fully opaque.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="opacity"/> is outside 0 to 1.</exception>
    public TypstWatermark Opacity(double opacity)
    {
        if (opacity is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(opacity), opacity, "an opacity is between 0 and 1");
        }

        OpacityValue = opacity;
        return this;
    }

    /// <summary>
    /// The <c>background</c> argument of <c>#set page</c>: the mark centred on the page, rotated,
    /// at its opacity.
    /// </summary>
    internal string ToPageBackground()
    {
        var fill = OpacityValue >= 1
            ? ColorValue.ToTypst()
            : ColorValue.ToTypst() + ".transparentize(" + Number((1 - OpacityValue) * 100) + "%)";

        return "place(center + horizon, rotate(" + Number(AngleValue) + "deg, text(size: "
            + SizeValue + ", fill: " + fill + ", " + LinkNode.Quote(TextValue) + ")))";
    }

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
