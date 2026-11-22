using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Documents;

/// <summary>What a number counts from: the whole document, or the section the element sits in.</summary>
public enum TypstNumberingScope
{
    /// <summary>One sequence for the whole document. The default, and the markup every document had before.</summary>
    Document = 0,

    /// <summary>A sequence that restarts at each section, that is, at each level-1 heading.</summary>
    Section = 1,
}

/// <summary>
/// The preamble that makes Typst restart footnote numbers at every section, and the guard that
/// writes it once.
/// </summary>
/// <remarks>
/// <para>
/// Typst numbers footnotes itself with <c>counter(footnote)</c>, and the only thing the model has to
/// add is a reset: <c>#show heading.where(level: 1): it => { counter(footnote).update(0); it }</c>.
/// A show rule is used rather than a reset written before each heading because the model writes the
/// document body in call order and cannot know, when a footnote is added, where the next section
/// starts.
/// </para>
/// <para>
/// Measured against Typst 0.15.1: the reset form gives 1, 2, 3 for notes after a heading, where the
/// composite form <c>#set footnote(numbering: "1.1")</c> is ignored by the compiler (it keeps
/// printing 1, 2, 3), and <c>numbering: n => numbering("1.1", counter(heading)…)</c> prefixes the
/// section but depends on heading numbering being on. The reset needs neither.
/// </para>
/// </remarks>
internal static class TypstFootnoteNumbering
{
    /// <summary>The show rule that restarts the footnote counter at each level-1 heading.</summary>
    internal const string SectionRule =
        "#show heading.where(level: 1): it => {\n" +
        "  counter(footnote).update(0)\n" +
        "  it\n" +
        "}";

    /// <summary>Adds the rules a section-scoped footnote needs, once.</summary>
    internal static void EnableSection(List<string> preamble)
    {
        ArgumentNullException.ThrowIfNull(preamble);
        AddOnce(preamble, SectionRule);
    }

    /// <summary>
    /// Whether a scope restarts at each section, rejecting a value the enum does not define.
    /// </summary>
    internal static bool IsSectionScoped(TypstNumberingScope scope, string parameterName) => scope switch
    {
        TypstNumberingScope.Document => false,
        TypstNumberingScope.Section => true,
        _ => throw new ArgumentOutOfRangeException(
            parameterName,
            scope,
            "a numbering scope is Document or Section"),
    };

    /// <summary>Appends a rule the document does not carry yet.</summary>
    internal static void AddOnce(List<string> preamble, string rule)
    {
        if (!preamble.Contains(rule))
        {
            preamble.Add(rule);
        }
    }
}
