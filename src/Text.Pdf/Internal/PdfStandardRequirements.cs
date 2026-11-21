using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace M3scalinum.Text.Pdf;

/// <summary>What a claimed standard requires, as far as this checker establishes it.</summary>
/// <param name="Standard">The claimed standard.</param>
/// <param name="Minimum">The lowest PDF version the standard allows.</param>
/// <param name="Maximum">The highest PDF version the standard allows.</param>
/// <param name="ArchivePart">The XMP <c>pdfaid:part</c> the standard requires, when it is PDF/A.</param>
/// <param name="ArchiveConformance">The XMP <c>pdfaid:conformance</c> required, when the level fixes one.</param>
internal readonly record struct PdfStandardRequirements(
    PdfConformanceStandard Standard,
    PdfVersion Minimum,
    PdfVersion Maximum,
    int? ArchivePart,
    string? ArchiveConformance)
{
    /// <summary>Whether the standard requires tagged structure: PDF/UA-1 and the <c>a</c> levels of PDF/A.</summary>
    internal bool RequiresTaggedStructure
        => Standard is PdfConformanceStandard.PdfUa1 or PdfConformanceStandard.PdfA1a or PdfConformanceStandard.PdfA2a or PdfConformanceStandard.PdfA3a;

    /// <summary>Whether the standard requires an XMP packet declaring it.</summary>
    internal bool RequiresMetadata => ArchivePart is not null || Standard == PdfConformanceStandard.PdfUa1;

    /// <summary>Whether fonts must be embedded with a Unicode mapping.</summary>
    internal bool RequiresFontChecks => ArchivePart is not null || Standard == PdfConformanceStandard.PdfUa1;

    /// <summary>Whether the standard requires a document title.</summary>
    internal bool RequiresTitle => Standard is PdfConformanceStandard.PdfUa1;

    /// <summary>The standard's name, as a reader knows it.</summary>
    internal string Name => Standard switch
    {
        PdfConformanceStandard.Pdf14 => "PDF 1.4",
        PdfConformanceStandard.Pdf15 => "PDF 1.5",
        PdfConformanceStandard.Pdf16 => "PDF 1.6",
        PdfConformanceStandard.Pdf17 => "PDF 1.7",
        PdfConformanceStandard.Pdf20 => "PDF 2.0",
        PdfConformanceStandard.PdfA1b => "PDF/A-1b",
        PdfConformanceStandard.PdfA1a => "PDF/A-1a",
        PdfConformanceStandard.PdfA2b => "PDF/A-2b",
        PdfConformanceStandard.PdfA2u => "PDF/A-2u",
        PdfConformanceStandard.PdfA2a => "PDF/A-2a",
        PdfConformanceStandard.PdfA3b => "PDF/A-3b",
        PdfConformanceStandard.PdfA3u => "PDF/A-3u",
        PdfConformanceStandard.PdfA3a => "PDF/A-3a",
        PdfConformanceStandard.PdfA4 => "PDF/A-4",
        PdfConformanceStandard.PdfA4f => "PDF/A-4f",
        PdfConformanceStandard.PdfA4e => "PDF/A-4e",
        PdfConformanceStandard.PdfUa1 => "PDF/UA-1",
        _ => Standard.ToString(),
    };

    internal static PdfStandardRequirements Of(PdfConformanceStandard standard) => standard switch
    {
        PdfConformanceStandard.Pdf14 => new(standard, new PdfVersion(1, 4), new PdfVersion(1, 4), null, null),
        PdfConformanceStandard.Pdf15 => new(standard, new PdfVersion(1, 5), new PdfVersion(1, 5), null, null),
        PdfConformanceStandard.Pdf16 => new(standard, new PdfVersion(1, 6), new PdfVersion(1, 6), null, null),
        PdfConformanceStandard.Pdf17 => new(standard, new PdfVersion(1, 7), new PdfVersion(1, 7), null, null),
        PdfConformanceStandard.Pdf20 => new(standard, new PdfVersion(2, 0), new PdfVersion(2, 0), null, null),
        PdfConformanceStandard.PdfA1b => new(standard, new PdfVersion(1, 4), new PdfVersion(1, 4), 1, "B"),
        PdfConformanceStandard.PdfA1a => new(standard, new PdfVersion(1, 4), new PdfVersion(1, 4), 1, "A"),
        PdfConformanceStandard.PdfA2b => new(standard, new PdfVersion(1, 7), new PdfVersion(1, 7), 2, "B"),
        PdfConformanceStandard.PdfA2u => new(standard, new PdfVersion(1, 7), new PdfVersion(1, 7), 2, "U"),
        PdfConformanceStandard.PdfA2a => new(standard, new PdfVersion(1, 7), new PdfVersion(1, 7), 2, "A"),
        PdfConformanceStandard.PdfA3b => new(standard, new PdfVersion(1, 7), new PdfVersion(1, 7), 3, "B"),
        PdfConformanceStandard.PdfA3u => new(standard, new PdfVersion(1, 7), new PdfVersion(1, 7), 3, "U"),
        PdfConformanceStandard.PdfA3a => new(standard, new PdfVersion(1, 7), new PdfVersion(1, 7), 3, "A"),
        PdfConformanceStandard.PdfA4 => new(standard, new PdfVersion(2, 0), new PdfVersion(2, 0), 4, null),
        PdfConformanceStandard.PdfA4f => new(standard, new PdfVersion(2, 0), new PdfVersion(2, 0), 4, "F"),
        PdfConformanceStandard.PdfA4e => new(standard, new PdfVersion(2, 0), new PdfVersion(2, 0), 4, "E"),
        PdfConformanceStandard.PdfUa1 => new(standard, new PdfVersion(1, 4), new PdfVersion(1, 7), null, null),
        _ => new(standard, new PdfVersion(1, 0), new PdfVersion(9, 9), null, null),
    };
}
