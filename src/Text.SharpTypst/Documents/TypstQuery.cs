using System;
using System.Globalization;

namespace M3scalinum.Text.SharpTypst.Documents;

/// <summary>Queries over the document.</summary>
public static class TypstQuery
{
    /// <summary>Queries a selector.</summary>
    public static TypstExpression Of(TypstExpression selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return TypstExpression.Raw($"query({selector.ToTypst()})");
    }

    /// <summary>Queries headings, optionally at one level.</summary>
    public static TypstExpression Headings(int? level = null)
    {
        if (level is < 1 or > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "a heading level is 1 to 6");
        }

        return TypstExpression.Raw(
            level is null
                ? "query(heading)"
                : $"query(heading.where(level: {level.Value.ToString(CultureInfo.InvariantCulture)}))");
    }

    /// <summary>Queries figures.</summary>
    public static TypstExpression Figures() => TypstExpression.Raw("query(figure)");

    /// <summary>Queries the elements carrying a label.</summary>
    public static TypstExpression Labels(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return TypstExpression.Raw($"query(<{label}>)");
    }
}