using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using M3scalinum.Text.Typst;

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
        var payment = settlement?.Child("SpecifiedTradeSettlementPaymentMeans");

        var lines = transaction.Children("IncludedSupplyChainTradeLineItem")
            .Select(Line)
            .ToList();

        var taxes = settlement?.Children("ApplicableTradeTax")
            .Select(Tax)
            .ToList() ?? [];

        return new FacturXInvoice
        {
            Number = document.Text("ID") ?? string.Empty,
            TypeCode = document.Text("TypeCode") ?? "380",
            Profile = Xml.Profile(root),
            Currency = settlement?.Text("InvoiceCurrencyCode") ?? "EUR",
            TaxCurrency = settlement?.Text("TaxCurrencyCode"),
            IssueDate = DateOf(document.Child("IssueDateTime")) ?? default,
            DueDate = DateOf(settlement?.Child("SpecifiedTradePaymentTerms")?.Child("DueDateDateTime")),
            Period = Period(settlement?.Child("BillingSpecifiedPeriod")),
            // BT-24 in CII is the guideline URN: it is what selects the conformance level, so it is
            // read as both the profile (below, through Xml.Profile) and the specification identifier.
            SpecificationId = root.Child("ExchangedDocumentContext")
                ?.Child("GuidelineSpecifiedDocumentContextParameter")?.Text("ID"),
            BusinessProcess = root.Child("ExchangedDocumentContext")
                ?.Child("BusinessProcessSpecifiedDocumentContextParameter")?.Text("ID"),
            PrecedingInvoiceNumber = settlement?.Child("InvoiceReferencedDocument")?.Text("IssuerAssignedID"),
            PrecedingInvoiceDate = DateOf(settlement?.Child("InvoiceReferencedDocument")?.Child("FormattedIssueDateTime")),
            BuyerReference = agreement?.Text("BuyerReference"),
            OrderReference = agreement?.Child("BuyerOrderReferencedDocument")?.Text("IssuerAssignedID"),
            ContractReference = agreement?.Child("ContractReferencedDocument")?.Text("IssuerAssignedID"),
            ProjectReference = agreement?.Child("SpecifiedProcuringProject")?.Text("ID"),
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
            PaymentReference = settlement?.Text("PaymentReference"),
            Mandate = Mandate(settlement, payment),
            Iban = payment?.Child("PayeePartyCreditorFinancialAccount")?.Text("IBANID"),
            Bic = payment?.Child("PayeeSpecifiedCreditorFinancialInstitution")?.Text("BICID"),
            Seller = Party(agreement?.Child("SellerTradeParty")),
            Buyer = Party(agreement?.Child("BuyerTradeParty")),
            DeliveryAddress = transaction.Child("ApplicableHeaderTradeDelivery")?.Child("ShipToTradeParty") is { } shipTo
                ? Party(shipTo)
                : null,
            Lines = lines,
            AllowancesCharges = settlement?.Children("SpecifiedTradeAllowanceCharge")
                .Select(AllowanceCharge)
                .ToList() ?? [],
            SupportingDocuments = agreement?.Children("AdditionalReferencedDocument")
                .Where(referenced => referenced.Text("TypeCode") == "916")
                .Select(SupportingDocument)
                .ToList() ?? [],
            TaxBreakdown = taxes,
            Totals = Totals(settlement?.Child("SpecifiedTradeSettlementHeaderMonetarySummation")),
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
        var contact = party.Child("DefinedTradeContact");

        var defined = contact is null
            ? null
            : new FacturXContact
            {
                Name = contact.Text("PersonName"),
                Phone = contact.Child("TelephoneUniversalCommunication")?.Text("CompleteNumber")
                    ?? contact.Child("Telephone")?.Text("CompleteNumber"),
                Email = contact.Child("EmailURIUniversalCommunication")?.Text("URIID")
                    ?? contact.Text("EmailURI"),
            };

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
            Contact = defined,
            Line1 = address?.Text("LineOne"),
            Line2 = address?.Text("LineTwo"),
            Postcode = address?.Text("PostcodeCode"),
            City = address?.Text("CityName"),
            Country = address?.Text("CountryID"),
            Email = defined?.Email,
        };
    }

    private static FacturXLine Line(XElement line)
    {
        var product = line.Child("SpecifiedTradeProduct");
        var quantity = line.Child("SpecifiedLineTradeDelivery")?.Child("BilledQuantity");
        var agreement = line.Child("SpecifiedLineTradeAgreement");
        var netPrice = agreement?.Child("NetPriceProductTradePrice");
        var grossPrice = agreement?.Child("GrossPriceProductTradePrice");
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
            Classifications = product?.Children("DesignatedProductClassification")
                .Select(classification => new FacturXClassification
                {
                    Code = classification.Text("ClassCode"),
                    Scheme = classification.Child("ClassCode").AttributeValue("listID"),
                })
                .Where(classification => classification.Code is { Length: > 0 })
                .ToList() ?? [],
            Quantity = quantity?.Value is { } text
                && decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
                    ? value
                    : 0m,
            UnitCode = quantity.AttributeValue("unitCode"),
            BaseQuantity = netPrice?.Amount("BasisQuantity"),
            UnitPrice = netPrice?.Number("ChargeAmount") ?? 0m,
            GrossUnitPrice = grossPrice?.Amount("ChargeAmount"),
            PriceDiscount = grossPrice?.Child("AppliedTradeAllowanceCharge")?.Amount("ActualAmount"),
            LineNetAmount = settlement
                ?.Child("SpecifiedTradeSettlementLineMonetarySummation")?.Number("LineTotalAmount") ?? 0m,
            Period = Period(settlement?.Child("BillingSpecifiedPeriod")),
            AllowancesCharges = settlement?.Children("SpecifiedTradeAllowanceCharge")
                .Select(AllowanceCharge)
                .ToList() ?? [],
            LineSeller = agreement?.Child("ItemSellerTradeParty") is { } lineSeller
                ? Party(lineSeller)
                : null,
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
        AllowanceTotal = summation?.Number("AllowanceTotalAmount") ?? 0m,
        ChargeTotal = summation?.Number("ChargeTotalAmount") ?? 0m,
        TaxBasisTotal = summation?.Number("TaxBasisTotalAmount") ?? 0m,
        TaxTotal = summation?.Number("TaxTotalAmount") ?? 0m,
        GrandTotal = summation?.Number("GrandTotalAmount") ?? 0m,
        Prepaid = summation?.Number("TotalPrepaidAmount") ?? 0m,
        Due = summation?.Number("DuePayableAmount") ?? 0m,
    };

    /// <summary>
    /// A <c>SpecifiedTradeAllowanceCharge</c>. The indicator holds a <c>udt:Indicator</c> child, so
    /// it is read by value rather than by text of a child element.
    /// </summary>
    private static FacturXAllowanceCharge AllowanceCharge(XElement element)
    {
        var tax = element.Child("CategoryTradeTax");

        return new FacturXAllowanceCharge
        {
            IsCharge = string.Equals(element.Child("ChargeIndicator")?.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase),
            Amount = element.Number("ActualAmount"),
            BaseAmount = element.Amount("BasisAmount"),
            Percentage = element.Amount("CalculationPercent"),
            Reason = element.Text("Reason"),
            ReasonCode = element.Text("ReasonCode"),
            VatCategory = tax?.Text("CategoryCode"),
            VatRate = tax?.Amount("RateApplicablePercent"),
        };
    }

    private static FacturXSupportingDocument SupportingDocument(XElement referenced) => new()
    {
        Id = referenced.Text("IssuerAssignedID"),
        Scheme = referenced.Text("ReferenceTypeCode"),
        Description = referenced.Text("Name"),
        Uri = referenced.Text("URIID"),
    };

    private static FacturXMandate? Mandate(XElement? settlement, XElement? payment)
    {
        var reference = settlement?.Text("CreditorReferenceID");
        var account = payment?.Child("PayerPartyDebtorFinancialAccount")?.Text("IBANID");

        return reference is null && account is null
            ? null
            : new FacturXMandate { Reference = reference, DebitedAccount = account };
    }

    /// <summary>A <c>BillingSpecifiedPeriod</c>, at document or line level.</summary>
    private static FacturXPeriod? Period(XElement? period)
    {
        if (period is null)
        {
            return null;
        }

        var start = DateOf(period.Child("StartDateTime"));
        var end = DateOf(period.Child("EndDateTime"));

        return start is null && end is null ? null : new FacturXPeriod { Start = start, End = end };
    }

    private static DateOnly? DateOf(XElement? element) => Xml.DateOf(element);
}
