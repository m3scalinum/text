using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using M3scalinum.Text.SharpTypst;

namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>Writes an invoice out as OASIS Universal Business Language 2.1.</summary>
/// <remarks>
/// The element order below is the one the official UBL 2.1 schema fixes, not a stylistic choice:
/// the schema is a sequence, so an element in the wrong position makes the document fail
/// validation. Each of those orderings has a regression test, and the two roots — <c>Invoice</c>
/// and <c>CreditNote</c> — are chosen from the document type code.
/// </remarks>
public static class UblWriter
{
    private static readonly XNamespace InvoiceNs = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
    private static readonly XNamespace CreditNoteNs = "urn:oasis:names:specification:ubl:schema:xsd:CreditNote-2";
    private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";

    /// <summary>Serializes an invoice to UBL XML text.</summary>
    public static string Write(FacturXInvoice invoice)
        => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(WriteBytes(invoice));

    /// <summary>Serializes an invoice to UBL XML bytes.</summary>
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

    private static XDocument Build(FacturXInvoice invoice)
    {
        var credit = invoice.IsCreditNote;

        var root = new XElement(
            (credit ? CreditNoteNs : InvoiceNs) + (credit ? "CreditNote" : "Invoice"),
            new XAttribute(XNamespace.Xmlns + "cac", Cac),
            new XAttribute(XNamespace.Xmlns + "cbc", Cbc),
            new XElement(Cbc + "UBLVersionID", "2.1"),
            // BT-24: the profile the document claims. Peppol names its own; Factur-X's URN is the
            // default because it is what the conformance level declares.
            new XElement(Cbc + "CustomizationID", invoice.SpecificationId is { Length: > 0 } specification
                ? specification
                : invoice.Profile.ToGuidelineUrn()),
            // BT-23: UBL spells the business process ProfileID, and it sequences before ID.
            invoice.BusinessProcess is { Length: > 0 } process
                ? new XElement(Cbc + "ProfileID", process)
                : null,
            new XElement(Cbc + "ID", invoice.Number),
            new XElement(Cbc + "IssueDate", Xml.IsoDate(invoice.IssueDate)));

        if (invoice.DueDate is { } due)
        {
            root.Add(new XElement(Cbc + "DueDate", Xml.IsoDate(due)));
        }

        root.Add(new XElement(Cbc + (credit ? "CreditNoteTypeCode" : "InvoiceTypeCode"), invoice.TypeCode));

        // UBL Note carries no subject code: BT-21 has no home in this syntax, so only the text is
        // written, with the plain Note first.
        if (invoice.Note is { Length: > 0 } note)
        {
            root.Add(new XElement(Cbc + "Note", note));
        }

        foreach (var extra in invoice.Notes)
        {
            if (extra.Content.Length > 0
                && !string.Equals(extra.Content, invoice.Note, StringComparison.Ordinal))
            {
                root.Add(new XElement(Cbc + "Note", extra.Content));
            }
        }

        root.Add(new XElement(Cbc + "DocumentCurrencyCode", invoice.Currency));

        if (invoice.TaxCurrency is { Length: > 0 } taxCurrency)
        {
            root.Add(new XElement(Cbc + "TaxCurrencyCode", taxCurrency));
        }

        if (invoice.BuyerReference is { Length: > 0 } buyerReference)
        {
            root.Add(new XElement(Cbc + "BuyerReference", buyerReference));
        }

        if (invoice.Period is { } period)
        {
            root.Add(Period("InvoicePeriod", period));
        }

        if (invoice.OrderReference is { Length: > 0 } order)
        {
            root.Add(new XElement(Cac + "OrderReference",
                new XElement(Cbc + "ID", order)));
        }

        // BG-3: BillingReference follows OrderReference and precedes ContractDocumentReference in
        // InvoiceType; InvoiceDocumentReference sequences ID before IssueDate.
        if (invoice.PrecedingInvoiceNumber is { Length: > 0 } preceding)
        {
            var reference = new XElement(Cac + "InvoiceDocumentReference",
                new XElement(Cbc + "ID", preceding));

            if (invoice.PrecedingInvoiceDate is { } precedingDate)
            {
                reference.Add(new XElement(Cbc + "IssueDate", Xml.IsoDate(precedingDate)));
            }

            root.Add(new XElement(Cac + "BillingReference", reference));
        }

        if (invoice.ContractReference is { Length: > 0 } contract)
        {
            root.Add(new XElement(Cac + "ContractDocumentReference",
                new XElement(Cbc + "ID", contract)));
        }

        foreach (var supporting in invoice.SupportingDocuments)
        {
            root.Add(SupportingDocument(supporting));
        }

        if (invoice.ProjectReference is { Length: > 0 } project)
        {
            root.Add(new XElement(Cac + "ProjectReference",
                new XElement(Cbc + "ID", project)));
        }

        root.Add(new XElement(Cac + "AccountingSupplierParty", Party(invoice.Seller)));
        root.Add(new XElement(Cac + "AccountingCustomerParty", Party(invoice.Buyer)));

        if (invoice.DeliveryAddress is { } delivery)
        {
            root.Add(new XElement(Cac + "Delivery",
                new XElement(Cac + "DeliveryLocation", Address("Address", delivery))));
        }

        if (PaymentMeans(invoice) is { } means)
        {
            root.Add(means);
        }

        if (invoice.PaymentTerms is { Length: > 0 } terms)
        {
            root.Add(new XElement(Cac + "PaymentTerms",
                new XElement(Cbc + "Note", terms)));
        }

        foreach (var adjustment in invoice.AllowancesCharges)
        {
            root.Add(AllowanceCharge(adjustment, invoice.Currency));
        }

        root.Add(TaxTotal(invoice));

        root.Add(MonetaryTotal(invoice));

        foreach (var line in invoice.Lines)
        {
            root.Add(Line(line, invoice.Currency, credit));
        }

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
    }

    private static XElement Party(FacturXParty party)
    {
        var element = new XElement(Cac + "Party");

        // Peppol carries the routing address as EndpointID, which UBL sequences before the other
        // identifiers.
        if (party.ElectronicAddress is { Length: > 0 } endpoint)
        {
            var routed = new XElement(Cbc + "EndpointID", endpoint);

            if (party.ElectronicAddressScheme is { Length: > 0 } scheme)
            {
                routed.Add(new XAttribute("schemeID", scheme));
            }

            element.Add(routed);
        }

        if (party.PartyId is { Length: > 0 } partyId)
        {
            element.Add(new XElement(Cac + "PartyIdentification", Identified("ID", partyId, party.PartyIdScheme)));
        }

        if (party.Name is { Length: > 0 } name)
        {
            element.Add(new XElement(Cac + "PartyName", new XElement(Cbc + "Name", name)));
        }

        element.Add(Address("PostalAddress", party));

        if (party.VatId is { Length: > 0 } vat)
        {
            element.Add(new XElement(Cac + "PartyTaxScheme",
                new XElement(Cbc + "CompanyID", vat),
                new XElement(Cac + "TaxScheme", new XElement(Cbc + "ID", "VAT"))));
        }

        if (party.LegalId is { Length: > 0 } legal)
        {
            element.Add(new XElement(Cac + "PartyLegalEntity",
                Identified("CompanyID", legal, party.LegalIdScheme)));
        }

        // BG-6 / BG-9 follow the legal entity in PartyType.
        var contactName = party.Contact?.Name;
        var contactPhone = party.Contact?.Phone;
        var contactEmail = party.Contact?.Email ?? party.Email;

        if (contactName is { Length: > 0 } || contactPhone is { Length: > 0 } || contactEmail is { Length: > 0 })
        {
            var contact = new XElement(Cac + "Contact");

            if (contactName is { Length: > 0 })
            {
                contact.Add(new XElement(Cbc + "Name", contactName));
            }

            if (contactPhone is { Length: > 0 })
            {
                contact.Add(new XElement(Cbc + "Telephone", contactPhone));
            }

            if (contactEmail is { Length: > 0 })
            {
                contact.Add(new XElement(Cbc + "ElectronicMail", contactEmail));
            }

            element.Add(contact);
        }

        return element;
    }

    /// <summary>An address element, in the order AddressType fixes: street, city, zone, country.</summary>
    private static XElement Address(string elementName, FacturXParty party)
    {
        var address = new XElement(Cac + elementName);

        if (party.Line1 is { Length: > 0 } line1)
        {
            address.Add(new XElement(Cbc + "StreetName", line1));
        }

        if (party.Line2 is { Length: > 0 } line2)
        {
            address.Add(new XElement(Cbc + "AdditionalStreetName", line2));
        }

        if (party.City is { Length: > 0 } city)
        {
            address.Add(new XElement(Cbc + "CityName", city));
        }

        if (party.Postcode is { Length: > 0 } postcode)
        {
            address.Add(new XElement(Cbc + "PostalZone", postcode));
        }

        if (party.Country is { Length: > 0 } country)
        {
            address.Add(new XElement(Cac + "Country",
                new XElement(Cbc + "IdentificationCode", country)));
        }

        return address;
    }

    /// <summary>An element holding an identifier, carrying its scheme attribute when given.</summary>
    private static XElement Identified(string name, string value, string? scheme)
        => scheme is { Length: > 0 }
            ? new XElement(Cbc + name, new XAttribute("schemeID", scheme), value)
            : new XElement(Cbc + name, value);

    private static XElement Period(string elementName, FacturXPeriod period)
    {
        var element = new XElement(Cac + elementName);

        if (period.Start is { } start)
        {
            element.Add(new XElement(Cbc + "StartDate", Xml.IsoDate(start)));
        }

        if (period.End is { } end)
        {
            element.Add(new XElement(Cbc + "EndDate", Xml.IsoDate(end)));
        }

        return element;
    }

    /// <summary>
    /// BG-16 / BG-19 as one <c>PaymentMeans</c>: the code, the remittance information, the account
    /// the direct debit is drawn on, the creditor's account, then the mandate — the order
    /// PaymentMeansType fixes.
    /// </summary>
    private static XElement? PaymentMeans(FacturXInvoice invoice)
    {
        if (invoice.PaymentMeansCode is not { Length: > 0 }
            && invoice.Iban is not { Length: > 0 }
            && invoice.PaymentReference is not { Length: > 0 }
            && invoice.Mandate is null)
        {
            return null;
        }

        // 58 is a SEPA credit transfer, the same default the CII writer uses.
        var means = new XElement(Cac + "PaymentMeans",
            new XElement(Cbc + "PaymentMeansCode", invoice.PaymentMeansCode ?? "58"));

        if (invoice.PaymentReference is { Length: > 0 } remittance)
        {
            means.Add(new XElement(Cbc + "PaymentID", remittance));
        }

        if (invoice.Mandate?.DebitedAccount is { Length: > 0 } debited)
        {
            means.Add(new XElement(Cac + "PayerFinancialAccount",
                new XElement(Cbc + "ID", debited)));
        }

        if (invoice.Iban is { Length: > 0 } iban)
        {
            var account = new XElement(Cac + "PayeeFinancialAccount",
                new XElement(Cbc + "ID", iban));

            if (invoice.Bic is { Length: > 0 } bic)
            {
                account.Add(new XElement(Cac + "FinancialInstitutionBranch",
                    new XElement(Cbc + "ID", bic)));
            }

            means.Add(account);
        }

        if (invoice.Mandate?.Reference is { Length: > 0 } mandate)
        {
            means.Add(new XElement(Cac + "PaymentMandate",
                new XElement(Cbc + "ID", mandate)));
        }

        return means;
    }

    /// <summary>
    /// A UBL <c>AllowanceCharge</c>: indicator, reason code, reason, percentage, amount, basis,
    /// then the VAT category — the order AllowanceChargeType fixes.
    /// </summary>
    private static XElement AllowanceCharge(FacturXAllowanceCharge adjustment, string currency)
    {
        var element = new XElement(Cac + "AllowanceCharge",
            new XElement(Cbc + "ChargeIndicator", adjustment.IsCharge ? "true" : "false"));

        if (adjustment.ReasonCode is { Length: > 0 } reasonCode)
        {
            element.Add(new XElement(Cbc + "AllowanceChargeReasonCode", reasonCode));
        }

        if (adjustment.Reason is { Length: > 0 } reason)
        {
            element.Add(new XElement(Cbc + "AllowanceChargeReason", reason));
        }

        if (adjustment.Percentage is { } percentage)
        {
            element.Add(new XElement(Cbc + "MultiplierFactorNumeric", Xml.Rate(percentage)));
        }

        element.Add(Amount("Amount", adjustment.Amount, currency));

        if (adjustment.BaseAmount is { } basis)
        {
            element.Add(Amount("BaseAmount", basis, currency));
        }

        if (adjustment.VatCategory is { Length: > 0 } category)
        {
            element.Add(TaxCategory("TaxCategory", category, adjustment.VatRate, null, null));
        }

        return element;
    }

    private static XElement TaxTotal(FacturXInvoice invoice)
    {
        var total = new XElement(Cac + "TaxTotal",
            Amount("TaxAmount", invoice.Totals.TaxTotal, invoice.Currency));

        foreach (var tax in invoice.TaxBreakdown)
        {
            var category = TaxCategory("TaxCategory", tax.Category, tax.Rate, null, tax.ExemptionReason);

            total.Add(new XElement(Cac + "TaxSubtotal",
                Amount("TaxableAmount", tax.Basis, invoice.Currency),
                Amount("TaxAmount", tax.Amount, invoice.Currency),
                category));
        }

        return total;
    }

    /// <summary>
    /// A <c>TaxCategory</c>: identifier, percentage, exemption reason, then the scheme. EN 16931
    /// writes the VAT scheme as the literal <c>VAT</c>.
    /// </summary>
    private static XElement TaxCategory(string elementName, string category, decimal? rate, string? exemptionCode, string? exemptionReason)
    {
        var element = new XElement(Cac + elementName,
            new XElement(Cbc + "ID", category));

        if (rate is { } percent)
        {
            element.Add(new XElement(Cbc + "Percent", Xml.Rate(percent)));
        }

        if (exemptionCode is { Length: > 0 } code)
        {
            element.Add(new XElement(Cbc + "TaxExemptionReasonCode", code));
        }

        if (exemptionReason is { Length: > 0 } reason)
        {
            element.Add(new XElement(Cbc + "TaxExemptionReason", reason));
        }

        element.Add(new XElement(Cac + "TaxScheme", new XElement(Cbc + "ID", "VAT")));

        return element;
    }

    private static XElement MonetaryTotal(FacturXInvoice invoice)
    {
        var totals = invoice.Totals;
        var money = new XElement(Cac + "LegalMonetaryTotal",
            Amount("LineExtensionAmount", totals.LineTotal, invoice.Currency),
            Amount("TaxExclusiveAmount", totals.TaxBasisTotal, invoice.Currency),
            Amount("TaxInclusiveAmount", totals.GrandTotal, invoice.Currency));

        // LegalMonetaryTotalType sequences the two adjustment totals between the tax-inclusive
        // amount and the prepaid amount.
        if (totals.AllowanceTotal != 0m)
        {
            money.Add(Amount("AllowanceTotalAmount", totals.AllowanceTotal, invoice.Currency));
        }

        if (totals.ChargeTotal != 0m)
        {
            money.Add(Amount("ChargeTotalAmount", totals.ChargeTotal, invoice.Currency));
        }

        if (totals.Prepaid != 0m)
        {
            money.Add(Amount("PrepaidAmount", totals.Prepaid, invoice.Currency));
        }

        money.Add(Amount("PayableAmount", totals.Due, invoice.Currency));
        return money;
    }

    private static XElement Line(FacturXLine line, string currency, bool credit)
    {
        var element = new XElement(
            Cac + (credit ? "CreditNoteLine" : "InvoiceLine"),
            new XElement(Cbc + "ID", line.Id ?? string.Empty),
            new XElement(Cbc + (credit ? "CreditedQuantity" : "InvoicedQuantity"),
                new XAttribute("unitCode", line.UnitCode ?? "C62"),
                Xml.Quantity(line.Quantity)),
            Amount("LineExtensionAmount", line.LineNetAmount, currency));

        if (line.Period is { } period)
        {
            element.Add(Period("InvoicePeriod", period));
        }

        // BG-27 / BG-28 precede TaxTotal and Item in InvoiceLineType.
        foreach (var adjustment in line.AllowancesCharges)
        {
            element.Add(AllowanceCharge(adjustment, currency));
        }

        var item = new XElement(Cac + "Item");

        if (line.Name is { Length: > 0 } name)
        {
            item.Add(new XElement(Cbc + "Name", name));
        }

        if (line.SellerAssignedId is { Length: > 0 } sellerId)
        {
            item.Add(new XElement(Cac + "SellersItemIdentification",
                new XElement(Cbc + "ID", sellerId)));
        }

        foreach (var classification in line.Classifications)
        {
            if (classification.Code is not { Length: > 0 } code)
            {
                continue;
            }

            var coded = new XElement(Cbc + "ItemClassificationCode", code);

            if (classification.Scheme is { Length: > 0 } scheme)
            {
                coded.Add(new XAttribute("listID", scheme));
            }

            item.Add(new XElement(Cac + "CommodityClassification", coded));
        }

        if (line.TaxCategory is { Length: > 0 } category)
        {
            item.Add(TaxCategory("ClassifiedTaxCategory", category, line.TaxRate, null, null));
        }

        element.Add(item);

        // PriceType sequences PriceAmount, BaseQuantity, then the price discount.
        var price = new XElement(Cac + "Price",
            Amount("PriceAmount", line.UnitPrice, currency));

        if (line.BaseQuantity is { } baseQuantity)
        {
            price.Add(new XElement(Cbc + "BaseQuantity",
                new XAttribute("unitCode", line.UnitCode ?? "C62"),
                Xml.Quantity(baseQuantity)));
        }

        if (line.PriceDiscount is { } discount)
        {
            var allowance = new XElement(Cac + "AllowanceCharge",
                new XElement(Cbc + "ChargeIndicator", "false"),
                Amount("Amount", discount, currency));

            if (line.GrossUnitPrice is { } gross)
            {
                allowance.Add(Amount("BaseAmount", gross, currency));
            }

            price.Add(allowance);
        }

        element.Add(price);
        return element;
    }

    private static XElement SupportingDocument(FacturXSupportingDocument supporting)
    {
        var reference = new XElement(Cac + "AdditionalDocumentReference");
        var id = supporting.Id is { Length: > 0 } value ? Identified("ID", value, supporting.Scheme) : null;

        if (id is not null)
        {
            reference.Add(id);
        }

        if (supporting.Description is { Length: > 0 } description)
        {
            reference.Add(new XElement(Cbc + "DocumentDescription", description));
        }

        // DocumentReferenceType sequences Attachment after the description.
        if (supporting.Uri is { Length: > 0 } uri)
        {
            reference.Add(new XElement(Cac + "Attachment",
                new XElement(Cac + "ExternalReference",
                    new XElement(Cbc + "URI", uri))));
        }

        return reference;
    }

    private static XElement Amount(string name, decimal value, string currency)
        => new(Cbc + name, new XAttribute("currencyID", currency), Xml.Money(value));
}
