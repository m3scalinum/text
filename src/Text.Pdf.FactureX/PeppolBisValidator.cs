using System;
using System.Collections.Generic;
using System.Linq;
using M3scalinum.Text.Typst;

namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>
/// The Peppol BIS Billing 3.0 CIUS rules that can be decided from the invoice itself.
/// </summary>
/// <remarks>
/// A Peppol access point rejects a document on the CIUS, not on the UBL schema: an invoice can
/// validate against the OASIS XSD and still be refused because the identifiers a receiver routes on
/// are missing. This rule set covers the part of the CIUS that is stated by the document — the
/// profile and specification identifiers, the electronic addresses and their schemes, the document
/// type codes, the credit-note reference, the payment and VAT cardinalities, the seller identifier
/// and the allowance and charge arithmetic.
/// <para>
/// It is a subset, not a replacement. The official rule set is published as Schematron — around
/// 165 assertions in the current release — and needs an XSLT 2.0 processor: it is the reference,
/// and <see cref="IInvoiceValidator"/> is where it plugs in. The rules below keep the codes of
/// their source assertion in the documentation, and the code <c>BR-PEPPOL-nn</c> is the library's
/// own, so an issue is traceable to the rule that produced it.
/// </para>
/// <para>
/// The rules run on their own for a caller who asks for them — pass <see cref="Default"/> to
/// <see cref="FacturXValidator.Validate(FacturXInvoice, IInvoiceValidator)"/> — and as part of
/// <see cref="FacturXValidator.Validate(FacturXInvoice)"/> when the document declares the CIUS, in
/// <c>BT-23</c> or <c>BT-24</c>.
/// </para>
/// </remarks>
public sealed class PeppolBisValidator : IInvoiceValidator
{
    /// <summary>
    /// The business process identifier a Peppol BIS Billing 3.0 document carries in <c>BT-23</c>,
    /// <c>PEPPOL-EN16931-R001</c>.
    /// </summary>
    public const string BusinessProcess = "urn:fdc:peppol.eu:2017:poacc:billing:01:1.0";

    /// <summary>
    /// The specification identifier the CIUS expects in <c>BT-24</c>,
    /// <c>PEPPOL-EN16931-R004</c>.
    /// </summary>
    public const string SpecificationId = "urn:cen.eu:en16931:2017#compliant#urn:fdc:peppol.eu:2017:poacc:billing:3.0";

    /// <summary>The shared instance: the rule set holds no state.</summary>
    public static PeppolBisValidator Default { get; } = new();

    /// <summary>
    /// The business process identifiers the CIUS accepts, <c>PEPPOL-EN16931-R007</c>.
    /// </summary>
    /// <remarks>
    /// The first is the one every access point knows; the two French ones and the
    /// <c>billing_with_response</c> profile are published in the same rule.
    /// </remarks>
    public static IReadOnlySet<string> ApprovedBusinessProcesses { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        BusinessProcess,
        "urn:peppol:france:billing:regulated",
        "urn:peppol:france:billing:non-regulated",
        "urn:peppol:bis:billing_with_response",
    };

    /// <summary>The invoice type codes the CIUS accepts, <c>PEPPOL-EN16931-P0100</c>.</summary>
    public static IReadOnlySet<string> InvoiceTypeCodes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "71", "80", "82", "84", "102", "218", "219", "326", "331", "380", "382", "383", "384", "386",
        "388", "393", "395", "553", "575", "623", "780", "817", "870", "875", "876", "877",
    };

    /// <summary>The credit note type codes the CIUS accepts, <c>PEPPOL-EN16931-P0101</c>.</summary>
    public static IReadOnlySet<string> CreditNoteTypeCodes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "381", "396", "81", "83", "532",
    };

    /// <summary>
    /// The electronic address scheme identifiers of the Peppol EAS code list,
    /// <c>PEPPOL-EN16931-CL008</c>.
    /// </summary>
    /// <remarks>
    /// Taken from the published Schematron, which carries the list inline; it is closed, so a code
    /// outside it is rejected rather than shape-checked. An access point routes on this code, and a
    /// scheme it does not know is a document it cannot deliver.
    /// </remarks>
    public static IReadOnlySet<string> ElectronicAddressSchemes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "0002", "0007", "0009", "0060", "0088", "0096", "0097", "0106", "0130", "0135", "0142",
        "0151", "0158", "0183", "0184", "0188", "0190", "0191", "0192", "0195", "0196", "0198",
        "0199", "0200", "0201", "0204", "0208", "0209", "0210", "0211", "0216", "0218", "0221",
        "0225", "0230", "0235", "0240", "0242", "0244", "0245", "0246", "0248", "9910", "9913",
        "9914", "9915", "9918", "9919", "9920", "9922", "9923", "9924", "9925", "9926", "9927",
        "9928", "9929", "9930", "9931", "9932", "9933", "9934", "9935", "9936", "9937", "9938",
        "9939", "9940", "9941", "9942", "9943", "9944", "9945", "9946", "9947", "9948", "9949",
        "9950", "9951", "9952", "9953", "9957", "9959",
    };

    /// <summary>
    /// Whether the document declares the Peppol CIUS, in <c>BT-23</c> or <c>BT-24</c>.
    /// </summary>
    /// <remarks>
    /// It is the gate <see cref="FacturXValidator.Validate(FacturXInvoice)"/> uses: a document falls
    /// under the CIUS because its issuer says so, the same way the French rules are gated on
    /// <c>BT-23</c>.
    /// </remarks>
    public static bool IsPeppol(FacturXInvoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        return IsPeppolBusinessProcess(invoice.BusinessProcess) || IsPeppolSpecificationId(invoice.SpecificationId);
    }

    /// <summary>Whether a value is a business process identifier of the CIUS.</summary>
    public static bool IsPeppolBusinessProcess(string? value)
        => value is { Length: > 0 } && ApprovedBusinessProcesses.Contains(value);

    /// <summary>Whether a value is the specification identifier of the CIUS.</summary>
    public static bool IsPeppolSpecificationId(string? value)
        => value is { Length: > 0 } && value.StartsWith(SpecificationId, StringComparison.Ordinal);

    /// <summary>Validates an invoice against the CIUS rules decidable from the document.</summary>
    /// <param name="invoice">The invoice to check.</param>
    /// <returns>The issues, empty when the invoice satisfies every rule below.</returns>
    public IReadOnlyList<FacturXIssue> Validate(FacturXInvoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        var issues = new List<FacturXIssue>();

        CheckIdentifiers(issues, invoice);
        CheckElectronicAddresses(issues, invoice);
        CheckReferences(issues, invoice);
        CheckTypeCodes(issues, invoice);
        CheckCreditNote(issues, invoice);
        CheckPayment(issues, invoice);
        CheckVat(issues, invoice);
        CheckSellerIdentifier(issues, invoice);
        CheckAllowances(issues, invoice);

        return issues;
    }

    /// <summary>BT-23 and BT-24: the two identifiers that put the document under the CIUS.</summary>
    private static void CheckIdentifiers(List<FacturXIssue> issues, FacturXInvoice invoice)
    {
        // PEPPOL-EN16931-R001: the business process is what an access point reads first.
        if (invoice.BusinessProcess is not { Length: > 0 })
        {
            issues.Add(new(
                "BR-PEPPOL-01",
                $"the business process (BT-23) is missing; a Peppol document states `{BusinessProcess}`"));
        }
        else if (!ApprovedBusinessProcesses.Contains(invoice.BusinessProcess))
        {
            // PEPPOL-EN16931-R007.
            issues.Add(new(
                "BR-PEPPOL-02",
                $"`{invoice.BusinessProcess}` is not a business process identifier the CIUS approves (BT-23)"));
        }

        // PEPPOL-EN16931-R004: the specification identifier names the CIUS and its version.
        if (invoice.SpecificationId is not { Length: > 0 } specification)
        {
            issues.Add(new("BR-PEPPOL-03", $"the specification identifier (BT-24) is missing; the CIUS expects `{SpecificationId}`"));
        }
        else if (!specification.StartsWith(SpecificationId, StringComparison.Ordinal))
        {
            issues.Add(new("BR-PEPPOL-03", $"`{specification}` does not begin with `{SpecificationId}` (BT-24)"));
        }
        else if (specification.Contains("::", StringComparison.Ordinal))
        {
            issues.Add(new("BR-PEPPOL-03", $"the specification identifier `{specification}` carries a `::` suffix the CIUS does not define (BT-24)"));
        }
    }

    /// <summary>BT-34 / BT-49 and their schemes: the addresses a receiver is routed by.</summary>
    private static void CheckElectronicAddresses(List<FacturXIssue> issues, FacturXInvoice invoice)
    {
        // PEPPOL-EN16931-R020 and R010.
        if (invoice.Seller.ElectronicAddress is not { Length: > 0 })
        {
            issues.Add(new("BR-PEPPOL-04", "the seller electronic address (BT-34) is missing; an access point has nowhere to deliver the document"));
        }

        if (invoice.Buyer.ElectronicAddress is not { Length: > 0 })
        {
            issues.Add(new("BR-PEPPOL-05", "the buyer electronic address (BT-49) is missing; the document is not addressed to anyone"));
        }

        // PEPPOL-EN16931-CL008.
        foreach (var (role, party) in new[] { ("seller", invoice.Seller), ("buyer", invoice.Buyer) })
        {
            if (party.ElectronicAddress is not { Length: > 0 } address)
            {
                continue;
            }

            if (party.ElectronicAddressScheme is not { Length: > 0 } scheme)
            {
                issues.Add(new("BR-PEPPOL-06", $"the {role} electronic address `{address}` carries no scheme identifier (BT-34-1 / BT-49-1)"));
            }
            else if (!ElectronicAddressSchemes.Contains(scheme))
            {
                issues.Add(new("BR-PEPPOL-06", $"`{scheme}` is not an electronic address scheme of the Peppol EAS code list; the {role} address `{address}` cannot be routed"));
            }
        }
    }

    /// <summary>BT-10 / BT-13 and the note count: the references a receiver books against.</summary>
    private static void CheckReferences(List<FacturXIssue> issues, FacturXInvoice invoice)
    {
        // PEPPOL-EN16931-R003: without one of the two, the buyer cannot match the invoice.
        if (string.IsNullOrWhiteSpace(invoice.BuyerReference) && string.IsNullOrWhiteSpace(invoice.OrderReference))
        {
            issues.Add(new("BR-PEPPOL-07", "neither the buyer reference (BT-10) nor the order reference (BT-13) is present"));
        }

        // PEPPOL-EN16931-R002: the CIUS allows one note, and only a German pair may carry more.
        var notes = invoice.Notes.Count > 0
            ? invoice.Notes.Count
            : invoice.Note is { Length: > 0 } ? 1 : 0;

        if (notes > 1 && !(IsGerman(invoice.Seller) && IsGerman(invoice.Buyer)))
        {
            issues.Add(new("BR-PEPPOL-08", $"the document carries {notes} notes; the CIUS allows one unless both parties are German"));
        }
    }

    /// <summary>BT-3: the document type codes the CIUS accepts.</summary>
    private static void CheckTypeCodes(List<FacturXIssue> issues, FacturXInvoice invoice)
    {
        // A credit note is written as a CreditNote root, and the writer picks that root on 381,
        // which the credit note list carries: there is nothing left to reject on that side.
        if (!invoice.IsCreditNote)
        {
            // PEPPOL-EN16931-P0101, read in the direction the document decides: a credit note code
            // on a document that is not one is written as an invoice carrying it, which no
            // receiver books.
            if (CreditNoteTypeCodes.Contains(invoice.TypeCode))
            {
                issues.Add(new(
                    "BR-PEPPOL-09",
                    $"`{invoice.TypeCode}` is a credit note type code and the document is not a credit note (BT-3)"));
            }
            else if (!InvoiceTypeCodes.Contains(invoice.TypeCode))
            {
                // PEPPOL-EN16931-P0100.
                issues.Add(new("BR-PEPPOL-10", $"`{invoice.TypeCode}` is not an invoice type code the CIUS accepts (BT-3)"));
            }
        }

        // PEPPOL-EN16931-P0112: the three codes that only a German pair, or a Slovak correction,
        // may carry.
        if (invoice.TypeCode is "326" or "384" or "389")
        {
            var german = IsGerman(invoice.Seller) && IsGerman(invoice.Buyer);
            var slovak = invoice.TypeCode == "384" && IsSlovak(invoice.Seller) && IsSlovak(invoice.Buyer);

            if (!german && !slovak)
            {
                issues.Add(new(
                    "BR-PEPPOL-11",
                    $"type code `{invoice.TypeCode}` is reserved to a German seller and buyer, or to a Slovak correction (BT-3)"));
            }
        }
    }

    /// <summary>BG-3: a credit note names the invoice it corrects.</summary>
    private static void CheckCreditNote(List<FacturXIssue> issues, FacturXInvoice invoice)
    {
        if (invoice.TypeCode is not ("381" or "384"))
        {
            return;
        }

        // The published CIUS restricts this to Germany (DE-R-026, for 384) and the Netherlands
        // (NL-R-001, for 381). Both are applied: a correction that names no invoice cannot be
        // matched by any receiver, and the national rules state the same thing one country at a
        // time.
        if (invoice.PrecedingInvoiceNumber is not { Length: > 0 })
        {
            issues.Add(new("BR-PEPPOL-12", $"a {invoice.TypeCode} document names no preceding invoice (BT-25, BG-3)"));
        }
    }

    /// <summary>BG-16 / BG-19: one payment means, and the terms it needs.</summary>
    private static void CheckPayment(List<FacturXIssue> issues, FacturXInvoice invoice)
    {
        // PEPPOL-EN16931-R061: a direct debit without a mandate reference cannot be collected.
        if (invoice.PaymentMeansCode is "49" or "59" && invoice.Mandate?.Reference is not { Length: > 0 })
        {
            issues.Add(new(
                "BR-PEPPOL-13",
                $"payment means `{invoice.PaymentMeansCode}` is a direct debit and the mandate reference (BT-89) is missing"));
        }

        // BR-61 of EN 16931, which the CIUS enforces as a whole: a credit transfer needs an account.
        if (invoice.PaymentMeansCode is "30" or "58" && invoice.Iban is not { Length: > 0 })
        {
            issues.Add(new(
                "BR-PEPPOL-14",
                $"payment means `{invoice.PaymentMeansCode}` is a credit transfer and the payment account (BT-84) is missing"));
        }
    }

    /// <summary>BG-23: the VAT categories a line uses and the identifiers they require.</summary>
    private static void CheckVat(List<FacturXIssue> issues, FacturXInvoice invoice)
    {
        var categories = invoice.Lines
            .Select(line => line.TaxCategory)
            .Where(category => category is { Length: > 0 })
            .ToHashSet(StringComparer.Ordinal);

        // BR-AE-02: under reverse charge the buyer accounts for the VAT, so the document has to
        // identify it.
        if (categories.Contains("AE") && invoice.Buyer.VatId is not { Length: > 0 })
        {
            issues.Add(new("BR-PEPPOL-15", "a reverse-charge line needs the buyer's VAT identifier (BT-48)"));
        }

        // BR-IC-02.
        if (categories.Contains("K") && invoice.Buyer.VatId is not { Length: > 0 })
        {
            issues.Add(new("BR-PEPPOL-16", "an intra-community supply line needs the buyer's VAT identifier (BT-48)"));
        }

        // BR-CO-04: every category and rate a line carries is in the breakdown, or the VAT cannot
        // be reconciled.
        foreach (var group in invoice.Lines
            .Where(line => line.TaxCategory is { Length: > 0 })
            .GroupBy(line => (Category: line.TaxCategory!, Rate: line.TaxRate ?? 0m)))
        {
            var covered = invoice.TaxBreakdown.Any(tax =>
                string.Equals(tax.Category, group.Key.Category, StringComparison.Ordinal)
                && (tax.Rate ?? 0m) == group.Key.Rate);

            if (!covered)
            {
                issues.Add(new(
                    "BR-PEPPOL-17",
                    $"the lines carry {group.Key.Category} at {group.Key.Rate}%, which the VAT breakdown (BG-23) does not"));
            }
        }

        // BR-E-08 and BR-Z-08 at line level: a category that has no rate does not carry one.
        foreach (var line in invoice.Lines)
        {
            if (line.TaxCategory is "Z" or "E" or "AE" or "K" or "G" or "O" && line.TaxRate is > 0m)
            {
                issues.Add(new(
                    "BR-PEPPOL-22",
                    $"line {line.Id ?? "(no id)"}: the {line.TaxCategory} category carries a {line.TaxRate}% rate"));
            }
        }
    }

    /// <summary>BR-CO-26: a receiver has to be able to identify the seller.</summary>
    private static void CheckSellerIdentifier(List<FacturXIssue> issues, FacturXInvoice invoice)
    {
        if (invoice.Seller.PartyId is not { Length: > 0 }
            && invoice.Seller.LegalId is not { Length: > 0 }
            && invoice.Seller.VatId is not { Length: > 0 })
        {
            issues.Add(new("BR-PEPPOL-18", "the seller carries none of BT-29, BT-30 and BT-31; a receiver cannot identify it"));
        }
    }

    /// <summary>BG-20 / BG-21 and BG-27 / BG-28: the base, the percentage and the amount.</summary>
    private static void CheckAllowances(List<FacturXIssue> issues, FacturXInvoice invoice)
    {
        var adjustments = invoice.AllowancesCharges
            .Select(adjustment => (Scope: "the document", Adjustment: adjustment))
            .Concat(invoice.Lines.SelectMany(line => line.AllowancesCharges.Select(adjustment =>
                (Scope: $"line {line.Id ?? "(no id)"}", Adjustment: adjustment))));

        foreach (var (scope, adjustment) in adjustments)
        {
            var kind = adjustment.IsCharge ? "charge" : "allowance";

            // PEPPOL-EN16931-R041 and R042: the two go together, in both directions.
            if (adjustment.Percentage is not null && adjustment.BaseAmount is null)
            {
                issues.Add(new("BR-PEPPOL-19", $"{scope}: a {kind} of {adjustment.Percentage}% carries no base amount"));
            }

            if (adjustment.BaseAmount is not null && adjustment.Percentage is null)
            {
                issues.Add(new("BR-PEPPOL-20", $"{scope}: a {kind} on a base amount of {adjustment.BaseAmount} carries no percentage"));
            }

            // PEPPOL-EN16931-R040.
            if (adjustment.BaseAmount is { } basis && adjustment.Percentage is { } percentage)
            {
                var expected = Math.Round(basis * percentage / 100m, 2, MidpointRounding.AwayFromZero);

                if (Math.Abs(expected - adjustment.Amount) > 0.02m)
                {
                    issues.Add(new(
                        "BR-PEPPOL-21",
                        $"{scope}: a {kind} of {percentage}% on {basis} is {expected}, but the document states {adjustment.Amount}"));
                }
            }
        }
    }

    private static bool IsGerman(FacturXParty party) => string.Equals(party.Country, "DE", StringComparison.Ordinal);

    private static bool IsSlovak(FacturXParty party) => string.Equals(party.Country, "SK", StringComparison.Ordinal);
}
