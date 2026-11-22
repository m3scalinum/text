using System;
using System.Collections.Generic;
using System.Linq;

namespace M3scalinum.Text.Documents;

/// <summary>A citation in the flow, resolved against the document's bibliography.</summary>
/// <remarks>
/// Numeric citations are written <c>[2]</c>; author-date citations <c>(Dupont, 2024)</c>. The node
/// resolves its key when the document is written, so an unknown key raises
/// <see cref="TypstCitationException"/> naming the key and the keys that exist instead of emitting a
/// placeholder.
/// </remarks>
internal sealed class TypstCitationNode(TypstCitationContext context, string key, bool prose) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
    {
        var entry = context.Resolve(key);
        var number = context.NumberOf(key);
        writer.WriteText(TypstBibliography.FormatCitation(entry, number, context.Style, prose));
    }
}

/// <summary>The final bibliography list, built by the model rather than by Typst.</summary>
/// <remarks>
/// The list is emitted as one paragraph per entry. Numeric style numbers the entries in order of
/// first citation, so entry <c>[n]</c> is the one the <c>n</c>th citation names; author-date style
/// sorts the cited entries by author and then year and numbers nothing. A document that cites
/// nothing lists every entry in file order.
/// </remarks>
internal sealed class TypstBibliographyNode(TypstCitationContext context) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
    {
        var bibliography = context.Bibliography
            ?? throw new InvalidOperationException(
                $"a bibliography needs {nameof(TypstBibliography)} entries: call {nameof(TypstDocument.Bibliography)}(…) with one");

        var cited = context.Order;
        TypstBibTexEntry[] entries = cited.Count == 0
            ? bibliography.Entries.ToArray()
            : cited.Select(bibliography.Resolve).ToArray();
        var ordered = context.Style == TypstBibliographyStyle.AuthorDate
            ? SortForAuthorDate(entries)
            : entries;

        var ordinal = 0;

        foreach (var entry in ordered)
        {
            ordinal++;

            writer.BlankLine();
            writer.WriteVerbatim(TypstBibliography.FormatReference(
                entry,
                context.Style == TypstBibliographyStyle.Numeric ? ordinal : null,
                context.Style));
            writer.NewLine();
        }
    }

    private static TypstBibTexEntry[] SortForAuthorDate(IEnumerable<TypstBibTexEntry> entries)
        => entries
            .OrderBy(entry => entry.Authors.Count > 0 ? entry.Authors[0].Family : entry.Key, StringComparer.InvariantCultureIgnoreCase)
            .ThenBy(entry => entry.Year ?? string.Empty, StringComparer.InvariantCultureIgnoreCase)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .ToArray();
}
