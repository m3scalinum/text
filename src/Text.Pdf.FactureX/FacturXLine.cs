using System;

namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>One invoice line.</summary>
public sealed class FacturXLine
{
    /// <summary>The line identifier, as printed.</summary>
    public string? Id { get; init; }

    /// <summary>The product or service name.</summary>
    public string? Name { get; init; }

    /// <summary>The seller's own article number.</summary>
    public string? SellerAssignedId { get; init; }

    /// <summary>Billed quantity.</summary>
    public decimal Quantity { get; init; }

    /// <summary>UN/ECE Recommendation 20 unit code, for example <c>C62</c> for a piece.</summary>
    public string? UnitCode { get; init; }

    /// <summary>The unit price, excluding tax.</summary>
    public decimal UnitPrice { get; init; }

    /// <summary>The line net amount, excluding tax.</summary>
    public decimal LineNetAmount { get; init; }

    /// <summary>The VAT category code: <c>S</c>, <c>Z</c>, <c>E</c>, <c>AE</c>, <c>O</c>…</summary>
    public string? TaxCategory { get; init; }

    /// <summary>The VAT rate, in percent.</summary>
    public decimal? TaxRate { get; init; }

    /// <summary>
    /// Whether the line is a group or a detail line, <c>BT-127</c> — <c>GROUP</c> or <c>DETAIL</c>.
    /// </summary>
    /// <remarks>
    /// Cascade invoicing: a <c>GROUP</c> line stands for a sub-invoice issued by another seller, and
    /// the detail lines below it carry that seller's own lines. The French rules only require it
    /// under the <c>S8</c>, <c>B8</c>, <c>M8</c>, <c>S9</c>, <c>B9</c> and <c>M9</c> frameworks.
    /// </remarks>
    public string? StatusReason { get; init; }

    /// <summary>The line this one belongs to — the <c>GROUP</c> line's id.</summary>
    public string? ParentLineId { get; init; }

    /// <summary>The seller of this line, when it is not the invoice's seller, <c>BG-29</c>.</summary>
    public FacturXParty? LineSeller { get; init; }

    /// <summary>The invoice this line corrects, <c>BT-25</c>.</summary>
    public string? PriorInvoiceNumber { get; init; }

    /// <summary>The issue date of <see cref="PriorInvoiceNumber"/>, <c>BT-26</c>.</summary>
    public DateOnly? PriorInvoiceDate { get; init; }

    /// <summary>The VAT amount of the line.</summary>
    public decimal? TaxTotal { get; init; }

    /// <summary>The object the line bills, <c>BT-128</c> — an <c>AFL</c> or <c>AVV</c> reference.</summary>
    public string? ObjectIdentifier { get; init; }

    /// <summary>The scheme of <see cref="ObjectIdentifier"/>, <c>BT-128-1</c>.</summary>
    public string? ObjectIdentifierScheme { get; init; }
}