using System;

namespace M3scalinum.Text.Documents;

/// <summary>
/// The body text of a <see cref="TypstDocumentStyle"/>: family, size, line height and justification,
/// configured through <see cref="TypstDocumentStyleBuilder.Body"/>.
/// </summary>
public sealed class TypstBodyStyle
{
    internal string? FontValue { get; private set; }

    internal TypstLength? SizeValue { get; private set; }

    internal double? LeadingValue { get; private set; }

    internal bool Justified { get; private set; }

    /// <summary>Sets the text family, for example <c>Libertinus Serif</c>.</summary>
    public TypstBodyStyle Font(string family)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        FontValue = family;
        return this;
    }

    /// <summary>Sets the font size.</summary>
    public TypstBodyStyle Size(TypstLength size)
    {
        SizeValue = size;
        return this;
    }

    /// <summary>Sets the line height, as a multiple of the font size.</summary>
    public TypstBodyStyle Leading(double multiple)
    {
        if (multiple <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(multiple), multiple, "leading is positive");
        }

        LeadingValue = multiple;
        return this;
    }

    /// <summary>Justifies the paragraphs, or turns justification back off.</summary>
    public TypstBodyStyle Justify(bool justified = true)
    {
        Justified = justified;
        return this;
    }
}
