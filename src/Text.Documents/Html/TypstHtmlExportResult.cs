using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Documents;

/// <summary>
/// The outcome of <see cref="TypstDocument.ToHtml"/>: the HTML and what the document held that HTML
/// cannot express.
/// </summary>
/// <remarks>
/// The HTML is a body fragment, not a full page: it carries the same content as
/// <see cref="TypstDocument.ToMarkup"/>, with no <c>html</c>, <c>head</c> or <c>body</c> wrapper.
/// Line endings are <c>\n</c> and the output is deterministic, so two exports of the same document
/// are byte for byte identical.
/// </remarks>
public sealed class TypstHtmlExportResult
{
    private readonly IReadOnlyList<TypstHtmlDroppedElement> _unrepresentable;

    /// <summary>Creates a result.</summary>
    /// <param name="html">The exported fragment.</param>
    /// <param name="unrepresentable">The nodes that have no HTML equivalent, sorted by name.</param>
    internal TypstHtmlExportResult(string html, IReadOnlyList<TypstHtmlDroppedElement> unrepresentable)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(unrepresentable);

        Html = html;
        _unrepresentable = unrepresentable;
    }

    /// <summary>The HTML5 body fragment, with <c>\n</c> line endings.</summary>
    public string Html { get; }

    /// <summary>
    /// The nodes that have no HTML equivalent, sorted by name, with their number of occurrences.
    /// Empty when the whole document could be written.
    /// </summary>
    /// <remarks>
    /// The entry type is the one the HTML importer uses for the elements it drops
    /// (<see cref="TypstHtmlDroppedElement"/>), so a caller can report losses from either direction
    /// with the same code. A node that cannot be represented is counted once and its subtree is not
    /// visited: nothing of it reaches the HTML.
    /// </remarks>
    public IReadOnlyList<TypstHtmlDroppedElement> Unrepresentable => _unrepresentable;

    /// <summary>How many nodes were not representable in HTML, across all names.</summary>
    public int UnrepresentableCount
    {
        get
        {
            var total = 0;

            foreach (var element in _unrepresentable)
            {
                total += element.Count;
            }

            return total;
        }
    }

    /// <summary>How many nodes of the given name were not representable.</summary>
    /// <param name="elementName">The lower-cased name, for example <c>raw</c> or <c>place</c>.</param>
    /// <returns>The number of occurrences, or zero when the name does not appear.</returns>
    public int CountOf(string elementName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(elementName);

        foreach (var element in _unrepresentable)
        {
            if (string.Equals(element.ElementName, elementName, StringComparison.Ordinal))
            {
                return element.Count;
            }
        }

        return 0;
    }
}
