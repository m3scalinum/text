namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>The document totals, as the XML states them.</summary>
public sealed class FacturXTotals
{
    /// <summary>The sum of the line net amounts, <c>BT-106</c>.</summary>
    public decimal LineTotal { get; init; }

    /// <summary>The sum of the document-level allowances, <c>BT-107</c>.</summary>
    public decimal AllowanceTotal { get; init; }

    /// <summary>The sum of the document-level charges, <c>BT-108</c>.</summary>
    public decimal ChargeTotal { get; init; }

    /// <summary>The sum of the taxable bases, <c>BT-109</c> — the total, excluding tax.</summary>
    public decimal TaxBasisTotal { get; init; }

    /// <summary>The total tax, <c>BT-110</c>.</summary>
    public decimal TaxTotal { get; init; }

    /// <summary>The grand total, tax included, <c>BT-112</c>.</summary>
    public decimal GrandTotal { get; init; }

    /// <summary>Any amount already paid, <c>BT-113</c>.</summary>
    public decimal Prepaid { get; init; }

    /// <summary>The amount still due, <c>BT-115</c>.</summary>
    public decimal Due { get; init; }
}
