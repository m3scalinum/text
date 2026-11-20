using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using M3scalinum.Text.SharpTypst;

namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>Reads the UN/CEFACT Cross Industry Invoice syntax, which is what Factur-X uses.</summary>
internal static class CiiInvoiceReader
{
    public static FacturXInvoice Read(XElement root)
    {
        var document = root.Child("ExchangedDocument")
            ?? throw new FacturXFormatException("CII document has no ExchangedDocument");

        var transaction = root.Child("SupplyChainTradeTransaction")
            ?? throw new FacturXFormatException("CII document has no SupplyChainTradeTransaction");

        var agreement = transaction.Child("ApplicableHeaderTradeAgreement");
        var settlement = transaction.Child("ApplicableHeaderTradeSettlement");

        var seller = Party(agreement?.Child("SellerTradeParty"));
        var buyer = Party(agreement?.Child("BuyerTradeParty"));

        var lines = transaction.Children("IncludedSupplyChainTradeLineItem")
            .Select(Line)
            .ToList();

        var taxes = settlement?.Children("ApplicableTradeTax")
            .Select(Tax)
            .ToList() ?? [];

        var totals = Totals(settlement?.Child("SpecifiedTradeSettlementHeaderMonetarySummation"));
        var payment = settlement?.Child("SpecifiedTradeSettlementPaymentMeans");

        return new FacturXInvoice
        {
            Number = document.Text("ID") ?? string.Empty,
            TypeCode = document.Text("TypeCode") ?? "380",
            Profile = Xml.Profile(root),
            Currency = settlement?.Text("InvoiceCurrencyCode") ?? "EUR",
            IssueDate = DateOf(document.Child("IssueDateTime")) ?? default,
            DueDate = DateOf(settlement?.Child("SpecifiedTradePaymentTerms")?.Child("DueDateDateTime")),
            BusinessProcess = root.Child("ExchangedDocumentContext")
                ?.Child("BusinessProcessSpecifiedDocumentContextParameter")?.Text("ID"),
            BuyerReference = agreement?.Text("BuyerReference"),
            OrderReference = agreement?.Child("BuyerOrderReferencedDocument")?.Text("IssuerAssignedID"),
            Note = document.Children("IncludedNote").Select(note => note.Text("Content")).FirstOrDefault(text => text is not null),
            Notes = document.Children("IncludedNote")
                .Select(note => new FacturXNote
                {
                    Content = note.Text("Content") ?? string.Empty,
                    SubjectCode = note.Text("SubjectCode"),
                })
                .Where(note => note.Content.Length > 0)
                .ToList(),
            PaymentTerms = settlement?.Child("SpecifiedTradePaymentTerms")?.Text("Description"),
            PaymentMeansCode = payment?.Text("TypeCode"),
            Iban = payment?.Child("PayeePartyCreditorFinancialAccount")?.Text("IBANID"),
            Bic = payment?.Child("PayeeSpecifiedCreditorFinancialInstitution")?.Text("BICID"),
            Seller = seller,
            Buyer = buyer,
            DeliveryAddress = transaction.Child("ApplicableHeaderTradeDelivery")?.Child("ShipToTradeParty") is { } shipTo
                ? Party(shipTo)
                : null,
            Lines = lines,
            TaxBreakdown = taxes,
            Totals = totals,
        };
    }

    private static FacturXParty Party(XElement? party)
    {
        if (party is null)
        {
            return new FacturXParty();
        }

        var address = party.Child("PostalTradeAddress");
        var tax = party.Children("SpecifiedTaxRegistration")
            .Select(registration => registration.Child("ID"))
            .FirstOrDefault(id => string.Equals(id.AttributeValue("schemeID"), "VA", StringComparison.Ordinal));

        var identifier = party.Child("ID");
        var legal = party.Child("SpecifiedLegalOrganization")?.Child("ID");
        var electronic = party.Child("URIUniversalCommunication")?.Child("URIID");

        return new FacturXParty
        {
            Name = party.Text("Name"),
            PartyId = identifier?.Value.Trim(),
            PartyIdScheme = identifier?.AttributeValue("schemeID"),
            LegalId = legal?.Value.Trim(),
            LegalIdScheme = legal?.AttributeValue("schemeID"),
            ElectronicAddress = electronic?.Value.Trim(),
            ElectronicAddressScheme = electronic?.AttributeValue("schemeID"),
            VatId = tax?.Value.Trim(),
            Line1 = address?.Text("LineOne"),
            Line2 = address?.Text("LineTwo"),
            Postcode = address?.Text("PostcodeCode"),
            City = address?.Text("CityName"),
            Country = address?.Text("CountryID"),
            Email = party.Child("DefinedTradeContact")?.Text("EmailURI"),
        };
    }

    private static FacturXLine Line(XElement line)
    {
        var product = line.Child("SpecifiedTradeProduct");
        var quantity = line.Child("SpecifiedLineTradeDelivery")?.Child("BilledQuantity");
        var settlement = line.Child("SpecifiedLineTradeSettlement");
        var tax = settlement?.Child("ApplicableTradeTax");
        var document = line.Child("AssociatedDocumentLineDocument");
        var prior = settlement?.Child("InvoiceReferencedDocument");
        var referenced = settlement?.Children("AdditionalReferencedDocument")
            .FirstOrDefault(candidate => candidate.Text("TypeCode") == "130");

        return new FacturXLine
        {
            Id = document?.Text("LineID"),
            ParentLineId = document?.Text("ParentLineID"),
            StatusReason = document?.Text("LineStatusReasonCode"),
            Name = product?.Text("Name"),
            SellerAssignedId = product?.Text("SellerAssignedID"),
            Quantity = quantity?.Value is { } text
                && decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
                    ? value
                    : 0m,
            UnitCode = quantity.AttributeValue("unitCode"),
            UnitPrice = line.Child("SpecifiedLineTradeAgreement")
                ?.Child("NetPriceProductTradePrice")?.Number("ChargeAmount") ?? 0m,
            LineSeller = line.Child("SpecifiedLineTradeAgreement")?.Child("ItemSellerTradeParty") is { } lineSeller
                ? Party(lineSeller)
                : null,
            LineNetAmount = settlement
                ?.Child("SpecifiedTradeSettlementLineMonetarySummation")?.Number("LineTotalAmount") ?? 0m,
            TaxTotal = settlement
                ?.Child("SpecifiedTradeSettlementLineMonetarySummation")?.Amount("TaxTotalAmount"),
            TaxCategory = tax?.Text("CategoryCode"),
            TaxRate = tax?.Amount("RateApplicablePercent"),
            PriorInvoiceNumber = prior?.Text("IssuerAssignedID"),
            PriorInvoiceDate = DateOf(prior?.Child("FormattedIssueDateTime")),
            ObjectIdentifier = referenced?.Text("IssuerAssignedID"),
            ObjectIdentifierScheme = referenced?.Text("ReferenceTypeCode"),
        };
    }

    private static FacturXTaxBreakdown Tax(XElement tax) => new()
    {
        Category = tax.Text("CategoryCode") ?? "S",
        Rate = tax.Amount("RateApplicablePercent"),
        Basis = tax.Number("BasisAmount"),
        Amount = tax.Number("CalculatedAmount"),
        ExemptionReason = tax.Text("ExemptionReason"),
    };

    private static FacturXTotals Totals(XElement? summation) => new()
    {
        LineTotal = summation?.Number("LineTotalAmount") ?? 0m,
        TaxBasisTotal = summation?.Number("TaxBasisTotalAmount") ?? 0m,
        TaxTotal = summation?.Number("TaxTotalAmount") ?? 0m,
        GrandTotal = summation?.Number("GrandTotalAmount") ?? 0m,
        Prepaid = summation?.Number("TotalPrepaidAmount") ?? 0m,
        Due = summation?.Number("DuePayableAmount") ?? 0m,
    };

    private static DateOnly? DateOf(XElement? element) => Xml.DateOf(element);
}
