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

        var taxTotal = root.Child("TaxTotal");
        var taxes = taxTotal?.Children("TaxSubtotal").Select(Tax).ToList() ?? [];

        var monetary = root.Child("LegalMonetaryTotal");
        var paymentMeans = root.Child("PaymentMeans");

        return new FacturXInvoice
        {
            Number = root.Text("ID") ?? string.Empty,
            TypeCode = root.Text("InvoiceTypeCode") ?? root.Text("CreditNoteTypeCode") ?? "380",
            Profile = Xml.Profile(root),
            Currency = root.Text("DocumentCurrencyCode") ?? "EUR",
            TaxCurrency = root.Text("TaxCurrencyCode"),
            IssueDate = root.Date("IssueDate") ?? default,
            DueDate = root.Date("DueDate"),
            Period = Period(root.Child("InvoicePeriod")),
            BuyerReference = root.Text("BuyerReference"),
            OrderReference = root.Child("OrderReference")?.Text("ID"),
            ContractReference = root.Child("ContractDocumentReference")?.Text("ID"),
            ProjectReference = root.Child("ProjectReference")?.Text("ID"),
            Note = root.Text("Note"),
            PaymentTerms = root.Child("PaymentTerms")?.Text("Note"),
            PaymentMeansCode = paymentMeans?.Text("PaymentMeansCode"),
            PaymentReference = paymentMeans?.Text("PaymentID"),
            Mandate = Mandate(paymentMeans),
            Iban = paymentMeans?.Child("PayeeFinancialAccount")?.Text("ID"),
            Bic = paymentMeans?.Child("PayeeFinancialAccount")?.Child("FinancialInstitutionBranch")?.Text("ID"),
            Seller = supplier,
            Buyer = customer,
            DeliveryAddress = Delivery(root.Child("Delivery")),
            Lines = lines,
            AllowancesCharges = root.Children("AllowanceCharge").Select(AllowanceCharge).ToList(),
            SupportingDocuments = root.Children("AdditionalDocumentReference")
                .Select(SupportingDocument)
                .Where(document => document.Id is not null || document.Uri is not null)
                .ToList(),
            TaxBreakdown = taxes,
            Totals = new FacturXTotals
            {
                LineTotal = monetary?.Number("LineExtensionAmount") ?? 0m,
                AllowanceTotal = monetary?.Number("AllowanceTotalAmount") ?? 0m,
                ChargeTotal = monetary?.Number("ChargeTotalAmount") ?? 0m,
                TaxBasisTotal = monetary?.Number("TaxExclusiveAmount") ?? 0m,
                TaxTotal = taxTotal?.Number("TaxAmount") ?? 0m,
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
        var contact = party.Child("Contact");

        var defined = contact is null
            ? null
            : new FacturXContact
            {
                Name = contact.Text("Name"),
                Phone = contact.Text("Telephone"),
                Email = contact.Text("ElectronicMail"),
            };

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
            Contact = defined,
            Line1 = party.Child("PostalAddress")?.Text("StreetName"),
            Line2 = party.Child("PostalAddress")?.Text("AdditionalStreetName"),
            Postcode = party.Child("PostalAddress")?.Text("PostalZone"),
            City = party.Child("PostalAddress")?.Text("CityName"),
            Country = party.Child("PostalAddress")?.Child("Country")?.Text("IdentificationCode"),
            Email = defined?.Email,
        };
    }

    /// <summary>
    /// BG-13: Peppol states the place of delivery as <c>Delivery/DeliveryLocation/Address</c>;
    /// a producer that names a party instead puts it under <c>DeliveryParty</c>.
    /// </summary>
    private static FacturXParty? Delivery(XElement? delivery)
    {
        if (delivery is null)
        {
            return null;
        }

        if (delivery.Child("DeliveryParty") is { } party)
        {
            return Party(party);
        }

        var address = delivery.Child("DeliveryLocation")?.Child("Address");

        return address is null
            ? null
            : new FacturXParty
            {
                Line1 = address.Text("StreetName"),
                Line2 = address.Text("AdditionalStreetName"),
                Postcode = address.Text("PostalZone"),
                City = address.Text("CityName"),
                Country = address.Child("Country")?.Text("IdentificationCode"),
            };
    }

    private static FacturXLine Line(XElement line)
    {
        var quantity = line.Child("InvoicedQuantity") ?? line.Child("CreditedQuantity");
        var item = line.Child("Item");
        var tax = item?.Child("ClassifiedTaxCategory");
        var price = line.Child("Price");
        var discount = price?.Children("AllowanceCharge").FirstOrDefault();

        return new FacturXLine
        {
            Id = line.Text("ID"),
            Name = item?.Text("Name"),
            SellerAssignedId = item?.Child("SellersItemIdentification")?.Text("ID"),
            Classifications = item?.Children("CommodityClassification")
                .Select(classification => new FacturXClassification
                {
                    Code = classification.Text("ItemClassificationCode"),
                    Scheme = classification.Child("ItemClassificationCode").AttributeValue("listID"),
                })
                .Where(classification => classification.Code is { Length: > 0 })
                .ToList() ?? [],
            Quantity = line.Amount("InvoicedQuantity") ?? line.Amount("CreditedQuantity") ?? 0m,
            UnitCode = quantity.AttributeValue("unitCode"),
            BaseQuantity = price?.Amount("BaseQuantity"),
            UnitPrice = price?.Number("PriceAmount") ?? 0m,
            GrossUnitPrice = discount?.Amount("BaseAmount"),
            PriceDiscount = discount?.Amount("Amount"),
            LineNetAmount = line.Number("LineExtensionAmount"),
            Period = Period(line.Child("InvoicePeriod")),
            AllowancesCharges = line.Children("AllowanceCharge").Select(AllowanceCharge).ToList(),
            TaxCategory = tax?.Text("ID"),
            TaxRate = tax?.Amount("Percent"),
        };
    }

    /// <summary>
    /// A UBL <c>AllowanceCharge</c>, at document or line level. The indicator is the word
    /// <c>true</c> or <c>false</c>; the reason code precedes the free-text reason in the schema.
    /// </summary>
    private static FacturXAllowanceCharge AllowanceCharge(XElement element)
    {
        var tax = element.Child("TaxCategory");

        return new FacturXAllowanceCharge
        {
            IsCharge = string.Equals(element.Text("ChargeIndicator"), "true", StringComparison.OrdinalIgnoreCase),
            Amount = element.Number("Amount"),
            BaseAmount = element.Amount("BaseAmount"),
            Percentage = element.Amount("MultiplierFactorNumeric"),
            Reason = element.Text("AllowanceChargeReason"),
            ReasonCode = element.Text("AllowanceChargeReasonCode"),
            VatCategory = tax?.Text("ID"),
            VatRate = tax?.Amount("Percent"),
        };
    }

    private static FacturXSupportingDocument SupportingDocument(XElement document) => new()
    {
        Id = document.Text("ID"),
        Scheme = document.Child("ID").AttributeValue("schemeID"),
        Description = document.Text("DocumentDescription"),
        Uri = document.Child("Attachment")?.Child("ExternalReference")?.Text("URI"),
    };

    private static FacturXMandate? Mandate(XElement? paymentMeans)
    {
        var reference = paymentMeans?.Child("PaymentMandate")?.Text("ID");
        var account = paymentMeans?.Child("PayerFinancialAccount")?.Text("ID");

        return reference is null && account is null
            ? null
            : new FacturXMandate { Reference = reference, DebitedAccount = account };
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
