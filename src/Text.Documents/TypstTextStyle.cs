using System;
using System.Collections.Generic;
using System.Globalization;

namespace M3scalinum.Text.Documents;

/// <summary>Text styling, emitted as <c>#text(…)[…]</c>.</summary>
public sealed class TypstTextStyle
{
    private readonly List<string> _arguments = [];

    /// <summary>Sets the font family.</summary>
    public TypstTextStyle Font(string family)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        _arguments.Add($"font: {LinkNode.Quote(family)}");
        return this;
    }

    /// <summary>Sets the font size.</summary>
    public TypstTextStyle Size(TypstLength size)
    {
        _arguments.Add($"size: {size}");
        return this;
    }

    /// <summary>Sets the weight, for example <c>bold</c> or a numeric weight.</summary>
    /// <remarks>
    /// A weight is either one of Typst's names — <c>thin</c>, <c>extralight</c>, <c>light</c>,
    /// <c>regular</c>, <c>medium</c>, <c>semibold</c>, <c>bold</c>, <c>extrabold</c>, <c>black</c> —
    /// or a number, which is what a variable font interpolates. The two are not interchangeable in
    /// Typst: a name is a string and a number is not, so <c>weight: "350"</c> is rejected by the
    /// compiler rather than read as 350. Use <see cref="Weight(int)"/> for a number.
    /// </remarks>
    public TypstTextStyle Weight(string weight)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(weight);

        if (int.TryParse(weight, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            throw new ArgumentException(
                $"'{weight}' is a number, and Typst reads a quoted weight as a name: use Weight(int) " +
                "for a numeric weight, or one of the names (thin, light, regular, medium, semibold, " +
                "bold, black).",
                nameof(weight));
        }

        _arguments.Add($"weight: {LinkNode.Quote(weight)}");
        return this;
    }

    /// <summary>Sets a numeric weight, from 1 to 1000, which a variable font interpolates.</summary>
    /// <remarks>
    /// 400 is regular and 700 is bold; 350 and 550 are the values a static font cannot draw and a
    /// variable one can. A family without a <c>wght</c> axis renders the nearest weight it has, and
    /// <c>TypstWorld.Fonts</c> is how a caller finds out before promising the typography.
    /// </remarks>
    public TypstTextStyle Weight(int weight)
    {
        if (weight is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(weight), weight, "a weight is between 1 and 1000");
        }

        _arguments.Add($"weight: {weight.ToString(CultureInfo.InvariantCulture)}");
        return this;
    }

    /// <summary>Sets the style: <c>normal</c>, <c>italic</c> or <c>oblique</c>.</summary>
    public TypstTextStyle Slant(string style)
    {
        if (style is not ("normal" or "italic" or "oblique"))
        {
            throw new ArgumentException("style is 'normal', 'italic' or 'oblique'", nameof(style));
        }

        _arguments.Add($"style: {LinkNode.Quote(style)}");
        return this;
    }

    /// <summary>Sets the text colour.</summary>
    public TypstTextStyle Fill(ITypstPaint paint)
    {
        ArgumentNullException.ThrowIfNull(paint);
        _arguments.Add($"fill: {paint.ToTypst()}");
        return this;
    }

    /// <summary>Sets the letter spacing.</summary>
    public TypstTextStyle Tracking(TypstLength tracking)
    {
        _arguments.Add($"tracking: {tracking}");
        return this;
    }

    /// <summary>Sets the line height, as a multiple of the font size.</summary>
    public TypstTextStyle Leading(double multiple)
    {
        if (multiple <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(multiple), multiple, "leading is positive");
        }

        _arguments.Add($"leading: {multiple.ToString("0.###", CultureInfo.InvariantCulture)}em");
        return this;
    }

    /// <summary>Emits the argument list, without the surrounding parentheses.</summary>
    internal string Arguments => string.Join(", ", _arguments);
}

/// <summary>Styled text, as content.</summary>
public sealed partial class TypstContent
{
    /// <summary>Appends text with an explicit style.</summary>
    public TypstContent Styled(string text, TypstTextStyle style)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(style);
        _nodes.Add(new StyledTextNode(text, style));
        return this;
    }
}