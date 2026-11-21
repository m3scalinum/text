using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using M3scalinum.Text.SharpTypst;

namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>Reads the Italian FatturaPA 1.2.2 syntax into the shared invoice model.</summary>
/// <remarks>
/// FatturaPA is a national schema aligned with EN 16931, not a rule overlay on it: it states the
/// same invoice in its own structure, so it is read here rather than validated elsewhere. The
/// document is <c>FatturaElettronica</c> — a header naming the transmitter and the two parties —
/// followed by one or more bodies, each a complete invoice. This reader takes the first body, which
/// is what every document that is not a batch carries.
/// </remarks>
public static class FatturaPaReader
{
    /// <summary>Reads an invoice from its XML text.</summary>
    /// <exception cref="FacturXFormatException">The document is not FatturaPA, or lacks a required part.</exception>
    public static FacturXInvoice Read(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        return Read(Xml.Parse(xml));
    }

    /// <summary>Reads an already parsed <c>FatturaElettronica</c> document.</summary>
    /// <exception cref="FacturXFormatException">The document lacks a required part.</exception>
    internal static FacturXInvoice Read(XElement root)
    {
        var header = root.Child("FatturaElettronicaHeader")
            ?? throw new FacturXFormatException("FatturaPA document has no FatturaElettronicaHeader");

        var body = root.Child("FatturaElettronicaBody")
            ?? throw new FacturXFormatException("FatturaPA document has no FatturaElettronicaBody");

        var general = body.Child("DatiGenerali")?.Child("DatiGeneraliDocumento")
            ?? throw new FacturXFormatException("FatturaPA document has no DatiGeneraliDocumento");

        var seller = Party(header.Child("CedentePrestatore"));
        var buyer = Recipient(header.Child("CessionarioCommittente"), header.Child("DatiTrasmissione"));

        var goods = body.Child("DatiBeniServizi");
        var lines = goods?.Children("DettaglioLinee").Select(Line).ToList() ?? [];
        var taxes = goods?.Children("DatiRiepilogo").Select(Tax).ToList() ?? [];
        var payments = body.Children("DatiPagamento")
            .SelectMany(payment => payment.Children("DettaglioPagamento"))
            .ToList();

        var taxBasisTotal = taxes.Sum(tax => tax.Basis);
        var taxTotal = taxes.Sum(tax => tax.Amount);
        var grandTotal = general.Amount("ImportoTotaleDocumento") ?? taxBasisTotal + taxTotal;

        return new FacturXInvoice
        {
            Number = general.Text("Numero") ?? string.Empty,
            TypeCode = FatturaPaDocumentTypes.TypeCode(general.Text("TipoDocumento")),
            Profile = FacturXProfile.En16931,
            Currency = general.Text("Divisa") ?? "EUR",
            IssueDate = general.Date("Data") ?? default,
            DueDate = payments.Select(detail => detail.Date("DataScadenzaPagamento")).FirstOrDefault(date => date is not null),
            Note = general.Children("Causale").Select(causale => causale.Value.Trim()).FirstOrDefault(text => text.Length > 0),
            Notes = general.Children("Causale")
                .Select(causale => causale.Value.Trim())
                .Where(text => text.Length > 0)
                .Select(text => new FacturXNote { Content = text })
                .ToList(),
            PrecedingInvoiceNumber = body.Child("DatiGenerali")?.Child("DatiFattureCollegate")?.Text("IdDocumento"),
            PrecedingInvoiceDate = body.Child("DatiGenerali")?.Child("DatiFattureCollegate")?.Date("Data"),
            PaymentMeansCode = payments
                .Select(detail => FatturaPaPaymentMeans.TypeCode(detail.Text("ModalitaPagamento")))
                .FirstOrDefault(code => code is not null),
            PaymentReference = payments.Select(detail => detail.Text("CodicePagamento")).FirstOrDefault(code => code is not null),
            Iban = payments.Select(detail => detail.Text("IBAN")).FirstOrDefault(iban => iban is not null),
            Bic = payments.Select(detail => detail.Text("BIC")).FirstOrDefault(bic => bic is not null),
            Seller = seller,
            Buyer = buyer,
            Lines = lines,
            AllowancesCharges = general.Children("ScontoMaggiorazione").Select(Adjustment).ToList(),
            TaxBreakdown = taxes,
            Totals = new FacturXTotals
            {
                LineTotal = lines.Sum(line => line.LineNetAmount),
                AllowanceTotal = general.Children("ScontoMaggiorazione")
                    .Where(adjustment => adjustment.Text("Tipo") != "MG")
                    .Sum(adjustment => adjustment.Number("Importo")),
                ChargeTotal = general.Children("ScontoMaggiorazione")
                    .Where(adjustment => adjustment.Text("Tipo") == "MG")
                    .Sum(adjustment => adjustment.Number("Importo")),
                TaxBasisTotal = taxBasisTotal,
                TaxTotal = taxTotal,
                GrandTotal = grandTotal,
                Due = payments.Count > 0
                    ? payments.Sum(detail => detail.Number("ImportoPagamento"))
                    : grandTotal,
            },
        };
    }

    /// <summary>A <c>CedentePrestatore</c>: the seller, with its tax identifiers and its seat.</summary>
    private static FacturXParty Party(XElement? party)
    {
        if (party is null)
        {
            return new FacturXParty();
        }

        var details = party.Child("DatiAnagrafici");
        var seats = party.Child("Sede");
        var contact = party.Child("Contatti");

        var defined = contact is null
            ? null
            : new FacturXContact
            {
                Phone = contact.Text("Telefono"),
                Email = contact.Text("Email"),
            };

        return new FacturXParty
        {
            Name = Name(details?.Child("Anagrafica")),
            VatId = Identifier(details?.Child("IdFiscaleIVA")),
            LegalId = details?.Text("CodiceFiscale"),
            Contact = defined,
            Line1 = Address(seats),
            Postcode = seats?.Text("CAP"),
            City = seats?.Text("Comune"),
            Country = seats?.Text("Nazione"),
            Email = defined?.Email,
        };
    }

    /// <summary>
    /// A <c>CessionarioCommittente</c>. Its routing address is not in the party but in the
    /// transmission block, which is addressed to it: the six-character recipient code, or the PEC
    /// address that stands in for it.
    /// </summary>
    private static FacturXParty Recipient(XElement? party, XElement? transmission)
    {
        var read = Party(party);
        var code = transmission?.Text("CodiceDestinatario");
        var pec = transmission?.Text("PECDestinatario");

        // `0000000` is the filler a sender writes when the address is the PEC one: it is not an
        // address, so it is not read as one.
        if (pec is { Length: > 0 })
        {
            return With(read, pec, "EM");
        }

        return code is { Length: > 0 } && code != "0000000"
            ? With(read, code, "0201")
            : read;
    }

    private static FacturXParty With(FacturXParty party, string address, string scheme) => new()
    {
        Name = party.Name,
        LegalId = party.LegalId,
        PartyId = party.PartyId,
        PartyIdScheme = party.PartyIdScheme,
        ElectronicAddress = address,
        ElectronicAddressScheme = scheme,
        VatId = party.VatId,
        Contact = party.Contact,
        Line1 = party.Line1,
        Line2 = party.Line2,
        Postcode = party.Postcode,
        City = party.City,
        Country = party.Country,
        Email = party.Email,
    };

    private static string? Name(XElement? anagrafica)
    {
        if (anagrafica is null)
        {
            return null;
        }

        var denomination = anagrafica.Text("Denominazione");

        if (denomination is { Length: > 0 })
        {
            return denomination;
        }

        var parts = new[] { anagrafica.Text("Nome"), anagrafica.Text("Cognome") }
            .Where(part => part is { Length: > 0 })
            .ToList();

        return parts.Count == 0 ? null : string.Join(' ', parts);
    }

    /// <summary>The country and the number, concatenated as EN 16931 states a VAT identifier.</summary>
    private static string? Identifier(XElement? identification)
    {
        var country = identification?.Text("IdPaese");
        var number = identification?.Text("IdCodice");

        return number is { Length: > 0 } ? country + number : null;
    }

    /// <summary>
    /// A <c>Sede</c>. The civic number is its own element and has no term in the model, so it is
    /// appended to the street line rather than dropped.
    /// </summary>
    private static string? Address(XElement? seat)
    {
        var street = seat?.Text("Indirizzo");

        if (street is null)
        {
            return null;
        }

        var number = seat?.Text("NumeroCivico");

        return number is { Length: > 0 } ? $"{street} {number}" : street;
    }

    private static FacturXLine Line(XElement line)
    {
        var rate = line.Amount("AliquotaIVA");

        return new FacturXLine
        {
            Id = line.Text("NumeroLinea"),
            Name = line.Text("Descrizione"),
            SellerAssignedId = line.Children("CodiceArticolo")
                .FirstOrDefault(code => !IsClassification(code.Text("CodiceTipo")))
                ?.Text("CodiceValore"),
            Classifications = line.Children("CodiceArticolo")
                .Where(code => IsClassification(code.Text("CodiceTipo")))
                .Select(code => new FacturXClassification
                {
                    Code = code.Text("CodiceValore"),
                    Scheme = code.Text("CodiceTipo"),
                })
                .ToList(),
            Quantity = line.Number("Quantita"),
            UnitCode = line.Text("UnitaMisura"),
            UnitPrice = line.Number("PrezzoUnitario"),
            LineNetAmount = line.Number("PrezzoTotale"),
            Period = Period(line),
            AllowancesCharges = line.Children("ScontoMaggiorazione").Select(Adjustment).ToList(),
            TaxCategory = FatturaPaNature.Category(line.Text("Natura")),
            TaxRate = rate,
        };
    }

    /// <summary>
    /// Whether a <c>CodiceTipo</c> names a classification list rather than the seller's own article
    /// number. The model has a term for each, and the Italian element carries both under one name.
    /// </summary>
    private static bool IsClassification(string? codeType)
        => codeType is "CPV" or "EER" or "TARIC" or "EAN" or "EAN13" or "GTIN";

    private static FacturXPeriod? Period(XElement line)
    {
        var start = line.Date("DataInizioPeriodo");
        var end = line.Date("DataFinePeriodo");

        return start is null && end is null ? null : new FacturXPeriod { Start = start, End = end };
    }

    /// <summary>
    /// A <c>ScontoMaggiorazione</c>: <c>SC</c> is an allowance, <c>MG</c> a charge, and either may
    /// state a percentage, an amount, or both.
    /// </summary>
    private static FacturXAllowanceCharge Adjustment(XElement element) => new()
    {
        IsCharge = element.Text("Tipo") == "MG",
        Amount = element.Number("Importo"),
        Percentage = element.Amount("Percentuale"),
    };

    private static FacturXTaxBreakdown Tax(XElement summary) => new()
    {
        Category = FatturaPaNature.Category(summary.Text("Natura")),
        Rate = summary.Amount("AliquotaIVA"),
        Basis = summary.Number("ImponibileImporto"),
        Amount = summary.Number("Imposta"),
        ExemptionReason = summary.Text("RiferimentoNormativo"),
    };
}
