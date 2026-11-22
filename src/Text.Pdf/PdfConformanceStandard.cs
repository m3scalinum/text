namespace M3scalinum.Text.Pdf;

/// <summary>A PDF standard <see cref="PdfConformance"/> can check a document against.</summary>
/// <remarks>
/// <para>
/// The values mirror the standards the compiler can enforce, so a caller reads the same names on both
/// sides. The two enums are deliberately separate: this package has no reference on the compiler, and
/// the compiler has none on this package. The name differs from
/// <c>M3scalinum.Text.Typst.Export.PdfStandard</c> because the suite forbids two public types
/// with the same name in different namespaces — a caller importing both would get CS0104.
/// <c>PdfConformanceStandard.PdfUa1</c> here is <c>PdfStandard.PdfUa1</c> there.
/// </para>
/// <para>
/// The members are exactly the levels <c>typst-pdf 0.15.1</c> enforces: <c>PdfStandard</c> declares
/// <c>V_1_4</c> to <c>V_2_0</c>, the PDF/A flavours through <c>A_4e</c>, and one accessibility
/// variant, <c>Ua_1</c>. <see cref="Pdf20"/> is reachable — <c>PdfStandard::V_2_0</c> writes a
/// <c>%PDF-2.0</c> header — while there is no PDF/UA-2 member because the embedded compiler has no
/// code path that produces it: <c>krilla 0.8.2</c> exposes a single accessibility validator,
/// <c>Accessibility::UA1</c>, whose maximum version is PDF 1.7. A member the export rejects would be a
/// value that cannot work. <c>docs/specifications/15-pdf-conformance.md</c> §8 states the level-by-level
/// limit, and <c>PdfUa2Tests</c> fails when a member is added without a producer.
/// </para>
/// </remarks>
public enum PdfConformanceStandard
{
    /// <summary>PDF 1.4.</summary>
    Pdf14,

    /// <summary>PDF 1.5.</summary>
    Pdf15,

    /// <summary>PDF 1.6.</summary>
    Pdf16,

    /// <summary>PDF 1.7.</summary>
    Pdf17,

    /// <summary>PDF 2.0 (ISO 32000-2).</summary>
    Pdf20,

    /// <summary>PDF/A-1b: basic archival conformance, PDF 1.4.</summary>
    PdfA1b,

    /// <summary>PDF/A-1a: archival conformance with tagged content, PDF 1.4.</summary>
    PdfA1a,

    /// <summary>PDF/A-2b: basic archival conformance, PDF 1.7.</summary>
    PdfA2b,

    /// <summary>PDF/A-2u: archival conformance with Unicode mapping, PDF 1.7.</summary>
    PdfA2u,

    /// <summary>PDF/A-2a: archival conformance with tagged content, PDF 1.7.</summary>
    PdfA2a,

    /// <summary>PDF/A-3b: archival conformance with embedded files, PDF 1.7.</summary>
    PdfA3b,

    /// <summary>PDF/A-3u: archival conformance with Unicode mapping and embedded files, PDF 1.7.</summary>
    PdfA3u,

    /// <summary>PDF/A-3a: archival conformance with tagged content and embedded files, PDF 1.7.</summary>
    PdfA3a,

    /// <summary>PDF/A-4: archival conformance for PDF 2.0.</summary>
    PdfA4,

    /// <summary>PDF/A-4f: PDF/A-4 with embedded files.</summary>
    PdfA4f,

    /// <summary>PDF/A-4e: PDF/A-4 with embedded files and 3D content.</summary>
    PdfA4e,

    /// <summary>PDF/UA-1: accessibility conformance, ISO 14289-1, PDF 1.4 to 1.7.</summary>
    /// <remarks>
    /// The upper bound is the standard's, not a choice: ISO 14289-1 is built on ISO 32000-1, and the
    /// embedded <c>krilla 0.8.2</c> fixes the same range for <c>Accessibility::UA1</c>. A PDF 2.0 file
    /// cannot claim it, and the compiler cannot write UA-1 over a 2.0 header.
    /// </remarks>
    PdfUa1,
}
