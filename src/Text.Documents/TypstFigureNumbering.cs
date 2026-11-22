using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Documents;

/// <summary>
/// The preamble that numbers image figures as <c>section.figure</c> and restarts the figure counter
/// at every section.
/// </summary>
/// <remarks>
/// <para>
/// Two rules are needed and both are Typst's own arithmetic, not the model's:
/// </para>
/// <list type="bullet">
/// <item>
/// <c>#show heading.where(level: 1): it => { counter(figure.where(kind: image)).update(0); it }</c>
/// restarts the figure numbering so the first figure of a section is 1 again. Only image figures
/// are reset: a table is a figure of another kind and keeps its own document-wide numbering.
/// </item>
/// <item>
/// <c>#show figure.where(kind: image): set figure(numbering: …)</c> prints the figure as
/// <c>section.figure</c>, and as a plain number when no section precedes it.
/// </item>
/// </list>
/// <para>
/// Measured against Typst 0.15.1: <c>#figure(…, numbering: "1.1")</c> is not a composite pattern —
/// the compiler prints 1, 2, 3 — so the section prefix comes from a numbering function that reads
/// <c>counter(heading)</c>, and the per-section sequence comes from the reset. Composite numbering
/// needs heading numbering, so the document turns <c>#set heading(numbering: "1.1")</c> on unless it
/// already numbers its headings: the same rule the model writes when a heading is labelled.
/// </para>
/// </remarks>
internal static class TypstFigureNumbering
{
    /// <summary>The heading numbering the section prefix reads, when the document sets none.</summary>
    internal const string HeadingNumberingRule = "#set heading(numbering: \"1.1\")";

    /// <summary>The show rule that restarts the image figure counter at each level-1 heading.</summary>
    internal const string SectionRule =
        "#show heading.where(level: 1): it => {\n" +
        "  counter(figure.where(kind: image)).update(0)\n" +
        "  it\n" +
        "}";

    /// <summary>The show rule that prints a figure as <c>section.figure</c>, or as a plain number.</summary>
    internal const string NumberingRule =
        "#show figure.where(kind: image): set figure(numbering: n => {\n" +
        "  let section = counter(heading).get().first()\n" +
        "  if section == 0 { numbering(\"1\", n) } else { numbering(\"1.1\", section, n) }\n" +
        "})";

    /// <summary>Adds the rules a section-scoped figure needs, once.</summary>
    internal static void EnableSection(List<string> preamble)
    {
        ArgumentNullException.ThrowIfNull(preamble);

        if (!preamble.Exists(line => line.Contains("heading(numbering", StringComparison.Ordinal)))
        {
            preamble.Add(HeadingNumberingRule);
        }

        TypstFootnoteNumbering.AddOnce(preamble, SectionRule);
        TypstFootnoteNumbering.AddOnce(preamble, NumberingRule);
    }
}
