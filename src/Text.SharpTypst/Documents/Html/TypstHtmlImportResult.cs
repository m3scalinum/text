using System;
using System.Collections.Generic;

namespace M3scalinum.Text.SharpTypst.Documents;

/// <summary>One element the importer dropped, and how many times it saw it.</summary>
public sealed class TypstHtmlDroppedElement
{
    /// <summary>Creates an entry.</summary>
    /// <param name="elementName">The lower-cased tag name.</param>
    /// <param name="count">How many occurrences were dropped.</param>
    public TypstHtmlDroppedElement(string elementName, int count)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(elementName);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);

        ElementName = elementName;
        Count = count;
    }

    /// <summary>The lower-cased tag name.</summary>
    public string ElementName { get; }

    /// <summary>How many occurrences were dropped.</summary>
    public int Count { get; }
}

/// <summary>The outcome of an HTML import: the document and what it lost.</summary>
public sealed class TypstHtmlImportResult
{
    internal TypstHtmlImportResult(TypstDocument document, IReadOnlyList<TypstHtmlDroppedElement> droppedElements)
    {
        Document = document;
        DroppedElements = droppedElements;
    }

    /// <summary>The imported document, ready for <see cref="TypstDocument.ToMarkup"/> or a compilation.</summary>
    public TypstDocument Document { get; }

    /// <summary>
    /// The elements that were outside the supported subset and therefore not imported, sorted by
    /// name. Empty when the fragment was entirely within the subset.
    /// </summary>
    public IReadOnlyList<TypstHtmlDroppedElement> DroppedElements { get; }

    /// <summary>How many element occurrences were dropped in total.</summary>
    public int DroppedElementCount
    {
        get
        {
            var total = 0;

            foreach (var element in DroppedElements)
            {
                total += element.Count;
            }

            return total;
        }
    }
}
