using System.Collections.Generic;
using M3scalinum.Text.SharpTypst.Internal;

namespace M3scalinum.Text.SharpTypst.Compilation;

/// <summary>
/// What a compiled document says about itself: its outline, its labels and its metadata entries.
/// </summary>
/// <remarks>
/// This is the host-side counterpart of Typst's introspection. It is read from the laid-out
/// document, so page numbers are final and cross-references are already resolved.
/// </remarks>
public sealed class TypstEvaluation
{
    internal TypstEvaluation(EvaluationDto dto)
    {
        PageCount = dto.PageCount;

        var outline = new List<TypstOutlineEntry>(dto.Outline?.Count ?? 0);
        foreach (var entry in dto.Outline ?? [])
        {
            outline.Add(new TypstOutlineEntry(
                entry.Level,
                entry.Title ?? string.Empty,
                entry.Page,
                entry.X,
                entry.Y,
                entry.Label));
        }

        var labels = new List<TypstLabelEntry>(dto.Labels?.Count ?? 0);
        foreach (var entry in dto.Labels ?? [])
        {
            labels.Add(new TypstLabelEntry(entry.Name ?? string.Empty, entry.Page, entry.X, entry.Y));
        }

        var metadata = new List<TypstMetadataEntry>(dto.Metadata?.Count ?? 0);
        foreach (var entry in dto.Metadata ?? [])
        {
            metadata.Add(new TypstMetadataEntry(entry.Value, entry.Page));
        }

        Outline = outline;
        Labels = labels;
        Metadata = metadata;
    }

    /// <summary>Number of pages in the document.</summary>
    public int PageCount { get; }

    /// <summary>Every heading, in document order.</summary>
    public IReadOnlyList<TypstOutlineEntry> Outline { get; }

    /// <summary>Every labelled element, in document order.</summary>
    public IReadOnlyList<TypstLabelEntry> Labels { get; }

    /// <summary>Every <c>#metadata(...)</c> entry, in document order.</summary>
    public IReadOnlyList<TypstMetadataEntry> Metadata { get; }
}