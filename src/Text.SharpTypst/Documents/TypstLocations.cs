using System;

namespace M3scalinum.Text.SharpTypst.Documents;

/// <summary>Location queries: <c>here()</c>, <c>here().page()</c>, <c>here().position()</c> and <c>locate(&lt;label&gt;)</c>.</summary>
/// <remarks>
/// Named in the plural so it does not collide with <see cref="M3scalinum.Text.SharpTypst.Diagnostics.TypstLocation"/>,
/// the record that carries a diagnostic's position.
/// </remarks>
public static class TypstLocations
{
    /// <summary>The current location.</summary>
    public static TypstExpression Here => TypstExpression.Raw("here()");

    /// <summary>The page of the current location.</summary>
    public static TypstExpression Page => TypstExpression.Raw("here().page()");

    /// <summary>The position of the current location.</summary>
    public static TypstExpression Position => TypstExpression.Raw("here().position()");

    /// <summary>Locates the element carrying a label.</summary>
    public static TypstExpression Locate(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return TypstExpression.Raw($"locate(<{label}>)");
    }
}