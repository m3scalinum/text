using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using M3scalinum.Text.Typst;

namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>Writes an invoice as a Spanish Facturae 3.2.2 document.</summary>
/// <remarks>
/// <para>
/// Facturae is a national schema aligned with EN 16931, not a CIUS: it states the same invoice in
/// its own structure, so producing one is a mapping, not a rule overlay. The element order is the
/// sequence the official XSD fixes — <c>FileHeader</c>, <c>Parties</c>, <c>Invoices</c>, and inside
/// an invoice <c>InvoiceHeader</c>, <c>InvoiceIssueData</c>, <c>TaxesOutputs</c>,
/// <c>InvoiceTotals</c>, <c>Items</c>, <c>PaymentDetails</c>.
/// </para>
/// <para>
/// Facturae has no credit-note class: a correction is a rectificative invoice (<c>InvoiceClass</c>
/// <c>OR</c>) carrying negative amounts and naming the invoice it corrects. A model credit note
/// (<c>TypeCode</c> <c>381</c>) is written that way and reads back as <c>384</c>, a corrected
/// invoice — which is the same document under the name Spanish receivers use.
/// </para>
/// </remarks>
public static class FacturaeWriter
{
    private static readonly XNamespace Fe = "http://www.facturae.gob.es/formato/Versiones/Facturaev3_2_2.xml";

    /// <summary>Serializes an invoice to Facturae XML text.</summary>
    public static string Write(FacturXInvoice invoice)
        => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(WriteBytes(invoice));

    /// <summary>Serializes an invoice to Facturae XML bytes.</summary>
    /// <remarks>
    /// Written to a stream for the same reason as <see cref="CiiWriter.WriteBytes"/>: a
    /// <see cref="StringBuilder"/> makes the declaration disagree with the bytes.
    /// </remarks>
    public static byte[] WriteBytes(FacturXInvoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            NewLineChars = "\n",
        };

        using var stream = new MemoryStream();

        using (var writer = XmlWriter.Create(stream, settings))
        {
            Build(invoice).Save(writer);
        }

        return stream.ToArray();
    }

    private static XDocument Build(FacturXInvoice invoice) => new(
        new XDeclaration("1.0", "UTF-8", null),
        new XElement(
            Fe + "Facturae",
            new XAttribute(XNamespace.Xmlns + "fe", Fe),
            FileHeader(invoice),
            new XElement(Fe + "Parties",
                Party("SellerParty", invoice.Seller),
                Party("BuyerParty", invoice.Buyer)),
            new XElement(Fe + "Invoices", Invoice(invoice))));

    private static XElement FileHeader(FacturXInvoice invoice)
    {
        var (_, sellerNumber) = Identification(invoice.Seller);

        return new XElement(
            Fe + "FileHeader",
            new XElement(Fe + "SchemaVersion", "3.2.2"),
            new XElement(Fe + "Modality", "I"),
            new XElement(Fe + "InvoiceIssuerType", "EM"),
            new XElement(
                Fe + "Batch",
                new XElement(Fe + "BatchIdentifier", $"{sellerNumber}-{invoice.Number}"),
                new XElement(Fe + "InvoicesCount", "1"),
                Amounts("TotalInvoicesAmount", invoice.Totals.GrandTotal),
                Amounts("TotalOutstandingAmount", invoice.Totals.Due),
                Amounts("TotalExecutableAmount", invoice.Totals.Due),
                new XElement(Fe + "InvoiceCurrencyCode", invoice.Currency)));
    }

    private static XElement Amounts(string name, decimal value)
        => new(Fe + name, new XElement(Fe + "TotalAmount", Xml.Money(value)));

    private static XElement Invoice(FacturXInvoice invoice)
    {
        var corrected = invoice.TypeCode is "381" or "384";

        var header = new XElement(
            Fe + "InvoiceHeader",
            new XElement(Fe + "InvoiceNumber", invoice.Number),
            new XElement(Fe + "InvoiceDocumentType", "FC"),
            new XElement(Fe + "InvoiceClass", corrected ? "OR" : "OO"));

        if (corrected || invoice.PrecedingInvoiceNumber is { Length: > 0 })
        {
            header.Add(Corrective(invoice));
        }

        var element = new XElement(
            Fe + "Invoice",
            header,
            IssueData(invoice),
            new XElement(
                Fe + "TaxesOutputs",
                VatBreakdown.Of(invoice).Select(Tax)));

        element.Add(Totals(invoice));
        element.Add(Items(invoice));

        if (Payment(invoice) is { } payment)
        {
            element.Add(payment);
        }

        // A note has no term in the issue data; AdditionalData is where Facturae states free text
        // about the invoice, and it follows PaymentDetails in the sequence.
        if (invoice.Note is { Length: > 0 } note)
        {
            element.Add(new XElement(Fe + "AdditionalData",
                new XElement(Fe + "InvoiceAdditionalInformation", note)));
        }

        return element;
    }

    /// <summary>
    /// The invoice a rectificative one corrects. Facturae makes the reason and the method codes
    /// mandatory: <c>01</c> is a correction, and the method <c>01</c> is a full replacement, which
    /// is what a document that restates the whole invoice is.
    /// </summary>
    private static XElement Corrective(FacturXInvoice invoice)
    {
        var start = invoice.Period?.Start ?? invoice.IssueDate;
        var end = invoice.Period?.End ?? invoice.IssueDate;

        return new XElement(Fe + "Corrective",
            new XElement(Fe + "InvoiceNumber", invoice.PrecedingInvoiceNumber ?? invoice.Number),
            // The two lists are enumerations, so the pair has to be one of them: 10 is the detail of
            // the operation, the only reason that is not about one specific field of the invoice.
            new XElement(Fe + "ReasonCode", "10"),
            new XElement(Fe + "ReasonDescription", "Detalle Operación"),
            new XElement(Fe + "TaxPeriod",
                new XElement(Fe + "StartDate", Xml.IsoDate(start)),
                new XElement(Fe + "EndDate", Xml.IsoDate(end))),
            new XElement(Fe + "CorrectionMethod", "01"),
            new XElement(Fe + "CorrectionMethodDescription", "Rectificación íntegra"));
    }

    private static XElement IssueData(FacturXInvoice invoice)
    {
        var issue = new XElement(
            Fe + "InvoiceIssueData",
            new XElement(Fe + "IssueDate", Xml.IsoDate(invoice.IssueDate)));

        if (invoice.Period is { } period)
        {
            issue.Add(new XElement(Fe + "InvoicingPeriod",
                period.Start is { } start ? new XElement(Fe + "StartDate", Xml.IsoDate(start)) : null,
                period.End is { } end ? new XElement(Fe + "EndDate", Xml.IsoDate(end)) : null));
        }

        issue.Add(new XElement(Fe + "InvoiceCurrencyCode", invoice.Currency));

        // Facturae requires an exchange rate whenever the tax currency is not the invoice currency,
        // and the model carries the code but not the rate: the invoice currency is stated for both
        // rather than a rate invented.
        issue.Add(new XElement(Fe + "TaxCurrencyCode", invoice.Currency));
        issue.Add(new XElement(Fe + "LanguageName", "es"));
        return issue;
    }

    /// <summary>A <c>Tax</c>: <c>01</c> is VAT, with its rate, its base and its amount.</summary>
    private static XElement Tax(FacturXTaxBreakdown tax)
    {
        // Facturae states the taxable base and the amount, and has no term for an exemption
        // reason: the reason a French or Italian document carries has nowhere to go.
        return new XElement(
            Fe + "Tax",
            new XElement(Fe + "TaxTypeCode", "01"),
            new XElement(Fe + "TaxRate", Xml.Money(tax.Rate ?? 0m)),
            new XElement(Fe + "TaxableBase", new XElement(Fe + "TotalAmount", Xml.Money(tax.Basis))),
            new XElement(Fe + "TaxAmount", new XElement(Fe + "TotalAmount", Xml.Money(tax.Amount))));
    }

    private static XElement Totals(FacturXInvoice invoice)
    {
        var totals = invoice.Totals;
        var element = new XElement(
            Fe + "InvoiceTotals",
            new XElement(Fe + "TotalGrossAmount", Xml.Money(totals.LineTotal)));

        var allowances = invoice.AllowancesCharges.Where(adjustment => !adjustment.IsCharge).ToList();
        var charges = invoice.AllowancesCharges.Where(adjustment => adjustment.IsCharge).ToList();

        if (allowances.Count > 0)
        {
            element.Add(new XElement(Fe + "GeneralDiscounts", allowances.Select(adjustment => new XElement(
                Fe + "Discount",
                new XElement(Fe + "DiscountReason", adjustment.Reason ?? "Descuento"),
                adjustment.Percentage is { } percentage ? new XElement(Fe + "DiscountRate", Xml.Money(percentage)) : null,
                new XElement(Fe + "DiscountAmount", Xml.Money(adjustment.Amount))))));
        }

        if (charges.Count > 0)
        {
            element.Add(new XElement(Fe + "GeneralSurcharges", charges.Select(adjustment => new XElement(
                Fe + "Surcharge",
                new XElement(Fe + "ChargeReason", adjustment.Reason ?? "Recargo"),
                adjustment.Percentage is { } percentage ? new XElement(Fe + "ChargeRate", Xml.Money(percentage)) : null,
                new XElement(Fe + "ChargeAmount", Xml.Money(adjustment.Amount))))));
        }

        if (totals.AllowanceTotal != 0m)
        {
            element.Add(new XElement(Fe + "TotalGeneralDiscounts", Xml.Money(totals.AllowanceTotal)));
        }

        if (totals.ChargeTotal != 0m)
        {
            element.Add(new XElement(Fe + "TotalGeneralSurcharges", Xml.Money(totals.ChargeTotal)));
        }

        element.Add(new XElement(Fe + "TotalGrossAmountBeforeTaxes", Xml.Money(totals.TaxBasisTotal)));
        element.Add(new XElement(Fe + "TotalTaxOutputs", Xml.Money(totals.TaxTotal)));
        element.Add(new XElement(Fe + "TotalTaxesWithheld", Xml.Money(0m)));
        element.Add(new XElement(Fe + "InvoiceTotal", Xml.Money(totals.GrandTotal)));
        element.Add(new XElement(Fe + "TotalOutstandingAmount", Xml.Money(totals.Due)));
        element.Add(new XElement(Fe + "TotalExecutableAmount", Xml.Money(totals.Due)));
        return element;
    }

    private static XElement Items(FacturXInvoice invoice) => new(
        Fe + "Items",
        invoice.Lines.Select(Line));

    private static XElement Line(FacturXLine line)
    {
        var element = new XElement(
            Fe + "InvoiceLine",
            new XElement(Fe + "ItemDescription", line.Name ?? line.Id ?? string.Empty),
            new XElement(Fe + "Quantity", Xml.Quantity(line.Quantity)));

        // Facturae's unit list is numeric and two digits; a UN/ECE code such as C62 has no
        // counterpart in it, so the unit is stated only when it already is one of those codes.
        if (line.UnitCode is { Length: 2 } unit && unit.All(char.IsDigit))
        {
            element.Add(new XElement(Fe + "UnitOfMeasure", unit));
        }

        element.Add(new XElement(Fe + "UnitPriceWithoutTax", Xml.Money(line.UnitPrice)));
        element.Add(new XElement(Fe + "TotalCost", Xml.Money(line.Quantity * line.UnitPrice)));

        var allowances = line.AllowancesCharges.Where(adjustment => !adjustment.IsCharge).ToList();
        var charges = line.AllowancesCharges.Where(adjustment => adjustment.IsCharge).ToList();

        if (allowances.Count > 0)
        {
            element.Add(new XElement(Fe + "DiscountsAndRebates", allowances.Select(adjustment => new XElement(
                Fe + "Discount",
                new XElement(Fe + "DiscountReason", adjustment.Reason ?? "Descuento"),
                adjustment.Percentage is { } percentage ? new XElement(Fe + "DiscountRate", Xml.Money(percentage)) : null,
                new XElement(Fe + "DiscountAmount", Xml.Money(adjustment.Amount))))));
        }

        if (charges.Count > 0)
        {
            element.Add(new XElement(Fe + "Charges", charges.Select(adjustment => new XElement(
                Fe + "Charge",
                new XElement(Fe + "ChargeReason", adjustment.Reason ?? "Recargo"),
                adjustment.Percentage is { } percentage ? new XElement(Fe + "ChargeRate", Xml.Money(percentage)) : null,
                new XElement(Fe + "ChargeAmount", Xml.Money(adjustment.Amount))))));
        }

        element.Add(new XElement(Fe + "GrossAmount", Xml.Money(line.LineNetAmount)));

        if (line.TaxRate is { } rate)
        {
            element.Add(new XElement(Fe + "TaxesOutputs", new XElement(
                Fe + "Tax",
                new XElement(Fe + "TaxTypeCode", "01"),
                new XElement(Fe + "TaxRate", Xml.Money(rate)),
                new XElement(Fe + "TaxableBase", new XElement(Fe + "TotalAmount", Xml.Money(line.LineNetAmount))),
                new XElement(Fe + "TaxAmount",
                    new XElement(Fe + "TotalAmount", Xml.Money(Math.Round(line.LineNetAmount * rate / 100m, 2, MidpointRounding.AwayFromZero)))))));
        }

        return element;
    }

    private static XElement? Payment(FacturXInvoice invoice)
    {
        if (invoice.DueDate is null
            && invoice.Iban is not { Length: > 0 }
            && invoice.PaymentReference is not { Length: > 0 }
            && invoice.PaymentMeansCode is not { Length: > 0 }
            && invoice.Totals.Due == 0m)
        {
            return null;
        }

        var installment = new XElement(
            Fe + "Installment",
            new XElement(Fe + "InstallmentDueDate", Xml.IsoDate(invoice.DueDate ?? invoice.IssueDate)),
            new XElement(Fe + "InstallmentAmount", Xml.Money(invoice.Totals.Due != 0m ? invoice.Totals.Due : invoice.Totals.GrandTotal)),
            new XElement(Fe + "PaymentMeans", FacturaePaymentMeans.Means(invoice.PaymentMeansCode)));

        if (invoice.Iban is { Length: > 0 } iban)
        {
            installment.Add(new XElement(Fe + "AccountToBeCredited",
                new XElement(Fe + "IBAN", iban),
                invoice.Bic is { Length: > 0 } bic ? new XElement(Fe + "BIC", bic) : null));
        }

        // InstallmentType sequences the account before the reconciliation reference.
        if (invoice.PaymentReference is { Length: > 0 } reference)
        {
            installment.Add(new XElement(Fe + "PaymentReconciliationReference", reference));
        }

        return new XElement(Fe + "PaymentDetails", installment);
    }

    /// <summary>
    /// A party. A Spanish address is an <c>AddressInSpain</c> with an ISO 3166-1 alpha-3 country
    /// code; anything else is an <c>OverseasAddress</c> with the alpha-2 code, which is what the
    /// schema splits on.
    /// </summary>
    private static XElement Party(string elementName, FacturXParty party)
    {
        var (country, number) = Identification(party);

        var legal = new XElement(
            Fe + "LegalEntity",
            new XElement(Fe + "CorporateName", party.Name ?? party.LegalId ?? string.Empty));

        if (string.Equals(country, "ES", StringComparison.OrdinalIgnoreCase))
        {
            legal.Add(new XElement(
                Fe + "AddressInSpain",
                new XElement(Fe + "Address", party.Line1 ?? party.City ?? string.Empty),
                new XElement(Fe + "PostCode", party.Postcode ?? string.Empty),
                new XElement(Fe + "Town", party.City ?? string.Empty),
                new XElement(Fe + "Province", party.City ?? string.Empty),
                new XElement(Fe + "CountryCode", "ESP")));
        }
        else
        {
            legal.Add(new XElement(
                Fe + "OverseasAddress",
                new XElement(Fe + "Address", party.Line1 ?? party.City ?? string.Empty),
                OverseasTown(party.Postcode, party.City),
                new XElement(Fe + "CountryCode", country)));
        }

        var phone = party.Contact?.Phone;
        var email = party.Contact?.Email ?? party.Email;

        if (phone is { Length: > 0 } || email is { Length: > 0 })
        {
            legal.Add(new XElement(
                Fe + "ContactDetails",
                phone is { Length: > 0 } ? new XElement(Fe + "Telephone", phone) : null,
                email is { Length: > 0 } ? new XElement(Fe + "ElectronicMail", email) : null));
        }

        return new XElement(
            Fe + elementName,
            new XElement(
                Fe + "TaxIdentification",
                new XElement(Fe + "PersonTypeCode", "J"),
                new XElement(Fe + "ResidenceTypeCode", "R"),
                new XElement(Fe + "TaxIdentificationNumber", number)),
            legal);
    }

    /// <summary>
    /// The post code and the town as Facturae states them abroad, in one field; omitted when the
    /// model has neither.
    /// </summary>
    private static XElement OverseasTown(string? postcode, string? city)
        => new(Fe + "PostCodeAndTown", string.Join(' ', new[] { postcode, city }.Where(part => part is { Length: > 0 })));

    /// <summary>
    /// The address country and the tax number without its country prefix: a Spanish NIF is stated
    /// on its own, and the model states a VAT identifier with the country in front of it.
    /// </summary>
    private static (string Country, string Number) Identification(FacturXParty party)
    {
        var country = party.Country is { Length: 2 } code ? code.ToUpperInvariant() : "ES";
        var vat = party.VatId ?? party.LegalId ?? string.Empty;
        var number = vat.StartsWith(country, StringComparison.OrdinalIgnoreCase) ? vat[2..] : vat;

        return (country, number);
    }
}
