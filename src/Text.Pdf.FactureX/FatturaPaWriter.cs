using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using M3scalinum.Text.Typst;

namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>Writes an invoice as an Italian FatturaPA 1.2.2 document.</summary>
/// <remarks>
/// <para>
/// FatturaPA is a national schema aligned with EN 16931, not a CIUS: it states the same invoice in
/// its own vocabulary, so producing one is a mapping, not a rule overlay. The element order below
/// is the sequence the official XSD fixes — <c>DatiTrasmissione</c> before
/// <c>CedentePrestatore</c>, <c>DatiGenerali</c> before <c>DatiBeniServizi</c> before
/// <c>DatiPagamento</c>, and inside a line the description before the quantity before the price —
/// and an element out of place makes the document fail the exchange system.
/// </para>
/// <para>
/// The root is in the namespace the published schema declares and the children are not, which is
/// what that schema says: it declares a <c>targetNamespace</c> and no <c>elementFormDefault</c>.
/// <c>versione</c> is <c>FPR12</c>, the private format;
/// <c>FPA12</c> addresses a public administration and is not produced.
/// </para>
/// </remarks>
public static class FatturaPaWriter
{
    /// <summary>
    /// The namespace the published schema declares. Only the root is in it: the schema fixes no
    /// <c>elementFormDefault</c>, so its local elements are unqualified, which is why every
    /// document that circulates is a <c>p:</c> root over plain children.
    /// </summary>
    private static readonly XNamespace P = "http://ivaservizi.agenziaentrate.gov.it/docs/xsd/fatture/v1.2";

    /// <summary>Serializes an invoice to FatturaPA XML text.</summary>
    public static string Write(FacturXInvoice invoice)
        => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(WriteBytes(invoice));

    /// <summary>Serializes an invoice to FatturaPA XML bytes.</summary>
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
            P + "FatturaElettronica",
            new XAttribute(XNamespace.Xmlns + "p", P),
            new XAttribute("versione", FatturaPaDocumentTypes.Format),
            Header(invoice),
            Body(invoice)));

    private static XElement Header(FacturXInvoice invoice)
    {
        var (country, taxNumber) = Identification(invoice.Seller, "00000000000");

        // The transmission block is addressed to the buyer, so its recipient code comes from the
        // buyer's electronic address: a six- or seven-character code is the CodiceDestinatario, and
        // anything else — a PEC address — goes in PECDestinatario, with the mandatory code filled
        // with zeros, which is what the schema prescribes for that case.
        var address = invoice.Buyer.ElectronicAddress;
        var coded = address is { Length: >= 6 and <= 7 } && address.All(char.IsLetterOrDigit);
        var transmission = new XElement("DatiTrasmissione",
            new XElement("IdTrasmittente",
                new XElement("IdPaese", country),
                new XElement("IdCodice", taxNumber)),
            new XElement("ProgressivoInvio", Progressive(invoice.Number)),
            new XElement("FormatoTrasmissione", FatturaPaDocumentTypes.Format),
            new XElement("CodiceDestinatario", coded ? address : "0000000"),
            coded || address is not { Length: > 0 } ? null : new XElement("PECDestinatario", address));

        return new XElement(
            "FatturaElettronicaHeader",
            transmission,
            new XElement(
                "CedentePrestatore",
                new XElement(
                    "DatiAnagrafici",
                    TaxIdentification(country, taxNumber),
                    CodiceFiscale(invoice.Seller),
                    Denomination(invoice.Seller, invoice.Number),
                    // RF01 is the ordinary regime. EN 16931 has no term for the Italian regime, so
                    // the writer states the one almost every document states.
                    new XElement("RegimeFiscale", "RF01")),
                Seat("Sede", invoice.Seller),
                Contact(invoice.Seller)),
            new XElement(
                "CessionarioCommittente",
                new XElement(
                    "DatiAnagrafici",
                    BuyerTaxIdentification(invoice.Buyer),
                    CodiceFiscale(invoice.Buyer),
                    Denomination(invoice.Buyer, invoice.Number)),
                Seat("Sede", invoice.Buyer)));
    }

    private static XElement Body(FacturXInvoice invoice)
    {
        var body = new XElement("FatturaElettronicaBody", General(invoice));

        var goods = new XElement("DatiBeniServizi");
        var number = 1;

        foreach (var line in invoice.Lines)
        {
            goods.Add(Line(line, number++));
        }

        foreach (var tax in VatBreakdown.Of(invoice))
        {
            goods.Add(Summary(tax));
        }

        body.Add(goods);

        if (Payment(invoice) is { } payment)
        {
            body.Add(payment);
        }

        return body;
    }

    private static XElement General(FacturXInvoice invoice)
    {
        var document = new XElement(
            "DatiGeneraliDocumento",
            // Sequence: type, currency, date, number, then the adjustments, the total, the reasons.
            new XElement("TipoDocumento", FatturaPaDocumentTypes.Tipo(invoice.TypeCode)),
            new XElement("Divisa", invoice.Currency),
            new XElement("Data", Xml.IsoDate(invoice.IssueDate)),
            new XElement("Numero", invoice.Number));

        foreach (var adjustment in invoice.AllowancesCharges)
        {
            document.Add(Adjustment(adjustment));
        }

        document.Add(new XElement("ImportoTotaleDocumento", Xml.Money(invoice.Totals.GrandTotal)));

        // Causale is the document's own statement of what it bills; a note is the nearest term the
        // model has for it, and a document may carry several.
        foreach (var causale in Reasons(invoice))
        {
            document.Add(new XElement("Causale", causale));
        }

        var general = new XElement("DatiGenerali", document);

        if (invoice.PrecedingInvoiceNumber is { Length: > 0 } preceding)
        {
            var reference = new XElement("DatiFattureCollegate",
                new XElement("IdDocumento", preceding));

            if (invoice.PrecedingInvoiceDate is { } date)
            {
                reference.Add(new XElement("Data", Xml.IsoDate(date)));
            }

            general.Add(reference);
        }

        return general;
    }

    /// <summary>The plain note first, then every coded note, each once.</summary>
    private static IEnumerable<string> Reasons(FacturXInvoice invoice)
    {
        if (invoice.Note is { Length: > 0 } note)
        {
            yield return note;
        }

        foreach (var extra in invoice.Notes)
        {
            if (extra.Content.Length > 0
                && !string.Equals(extra.Content, invoice.Note, StringComparison.Ordinal))
            {
                yield return extra.Content;
            }
        }
    }

    private static XElement Line(FacturXLine line, int number)
    {
        var element = new XElement("DettaglioLinee",
            new XElement("NumeroLinea", number.ToString(CultureInfo.InvariantCulture)));

        // CodiceArticolo: the seller's own number first, then the classification lists. The model
        // keeps them apart and the Italian element holds both.
        if (line.SellerAssignedId is { Length: > 0 } sellerId)
        {
            element.Add(ArticleCode("INT", sellerId));
        }

        foreach (var classification in line.Classifications)
        {
            if (classification.Code is { Length: > 0 } code)
            {
                element.Add(ArticleCode(classification.Scheme ?? "CPV", code));
            }
        }

        element.Add(new XElement("Descrizione", line.Name ?? line.SellerAssignedId ?? string.Empty));

        // QuantitaType refuses a negative number, so a credit note states the absolute quantity:
        // the document type and the negative line amount carry the direction, as they do in the
        // Italian practice the schema encodes.
        if (line.Quantity != 0m)
        {
            element.Add(new XElement("Quantita", Xml.TwoToEightDecimals(Math.Abs(line.Quantity))));
        }

        if (line.UnitCode is { Length: > 0 } unit)
        {
            element.Add(new XElement("UnitaMisura", unit));
        }

        if (line.Period?.Start is { } start)
        {
            element.Add(new XElement("DataInizioPeriodo", Xml.IsoDate(start)));
        }

        if (line.Period?.End is { } end)
        {
            element.Add(new XElement("DataFinePeriodo", Xml.IsoDate(end)));
        }

        element.Add(new XElement("PrezzoUnitario", Xml.Money(line.UnitPrice)));

        foreach (var adjustment in line.AllowancesCharges)
        {
            element.Add(Adjustment(adjustment));
        }

        element.Add(new XElement("PrezzoTotale", Xml.Money(line.LineNetAmount)));

        var rate = line.TaxRate ?? 0m;
        element.Add(new XElement("AliquotaIVA", Xml.Money(rate)));

        if (FatturaPaNature.Natura(line.TaxCategory, rate) is { Length: > 0 } natura)
        {
            element.Add(new XElement("Natura", natura));
        }

        return element;
    }

    private static XElement ArticleCode(string type, string value)
        => new("CodiceArticolo", new XElement("CodiceTipo", type), new XElement("CodiceValore", value));

    /// <summary>
    /// <c>ScontoMaggiorazione</c>: <c>SC</c> for an allowance, <c>MG</c> for a charge, with the
    /// percentage and the amount when the model states them.
    /// </summary>
    private static XElement Adjustment(FacturXAllowanceCharge adjustment)
    {
        var element = new XElement("ScontoMaggiorazione",
            new XElement("Tipo", adjustment.IsCharge ? "MG" : "SC"));

        if (adjustment.Percentage is { } percentage)
        {
            element.Add(new XElement("Percentuale", Xml.Money(percentage)));
        }

        // The schema requires at least one of the two; the amount is the one a receiver books.
        element.Add(new XElement("Importo", Xml.Money(adjustment.Amount)));
        return element;
    }

    /// <summary>A <c>DatiRiepilogo</c>: rate, nature, taxable base and tax.</summary>
    private static XElement Summary(FacturXTaxBreakdown tax)
    {
        var element = new XElement("DatiRiepilogo",
            new XElement("AliquotaIVA", Xml.Money(tax.Rate ?? 0m)));

        if (FatturaPaNature.Natura(tax.Category, tax.Rate) is { Length: > 0 } natura)
        {
            element.Add(new XElement("Natura", natura));
        }

        element.Add(new XElement("ImponibileImporto", Xml.Money(tax.Basis)));
        element.Add(new XElement("Imposta", Xml.Money(tax.Amount)));

        if (tax.ExemptionReason is { Length: > 0 } reason)
        {
            element.Add(new XElement("RiferimentoNormativo", reason));
        }

        return element;
    }

    private static XElement? Payment(FacturXInvoice invoice)
    {
        if (invoice.DueDate is null
            && invoice.Iban is not { Length: > 0 }
            && invoice.PaymentMeansCode is not { Length: > 0 }
            && invoice.PaymentReference is not { Length: > 0 }
            && invoice.Totals.Due == 0m)
        {
            return null;
        }

        // TP02 is payment in full, TP03 an advance: the model states a prepaid amount and nothing
        // else about the conditions.
        var detail = new XElement("DettaglioPagamento",
            new XElement("ModalitaPagamento", FatturaPaPaymentMeans.Modality(invoice.PaymentMeansCode)));

        if (invoice.DueDate is { } due)
        {
            detail.Add(new XElement("DataScadenzaPagamento", Xml.IsoDate(due)));
        }

        detail.Add(new XElement("ImportoPagamento", Xml.Money(invoice.Totals.Due != 0m ? invoice.Totals.Due : invoice.Totals.GrandTotal)));

        if (invoice.Iban is { Length: > 0 } iban)
        {
            detail.Add(new XElement("IBAN", iban));
        }

        if (invoice.Bic is { Length: > 0 } bic)
        {
            detail.Add(new XElement("BIC", bic));
        }

        if (invoice.PaymentReference is { Length: > 0 } reference)
        {
            detail.Add(new XElement("CodicePagamento", reference));
        }

        return new XElement("DatiPagamento",
            new XElement("CondizioniPagamento", invoice.Totals.Prepaid > 0m ? "TP03" : "TP02"),
            detail);
    }

    private static XElement TaxIdentification(string country, string number)
        => new("IdFiscaleIVA",
            new XElement("IdPaese", country),
            new XElement("IdCodice", number));

    /// <summary>
    /// The buyer's tax identification, which FatturaPA makes optional: a private buyer states its
    /// fiscal code instead of a VAT number.
    /// </summary>
    private static XElement? BuyerTaxIdentification(FacturXParty party)
    {
        if (party.VatId is not { Length: > 0 } vat)
        {
            return null;
        }

        var (country, number) = Identification(party, vat);
        return TaxIdentification(country, number);
    }

    private static XElement? CodiceFiscale(FacturXParty party)
        => party.LegalId is { Length: > 0 } legal ? new XElement("CodiceFiscale", legal) : null;

    /// <summary>
    /// The denomination every party must carry. The model lets a name be absent, the Italian
    /// schema does not, so the writer falls back to an identifier rather than to invented text.
    /// </summary>
    private static XElement Denomination(FacturXParty party, string invoiceNumber)
        => new("Anagrafica",
            new XElement(
                "Denominazione",
                party.Name
                    ?? party.LegalId
                    ?? party.PartyId
                    ?? (invoiceNumber.Length > 0 ? invoiceNumber : "N/A")));

    private static XElement Seat(string name, FacturXParty party)
        => new(name,
            new XElement("Indirizzo", party.Line1 ?? party.City ?? party.Country ?? string.Empty),
            party.Postcode is { Length: > 0 } postcode ? new XElement("CAP", postcode) : null,
            party.City is { Length: > 0 } city ? new XElement("Comune", city) : null,
            party.Country is { Length: > 0 } country ? new XElement("Nazione", country) : null);

    private static XElement? Contact(FacturXParty party)
    {
        var phone = party.Contact?.Phone;
        var email = party.Contact?.Email ?? party.Email;

        if (phone is not { Length: > 0 } && email is not { Length: > 0 })
        {
            return null;
        }

        return new XElement("Contatti",
            phone is { Length: > 0 } ? new XElement("Telefono", phone) : null,
            email is { Length: > 0 } ? new XElement("Email", email) : null);
    }

    /// <summary>
    /// The country and the number of a party's tax identification. A VAT identifier is stated
    /// without a separator in the model, so the first two letters are the country when they are
    /// letters; otherwise the party's address decides.
    /// </summary>
    private static (string Country, string Number) Identification(FacturXParty party, string fallback)
    {
        var vat = party.VatId;

        if (vat is { Length: > 2 } && char.IsLetter(vat[0]) && char.IsLetter(vat[1]))
        {
            return (vat[..2].ToUpperInvariant(), vat[2..]);
        }

        var country = party.Country is { Length: 2 } code ? code.ToUpperInvariant() : "IT";

        return (country, vat ?? party.LegalId ?? party.PartyId ?? fallback);
    }

    /// <summary>
    /// The progressive number of the transmission, which the schema states in up to ten digits.
    /// The invoice number's digits make a stable one; a document without any gets <c>00001</c>.
    /// </summary>
    private static string Progressive(string number)
    {
        var digits = new string(number.Where(char.IsDigit).ToArray());

        return digits.Length == 0
            ? "00001"
            : digits.Length <= 10 ? digits : digits[^10..];
    }
}
