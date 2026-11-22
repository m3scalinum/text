using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Documents;

/// <summary>
/// A reusable document style — page, body text, heading levels 1 to 3, language, header and footer —
/// declared once and applied to any <see cref="TypstDocument"/> with <see cref="TypstDocument.Apply"/>.
/// </summary>
/// <remarks>
/// <para>
/// A style is built with <see cref="Create"/> and never changes afterwards: the fluent configuration
/// lives on <see cref="TypstDocumentStyleBuilder"/>, and <see cref="TypstDocumentStyleBuilder.Build"/>
/// freezes the <c>#set</c> and <c>#show</c> lines it will write. One object, one application: there is
/// no options type beside the style, and no switch that turns its effect on.
/// </para>
/// <para>
/// Applying a style writes rules for what it declares and nothing else, so a document that already
/// set a page background, a font or a header keeps them wherever the style is silent, and a document
/// with no style keeps exactly the markup it had.
/// </para>
/// </remarks>
public sealed class TypstDocumentStyle
{
    private readonly IReadOnlyList<string> _rules;

    private TypstDocumentStyle(IReadOnlyList<string> rules) => _rules = rules;

    /// <summary>
    /// The Typst lines the style contributes, in the order they are written after the document's own
    /// <c>#set</c> rules.
    /// </summary>
    internal IReadOnlyList<string> SetRules => _rules;

    /// <summary>Starts a style. Call <see cref="TypstDocumentStyleBuilder.Build"/> to freeze it.</summary>
    public static TypstDocumentStyleBuilder Create() => new();

    internal static TypstDocumentStyle FromRules(IReadOnlyList<string> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return new TypstDocumentStyle(rules);
    }
}
