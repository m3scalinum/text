using System;
using System.Collections.Generic;
using System.Globalization;

namespace M3scalinum.Text.SharpTypst.Documents;

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
    public TypstTextStyle Weight(string weight)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(weight);
        _arguments.Add($"weight: {LinkNode.Quote(weight)}");
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