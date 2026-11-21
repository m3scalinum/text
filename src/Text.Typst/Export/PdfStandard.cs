namespace M3scalinum.Text.Typst.Export;

/// <summary>A PDF standard the exporter can enforce.</summary>
/// <remarks>
/// The names mirror the values Typst accepts, so a manifest and this enum read the same way.
/// Several standards can be combined when they are compatible; the exporter rejects an
/// incompatible combination rather than silently picking one.
/// </remarks>
public enum PdfStandard
{
    /// <summary>PDF 1.4.</summary>
    Pdf14,

    /// <summary>PDF 1.5.</summary>
    Pdf15,

    /// <summary>PDF 1.6.</summary>
    Pdf16,

    /// <summary>PDF 1.7.</summary>
    Pdf17,

    /// <summary>PDF 2.0.</summary>
    Pdf20,

    /// <summary>PDF/A-1b: basic archival conformance.</summary>
    PdfA1b,

    /// <summary>PDF/A-1a: archival conformance with tagged content.</summary>
    PdfA1a,

    /// <summary>PDF/A-2b: basic archival conformance, PDF 1.7.</summary>
    PdfA2b,

    /// <summary>PDF/A-2u: archival conformance with Unicode mapping.</summary>
    PdfA2u,

    /// <summary>PDF/A-2a: archival conformance with tagged content.</summary>
    PdfA2a,

    /// <summary>PDF/A-3b: archival conformance, embedded files allowed.</summary>
    PdfA3b,

    /// <summary>PDF/A-3u: archival conformance with Unicode mapping and embedded files.</summary>
    PdfA3u,

    /// <summary>PDF/A-3a: archival conformance with tagged content and embedded files.</summary>
    PdfA3a,

    /// <summary>PDF/A-4: archival conformance for PDF 2.0.</summary>
    PdfA4,

    /// <summary>PDF/A-4f: PDF/A-4 with embedded files.</summary>
    PdfA4f,

    /// <summary>PDF/A-4e: PDF/A-4 with embedded files and 3D content.</summary>
    PdfA4e,

    /// <summary>PDF/UA-1: accessibility conformance.</summary>
    PdfUa1,
}
