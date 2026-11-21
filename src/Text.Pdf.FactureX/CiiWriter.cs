using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using M3scalinum.Text.SharpTypst;

namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>Writes an invoice back out as UN/CEFACT Cross Industry Invoice XML.</summary>
/// <remarks>
/// This is the canonical syntax Factur-X embeds, and it is what makes an invoice built in C# — and
/// not only one read from a file — attachable to a PDF. The mapping covers the same subset the
/// reader understands; anything outside it stays out of the file rather than being guessed at.
/// </remarks>
public static class CiiWriter
{
    private static readonly XNamespace Rsm = "urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100";
    private static readonly XNamespace Ram = "urn:un:unece:uncefact:data:standard:ReusableAggregateBusinessInformationEntity:100";
    private static readonly XNamespace Udt = "urn:un:unece:uncefact:data:standard:UnqualifiedDataType:100";
    private static readonly XNamespace Qdt = "urn:un:unece:uncefact:data:standard:QualifiedDataType:100";

    /// <summary>Serializes an invoice to CII XML text.</summary>
    public static string Write(FacturXInvoice invoice)
        => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(WriteBytes(invoice));

    /// <summary>Serializes an invoice to CII XML bytes, as they are embedded in the PDF.</summary>
    /// <remarks>
    /// The document is written to a stream, not a <see cref="StringBuilder"/>: serializing into a
    /// string makes <see cref="XmlWriter"/> declare <c>encoding="utf-16"</c> over what is then
    /// encoded as UTF-8, and a receiver that honours the declaration reads garbage.
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
        new XElement(Rsm + "CrossIndustryInvoice",
            new XAttribute(XNamespace.Xmlns + "rsm", Rsm),
            new XAttribute(XNamespace.Xmlns + "ram", Ram),
            new XAttribute(XNamespace.Xmlns + "qdt", Qdt),
            new XAttribute(XNamespace.Xmlns + "udt", Udt),
            Context(invoice),
            Header(invoice),
            Transaction(invoice)));

    private static XElement Context(FacturXInvoice invoice)
    {
        var context = new XElement(Rsm + "ExchangedDocumentContext");

        // BT-23 comes first: the schema sequences BusinessProcess before Guideline.
        if (invoice.BusinessProcess is { Length: > 0 } process)
        {
            context.Add(new XElement(
                Ram + "BusinessProcessSpecifiedDocumentContextParameter",
                new XElement(Ram + "ID", process)));
        }

        context.Add(new XElement(
            Ram + "GuidelineSpecifiedDocumentContextParameter",
            new XElement(Ram + "ID", invoice.Profile.ToGuidelineUrn())));

        return context;
    }

    private static XElement Header(FacturXInvoice invoice)
    {
        var header = new XElement(Rsm + "ExchangedDocument",
            new XElement(Ram + "ID", invoice.Number),
            new XElement(Ram + "TypeCode", invoice.TypeCode),
            new XElement(Ram + "IssueDateTime",
                new XElement(Udt + "DateTimeString",
                    new XAttribute("format", "102"),
                    Xml.CompactDate(invoice.IssueDate))));

        // NoteType sequences Content before SubjectCode: the reverse fails the official XSD.
        // The coded notes already carry every note, the first one included, so the plain Note is
        // only written when there are none — writing both would repeat the text.
        if (invoice.Notes.Count > 0)
        {
            foreach (var extra in invoice.Notes)
            {
                var element = new XElement(Ram + "IncludedNote", new XElement(Ram + "Content", extra.Content));

                if (extra.SubjectCode is { Length: > 0 } code)
                {
                    element.Add(new XElement(Ram + "SubjectCode", code));
                }

                header.Add(element);
            }
        }
        else if (invoice.Note is { Length: > 0 } note)
        {
            header.Add(new XElement(Ram + "IncludedNote", new XElement(Ram + "Content", note)));
        }

        return header;
    }

    /// <summary>
    /// Whether the profile's line settlement may carry <c>InvoiceReferencedDocument</c>. Only the
    /// EXTENDED schema has the element there; EN 16931 does not, and the official XSD rejects a
    /// document that writes it.
    /// </summary>
    private static bool AllowsLineReference(FacturXProfile profile)
        => profile is FacturXProfile.Extended or FacturXProfile.ExtendedCtcFr;

    private static XElement Transaction(FacturXInvoice invoice)
    {
        var transaction = new XElement(Rsm + "SupplyChainTradeTransaction");

        var lineNumber = 1;
        foreach (var line in invoice.Lines)
        {
            transaction.Add(Line(line, lineNumber++, invoice.Currency, invoice.Profile));
        }

        transaction.Add(Agreement(invoice));
        var delivery = new XElement(Ram + "ApplicableHeaderTradeDelivery");

        if (invoice.DeliveryAddress is { } shipTo)
        {
            delivery.Add(Party("ShipToTradeParty", shipTo));
        }

        transaction.Add(delivery);
        transaction.Add(Settlement(invoice));

        return transaction;
    }

    private static XElement Line(FacturXLine line, int number, string currency, FacturXProfile profile)
    {
        // DocumentLineDocumentType sequences LineID, ParentLineID, LineStatusReasonCode.
        var document = new XElement(Ram + "AssociatedDocumentLineDocument",
            new XElement(Ram + "LineID", line.Id ?? number.ToString(CultureInfo.InvariantCulture)));

        if (line.ParentLineId is { Length: > 0 } parent)
        {
            document.Add(new XElement(Ram + "ParentLineID", parent));
        }

        if (line.StatusReason is { Length: > 0 } status)
        {
            document.Add(new XElement(Ram + "LineStatusReasonCode", status));
        }

        var item = new XElement(Ram + "IncludedSupplyChainTradeLineItem", document);

        var product = new XElement(Ram + "SpecifiedTradeProduct");
        if (line.SellerAssignedId is { Length: > 0 } assignedId)
        {
            product.Add(new XElement(Ram + "SellerAssignedID", assignedId));
        }

        if (line.Name is { Length: > 0 } name)
        {
            product.Add(new XElement(Ram + "Name", name));
        }

        // BT-158 follows Name in TradeProductType, before the origin and the instances.
        foreach (var classification in line.Classifications)
        {
            if (classification.Code is not { Length: > 0 } classCode)
            {
                continue;
            }

            var coded = new XElement(Ram + "ClassCode", classCode);

            if (classification.Scheme is { Length: > 0 } scheme)
            {
                coded.Add(new XAttribute("listID", scheme));
            }

            product.Add(new XElement(Ram + "DesignatedProductClassification", coded));
        }

        item.Add(product);

        // GrossPriceProductTradePrice sequences before NetPriceProductTradePrice: BT-148 lives in
        // the first and BT-146 in the second.
        var agreement = new XElement(Ram + "SpecifiedLineTradeAgreement");

        if (line.GrossUnitPrice is { } gross)
        {
            var grossPrice = new XElement(Ram + "GrossPriceProductTradePrice",
                new XElement(Ram + "ChargeAmount", Xml.Money(gross)));

            if (line.PriceDiscount is { } discount)
            {
                grossPrice.Add(new XElement(Ram + "AppliedTradeAllowanceCharge",
                    new XElement(Ram + "ChargeIndicator", new XElement(Udt + "Indicator", "false")),
                    new XElement(Ram + "BasisAmount", Xml.Money(gross)),
                    new XElement(Ram + "ActualAmount", Xml.Money(discount))));
            }

            agreement.Add(grossPrice);
        }

        var netPrice = new XElement(Ram + "NetPriceProductTradePrice",
            new XElement(Ram + "ChargeAmount", Xml.Money(line.UnitPrice)));

        if (line.BaseQuantity is { } baseQuantity)
        {
            netPrice.Add(new XElement(Ram + "BasisQuantity",
                new XAttribute("unitCode", line.UnitCode ?? "C62"),
                Xml.Quantity(baseQuantity)));
        }

        agreement.Add(netPrice);

        // LineTradeAgreementType places ItemSellerTradeParty after the prices.
        if (line.LineSeller is { } lineSeller)
        {
            agreement.Add(Party("ItemSellerTradeParty", lineSeller));
        }

        item.Add(agreement);

        item.Add(new XElement(Ram + "SpecifiedLineTradeDelivery",
            new XElement(Ram + "BilledQuantity",
                new XAttribute("unitCode", line.UnitCode ?? "C62"),
                Xml.Quantity(line.Quantity))));

        var settlement = new XElement(Ram + "SpecifiedLineTradeSettlement");

        if (line.TaxCategory is { Length: > 0 } category)
        {
            // The CII sequence puts TypeCode before CategoryCode; a line tax without it is
            // rejected by the official schema.
            var tax = new XElement(Ram + "ApplicableTradeTax",
                new XElement(Ram + "TypeCode", "VAT"),
                new XElement(Ram + "CategoryCode", category));

            if (line.TaxRate is { } rate)
            {
                tax.Add(new XElement(Ram + "RateApplicablePercent", Xml.Rate(rate)));
            }

            settlement.Add(tax);
        }

        // BG-26 and BG-27 / BG-28 precede the monetary summation in LineTradeSettlementType.
        if (line.Period is { } linePeriod)
        {
            settlement.Add(Period("BillingSpecifiedPeriod", linePeriod));
        }

        foreach (var adjustment in line.AllowancesCharges)
        {
            settlement.Add(AllowanceCharge("SpecifiedTradeAllowanceCharge", adjustment));
        }

        var summation = new XElement(Ram + "SpecifiedTradeSettlementLineMonetarySummation",
            new XElement(Ram + "LineTotalAmount", Xml.Money(line.LineNetAmount)));

        if (line.TaxTotal is { } lineTax)
        {
            summation.Add(new XElement(Ram + "TaxTotalAmount",
                new XAttribute("currencyID", currency),
                Xml.Money(lineTax)));
        }

        settlement.Add(summation);

        // LineTradeSettlementType places InvoiceReferencedDocument after the monetary summation —
        // but only the EXTENDED schema has it there: the EN 16931 profile's line settlement does
        // not allow the element, and the official validator refuses the whole document for it.
        if (line.PriorInvoiceNumber is { Length: > 0 } prior && AllowsLineReference(profile))
        {
            var reference = new XElement(Ram + "InvoiceReferencedDocument",
                new XElement(Ram + "IssuerAssignedID", prior));

            if (line.PriorInvoiceDate is { } priorDate)
            {
                reference.Add(new XElement(Ram + "FormattedIssueDateTime",
                    new XElement(Qdt + "DateTimeString",
                        new XAttribute("format", "102"),
                        Xml.CompactDate(priorDate))));
            }

            settlement.Add(reference);
        }

        // BT-128: the object the line bills, after InvoiceReferencedDocument in the sequence.
        if (line.ObjectIdentifier is { Length: > 0 } objectId)
        {
            var referenced = new XElement(Ram + "AdditionalReferencedDocument",
                new XElement(Ram + "IssuerAssignedID", objectId),
                new XElement(Ram + "TypeCode", "130"));

            if (line.ObjectIdentifierScheme is { Length: > 0 } scheme)
            {
                referenced.Add(new XElement(Ram + "ReferenceTypeCode", scheme));
            }

            settlement.Add(referenced);
        }

        item.Add(settlement);
        return item;
    }

    private static XElement Agreement(FacturXInvoice invoice)
    {
        var agreement = new XElement(Ram + "ApplicableHeaderTradeAgreement");

        if (invoice.BuyerReference is { Length: > 0 } reference)
        {
            agreement.Add(new XElement(Ram + "BuyerReference", reference));
        }

        agreement.Add(Party("SellerTradeParty", invoice.Seller));
        agreement.Add(Party("BuyerTradeParty", invoice.Buyer));

        // Agreement sequence: order reference, contract reference, additional documents, project.
        if (invoice.OrderReference is { Length: > 0 } order)
        {
            agreement.Add(new XElement(Ram + "BuyerOrderReferencedDocument",
                new XElement(Ram + "IssuerAssignedID", order)));
        }

        if (invoice.ContractReference is { Length: > 0 } contract)
        {
            agreement.Add(new XElement(Ram + "ContractReferencedDocument",
                new XElement(Ram + "IssuerAssignedID", contract)));
        }

        // BG-24: TypeCode 916 marks a supporting document, and ReferencedDocumentType sequences
        // IssuerAssignedID, URIID, TypeCode, Name, ReferenceTypeCode.
        foreach (var supporting in invoice.SupportingDocuments)
        {
            var referenced = new XElement(Ram + "AdditionalReferencedDocument");

            if (supporting.Id is { Length: > 0 } id)
            {
                referenced.Add(new XElement(Ram + "IssuerAssignedID", id));
            }

            if (supporting.Uri is { Length: > 0 } uri)
            {
                referenced.Add(new XElement(Ram + "URIID", uri));
            }

            referenced.Add(new XElement(Ram + "TypeCode", "916"));

            if (supporting.Description is { Length: > 0 } description)
            {
                referenced.Add(new XElement(Ram + "Name", description));
            }

            if (supporting.Scheme is { Length: > 0 } scheme)
            {
                referenced.Add(new XElement(Ram + "ReferenceTypeCode", scheme));
            }

            agreement.Add(referenced);
        }

        // BT-11 comes last in the agreement sequence. ProcuringProjectType makes both ID and Name
        // mandatory, and EN 16931 spends only one term on the project: the reference fills both.
        if (invoice.ProjectReference is { Length: > 0 } project)
        {
            agreement.Add(new XElement(Ram + "SpecifiedProcuringProject",
                new XElement(Ram + "ID", project),
                new XElement(Ram + "Name", project)));
        }

        return agreement;
    }

    private static XElement Party(string elementName, FacturXParty party)
    {
        var element = new XElement(Ram + elementName);

        // TradeParty sequences ID before Name: BT-29 (the identifier the counterpart knows) sits
        // here, and BT-30 / BT-47 (the legal registration) further down inside
        // SpecifiedLegalOrganization. Swapping them fails the official XSD.
        if (party.PartyId is { Length: > 0 } partyId)
        {
            element.Add(Identified("ID", partyId, party.PartyIdScheme));
        }

        if (party.Name is { Length: > 0 } name)
        {
            element.Add(new XElement(Ram + "Name", name));
        }

        if (party.LegalId is { Length: > 0 } legalId)
        {
            element.Add(new XElement(
                Ram + "SpecifiedLegalOrganization",
                Identified("ID", legalId, party.LegalIdScheme)));
        }

        // BG-6 / BG-9 sit between the legal organisation and the postal address. Email is the
        // shorthand the model carried before contacts existed, so it fills the e-mail when no
        // contact states one.
        var contactName = party.Contact?.Name;
        var contactPhone = party.Contact?.Phone;
        var contactEmail = party.Contact?.Email ?? party.Email;

        if (contactName is { Length: > 0 } || contactPhone is { Length: > 0 } || contactEmail is { Length: > 0 })
        {
            var defined = new XElement(Ram + "DefinedTradeContact");

            if (contactName is { Length: > 0 })
            {
                defined.Add(new XElement(Ram + "PersonName", contactName));
            }

            if (contactPhone is { Length: > 0 })
            {
                defined.Add(new XElement(Ram + "TelephoneUniversalCommunication",
                    new XElement(Ram + "CompleteNumber", contactPhone)));
            }

            if (contactEmail is { Length: > 0 })
            {
                defined.Add(new XElement(Ram + "EmailURIUniversalCommunication",
                    new XElement(Ram + "URIID", contactEmail)));
            }

            element.Add(defined);
        }

        // The EN 16931 profile reorders TradeAddressType: PostcodeCode first, then the street
        // lines, the city and the country. The base CII schema order is different, and following
        // it makes the document fail the official XSD.
        var address = new XElement(Ram + "PostalTradeAddress");

        if (party.Postcode is { Length: > 0 } postcode)
        {
            address.Add(new XElement(Ram + "PostcodeCode", postcode));
        }

        if (party.Line1 is { Length: > 0 } line1)
        {
            address.Add(new XElement(Ram + "LineOne", line1));
        }

        if (party.Line2 is { Length: > 0 } line2)
        {
            address.Add(new XElement(Ram + "LineTwo", line2));
        }

        if (party.City is { Length: > 0 } city)
        {
            address.Add(new XElement(Ram + "CityName", city));
        }

        if (party.Country is { Length: > 0 } country)
        {
            address.Add(new XElement(Ram + "CountryID", country));
        }

        element.Add(address);

        // URIUniversalCommunication follows the postal address and precedes the tax registration.
        if (party.ElectronicAddress is { Length: > 0 } electronic)
        {
            element.Add(new XElement(
                Ram + "URIUniversalCommunication",
                Identified("URIID", electronic, party.ElectronicAddressScheme)));
        }

        if (party.VatId is { Length: > 0 } vat)
        {
            element.Add(new XElement(Ram + "SpecifiedTaxRegistration",
                new XElement(Ram + "ID", new XAttribute("schemeID", "VA"), vat)));
        }

        return element;
    }

    /// <summary>An element holding an identifier, carrying its scheme when the caller gave one.</summary>
    private static XElement Identified(string name, string value, string? scheme)
        => scheme is { Length: > 0 }
            ? new XElement(Ram + name, new XAttribute("schemeID", scheme), value)
            : new XElement(Ram + name, value);

    /// <summary>A <c>BillingSpecifiedPeriod</c>, whose two date-times are optional and ordered.</summary>
    private static XElement Period(string elementName, FacturXPeriod period)
    {
        var element = new XElement(Ram + elementName);

        if (period.Start is { } start)
        {
            element.Add(new XElement(Ram + "StartDateTime",
                new XElement(Udt + "DateTimeString",
                    new XAttribute("format", "102"),
                    Xml.CompactDate(start))));
        }

        if (period.End is { } end)
        {
            element.Add(new XElement(Ram + "EndDateTime",
                new XElement(Udt + "DateTimeString",
                    new XAttribute("format", "102"),
                    Xml.CompactDate(end))));
        }

        return element;
    }

    /// <summary>
    /// A <c>SpecifiedTradeAllowanceCharge</c>: indicator, percentage, basis, amount, reason code,
    /// reason, then the VAT category — the order TradeAllowanceChargeType fixes.
    /// </summary>
    private static XElement AllowanceCharge(string elementName, FacturXAllowanceCharge adjustment)
    {
        var element = new XElement(Ram + elementName,
            new XElement(Ram + "ChargeIndicator",
                new XElement(Udt + "Indicator", adjustment.IsCharge ? "true" : "false")));

        if (adjustment.Percentage is { } percentage)
        {
            element.Add(new XElement(Ram + "CalculationPercent", Xml.Rate(percentage)));
        }

        if (adjustment.BaseAmount is { } basis)
        {
            element.Add(new XElement(Ram + "BasisAmount", Xml.Money(basis)));
        }

        element.Add(new XElement(Ram + "ActualAmount", Xml.Money(adjustment.Amount)));

        if (adjustment.ReasonCode is { Length: > 0 } reasonCode)
        {
            element.Add(new XElement(Ram + "ReasonCode", reasonCode));
        }

        if (adjustment.Reason is { Length: > 0 } reason)
        {
            element.Add(new XElement(Ram + "Reason", reason));
        }

        if (adjustment.VatCategory is { Length: > 0 } category)
        {
            var tax = new XElement(Ram + "CategoryTradeTax",
                new XElement(Ram + "TypeCode", "VAT"),
                new XElement(Ram + "CategoryCode", category));

            if (adjustment.VatRate is { } rate)
            {
                tax.Add(new XElement(Ram + "RateApplicablePercent", Xml.Rate(rate)));
            }

            element.Add(tax);
        }

        return element;
    }

    private static XElement Settlement(FacturXInvoice invoice)
    {
        var settlement = new XElement(Ram + "ApplicableHeaderTradeSettlement");

        // Settlement sequence: CreditorReferenceID (BT-89), PaymentReference (BT-83),
        // TaxCurrencyCode (BT-6), then InvoiceCurrencyCode (BT-5).
        if (invoice.Mandate?.Reference is { Length: > 0 } mandate)
        {
            settlement.Add(new XElement(Ram + "CreditorReferenceID", mandate));
        }

        if (invoice.PaymentReference is { Length: > 0 } remittance)
        {
            settlement.Add(new XElement(Ram + "PaymentReference", remittance));
        }

        if (invoice.TaxCurrency is { Length: > 0 } taxCurrency)
        {
            settlement.Add(new XElement(Ram + "TaxCurrencyCode", taxCurrency));
        }

        settlement.Add(new XElement(Ram + "InvoiceCurrencyCode", invoice.Currency));

        if (invoice.Iban is { Length: > 0 }
            || invoice.Mandate?.DebitedAccount is { Length: > 0 }
            || invoice.PaymentMeansCode is { Length: > 0 })
        {
            // 58 is a SEPA credit transfer: the only sensible default when an IBAN is given and
            // the caller did not say how it is paid.
            var means = new XElement(Ram + "SpecifiedTradeSettlementPaymentMeans",
                new XElement(Ram + "TypeCode", invoice.PaymentMeansCode ?? "58"));

            // BT-91: the account the direct debit is drawn on precedes the creditor's account.
            if (invoice.Mandate?.DebitedAccount is { Length: > 0 } debited)
            {
                means.Add(new XElement(Ram + "PayerPartyDebtorFinancialAccount",
                    new XElement(Ram + "IBANID", debited)));
            }

            if (invoice.Iban is { Length: > 0 } payee)
            {
                means.Add(new XElement(Ram + "PayeePartyCreditorFinancialAccount",
                    new XElement(Ram + "IBANID", payee)));
            }

            if (invoice.Bic is { Length: > 0 } bic)
            {
                means.Add(new XElement(Ram + "PayeeSpecifiedCreditorFinancialInstitution",
                    new XElement(Ram + "BICID", bic)));
            }

            settlement.Add(means);
        }

        foreach (var tax in invoice.TaxBreakdown)
        {
            var element = new XElement(Ram + "ApplicableTradeTax",
                new XElement(Ram + "CalculatedAmount", Xml.Money(tax.Amount)),
                new XElement(Ram + "TypeCode", "VAT"),
                new XElement(Ram + "BasisAmount", Xml.Money(tax.Basis)),
                new XElement(Ram + "CategoryCode", tax.Category));

            if (tax.Rate is { } rate)
            {
                element.Add(new XElement(Ram + "RateApplicablePercent", Xml.Rate(rate)));
            }

            if (tax.ExemptionReason is { Length: > 0 } reason)
            {
                element.Add(new XElement(Ram + "ExemptionReason", reason));
            }

            settlement.Add(element);
        }

        // BG-14 and BG-20 / BG-21 follow the VAT breakdown and precede the payment terms.
        if (invoice.Period is { } period)
        {
            settlement.Add(Period("BillingSpecifiedPeriod", period));
        }

        foreach (var adjustment in invoice.AllowancesCharges)
        {
            settlement.Add(AllowanceCharge("SpecifiedTradeAllowanceCharge", adjustment));
        }

        if (invoice.PaymentTerms is { Length: > 0 } || invoice.DueDate is not null)
        {
            var payment = new XElement(Ram + "SpecifiedTradePaymentTerms");

            if (invoice.PaymentTerms is { Length: > 0 } terms)
            {
                payment.Add(new XElement(Ram + "Description", terms));
            }

            if (invoice.DueDate is { } due)
            {
                payment.Add(new XElement(Ram + "DueDateDateTime",
                    new XElement(Udt + "DateTimeString",
                        new XAttribute("format", "102"),
                        Xml.CompactDate(due))));
            }

            settlement.Add(payment);
        }

        // BG-3: InvoiceReferencedDocument sits just before the monetary summation in
        // HeaderTradeSettlementType.
        if (invoice.PrecedingInvoiceNumber is { Length: > 0 } precedingInvoice)
        {
            var reference = new XElement(Ram + "InvoiceReferencedDocument",
                new XElement(Ram + "IssuerAssignedID", precedingInvoice));

            if (invoice.PrecedingInvoiceDate is { } precedingDate)
            {
                reference.Add(new XElement(Ram + "FormattedIssueDateTime",
                    new XElement(Udt + "DateTimeString",
                        new XAttribute("format", "102"),
                        Xml.CompactDate(precedingDate))));
            }

            settlement.Add(reference);
        }

        var totals = new XElement(Ram + "SpecifiedTradeSettlementHeaderMonetarySummation",
            new XElement(Ram + "LineTotalAmount", Xml.Money(invoice.Totals.LineTotal)));

        // HeaderMonetarySummationType sequences ChargeTotalAmount before AllowanceTotalAmount.
        if (invoice.Totals.ChargeTotal != 0m)
        {
            totals.Add(new XElement(Ram + "ChargeTotalAmount", Xml.Money(invoice.Totals.ChargeTotal)));
        }

        if (invoice.Totals.AllowanceTotal != 0m)
        {
            totals.Add(new XElement(Ram + "AllowanceTotalAmount", Xml.Money(invoice.Totals.AllowanceTotal)));
        }

        totals.Add(new XElement(Ram + "TaxBasisTotalAmount", Xml.Money(invoice.Totals.TaxBasisTotal)));
        totals.Add(new XElement(Ram + "TaxTotalAmount",
            new XAttribute("currencyID", invoice.Currency),
            Xml.Money(invoice.Totals.TaxTotal)));
        totals.Add(new XElement(Ram + "GrandTotalAmount", Xml.Money(invoice.Totals.GrandTotal)));

        if (invoice.Totals.Prepaid != 0m)
        {
            totals.Add(new XElement(Ram + "TotalPrepaidAmount", Xml.Money(invoice.Totals.Prepaid)));
        }

        totals.Add(new XElement(Ram + "DuePayableAmount", Xml.Money(invoice.Totals.Due)));
        settlement.Add(totals);

        return settlement;
    }
}
