namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>The document totals, as the XML states them.</summary>
public sealed class FacturXTotals
{
    /// <summary>The sum of the line net amounts.</summary>
    public decimal LineTotal { get; init; }

    /// <summary>The sum of the taxable bases.</summary>
    public decimal TaxBasisTotal { get; init; }

    /// <summary>The total tax.</summary>
    public decimal TaxTotal { get; init; }

    /// <summary>The grand total, tax included.</summary>
    public decimal GrandTotal { get; init; }

    /// <summary>Any amount already paid.</summary>
    public decimal Prepaid { get; init; }

    /// <summary>The amount still due.</summary>
    public decimal Due { get; init; }
}