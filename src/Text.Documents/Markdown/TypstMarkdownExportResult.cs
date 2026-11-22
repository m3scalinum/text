using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Documents;

/// <summary>The outcome of a Markdown export: the Markdown text and what it could not carry.</summary>
/// <remarks>
/// The shape mirrors <see cref="TypstMarkdownImportResult"/>: the import reports what it dropped
/// because a construct has no model, the export reports what it dropped because a construct has no
/// Markdown. A round trip therefore loses nothing silently.
/// </remarks>
public sealed class TypstMarkdownExportResult
{
    internal TypstMarkdownExportResult(
        string markdown,
        IReadOnlyList<TypstMarkdownDroppedConstruct> unrepresentable)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(unrepresentable);

        Markdown = markdown;
        Unrepresentable = unrepresentable;
    }

    /// <summary>The Markdown text, with <c>\n</c> line endings and one trailing newline.</summary>
    public string Markdown { get; }

    /// <summary>
    /// The constructs that have no Markdown representation and were therefore not written, sorted
    /// by name. Empty when every node of the document has a mapping.
    /// </summary>
    public IReadOnlyList<TypstMarkdownDroppedConstruct> Unrepresentable { get; }

    /// <summary>How many occurrences were not written in total.</summary>
    public int UnrepresentableCount
    {
        get
        {
            var total = 0;

            foreach (var construct in Unrepresentable)
            {
                total += construct.Count;
            }

            return total;
        }
    }

    /// <summary>How many occurrences of one construct were not written; zero when it was not.</summary>
    /// <param name="constructName">The construct name to look for.</param>
    public int CountOf(string constructName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(constructName);

        foreach (var construct in Unrepresentable)
        {
            if (string.Equals(construct.ConstructName, constructName, StringComparison.Ordinal))
            {
                return construct.Count;
            }
        }

        return 0;
    }
}
