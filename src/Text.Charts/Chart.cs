using System;
using System.Text;
using M3scalinum.Text.Charts.Internal;

namespace M3scalinum.Text.Charts;

/// <summary>Renders a <see cref="ChartSpec"/> as SVG, and as the Typst markup that embeds it.</summary>
/// <remarks>
/// <para>
/// The output is standalone: one <c>&lt;svg&gt;</c> element, a <c>viewBox</c> that scales, no
/// <c>&lt;image&gt;</c>, no <c>href</c>, no stylesheet, no script and no embedded font. Text is
/// written as <c>&lt;text&gt;</c> naming the font family the spec asks for, so the consumer supplies
/// the face. That is what keeps <c>M3scalinum.Text.Charts</c> dependency-free and AOT-clean: it
/// writes a string, and a string needs no package behind it.
/// </para>
/// <para>
/// Rendering is deterministic: the same spec at the same size produces the same bytes, with every
/// number formatted through the invariant culture and every collection in the order it was given.
/// </para>
/// </remarks>
public static class Chart
{
    /// <summary>The font family the SVG names when the spec names none.</summary>
    public const string DefaultFontFamily = "sans-serif";

    /// <summary>Renders the spec as standalone SVG at <see cref="ChartSize.Default"/>.</summary>
    /// <param name="spec">The chart.</param>
    /// <exception cref="ArgumentNullException"><paramref name="spec"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The spec's kind cannot draw what it holds: two series in a pie, a negative slice, two series in a sparkline.</exception>
    public static string RenderSvg(ChartSpec spec) => RenderSvg(spec, ChartSize.Default);

    /// <summary>Renders the spec as standalone SVG.</summary>
    /// <param name="spec">The chart.</param>
    /// <param name="size">The drawing surface, in SVG user units.</param>
    /// <exception cref="ArgumentNullException"><paramref name="spec"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The spec's kind cannot draw what it holds: two series in a pie, a negative slice, two series in a sparkline.</exception>
    public static string RenderSvg(ChartSpec spec, ChartSize size) => ChartRenderer.Render(spec, size);

    /// <summary>Renders the chart as Typst markup that embeds the SVG, at <see cref="ChartSize.Default"/> and full width.</summary>
    /// <param name="spec">The chart.</param>
    public static string ToTypstImage(ChartSpec spec) => ToTypstImage(spec, ChartSize.Default, null);

    /// <summary>Renders the chart as Typst markup that embeds the SVG, at full width.</summary>
    /// <param name="spec">The chart.</param>
    /// <param name="size">The drawing surface, in SVG user units.</param>
    public static string ToTypstImage(ChartSpec spec, ChartSize size) => ToTypstImage(spec, size, null);

    /// <summary>
    /// Renders the chart and returns the Typst call that draws it: <c>#image(bytes("…"), format:
    /// "svg", width: …)</c>.
    /// </summary>
    /// <param name="spec">The chart.</param>
    /// <param name="size">The drawing surface, in SVG user units.</param>
    /// <param name="width">The width the image takes, as a Typst length such as <c>100%</c>, <c>12cm</c> or <c>240pt</c>; null means <c>100%</c>.</param>
    /// <returns>One line of Typst source, with the SVG already escaped.</returns>
    /// <exception cref="ArgumentException"><paramref name="width"/> is not a plain length.</exception>
    /// <remarks>
    /// <para>
    /// The image is embedded as bytes, so the markup carries the chart: nothing else has to be
    /// registered, shipped or resolved, and a report is a single self-contained source file.
    /// <c>image(…)</c> is the same node the document model's figure emits, which is why the result
    /// drops into a template or a <c>Raw</c> block unchanged.
    /// </para>
    /// <para>
    /// The escaping of the SVG is done here, once: a caller never concatenates a chart into markup.
    /// </para>
    /// </remarks>
    public static string ToTypstImage(ChartSpec spec, ChartSize size, string? width)
    {
        var svg = RenderSvg(spec, size);
        var length = width is null ? "100%" : TypstText.Length(width, nameof(width));
        var markup = new StringBuilder(svg.Length + 96);

        markup.Append("#image(bytes(").Append(TypstText.Quote(svg))
            .Append("), format: \"svg\", width: ").Append(length);

        if (spec.Title is not null)
        {
            markup.Append(", alt: ").Append(TypstText.Quote(spec.Title));
        }

        markup.Append(")\n");

        return markup.ToString();
    }
}
