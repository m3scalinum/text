using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using M3scalinum.Text.SharpTypst;

namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>Reads the OASIS Universal Business Language invoice syntax.</summary>
internal static class UblInvoiceReader
{
    public static FacturXInvoice Read(XElement root)
    {
        var supplier = Party(root.Child("AccountingSupplierParty")?.Child("Party"));
        var customer = Party(root.Child("AccountingCustomerParty")?.Child("Party"));

        // A UBL credit note carries CreditNoteLine instead of InvoiceLine; reading only the latter
        // yields a document with no lines, which validates as an empty invoice.
        var lines = root.Children("InvoiceLine")
            .Concat(root.Children("CreditNoteLine"))
            .Select(Line)
            .ToList();

        var taxes = root.Child("TaxTotal")?.Children("TaxSubtotal").Select(Tax).ToList() ?? [];

        var monetary = root.Child("LegalMonetaryTotal");
        var paymentMeans = root.Child("PaymentMeans");

        return new FacturXInvoice
        {
            Number = root.Text("ID") ?? string.Empty,
            TypeCode = root.Text("InvoiceTypeCode") ?? root.Text("CreditNoteTypeCode") ?? "380",
            Profile = Xml.Profile(root),
            Currency = root.Text("DocumentCurrencyCode") ?? "EUR",
            IssueDate = root.Date("IssueDate") ?? default,
            DueDate = root.Date("DueDate"),
            BuyerReference = root.Text("BuyerReference"),
            OrderReference = root.Child("OrderReference")?.Text("ID"),
            Note = root.Text("Note"),
            PaymentTerms = root.Child("PaymentTerms")?.Text("Note"),
            PaymentMeansCode = paymentMeans?.Text("PaymentMeansCode"),
            Iban = paymentMeans?.Child("PayeeFinancialAccount")?.Text("ID"),
            Bic = paymentMeans?.Child("PayeeFinancialAccount")?.Child("FinancialInstitutionBranch")?.Text("ID"),
            Seller = supplier,
            Buyer = customer,
            Lines = lines,
            TaxBreakdown = taxes,
            Totals = new FacturXTotals
            {
                LineTotal = monetary?.Number("LineExtensionAmount") ?? 0m,
                TaxBasisTotal = monetary?.Number("TaxExclusiveAmount") ?? 0m,
                TaxTotal = root.Child("TaxTotal")?.Number("TaxAmount") ?? 0m,
                GrandTotal = monetary?.Number("TaxInclusiveAmount") ?? 0m,
                Prepaid = monetary?.Number("PrepaidAmount") ?? 0m,
                Due = monetary?.Number("PayableAmount") ?? 0m,
            },
        };
    }

    private static FacturXParty Party(XElement? party)
    {
        if (party is null)
        {
            return new FacturXParty();
        }

        var name = party.Child("PartyName")?.Text("Name") ?? party.Text("Name");

        // UBL carries the same three identifiers under different names: PartyIdentification is
        // BT-29, PartyLegalEntity/CompanyID is BT-30 / BT-47, EndpointID is BT-34 / BT-49.
        var identifier = party.Child("PartyIdentification")?.Child("ID");
        var legal = party.Child("PartyLegalEntity")?.Child("CompanyID");
        var endpoint = party.Child("EndpointID");

        return new FacturXParty
        {
            Name = name,
            PartyId = identifier?.Value.Trim(),
            PartyIdScheme = identifier?.AttributeValue("schemeID"),
            LegalId = legal?.Value.Trim(),
            LegalIdScheme = legal?.AttributeValue("schemeID"),
            ElectronicAddress = endpoint?.Value.Trim(),
            ElectronicAddressScheme = endpoint?.AttributeValue("schemeID"),
            VatId = party.Child("PartyTaxScheme")?.Text("CompanyID"),
            Line1 = party.Child("PostalAddress")?.Text("StreetName"),
            Line2 = party.Child("PostalAddress")?.Text("AdditionalStreetName"),
            Postcode = party.Child("PostalAddress")?.Text("PostalZone"),
            City = party.Child("PostalAddress")?.Text("CityName"),
            Country = party.Child("PostalAddress")?.Child("Country")?.Text("IdentificationCode"),
            Email = party.Child("Contact")?.Text("ElectronicMail"),
        };
    }

    private static FacturXLine Line(XElement line)
    {
        var quantity = line.Child("InvoicedQuantity") ?? line.Child("CreditedQuantity");
        var item = line.Child("Item");
        var tax = item?.Child("ClassifiedTaxCategory");

        return new FacturXLine
        {
            Id = line.Text("ID"),
            Name = item?.Text("Name"),
            SellerAssignedId = item?.Child("SellersItemIdentification")?.Text("ID"),
            Quantity = line.Amount("InvoicedQuantity") ?? line.Amount("CreditedQuantity") ?? 0m,
            UnitCode = quantity.AttributeValue("unitCode"),
            UnitPrice = line.Child("Price")?.Number("PriceAmount") ?? 0m,
            LineNetAmount = line.Number("LineExtensionAmount"),
            TaxCategory = tax?.Text("ID"),
            TaxRate = tax?.Amount("Percent"),
        };
    }

    private static FacturXTaxBreakdown Tax(XElement subtotal)
    {
        var category = subtotal.Child("TaxCategory");

        return new FacturXTaxBreakdown
        {
            Category = category?.Text("ID") ?? "S",
            Rate = category?.Amount("Percent"),
            Basis = subtotal.Number("TaxableAmount"),
            Amount = subtotal.Number("TaxAmount"),
            ExemptionReason = category?.Text("TaxExemptionReason"),
        };
    }
}
