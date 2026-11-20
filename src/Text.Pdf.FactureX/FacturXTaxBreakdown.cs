namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>One VAT breakdown entry: a category and rate, with its basis and amount.</summary>
public sealed class FacturXTaxBreakdown
{
    /// <summary>The VAT category code.</summary>
    public string Category { get; init; } = "S";

    /// <summary>The rate, in percent. <see langword="null"/> for a category that has none.</summary>
    public decimal? Rate { get; init; }

    /// <summary>The taxable basis.</summary>
    public decimal Basis { get; init; }

    /// <summary>The tax amount.</summary>
    public decimal Amount { get; init; }

    /// <summary>The exemption reason, required by EN 16931 for a zero-rated category.</summary>
    public string? ExemptionReason { get; init; }
}