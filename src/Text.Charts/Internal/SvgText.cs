using System;
using System.Globalization;
using System.Text;

namespace M3scalinum.Text.Charts.Internal;

/// <summary>Number, text and metric helpers shared by the renderer.</summary>
/// <remarks>
/// Every number in the SVG goes through <see cref="Number"/> or <see cref="TickLabel"/>, and both
/// format through <see cref="CultureInfo.InvariantCulture"/> explicitly: the suite runs with
/// <c>InvariantGlobalization</c> on, and a chart must not depend on the ambient culture either.
/// </remarks>
internal static class SvgText
{
    /// <summary>The advance width of one character, in font-size units, used to estimate a label's width.</summary>
    /// <remarks>
    /// No font is measured, and none can be: the package embeds no font. A proportional sans-serif
    /// averages a little over half an em per character, so 0.6 is the estimate every fit decision
    /// uses — layout and label thinning alike. A wider face may clip; a narrower one leaves a gap.
    /// </remarks>
    internal const double CharacterWidth = 0.6;

    /// <summary>Formats a coordinate or a size: invariant, at most three decimals, never <c>-0</c>.</summary>
    internal static string Number(double value)
    {
        if (value == 0)
        {
            return "0";
        }

        var text = value.ToString("0.###", CultureInfo.InvariantCulture);

        return text == "-0" ? "0" : text;
    }

    /// <summary>Escapes text for an element body or an attribute value.</summary>
    internal static string Escape(string value)
    {
        if (value.AsSpan().IndexOfAny("&<>\"'") < 0)
        {
            return value;
        }

        var escaped = new StringBuilder(value.Length + 8);

        foreach (var c in value)
        {
            switch (c)
            {
                case '&':
                    escaped.Append("&amp;");
                    break;
                case '<':
                    escaped.Append("&lt;");
                    break;
                case '>':
                    escaped.Append("&gt;");
                    break;
                case '"':
                    escaped.Append("&quot;");
                    break;
                case '\'':
                    escaped.Append("&#39;");
                    break;
                default:
                    escaped.Append(c);
                    break;
            }
        }

        return escaped.ToString();
    }

    /// <summary>Estimates the width of <paramref name="text"/> at <paramref name="fontSize"/>, in user units.</summary>
    internal static double EstimateWidth(string text, double fontSize)
        => text.Length * fontSize * CharacterWidth;

    /// <summary>Formats a tick label so that adjacent ticks differ by a readable amount.</summary>
    /// <param name="value">The tick value.</param>
    /// <param name="step">The distance between two ticks, which decides how many decimals are shown.</param>
    internal static string TickLabel(double value, double step)
    {
        if (value == 0)
        {
            return "0";
        }

        var magnitude = Math.Abs(value);

        // A plain decimal for a magnitude a reader can take in at a glance; scientific beyond, so a
        // huge or a tiny range stays short enough to label a 640-unit axis.
        if (magnitude >= 1e7 || magnitude < 1e-4)
        {
            return value.ToString("0.###E+0", CultureInfo.InvariantCulture);
        }

        var decimals = Decimals(step);

        var text = value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        // 10.0 becomes 10 and 2.50 becomes 2.5: the decimals exist to keep a step exact, not to pad
        // every label to the same width.
        if (text.Contains('.', StringComparison.Ordinal))
        {
            text = text.TrimEnd('0').TrimEnd('.');
        }

        return text.TrimStart('-').Trim('0', '.').Length == 0 ? "0" : text;
    }

    /// <summary>The number of decimals a step needs to be written exactly.</summary>
    private static int Decimals(double step)
    {
        var decimals = 0;
        var scaled = Math.Abs(step);

        while (decimals < 6 && Math.Abs(scaled - Math.Round(scaled)) > 1e-9)
        {
            scaled *= 10;
            decimals++;
        }

        return decimals;
    }
}
