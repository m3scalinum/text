using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using M3scalinum.Text.Typst;

namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>Reads the Spanish Facturae 3.2.2 syntax into the shared invoice model.</summary>
/// <remarks>
/// Facturae is a national schema aligned with EN 16931, not a CIUS: it states the same invoice in
/// its own structure, so it is read here rather than validated elsewhere. The document is
/// <c>Facturae</c> — a file header, the two parties, then one or more invoices — and this reader
/// takes the first invoice, which is what every document that is not a batch carries.
/// </remarks>
public static class FacturaeReader
{
    /// <summary>Reads an invoice from its XML text.</summary>
    /// <exception cref="FacturXFormatException">The document is not Facturae, or lacks a required part.</exception>
    public static FacturXInvoice Read(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        return Read(Xml.Parse(xml));
    }

    /// <summary>Reads an already parsed <c>Facturae</c> document.</summary>
    /// <exception cref="FacturXFormatException">The document lacks a required part.</exception>
    internal static FacturXInvoice Read(XElement root)
    {
        var invoice = root.Child("Invoices")?.Child("Invoice")
            ?? throw new FacturXFormatException("Facturae document has no Invoices/Invoice");

        var header = invoice.Child("InvoiceHeader")
            ?? throw new FacturXFormatException("Facturae document has no InvoiceHeader");

        var issue = invoice.Child("InvoiceIssueData")
            ?? throw new FacturXFormatException("Facturae document has no InvoiceIssueData");

        var parties = root.Child("Parties");
        var lines = invoice.Child("Items")?.Children("InvoiceLine").Select(Line).ToList() ?? [];
        var taxes = invoice.Children("TaxesOutputs").SelectMany(output => output.Children("Tax")).Select(Tax).ToList();
        var totals = invoice.Child("InvoiceTotals");
        var installments = invoice.Child("PaymentDetails")?.Children("Installment").ToList() ?? [];
        var corrective = header.Child("Corrective");

        var invoiceTotal = totals?.Number("InvoiceTotal") ?? taxes.Sum(tax => tax.Amount) + taxes.Sum(tax => tax.Basis);
        var outstanding = totals?.Amount("TotalOutstandingAmount") ?? invoiceTotal;

        // The two totals are optional, and a producer that omits them still states the discounts
        // and the surcharges: the sum of what is there is the total, as the schema requires.
        var adjustments = Adjustments(totals);

        return new FacturXInvoice
        {
            Number = header.Text("InvoiceNumber") ?? string.Empty,
            TypeCode = TypeCode(header, invoiceTotal),
            Profile = FacturXProfile.En16931,
            Currency = issue.Text("InvoiceCurrencyCode") ?? "EUR",
            TaxCurrency = issue.Text("TaxCurrencyCode"),
            IssueDate = issue.Date("IssueDate") ?? default,
            DueDate = installments.Select(installment => installment.Date("InstallmentDueDate")).FirstOrDefault(date => date is not null),
            Period = Period(issue.Child("InvoicingPeriod")),
            Note = Additional(invoice),
            Notes = TextNotes(invoice),
            PrecedingInvoiceNumber = corrective?.Text("InvoiceNumber"),
            PaymentMeansCode = installments
                .Select(installment => FacturaePaymentMeans.TypeCode(installment.Text("PaymentMeans")))
                .FirstOrDefault(code => code is not null),
            PaymentReference = installments
                .Select(installment => installment.Text("PaymentReconciliationReference"))
                .FirstOrDefault(reference => reference is not null),
            Iban = installments
                .Select(installment => installment.Child("AccountToBeCredited")?.Text("IBAN"))
                .FirstOrDefault(iban => iban is not null),
            Bic = installments
                .Select(installment => installment.Child("AccountToBeCredited")?.Text("BIC"))
                .FirstOrDefault(bic => bic is not null),
            Seller = Party(parties?.Child("SellerParty")),
            Buyer = Party(parties?.Child("BuyerParty")),
            Lines = lines,
            AllowancesCharges = adjustments,
            TaxBreakdown = taxes,
            Totals = new FacturXTotals
            {
                LineTotal = totals?.Amount("TotalGrossAmount") ?? lines.Sum(line => line.LineNetAmount),
                AllowanceTotal = totals?.Amount("TotalGeneralDiscounts")
                    ?? adjustments.Where(adjustment => !adjustment.IsCharge).Sum(adjustment => adjustment.Amount),
                ChargeTotal = totals?.Amount("TotalGeneralSurcharges")
                    ?? adjustments.Where(adjustment => adjustment.IsCharge).Sum(adjustment => adjustment.Amount),
                TaxBasisTotal = totals?.Number("TotalGrossAmountBeforeTaxes") ?? taxes.Sum(tax => tax.Basis),
                TaxTotal = totals?.Number("TotalTaxOutputs") ?? taxes.Sum(tax => tax.Amount),
                GrandTotal = invoiceTotal,
                Prepaid = invoiceTotal > outstanding ? invoiceTotal - outstanding : 0m,
                Due = outstanding,
            },
        };
    }

    /// <summary>
    /// The document type. Facturae states the kind of document and, separately, the class: a
    /// rectificative invoice with a negative total is what every other syntax calls a credit note.
    /// </summary>
    private static string TypeCode(XElement header, decimal invoiceTotal)
    {
        var invoiceClass = header.Text("InvoiceClass");

        if (invoiceClass is "OR" or "CR")
        {
            return invoiceTotal < 0m ? "381" : "384";
        }

        return header.Text("InvoiceDocumentType") is "AF" ? "389" : "380";
    }

    private static FacturXParty Party(XElement? party)
    {
        if (party is null)
        {
            return new FacturXParty();
        }

        var identification = party.Child("TaxIdentification");
        var legal = party.Child("LegalEntity");
        var address = legal?.Child("AddressInSpain");
        var overseas = legal?.Child("OverseasAddress");
        var contact = legal?.Child("ContactDetails");

        var defined = contact is null
            ? null
            : new FacturXContact
            {
                Phone = contact.Text("Telephone"),
                Email = contact.Text("ElectronicMail"),
            };

        var country = Domestic(address) ?? Abroad(overseas);
        var number = identification?.Text("TaxIdentificationNumber");

        return new FacturXParty
        {
            Name = legal?.Text("CorporateName") ?? legal?.Text("TradeName"),
            // A Spanish NIF is stated without its country; the model states a VAT identifier with
            // one, so the country the address gives is what makes the two spellings agree.
            VatId = number is { Length: > 0 } && country is { Length: 2 } code
                ? (number.StartsWith(code, StringComparison.OrdinalIgnoreCase) ? number : code + number)
                : number,
            Contact = defined,
            Line1 = address?.Text("Address") ?? overseas?.Text("Address"),
            Postcode = address?.Text("PostCode"),
            City = address?.Text("Town") ?? overseas?.Text("PostCodeAndTown"),
            Country = country,
            Email = defined?.Email,
        };
    }

    /// <summary>The country of an <c>AddressInSpain</c>, whose code is ISO 3166-1 alpha-3.</summary>
    private static string? Domestic(XElement? address)
    {
        var code = address?.Text("CountryCode");

        if (code is null)
        {
            return null;
        }

        return string.Equals(code, "ESP", StringComparison.OrdinalIgnoreCase) ? "ES" : code;
    }

    /// <summary>The country of an <c>OverseasAddress</c>, whose code is ISO 3166-1 alpha-2.</summary>
    private static string? Abroad(XElement? address) => address?.Text("CountryCode");

    private static FacturXLine Line(XElement line)
    {
        var adjustments = new List<FacturXAllowanceCharge>();
        adjustments.AddRange(Adjustments(line.Child("DiscountsAndRebates"), isCharge: false));
        adjustments.AddRange(Adjustments(line.Child("Charges"), isCharge: true));

        // A line may state its own tax; when it does not, the breakdown is the only place the rate
        // appears, and the line keeps no category rather than claiming the standard one.
        var tax = line.Child("TaxesOutputs")?.Child("Tax");
        var rate = tax?.Amount("TaxRate");

        return new FacturXLine
        {
            Name = line.Text("ItemDescription"),
            Quantity = line.Number("Quantity"),
            UnitCode = line.Text("UnitOfMeasure"),
            UnitPrice = line.Number("UnitPriceWithoutTax"),
            LineNetAmount = line.Number("GrossAmount"),
            AllowancesCharges = adjustments,
            TaxCategory = rate is null ? null : rate > 0m ? "S" : "E",
            TaxRate = rate,
        };
    }

    /// <summary>The document-level adjustments, which Facturae states as discounts and surcharges.</summary>
    private static List<FacturXAllowanceCharge> Adjustments(XElement? totals)
    {
        var adjustments = new List<FacturXAllowanceCharge>();
        adjustments.AddRange(Adjustments(totals?.Child("GeneralDiscounts"), isCharge: false));
        adjustments.AddRange(Adjustments(totals?.Child("GeneralSurcharges"), isCharge: true));
        return adjustments;
    }

    private static List<FacturXAllowanceCharge> Adjustments(XElement? container, bool isCharge)
    {
        if (container is null)
        {
            return [];
        }

        return container.Elements()
            .Select(element => new FacturXAllowanceCharge
            {
                IsCharge = isCharge,
                Amount = isCharge ? element.Number("ChargeAmount") : element.Number("DiscountAmount"),
                Percentage = isCharge ? element.Amount("ChargeRate") : element.Amount("DiscountRate"),
                Reason = isCharge ? element.Text("ChargeReason") : element.Text("DiscountReason"),
            })
            .ToList();
    }

    /// <summary>
    /// A <c>Tax</c>: <c>01</c> is VAT, and a zero rate is what EN 16931 calls an exempt category —
    /// Facturae has no separate category code.
    /// </summary>
    private static FacturXTaxBreakdown Tax(XElement tax)
    {
        var rate = tax.Amount("TaxRate");

        return new FacturXTaxBreakdown
        {
            Category = rate is > 0m ? "S" : "E",
            Rate = rate,
            Basis = tax.Child("TaxableBase")?.Number("TotalAmount") ?? 0m,
            Amount = tax.Child("TaxAmount")?.Number("TotalAmount") ?? 0m,
        };
    }

    private static FacturXPeriod? Period(XElement? period)
    {
        if (period is null)
        {
            return null;
        }

        var start = period.Date("StartDate");
        var end = period.Date("EndDate");

        return start is null && end is null ? null : new FacturXPeriod { Start = start, End = end };
    }

    /// <summary>
    /// The free text about the invoice. Facturae states it in <c>AdditionalData</c>, after the
    /// payment details; <c>InvoiceIssueData</c> carries none.
    /// </summary>
    private static string? Additional(XElement invoice)
        => invoice.Child("AdditionalData")?.Text("InvoiceAdditionalInformation");

    private static IReadOnlyList<FacturXNote> TextNotes(XElement invoice)
        => Additional(invoice) is { Length: > 0 } description
            ? [new FacturXNote { Content = description }]
            : [];
}
