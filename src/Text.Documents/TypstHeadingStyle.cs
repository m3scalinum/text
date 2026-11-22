using System;

namespace M3scalinum.Text.Documents;

/// <summary>
/// One heading level of a <see cref="TypstDocumentStyle"/>: size, weight and spacing, configured
/// through <see cref="TypstDocumentStyleBuilder.Heading"/>.
/// </summary>
public sealed class TypstHeadingStyle
{
    internal TypstLength? SizeValue { get; private set; }

    internal string? WeightValue { get; private set; }

    internal TypstLength? AboveValue { get; private set; }

    internal TypstLength? BelowValue { get; private set; }

    /// <summary>Sets the heading size.</summary>
    public TypstHeadingStyle Size(TypstLength size)
    {
        SizeValue = size;
        return this;
    }

    /// <summary>Sets the weight, for example <c>bold</c> or a numeric weight.</summary>
    public TypstHeadingStyle Weight(string weight)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(weight);
        WeightValue = weight;
        return this;
    }

    /// <summary>Sets the space above and below the heading.</summary>
    /// <param name="above">Space before the heading.</param>
    /// <param name="below">Space after the heading.</param>
    public TypstHeadingStyle Spacing(TypstLength above, TypstLength below)
    {
        AboveValue = above;
        BelowValue = below;
        return this;
    }
}
