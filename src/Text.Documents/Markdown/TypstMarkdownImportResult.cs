using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Documents;

/// <summary>One construct the importer could not represent, and how many times it saw it.</summary>
public sealed class TypstMarkdownDroppedConstruct
{
    /// <summary>Creates an entry.</summary>
    /// <param name="constructName">The name of the construct, as listed in the fidelity contract.</param>
    /// <param name="count">How many occurrences were dropped.</param>
    public TypstMarkdownDroppedConstruct(string constructName, int count)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(constructName);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);

        ConstructName = constructName;
        Count = count;
    }

    /// <summary>The name of the construct.</summary>
    public string ConstructName { get; }

    /// <summary>How many occurrences were dropped.</summary>
    public int Count { get; }
}

/// <summary>The outcome of a Markdown import: the document and what it lost.</summary>
public sealed class TypstMarkdownImportResult
{
    internal TypstMarkdownImportResult(
        TypstDocument document,
        IReadOnlyList<TypstMarkdownDroppedConstruct> droppedConstructs)
    {
        Document = document;
        DroppedConstructs = droppedConstructs;
    }

    /// <summary>The imported document, ready for <see cref="TypstDocument.ToMarkup"/> or a compilation.</summary>
    public TypstDocument Document { get; }

    /// <summary>
    /// The constructs that had no representation in the document model and were therefore not
    /// imported, sorted by name. Empty when the source was entirely within the subset.
    /// </summary>
    public IReadOnlyList<TypstMarkdownDroppedConstruct> DroppedConstructs { get; }

    /// <summary>How many occurrences were dropped in total.</summary>
    public int DroppedConstructCount
    {
        get
        {
            var total = 0;

            foreach (var construct in DroppedConstructs)
            {
                total += construct.Count;
            }

            return total;
        }
    }

    /// <summary>How many occurrences of one construct were dropped; zero when it was not.</summary>
    /// <param name="constructName">The construct name to look for.</param>
    public int CountOf(string constructName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(constructName);

        foreach (var construct in DroppedConstructs)
        {
            if (string.Equals(construct.ConstructName, constructName, StringComparison.Ordinal))
            {
                return construct.Count;
            }
        }

        return 0;
    }
}
