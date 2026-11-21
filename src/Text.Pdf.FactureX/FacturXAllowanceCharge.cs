namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>
/// An allowance or a charge, at document level (<c>BG-20</c> / <c>BG-21</c>) or at line level
/// (<c>BG-27</c> / <c>BG-28</c>).
/// </summary>
/// <remarks>
/// The same shape carries both directions: <see cref="IsCharge"/> says which, because the standard
/// states a charge as a positive amount that is added and an allowance as a positive amount that is
/// subtracted. The two are never written with opposite signs, which is why the flag exists rather
/// than a signed amount.
/// </remarks>
public sealed class FacturXAllowanceCharge
{
    /// <summary>
    /// Whether this is a charge (<c>BG-21</c> / <c>BG-28</c>) rather than an allowance
    /// (<c>BG-20</c> / <c>BG-27</c>).
    /// </summary>
    public bool IsCharge { get; init; }

    /// <summary>
    /// The amount, excluding tax: <c>BT-92</c> / <c>BT-99</c> at document level,
    /// <c>BT-136</c> / <c>BT-141</c> at line level. Always positive.
    /// </summary>
    public decimal Amount { get; init; }

    /// <summary>The amount the allowance or charge applies to, <c>BT-93</c> / <c>BT-100</c>.</summary>
    public decimal? BaseAmount { get; init; }

    /// <summary>The percentage the allowance or charge represents, <c>BT-94</c> / <c>BT-101</c>.</summary>
    public decimal? Percentage { get; init; }

    /// <summary>The reason, as free text, <c>BT-97</c> / <c>BT-104</c>.</summary>
    public string? Reason { get; init; }

    /// <summary>
    /// The reason as a code of UNTDID 5189, <c>BT-98</c> / <c>BT-105</c>.
    /// </summary>
    /// <remarks>
    /// A document-level allowance or charge carries either a reason or a reason code — the standard
    /// accepts both, and rejects neither one being present (<c>BR-CO-21</c>).
    /// </remarks>
    public string? ReasonCode { get; init; }

    /// <summary>
    /// The VAT category the document-level allowance or charge is assigned to,
    /// <c>BT-95</c> / <c>BT-102</c>.
    /// </summary>
    /// <remarks>
    /// It is what lets the VAT breakdown include the adjustment in the right category. A line-level
    /// allowance or charge inherits the category from its line and leaves this empty.
    /// </remarks>
    public string? VatCategory { get; init; }

    /// <summary>The VAT rate of the category, <c>BT-96</c> / <c>BT-103</c>.</summary>
    public decimal? VatRate { get; init; }
}
