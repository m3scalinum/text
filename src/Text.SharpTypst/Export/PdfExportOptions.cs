using System;
using System.Collections.Generic;

namespace M3scalinum.Text.SharpTypst.Export;

/// <summary>Options for PDF export.</summary>
/// <remarks>
/// The defaults match Typst's: no standard enforced, every page, tagged output on. A tagged PDF is
/// the baseline for accessibility; turn it off only to shrink the file.
/// </remarks>
public sealed record PdfExportOptions
{
    /// <summary>Standards to enforce, for example <c>[PdfStandard.PdfA2b]</c>.</summary>
    public IReadOnlyList<PdfStandard> Standards { get; init; } = [];

    /// <summary>
    /// One-indexed page ranges to export, for example <c>"1-3,5"</c> or <c>"2-"</c>. An open bound
    /// means "from the start" or "to the end". <see langword="null"/> exports every page.
    /// </summary>
    /// <remarks>
    /// Typst rejects a page range combined with <see cref="Tagged"/>: a tagged PDF describes the
    /// whole document. Set <see cref="Tagged"/> to <see langword="false"/> when you subset pages.
    /// </remarks>
    public string? PageRanges { get; init; }

    /// <summary>Whether to write a tagged PDF. Default <see langword="true"/>.</summary>
    public bool Tagged { get; init; } = true;

    /// <summary>
    /// The creation timestamp written into the PDF. PDF/A requires a document date; set this, or
    /// set <c>#set document(date: …)</c> in the markup, or the export fails.
    /// </summary>
    public DateTimeOffset? Timestamp { get; init; }

    /// <summary>
    /// A stable identifier for the document, used to derive the PDF's document ID. Leave
    /// <see langword="null"/> to derive it from the title and author instead.
    /// </summary>
    public string? Ident { get; init; }

    /// <summary>The <c>/Creator</c> metadata. <see langword="null"/> uses <c>Typst 0.15.1</c>.</summary>
    public string? Creator { get; init; }

    /// <summary>Whether to format the PDF in a human-readable way. Default <see langword="false"/>.</summary>
    public bool Pretty { get; init; }
}
