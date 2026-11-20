using System;
using System.Globalization;
using System.Linq;
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
                    invoice.IssueDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture))));

        // NoteType sequences Content before SubjectCode: the reverse fails the official XSD.
        if (invoice.Note is { Length: > 0 } note)
        {
            header.Add(new XElement(Ram + "IncludedNote", new XElement(Ram + "Content", note)));
        }

        foreach (var extra in invoice.Notes)
        {
            var element = new XElement(Ram + "IncludedNote", new XElement(Ram + "Content", extra.Content));

            if (extra.SubjectCode is { Length: > 0 } code)
            {
                element.Add(new XElement(Ram + "SubjectCode", code));
            }

            header.Add(element);
        }

        return header;
    }

    private static XElement Transaction(FacturXInvoice invoice)
    {
        var transaction = new XElement(Rsm + "SupplyChainTradeTransaction");

        var lineNumber = 1;
        foreach (var line in invoice.Lines)
        {
            transaction.Add(Line(line, lineNumber++));
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

    private static XElement Line(FacturXLine line, int number)
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

        item.Add(product);

        var agreement = new XElement(Ram + "SpecifiedLineTradeAgreement",
            new XElement(Ram + "NetPriceProductTradePrice",
                new XElement(Ram + "ChargeAmount", Amount(line.UnitPrice))));

        // LineTradeAgreementType places ItemSellerTradeParty after the net price.
        if (line.LineSeller is { } lineSeller)
        {
            agreement.Add(Party("ItemSellerTradeParty", lineSeller));
        }

        item.Add(agreement);

        item.Add(new XElement(Ram + "SpecifiedLineTradeDelivery",
            new XElement(Ram + "BilledQuantity",
                new XAttribute("unitCode", line.UnitCode ?? "C62"),
                Quantity(line.Quantity))));

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
                tax.Add(new XElement(Ram + "RateApplicablePercent", Percent(rate)));
            }

            settlement.Add(tax);
        }

        var summation = new XElement(Ram + "SpecifiedTradeSettlementLineMonetarySummation",
            new XElement(Ram + "LineTotalAmount", Amount(line.LineNetAmount)));

        if (line.TaxTotal is { } lineTax)
        {
            summation.Add(new XElement(Ram + "TaxTotalAmount",
                new XAttribute("currencyID", "EUR"),
                Amount(lineTax)));
        }

        settlement.Add(summation);

        // LineTradeSettlementType places InvoiceReferencedDocument after the monetary summation.
        if (line.PriorInvoiceNumber is { Length: > 0 } prior)
        {
            var reference = new XElement(Ram + "InvoiceReferencedDocument",
                new XElement(Ram + "IssuerAssignedID", prior));

            if (line.PriorInvoiceDate is { } priorDate)
            {
                reference.Add(new XElement(Ram + "FormattedIssueDateTime",
                    new XElement(Qdt + "DateTimeString",
                        new XAttribute("format", "102"),
                        priorDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture))));
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

        if (invoice.OrderReference is { Length: > 0 } order)
        {
            agreement.Add(new XElement(Ram + "BuyerOrderReferencedDocument",
                new XElement(Ram + "IssuerAssignedID", order)));
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

    private static XElement Settlement(FacturXInvoice invoice)
    {
        var settlement = new XElement(Ram + "ApplicableHeaderTradeSettlement",
            new XElement(Ram + "InvoiceCurrencyCode", invoice.Currency));

        if (invoice.Iban is { Length: > 0 } iban)
        {
            // 58 is a SEPA credit transfer: the only sensible default when an IBAN is given and
            // the caller did not say how it is paid.
            var means = new XElement(Ram + "SpecifiedTradeSettlementPaymentMeans",
                new XElement(Ram + "TypeCode", invoice.PaymentMeansCode ?? "58"),
                new XElement(Ram + "PayeePartyCreditorFinancialAccount",
                    new XElement(Ram + "IBANID", iban)));

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
                new XElement(Ram + "CalculatedAmount", Amount(tax.Amount)),
                new XElement(Ram + "TypeCode", "VAT"),
                new XElement(Ram + "BasisAmount", Amount(tax.Basis)),
                new XElement(Ram + "CategoryCode", tax.Category));

            if (tax.Rate is { } rate)
            {
                element.Add(new XElement(Ram + "RateApplicablePercent", Percent(rate)));
            }

            if (tax.ExemptionReason is { Length: > 0 } reason)
            {
                element.Add(new XElement(Ram + "ExemptionReason", reason));
            }

            settlement.Add(element);
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
                        due.ToString("yyyyMMdd", CultureInfo.InvariantCulture))));
            }

            settlement.Add(payment);
        }

        var totals = new XElement(Ram + "SpecifiedTradeSettlementHeaderMonetarySummation",
            new XElement(Ram + "LineTotalAmount", Amount(invoice.Totals.LineTotal)),
            new XElement(Ram + "TaxBasisTotalAmount", Amount(invoice.Totals.TaxBasisTotal)),
            new XElement(Ram + "TaxTotalAmount",
                new XAttribute("currencyID", invoice.Currency),
                Amount(invoice.Totals.TaxTotal)),
            new XElement(Ram + "GrandTotalAmount", Amount(invoice.Totals.GrandTotal)));

        if (invoice.Totals.Prepaid != 0m)
        {
            totals.Add(new XElement(Ram + "TotalPrepaidAmount", Amount(invoice.Totals.Prepaid)));
        }

        totals.Add(new XElement(Ram + "DuePayableAmount", Amount(invoice.Totals.Due)));
        settlement.Add(totals);

        return settlement;
    }

    private static string Amount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Percent(decimal value) =>
        value.ToString(value == decimal.Truncate(value) ? "0" : "0.00", CultureInfo.InvariantCulture);

    private static string Quantity(decimal value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);
}
