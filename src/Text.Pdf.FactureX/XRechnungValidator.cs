using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>
/// The XRechnung CIUS rules that can be decided from the invoice itself.
/// </summary>
/// <remarks>
/// XRechnung is Germany's CIUS of EN 16931: mandatory for an invoice to a public authority and,
/// since 2025, the syntax a German receiver accepts for a B2B invoice. It is published by KoSIT for
/// both UBL and CII, and it is stricter than the standard where a German receiver needs the data:
/// the buyer reference is the Leitweg-ID a public authority routes on, the seller contact is who the
/// receiver calls when an invoice is wrong, and the payment instructions decide whether the amount
/// can be paid at all.
/// <para>
/// It is a subset, not a replacement. The official rule set is published as Schematron — the German
/// business rules <c>BR-DE-nn</c>, the extension rules <c>BR-DEX-nn</c> and the Peppol rules the
/// CIUS incorporated when the two were harmonised — and needs an XSLT 2.0 processor: it is the
/// reference, and <see cref="IInvoiceValidator"/> is where it plugs in. Each rule below names the
/// assertion it comes from in the documentation; the code <c>BR-XR-nn</c> is the library's own, so an
/// issue is traceable to the rule that produced it.
/// </para>
/// <para>
/// The rules run on their own for a caller who asks for them — pass <see cref="Default"/> to
/// <see cref="FacturXValidator.Validate(FacturXInvoice, IInvoiceValidator)"/> — and as part of
/// <see cref="FacturXValidator.Validate(FacturXInvoice)"/> when the document declares XRechnung in
/// <c>BT-24</c>. A document that declares the CIUS is not held to the Peppol or French rules: the
/// three overlays are mutually exclusive, and XRechnung is read first.
/// </para>
/// </remarks>
public sealed partial class XRechnungValidator : IInvoiceValidator
{
    /// <summary>
    /// The specification identifier XRechnung 3.0 expects in <c>BT-24</c>, <c>BR-DE-21</c>.
    /// </summary>
    public const string SpecificationId = "urn:cen.eu:en16931:2017#compliant#urn:xeinkauf.de:kosit:xrechnung_3.0";

    /// <summary>
    /// The identifier of the XRechnung extension, which adds the construction and sub-invoice
    /// terms on top of the CIUS.
    /// </summary>
    public const string ExtensionSpecificationId = SpecificationId + "#conformant#urn:xeinkauf.de:kosit:extension:xrechnung_3.0";

    /// <summary>The identifier of the vehicle-document CIUS XRechnung CVD.</summary>
    public const string VehicleSpecificationId = SpecificationId + "#compliant#urn:xeinkauf.de:kosit:xrechnung:cvd_0.9";

    /// <summary>
    /// The identifier of the XRechnung 4.0 pre-release, which KoSIT published in September 2026 for
    /// EN 16931-1:2026 and which is not yet intended for productive use.
    /// </summary>
    /// <remarks>
    /// The year segment changes with the revision of the European standard — <c>2026</c> where 3.0
    /// says <c>2017</c> — so the two are not variants of one spelling and both are accepted.
    /// </remarks>
    public const string DraftSpecificationId = "urn:cen.eu:en16931:2026#compliant#urn:xeinkauf.de:kosit:xrechnung_4.0";

    /// <summary>
    /// The prefixes every published XRechnung identifier shares: the European standard the CIUS is
    /// conformant to, then the German rule set and the version.
    /// </summary>
    /// <remarks>
    /// The gate reads a prefix rather than the exact string so that the extension and the CVD
    /// variant — which append their own segments — are recognised as XRechnung too, and so that a
    /// version this build has not seen is validated rather than ignored.
    /// </remarks>
    private static readonly string[] SpecificationPrefixes =
    [
        "urn:cen.eu:en16931:2017#compliant#urn:xeinkauf.de:kosit:xrechnung_",
        "urn:cen.eu:en16931:2026#compliant#urn:xeinkauf.de:kosit:xrechnung_",
    ];

    /// <summary>The shared instance: the rule set holds no state.</summary>
    public static XRechnungValidator Default { get; } = new();

    /// <summary>
    /// The specification identifiers <c>BR-DE-21</c> approves, one per published variant.
    /// </summary>
    public static IReadOnlySet<string> SpecificationIds { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        SpecificationId,
        ExtensionSpecificationId,
        VehicleSpecificationId,
        DraftSpecificationId,
    };

    /// <summary>
    /// The invoice type codes <c>BR-DE-17</c> names — the commercial, partial, corrected, self-billed
    /// and construction documents, and the credit note.
    /// </summary>
    /// <remarks>
    /// The source assertion is a <em>warning</em>: the code list of UNTDID 1001 is open, and the CIUS
    /// states which entries a German receiver expects rather than forbidding the others.
    /// </remarks>
    public static IReadOnlySet<string> InvoiceTypeCodes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "326", "380", "381", "384", "389", "875", "876", "877",
    };

    /// <summary>
    /// The VAT categories of <c>BR-DE-16</c>: the ones that require the seller to be identified for
    /// VAT or for tax.
    /// </summary>
    public static IReadOnlySet<string> TaxIdentifiedCategories { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "S", "Z", "E", "AE", "K", "G", "L", "M",
    };

    /// <summary>The payment means codes that state a credit transfer, <c>BR-DE-23</c>.</summary>
    public static IReadOnlySet<string> CreditTransferCodes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "30", "58",
    };

    /// <summary>The payment means codes that state a card payment, <c>BR-DE-24</c>.</summary>
    public static IReadOnlySet<string> CardCodes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "48", "54", "55",
    };

    /// <summary>The payment means code that states a SEPA direct debit, <c>BR-DE-25</c>.</summary>
    public const string DirectDebitCode = "59";

    /// <summary>
    /// Whether the document declares XRechnung, in <c>BT-24</c> or in <c>BT-23</c>.
    /// </summary>
    /// <remarks>
    /// It is the gate <see cref="FacturXValidator.Validate(FacturXInvoice)"/> uses, and it is read
    /// before the Peppol and French ones. <c>BT-24</c> is where the CIUS is declared — KoSIT states
    /// that the identifier is how a document claims conformance. <c>BT-23</c> carries the buyer's
    /// business process, and its documented default under XRechnung is the Peppol billing process,
    /// which is why a Peppol business process alone does not make a document an XRechnung one: an
    /// issuer that declares the CIUS in <c>BT-23</c> writes the CIUS identifier there.
    /// </remarks>
    public static bool IsXRechnung(FacturXInvoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        return IsXRechnungBusinessProcess(invoice.BusinessProcess)
            || IsXRechnungSpecificationId(invoice.SpecificationId);
    }

    /// <summary>Whether a value is a business process identifier that names the CIUS.</summary>
    public static bool IsXRechnungBusinessProcess(string? value) => IsXRechnungSpecificationId(value);

    /// <summary>Whether a value begins with a published XRechnung identifier.</summary>
    public static bool IsXRechnungSpecificationId(string? value)
        => value is { Length: > 0 }
            && SpecificationPrefixes.Any(prefix => value.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>Validates an invoice against the CIUS rules decidable from the document.</summary>
    /// <param name="invoice">The invoice to check.</param>
    /// <returns>The issues, empty when the invoice satisfies every rule below.</returns>
    public IReadOnlyList<FacturXIssue> Validate(FacturXInvoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        var issues = new List<FacturXIssue>();

        CheckIdentifiers(issues, invoice);
        CheckContact(issues, invoice);
        CheckAddresses(issues, invoice);
        CheckPayment(issues, invoice);
        CheckTax(issues, invoice);
        CheckLines(issues, invoice);
        CheckAdjustments(issues, invoice);
        CheckExtension(issues, invoice);

        return issues;
    }

    /// <summary>BT-24, BT-23 and BT-10: what the document says it is, and who it is for.</summary>
    private static void CheckIdentifiers(List<FacturXIssue> issues, FacturXInvoice invoice)
    {
        // BR-DE-21: the identifier is how the document claims conformance to the CIUS.
        if (invoice.SpecificationId is not { Length: > 0 } specification)
        {
            issues.Add(new(
                "BR-XR-01",
                $"the specification identifier (BT-24) is missing; the CIUS expects `{SpecificationId}`"));
        }
        else if (!SpecificationIds.Contains(specification))
        {
            issues.Add(new(
                "BR-XR-02",
                $"`{specification}` is not a specification identifier KoSIT publishes (BT-24); the CIUS is `{SpecificationId}`"));
        }

        // PEPPOL-EN16931-R001, which XRechnung 3.0 made its own: BT-23 is 1..1.
        if (invoice.BusinessProcess is not { Length: > 0 })
        {
            issues.Add(new(
                "BR-XR-03",
                "the business process (BT-23) is missing; a German receiver reads it before anything else"));
        }

        // BR-DE-15: for a public authority this is the Leitweg-ID an invoice is routed by.
        if (string.IsNullOrWhiteSpace(invoice.BuyerReference))
        {
            issues.Add(new("BR-XR-04", "the buyer reference (BT-10) is missing; a public authority routes on it as the Leitweg-ID"));
        }
    }

    /// <summary>BG-6: the seller contact a receiver calls when the invoice is wrong.</summary>
    private static void CheckContact(List<FacturXIssue> issues, FacturXInvoice invoice)
    {
        if (invoice.Seller.Contact is not { } contact)
        {
            issues.Add(new("BR-XR-05", "the seller contact (BG-6) is missing: a name, a telephone number and an e-mail address are required"));
            return;
        }

        // BR-DE-5, BR-DE-6 and BR-DE-7: all three terms are mandatory, not one of them.
        if (string.IsNullOrWhiteSpace(contact.Name))
        {
            issues.Add(new("BR-XR-06", "the seller contact carries no name (BT-41)"));
        }

        if (string.IsNullOrWhiteSpace(contact.Phone))
        {
            issues.Add(new("BR-XR-07", "the seller contact carries no telephone number (BT-42)"));
        }
        else if (contact.Phone.Count(char.IsAsciiDigit) < 3)
        {
            // BR-DE-27: a number nobody can dial.
            issues.Add(new("BR-XR-08", $"the seller contact telephone `{contact.Phone}` carries fewer than three digits (BT-42)"));
        }

        if (string.IsNullOrWhiteSpace(contact.Email))
        {
            issues.Add(new("BR-XR-09", "the seller contact carries no e-mail address (BT-43)"));
        }
        else if (!EmailPattern().IsMatch(contact.Email.Trim()))
        {
            // BR-DE-28.
            issues.Add(new("BR-XR-10", $"the seller contact e-mail `{contact.Email}` is not an address (BT-43)"));
        }
    }

    /// <summary>BG-5, BG-8 and BG-15: the postal terms the CIUS makes mandatory.</summary>
    private static void CheckAddresses(List<FacturXIssue> issues, FacturXInvoice invoice)
    {
        // BR-DE-3 and BR-DE-4.
        if (string.IsNullOrWhiteSpace(invoice.Seller.City))
        {
            issues.Add(new("BR-XR-11", "the seller postal address carries no city (BT-37)"));
        }

        if (string.IsNullOrWhiteSpace(invoice.Seller.Postcode))
        {
            issues.Add(new("BR-XR-12", "the seller postal address carries no post code (BT-38)"));
        }

        // BR-DE-8 and BR-DE-9.
        if (string.IsNullOrWhiteSpace(invoice.Buyer.City))
        {
            issues.Add(new("BR-XR-13", "the buyer postal address carries no city (BT-52)"));
        }

        if (string.IsNullOrWhiteSpace(invoice.Buyer.Postcode))
        {
            issues.Add(new("BR-XR-14", "the buyer postal address carries no post code (BT-53)"));
        }

        // BR-DE-10 and BR-DE-11: the group is optional, but a group that is there is complete.
        if (invoice.DeliveryAddress is { } delivery)
        {
            if (string.IsNullOrWhiteSpace(delivery.City))
            {
                issues.Add(new("BR-XR-15", "the deliver-to address carries no city (BT-77)"));
            }

            if (string.IsNullOrWhiteSpace(delivery.Postcode))
            {
                issues.Add(new("BR-XR-16", "the deliver-to address carries no post code (BT-78)"));
            }
        }

        // PEPPOL-EN16931-R020 and R010, which XRechnung 3.0 adopted: an address to deliver to.
        if (invoice.Seller.ElectronicAddress is not { Length: > 0 })
        {
            issues.Add(new("BR-XR-17", "the seller electronic address (BT-34) is missing"));
        }

        if (invoice.Buyer.ElectronicAddress is not { Length: > 0 })
        {
            issues.Add(new("BR-XR-18", "the buyer electronic address (BT-49) is missing"));
        }
    }

    /// <summary>BG-16, BG-17, BG-19 and BT-20: how the invoice is paid, and what that requires.</summary>
    private static void CheckPayment(List<FacturXIssue> issues, FacturXInvoice invoice)
    {
        // BR-DE-17: the type codes a German receiver books.
        if (!InvoiceTypeCodes.Contains(invoice.TypeCode))
        {
            issues.Add(new(
                "BR-XR-19",
                $"`{invoice.TypeCode}` is not a document type code XRechnung names (BT-3)"));
        }

        // BR-DE-1: without payment instructions nobody knows where the money goes.
        if (invoice.PaymentMeansCode is not { Length: > 0 } means)
        {
            issues.Add(new("BR-XR-20", "the payment instructions (BG-16) are missing: no payment means code (BT-81)"));
            return;
        }

        // BR-DE-18: a Skonto offer is read by machine, so its spelling is fixed.
        CheckSkonto(issues, invoice);

        if (CreditTransferCodes.Contains(means))
        {
            // BR-DE-23-a.
            if (invoice.Iban is not { Length: > 0 })
            {
                issues.Add(new("BR-XR-21", $"payment means `{means}` is a credit transfer and the payment account (BT-84) is missing"));
            }

            // BR-DE-23-b: the two other BG-16 variants cannot be stated at the same time.
            if (invoice.Mandate is not null)
            {
                issues.Add(new("BR-XR-22", $"payment means `{means}` is a credit transfer and the document also states a direct debit mandate (BG-19)"));
            }
        }
        else if (CardCodes.Contains(means))
        {
            // BR-DE-24-b: a card payment names neither an account to transfer to nor one to debit.
            if (invoice.Iban is { Length: > 0 })
            {
                issues.Add(new("BR-XR-23", $"payment means `{means}` is a card payment and the document carries a payment account (BT-84)"));
            }

            if (invoice.Mandate is not null)
            {
                issues.Add(new("BR-XR-24", $"payment means `{means}` is a card payment and the document also states a direct debit mandate (BG-19)"));
            }
        }
        else if (string.Equals(means, DirectDebitCode, StringComparison.Ordinal))
        {
            // BR-DE-25-a: a direct debit is collected under a mandate.
            if (invoice.Mandate is not { } mandate || string.IsNullOrWhiteSpace(mandate.Reference))
            {
                issues.Add(new("BR-XR-25", $"payment means `{means}` is a direct debit and the mandate reference (BT-89) is missing"));
            }

            // BR-DE-25-b.
            if (invoice.Iban is { Length: > 0 })
            {
                issues.Add(new("BR-XR-26", $"payment means `{means}` is a direct debit and the document carries a payment account (BT-84)"));
            }
        }

        // BR-DE-19 and BR-DE-20: the account a payment is made to or from has to be a real IBAN.
        if (invoice.Iban is { Length: > 5 } iban && !IsIban(iban))
        {
            issues.Add(new("BR-XR-27", $"the payment account `{iban}` is not a valid IBAN (BT-84)"));
        }
    }

    /// <summary>BR-DE-18: the <c>SKONTO</c> lines of the payment terms, in the spelling KoSIT fixes.</summary>
    private static void CheckSkonto(List<FacturXIssue> issues, FacturXInvoice invoice)
    {
        if (invoice.PaymentTerms is not { Length: > 0 } terms)
        {
            return;
        }

        foreach (var line in terms.Split('\n'))
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith('#') && !SkontoPattern().IsMatch(trimmed))
            {
                issues.Add(new("BR-XR-28", $"the Skonto line `{trimmed}` is not in the spelling XRechnung fixes: `#SKONTO#TAGE=n#PROZENT=n.nn#` (BT-20)"));
            }
        }
    }

    /// <summary>BG-23 and BT-31: the rate of each breakdown, and the seller the VAT is collected from.</summary>
    private static void CheckTax(List<FacturXIssue> issues, FacturXInvoice invoice)
    {
        // BR-DE-14.
        foreach (var tax in invoice.TaxBreakdown.Where(tax => tax.Rate is null))
        {
            issues.Add(new("BR-XR-29", $"the {tax.Category} breakdown carries no VAT rate (BT-119)"));
        }

        // BR-DE-16: BT-32 and BG-11 are not in the model, so the rule is applied to the term it
        // carries: a document that states a VAT category and no seller VAT identifier at all is one
        // the receiver cannot check the VAT of.
        var categories = invoice.Lines
            .Select(line => line.TaxCategory)
            .Concat(invoice.AllowancesCharges.Select(adjustment => adjustment.VatCategory))
            .Concat(invoice.TaxBreakdown.Select(tax => tax.Category))
            .Where(category => category is { Length: > 0 })
            .ToHashSet(StringComparer.Ordinal);

        if (categories.Overlaps(TaxIdentifiedCategories) && string.IsNullOrWhiteSpace(invoice.Seller.VatId))
        {
            issues.Add(new(
                "BR-XR-30",
                "the document uses a VAT category that requires the seller to be identified, and carries no seller VAT identifier (BT-31)"));
        }
    }

    /// <summary>BG-25 and BG-26: the price base and the period of a line.</summary>
    private static void CheckLines(List<FacturXIssue> issues, FacturXInvoice invoice)
    {
        foreach (var line in invoice.Lines)
        {
            var id = line.Id ?? "(no id)";

            // PEPPOL-EN16931-R121: a base quantity of zero divides the price by nothing.
            if (line.BaseQuantity is { } baseQuantity && baseQuantity <= 0m)
            {
                issues.Add(new("BR-XR-31", $"line {id}: the price base quantity {baseQuantity} is not above zero (BT-149)"));
            }

            // PEPPOL-EN16931-R046: the net price is the gross price less the discount.
            if (line.GrossUnitPrice is { } gross)
            {
                var expected = gross - (line.PriceDiscount ?? 0m);

                if (Math.Abs(expected - line.UnitPrice) > 0.02m)
                {
                    issues.Add(new(
                        "BR-XR-32",
                        $"line {id}: the gross price {gross} less the discount {line.PriceDiscount ?? 0m} is {expected}, but the document states {line.UnitPrice} (BT-146)"));
                }
            }

            // PEPPOL-EN16931-R110 and R111: a line period sits inside the invoice period.
            if (line.Period is { } period && invoice.Period is { } invoicePeriod)
            {
                if (period.Start is { } start && invoicePeriod.Start is { } from && start < from)
                {
                    issues.Add(new("BR-XR-33", $"line {id}: the line period starts {start:yyyy-MM-dd}, before the invoice period starts (BT-134)"));
                }

                if (period.End is { } end && invoicePeriod.End is { } to && end > to)
                {
                    issues.Add(new("BR-XR-34", $"line {id}: the line period ends {end:yyyy-MM-dd}, after the invoice period ends (BT-135)"));
                }
            }
        }
    }

    /// <summary>BG-20, BG-21, BG-27 and BG-28: the base and the percentage of an adjustment.</summary>
    private static void CheckAdjustments(List<FacturXIssue> issues, FacturXInvoice invoice)
    {
        var adjustments = invoice.AllowancesCharges
            .Select(adjustment => (Scope: "the document", Adjustment: adjustment))
            .Concat(invoice.Lines.SelectMany(line => line.AllowancesCharges.Select(adjustment =>
                (Scope: $"line {line.Id ?? "(no id)"}", Adjustment: adjustment))));

        foreach (var (scope, adjustment) in adjustments)
        {
            var kind = adjustment.IsCharge ? "charge" : "allowance";

            // PEPPOL-EN16931-R041 and R042, which XRechnung adopted: the two go together.
            if (adjustment.Percentage is not null && adjustment.BaseAmount is null)
            {
                issues.Add(new("BR-XR-35", $"{scope}: a {kind} of {adjustment.Percentage}% carries no base amount"));
            }

            if (adjustment.BaseAmount is not null && adjustment.Percentage is null)
            {
                issues.Add(new("BR-XR-36", $"{scope}: a {kind} on a base amount of {adjustment.BaseAmount} carries no percentage"));
            }

            // PEPPOL-EN16931-R040.
            if (adjustment.BaseAmount is { } basis && adjustment.Percentage is { } percentage)
            {
                var expected = Math.Round(basis * percentage / 100m, 2, MidpointRounding.AwayFromZero);

                if (Math.Abs(expected - adjustment.Amount) > 0.02m)
                {
                    issues.Add(new(
                        "BR-XR-37",
                        $"{scope}: a {kind} of {percentage}% on {basis} is {expected}, but the document states {adjustment.Amount}"));
                }
            }
        }
    }

    /// <summary>The German terms of the CIUS and its extension that the model states.</summary>
    private static void CheckExtension(List<FacturXIssue> issues, FacturXInvoice invoice)
    {
        // BR-DE-26: a corrected invoice names what it corrects.
        if (string.Equals(invoice.TypeCode, "384", StringComparison.Ordinal)
            && invoice.PrecedingInvoiceNumber is not { Length: > 0 })
        {
            issues.Add(new("BR-XR-38", "a corrected invoice (384) names no preceding invoice (BG-3)"));
        }

        // PEPPOL-EN16931-R005, adopted by XRechnung: two currencies would state the same amount twice.
        if (invoice.TaxCurrency is { Length: > 0 } taxCurrency
            && string.Equals(taxCurrency, invoice.Currency, StringComparison.Ordinal))
        {
            issues.Add(new("BR-XR-39", $"the VAT accounting currency `{taxCurrency}` is the invoice currency (BT-6, BT-5)"));
        }

        // BR-DEX-15: sub invoice lines are a French cascade term, not an XRechnung one — the German
        // Schematron warns on a document that carries them and declares the extension.
        if (string.Equals(invoice.SpecificationId, ExtensionSpecificationId, StringComparison.Ordinal)
            && invoice.Lines.Any(line => line.ParentLineId is { Length: > 0 }))
        {
            issues.Add(new("BR-XR-40", "the document carries sub invoice lines (BT-127), which XRechnung does not support"));
        }
    }

    /// <summary>
    /// Whether a value is an IBAN: the ISO 13616 structure and the mod-97 check digit.
    /// </summary>
    /// <remarks>
    /// The structure alone accepts a transposed digit, which is the mistake that actually happens;
    /// the check digit is what makes the account identifiable.
    /// </remarks>
    public static bool IsIban(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var normalized = new string(value.Where(character => !char.IsWhiteSpace(character)).ToArray()).ToUpperInvariant();

        if (normalized.Length is < 5 or > 34
            || !char.IsAsciiLetter(normalized[0])
            || !char.IsAsciiLetter(normalized[1])
            || !char.IsAsciiDigit(normalized[2])
            || !char.IsAsciiDigit(normalized[3]))
        {
            return false;
        }

        var remainder = 0;

        foreach (var character in string.Concat(normalized.AsSpan(4), normalized.AsSpan(0, 4)))
        {
            if (char.IsAsciiDigit(character))
            {
                remainder = (remainder * 10 + (character - '0')) % 97;
            }
            else if (character is >= 'A' and <= 'Z')
            {
                remainder = (remainder * 100 + (character - 'A' + 10)) % 97;
            }
            else
            {
                return false;
            }
        }

        return remainder == 1;
    }

    /// <summary>The <c>SKONTO</c> grammar of <c>BR-DE-18</c>, without its trailing lowercase form.</summary>
    [GeneratedRegex("^#SKONTO#TAGE=[0-9]+#PROZENT=[0-9]+\\.[0-9]{2}(#BASISBETRAG=-?[0-9]+\\.[0-9]{2})?#$")]
    private static partial Regex SkontoPattern();

    /// <summary>The e-mail shape of <c>BR-DE-28</c>.</summary>
    [GeneratedRegex("^[^@\\s]+@([^@.\\s]+\\.)+[^@.\\s]+$")]
    private static partial Regex EmailPattern();
}
