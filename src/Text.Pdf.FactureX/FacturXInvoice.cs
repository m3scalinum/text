using System;
using System.Collections.Generic;
using M3scalinum.Text.SharpTypst;

namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>An invoice read from a CII or UBL document.</summary>
public sealed class FacturXInvoice
{
    /// <summary>The invoice number, <c>BT-1</c>.</summary>
    public string Number { get; init; } = string.Empty;

    /// <summary>
    /// The document type code, <c>BT-3</c>: <c>380</c> for an invoice, <c>381</c> for a credit note,
    /// <c>384</c> for a corrected invoice. The CII schema places it between the identifier and the
    /// issue date.
    /// </summary>
    public string TypeCode { get; init; } = "380";

    /// <summary>The conformance level declared by the document.</summary>
    public FacturXProfile Profile { get; init; }

    /// <summary>The ISO 4217 currency code, <c>BT-5</c>.</summary>
    public string Currency { get; init; } = "EUR";

    /// <summary>
    /// The currency the VAT is accounted for in when it is not the invoice currency, <c>BT-6</c>.
    /// </summary>
    /// <remarks>
    /// A document that states it carries a second tax total in that currency; the model keeps the
    /// code, not that second total, because the rate is what a receiver applies.
    /// </remarks>
    public string? TaxCurrency { get; init; }

    /// <summary>The issue date, <c>BT-2</c>.</summary>
    public DateOnly IssueDate { get; init; }

    /// <summary>The payment due date, <c>BT-9</c>.</summary>
    public DateOnly? DueDate { get; init; }

    /// <summary>
    /// The period the invoice covers, <c>BG-14</c> — a month of service, a delivery window.
    /// </summary>
    public FacturXPeriod? Period { get; init; }

    /// <summary>The buyer reference, <c>BT-10</c>.</summary>
    public string? BuyerReference { get; init; }

    /// <summary>
    /// The specification identifier, <c>BT-24</c> — <c>cbc:CustomizationID</c> in UBL.
    /// </summary>
    /// <remarks>
    /// In CII it is the Factur-X guideline URN, which is what selects the conformance level; in UBL
    /// it names the profile the document claims, and for a document sent over Peppol it is
    /// <c>urn:cen.eu:en16931:2017#compliant#urn:fdc:peppol.eu:2017:poacc:billing:3.0</c>. Left
    /// empty, the writers fall back to the URN of <see cref="Profile"/>, which is the Factur-X
    /// spelling and not a Peppol one.
    /// </remarks>
    public string? SpecificationId { get; init; }

    /// <summary>
    /// The business process the document belongs to, <c>BT-23</c> — <c>cbc:ProfileID</c> in UBL.
    /// </summary>
    /// <remarks>
    /// The French CIUS calls this the <em>cadre de facturation</em>: <c>B1</c> for a B2B invoice,
    /// <c>S1</c> for a public-sector one, and so on. It decides which of the <c>BR-FR</c> rules
    /// apply, so a document that claims a French framework is held to them.
    /// </remarks>
    public string? BusinessProcess { get; init; }

    /// <summary>The order reference, <c>BT-13</c>.</summary>
    public string? OrderReference { get; init; }

    /// <summary>The contract the invoice is issued under, <c>BT-12</c>.</summary>
    public string? ContractReference { get; init; }

    /// <summary>The project the invoice relates to, <c>BT-11</c>.</summary>
    public string? ProjectReference { get; init; }

    /// <summary>A free-text note, <c>BT-22</c>. The first of <see cref="Notes"/>, for the simple case.</summary>
    public string? Note { get; init; }

    /// <summary>The notes, each with the subject code that qualifies it, <c>BG-1</c>.</summary>
    public IReadOnlyList<FacturXNote> Notes { get; init; } = [];

    /// <summary>The payment terms, <c>BT-20</c>.</summary>
    public string? PaymentTerms { get; init; }

    /// <summary>
    /// The invoice this document corrects, <c>BT-25</c> — the preceding invoice reference of
    /// <c>BG-3</c>, which a credit note carries.
    /// </summary>
    /// <remarks>
    /// A line also has a prior-invoice reference (<see cref="FacturXLine.PriorInvoiceNumber"/>),
    /// which is the cascade one: a detail line of a group line corrects a sub-invoice. This one
    /// places the whole document against the invoice it cancels or corrects.
    /// </remarks>
    public string? PrecedingInvoiceNumber { get; init; }

    /// <summary>The issue date of <see cref="PrecedingInvoiceNumber"/>, <c>BT-26</c>.</summary>
    public DateOnly? PrecedingInvoiceDate { get; init; }

    /// <summary>The payment means code, <c>BT-81</c>: <c>58</c> for a SEPA credit transfer.</summary>
    public string? PaymentMeansCode { get; init; }

    /// <summary>
    /// The remittance information the payer quotes when paying, <c>BT-83</c> — the structured
    /// reference a reconciliation engine matches on.
    /// </summary>
    public string? PaymentReference { get; init; }

    /// <summary>The SEPA direct debit the invoice is collected under, <c>BG-19</c>.</summary>
    public FacturXMandate? Mandate { get; init; }

    /// <summary>Whether this document is a credit note (<c>BT-3</c> = <c>381</c>).</summary>
    public bool IsCreditNote => string.Equals(TypeCode, "381", StringComparison.Ordinal);

    /// <summary>The seller's IBAN, <c>BT-84</c>.</summary>
    public string? Iban { get; init; }

    /// <summary>The seller's BIC, <c>BT-86</c>.</summary>
    public string? Bic { get; init; }

    /// <summary>Who issued the invoice, <c>BT-27</c> and following.</summary>
    public FacturXParty Seller { get; init; } = new();

    /// <summary>Who receives it, <c>BT-44</c> and following.</summary>
    public FacturXParty Buyer { get; init; } = new();

    /// <summary>
    /// Where the goods are delivered when it differs from the buyer's address, <c>BG-13</c>.
    /// </summary>
    /// <remarks>
    /// One of the four mentions the French mandate added: it is required only when the place of
    /// delivery is not the place of invoicing, so it stays empty for a service invoiced at the
    /// customer's address.
    /// </remarks>
    public FacturXParty? DeliveryAddress { get; init; }

    /// <summary>
    /// What is sold — goods, services, or both — one of the four mentions the French mandate added.
    /// </summary>
    /// <remarks>
    /// It has no home in the CII of the profile: the French Schematron never reads it, and the
    /// standard has no such term. It is a legal mention of the document, so the renderer prints it
    /// and the structured data does not carry it. Inventing an element for it would produce a
    /// document no platform expects.
    /// </remarks>
    public FacturXOperationCategory? OperationCategory { get; init; }

    /// <summary>
    /// Whether the seller pays VAT on debits rather than on collections, <c>BT-8</c> style mention.
    /// </summary>
    /// <remarks>Like <see cref="OperationCategory"/>, a rendered mention rather than a CII term.</remarks>
    public bool VatOnDebits { get; init; }

    /// <summary>The invoice lines, <c>BG-25</c>.</summary>
    public IReadOnlyList<FacturXLine> Lines { get; init; } = [];

    /// <summary>The allowances and charges of the document, <c>BG-20</c> and <c>BG-21</c>.</summary>
    public IReadOnlyList<FacturXAllowanceCharge> AllowancesCharges { get; init; } = [];

    /// <summary>The documents the invoice refers to, <c>BG-24</c>.</summary>
    public IReadOnlyList<FacturXSupportingDocument> SupportingDocuments { get; init; } = [];

    /// <summary>The VAT breakdown, <c>BG-23</c>.</summary>
    public IReadOnlyList<FacturXTaxBreakdown> TaxBreakdown { get; init; } = [];

    /// <summary>The document totals, <c>BG-22</c>.</summary>
    public FacturXTotals Totals { get; init; } = new();

    /// <summary>
    /// Renders a copy of this invoice with a different profile. The profile decides the XMP
    /// <c>fx:ConformanceLevel</c>, so it has to match the XML the PDF carries.
    /// </summary>
    public FacturXInvoice WithProfile(FacturXProfile profile) => new()
    {
        Number = Number,
        TypeCode = TypeCode,
        Profile = profile,
        Currency = Currency,
        TaxCurrency = TaxCurrency,
        IssueDate = IssueDate,
        DueDate = DueDate,
        Period = Period,
        BuyerReference = BuyerReference,
        SpecificationId = SpecificationId,
        BusinessProcess = BusinessProcess,
        OrderReference = OrderReference,
        ContractReference = ContractReference,
        ProjectReference = ProjectReference,
        Note = Note,
        Notes = Notes,
        PaymentTerms = PaymentTerms,
        PrecedingInvoiceNumber = PrecedingInvoiceNumber,
        PrecedingInvoiceDate = PrecedingInvoiceDate,
        PaymentMeansCode = PaymentMeansCode,
        PaymentReference = PaymentReference,
        Mandate = Mandate,
        Iban = Iban,
        Bic = Bic,
        Seller = Seller,
        Buyer = Buyer,
        DeliveryAddress = DeliveryAddress,
        OperationCategory = OperationCategory,
        VatOnDebits = VatOnDebits,
        Lines = Lines,
        AllowancesCharges = AllowancesCharges,
        SupportingDocuments = SupportingDocuments,
        TaxBreakdown = TaxBreakdown,
        Totals = Totals,
    };
}
