using System;

namespace M3scalinum.Text.Documents;

/// <summary>Location queries: <c>here()</c>, <c>here().page()</c>, <c>here().position()</c> and <c>locate(&lt;label&gt;)</c>.</summary>
/// <remarks>
/// Named in the plural so it does not collide with <c>M3scalinum.Text.Typst.Diagnostics.TypstLocation</c>,
/// the record that carries a diagnostic's position: the suite forbids two public types sharing a name.
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