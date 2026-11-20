using M3scalinum.Text.SharpTypst;

namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>
/// The Factur-X conformance level. The value is written verbatim into the XMP metadata, where the
/// standard spells it exactly like this.
/// </summary>
public enum FacturXProfile
{
    /// <summary>MINIMUM — the header only, no lines. Accepted for non-domestic invoices.</summary>
    Minimum,

    /// <summary>BASIC WL — header and VAT breakdown, without line items.</summary>
    BasicWithoutLines,

    /// <summary>BASIC — header, lines and VAT breakdown.</summary>
    Basic,

    /// <summary>EN 16931 (COMFORT) — the full European semantic model. The usual choice in France.</summary>
    En16931,

    /// <summary>EXTENDED — everything EN 16931 carries, plus the German extensions.</summary>
    Extended,

    /// <summary>
    /// EXTENDED-CTC-FR — the French variant of EXTENDED, carrying the lifecycle data a French
    /// platform reports to the tax administration.
    /// </summary>
    /// <remarks>
    /// It is a subset of EXTENDED, so it declares the same XMP conformance level; only the
    /// guideline URN tells the two apart.
    /// </remarks>
    ExtendedCtcFr,
}