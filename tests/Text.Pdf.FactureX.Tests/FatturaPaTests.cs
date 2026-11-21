using System;
using System.Linq;
using System.Xml.Linq;
using M3scalinum.Text.Pdf.FactureX;
using Xunit;

namespace M3scalinum.Text.Pdf.FactureX.Tests;

/// <summary>
/// FatturaPA 1.2.2 is a national schema aligned with EN 16931: it states the same invoice in its
/// own vocabulary. These tests pin the mapping in both directions and the round trip a caller makes
/// when an Italian document arrives at a system that speaks the European model.
/// </summary>
public sealed class FatturaPaTests
{
    private static string Fixture() => ReaderTests.FixtureAt("national/fattura-pa.xml");

    [Fact]
    public void Reads_a_fattura_pa_document()
    {
        var invoice = FacturXReader.Read(Fixture());

        Assert.Equal("FA-2026-0100", invoice.Number);
        Assert.Equal("380", invoice.TypeCode);
        Assert.Equal("EUR", invoice.Currency);
        Assert.Equal(new DateOnly(2026, 4, 5), invoice.IssueDate);
        Assert.Equal(new DateOnly(2026, 5, 5), invoice.DueDate);
        Assert.Equal("Consulenza editoriale aprile 2026", invoice.Note);
        Assert.Equal("FA-2026-0001", invoice.PrecedingInvoiceNumber);
        Assert.Equal(new DateOnly(2026, 3, 1), invoice.PrecedingInvoiceDate);
    }

    [Fact]
    public void Reads_the_two_parties_and_the_recipient_code()
    {
        var invoice = FacturXReader.Read(Fixture());

        Assert.Equal("Atelier Tipografico S.r.l.", invoice.Seller.Name);
        Assert.Equal("IT01234567890", invoice.Seller.VatId);
        Assert.Equal("01234567890", invoice.Seller.LegalId);
        Assert.Equal("Via dei Glifi 12", invoice.Seller.Line1);
        Assert.Equal("20121", invoice.Seller.Postcode);
        Assert.Equal("Milano", invoice.Seller.City);
        Assert.Equal("IT", invoice.Seller.Country);
        Assert.Equal("+39021234567", invoice.Seller.Contact?.Phone);
        Assert.Equal("amministrazione@atelier.it", invoice.Seller.Contact?.Email);

        Assert.Equal("Società Esempio S.p.A.", invoice.Buyer.Name);
        Assert.Equal("IT09876543210", invoice.Buyer.VatId);
        Assert.Equal("Roma", invoice.Buyer.City);

        // The routing address of an Italian document is not in the party: it is the recipient code
        // of the transmission block, which EAS numbers 0201.
        Assert.Equal("ABC1234", invoice.Buyer.ElectronicAddress);
        Assert.Equal("0201", invoice.Buyer.ElectronicAddressScheme);
    }

    [Fact]
    public void Reads_the_lines_with_their_rate_and_their_classification()
    {
        var invoice = FacturXReader.Read(Fixture());

        Assert.Equal(2, invoice.Lines.Count);

        var service = invoice.Lines[0];
        Assert.Equal("1", service.Id);
        Assert.Equal("Giornata di consulenza editoriale", service.Name);
        Assert.Equal("CONSULT-01", service.SellerAssignedId);
        Assert.Equal(2m, service.Quantity);
        Assert.Equal("C62", service.UnitCode);
        Assert.Equal(150m, service.UnitPrice);
        Assert.Equal(300m, service.LineNetAmount);
        Assert.Equal("S", service.TaxCategory);
        Assert.Equal(22m, service.TaxRate);

        var classification = Assert.Single(service.Classifications);
        Assert.Equal("72000000", classification.Code);
        Assert.Equal("CPV", classification.Scheme);

        Assert.Equal(10m, invoice.Lines[1].TaxRate);
        Assert.Equal(100m, invoice.Lines[1].LineNetAmount);
    }

    [Fact]
    public void A_document_with_several_rates_keeps_one_riepilogo_per_rate()
    {
        var invoice = FacturXReader.Read(Fixture());

        Assert.Equal(2, invoice.TaxBreakdown.Count);

        Assert.Equal("S", invoice.TaxBreakdown[0].Category);
        Assert.Equal(22m, invoice.TaxBreakdown[0].Rate);
        Assert.Equal(290m, invoice.TaxBreakdown[0].Basis);
        Assert.Equal(63.80m, invoice.TaxBreakdown[0].Amount);

        Assert.Equal(10m, invoice.TaxBreakdown[1].Rate);
        Assert.Equal(100m, invoice.TaxBreakdown[1].Basis);
        Assert.Equal(10m, invoice.TaxBreakdown[1].Amount);
    }

    [Fact]
    public void Reads_the_totals_and_the_payment_details()
    {
        var invoice = FacturXReader.Read(Fixture());

        Assert.Equal(400m, invoice.Totals.LineTotal);
        Assert.Equal(10m, invoice.Totals.AllowanceTotal);
        Assert.Equal(390m, invoice.Totals.TaxBasisTotal);
        Assert.Equal(73.80m, invoice.Totals.TaxTotal);
        Assert.Equal(463.80m, invoice.Totals.GrandTotal);
        Assert.Equal(463.80m, invoice.Totals.Due);

        // MP05 is a bank transfer, which UNTDID 4461 states as 30.
        Assert.Equal("30", invoice.PaymentMeansCode);
        Assert.Equal("IT60X0542811101000000123456", invoice.Iban);
        Assert.Equal("BPMOIT22XXX", invoice.Bic);
        Assert.Equal("RF18539007547034", invoice.PaymentReference);
    }

    [Fact]
    public void Reads_the_document_level_allowance()
    {
        var adjustment = Assert.Single(FacturXReader.Read(Fixture()).AllowancesCharges);

        Assert.False(adjustment.IsCharge);
        Assert.Equal(10m, adjustment.Amount);
        Assert.Equal(2.5m, adjustment.Percentage);
    }

    [Fact]
    public void The_written_document_reads_back_as_the_same_invoice()
    {
        var original = FacturXReader.Read(Fixture());
        var read = FacturXReader.Read(FatturaPaWriter.Write(original));

        Assert.Equal(original.Number, read.Number);
        Assert.Equal(original.TypeCode, read.TypeCode);
        Assert.Equal(original.Currency, read.Currency);
        Assert.Equal(original.IssueDate, read.IssueDate);
        Assert.Equal(original.DueDate, read.DueDate);
        Assert.Equal(original.Note, read.Note);
        Assert.Equal(original.PrecedingInvoiceNumber, read.PrecedingInvoiceNumber);
        Assert.Equal(original.PrecedingInvoiceDate, read.PrecedingInvoiceDate);

        Assert.Equal(original.Seller.Name, read.Seller.Name);
        Assert.Equal(original.Seller.VatId, read.Seller.VatId);
        Assert.Equal(original.Seller.LegalId, read.Seller.LegalId);
        Assert.Equal(original.Seller.Line1, read.Seller.Line1);
        Assert.Equal(original.Seller.Postcode, read.Seller.Postcode);
        Assert.Equal(original.Seller.City, read.Seller.City);
        Assert.Equal(original.Seller.Country, read.Seller.Country);
        Assert.Equal(original.Seller.Contact?.Phone, read.Seller.Contact?.Phone);
        Assert.Equal(original.Seller.Contact?.Email, read.Seller.Contact?.Email);

        Assert.Equal(original.Buyer.Name, read.Buyer.Name);
        Assert.Equal(original.Buyer.VatId, read.Buyer.VatId);
        Assert.Equal(original.Buyer.ElectronicAddress, read.Buyer.ElectronicAddress);
        Assert.Equal(original.Buyer.ElectronicAddressScheme, read.Buyer.ElectronicAddressScheme);

        Assert.Equal(original.Lines.Count, read.Lines.Count);
        Assert.Equal(original.Lines[0].Name, read.Lines[0].Name);
        Assert.Equal(original.Lines[0].SellerAssignedId, read.Lines[0].SellerAssignedId);
        Assert.Equal(original.Lines[0].Quantity, read.Lines[0].Quantity);
        Assert.Equal(original.Lines[0].UnitPrice, read.Lines[0].UnitPrice);
        Assert.Equal(original.Lines[0].LineNetAmount, read.Lines[0].LineNetAmount);
        Assert.Equal(original.Lines[0].TaxRate, read.Lines[0].TaxRate);
        Assert.Equal(original.Lines[0].TaxCategory, read.Lines[0].TaxCategory);
        Assert.Equal(original.Lines[0].UnitCode, read.Lines[0].UnitCode);
        Assert.Equal(original.Lines[0].Classifications[0].Code, read.Lines[0].Classifications[0].Code);

        Assert.Equal(original.AllowancesCharges.Count, read.AllowancesCharges.Count);
        Assert.Equal(original.AllowancesCharges[0].Amount, read.AllowancesCharges[0].Amount);
        Assert.Equal(original.AllowancesCharges[0].Percentage, read.AllowancesCharges[0].Percentage);

        Assert.Equal(original.TaxBreakdown.Count, read.TaxBreakdown.Count);
        Assert.Equal(original.TaxBreakdown[0].Rate, read.TaxBreakdown[0].Rate);
        Assert.Equal(original.TaxBreakdown[0].Basis, read.TaxBreakdown[0].Basis);
        Assert.Equal(original.TaxBreakdown[0].Amount, read.TaxBreakdown[0].Amount);

        Assert.Equal(original.Totals.LineTotal, read.Totals.LineTotal);
        Assert.Equal(original.Totals.AllowanceTotal, read.Totals.AllowanceTotal);
        Assert.Equal(original.Totals.TaxBasisTotal, read.Totals.TaxBasisTotal);
        Assert.Equal(original.Totals.TaxTotal, read.Totals.TaxTotal);
        Assert.Equal(original.Totals.GrandTotal, read.Totals.GrandTotal);
        Assert.Equal(original.Totals.Due, read.Totals.Due);

        Assert.Equal(original.PaymentMeansCode, read.PaymentMeansCode);
        Assert.Equal(original.Iban, read.Iban);
        Assert.Equal(original.Bic, read.Bic);
        Assert.Equal(original.PaymentReference, read.PaymentReference);
    }

    [Fact]
    public void A_credit_note_is_written_as_a_nota_di_credito_and_reads_back_as_one()
    {
        var credit = new FacturXInvoice
        {
            Number = "NC-2026-0001",
            TypeCode = "381",
            Currency = "EUR",
            IssueDate = new DateOnly(2026, 4, 6),
            PrecedingInvoiceNumber = "FA-2026-0100",
            Seller = new FacturXParty { Name = "Atelier Tipografico S.r.l.", VatId = "IT01234567890", Country = "IT" },
            Buyer = new FacturXParty { Name = "Società Esempio S.p.A.", VatId = "IT09876543210", Country = "IT" },
            Lines =
            [
                new FacturXLine
                {
                    Name = "Avoir",
                    Quantity = -1m,
                    UnitPrice = 100m,
                    LineNetAmount = -100m,
                    TaxCategory = "S",
                    TaxRate = 22m,
                },
            ],
            TaxBreakdown = [new FacturXTaxBreakdown { Category = "S", Rate = 22m, Basis = -100m, Amount = -22m }],
            Totals = new FacturXTotals
            {
                LineTotal = -100m,
                TaxBasisTotal = -100m,
                TaxTotal = -22m,
                GrandTotal = -122m,
                Due = -122m,
            },
        };

        var xml = FatturaPaWriter.Write(credit);

        Assert.Contains("<TipoDocumento>TD04</TipoDocumento>", xml, StringComparison.Ordinal);

        var read = FacturXReader.Read(xml);
        Assert.Equal("381", read.TypeCode);
        Assert.Equal(-122m, read.Totals.GrandTotal);
        Assert.Equal(-22m, read.Totals.TaxTotal);
        Assert.Equal(-100m, read.Lines[0].LineNetAmount);
    }

    [Fact]
    public void An_exempt_category_is_written_as_a_natura_code()
    {
        var invoice = new FacturXInvoice
        {
            Number = "FA-2026-0101",
            Currency = "EUR",
            IssueDate = new DateOnly(2026, 4, 7),
            Seller = new FacturXParty { Name = "Atelier", VatId = "IT01234567890", Country = "IT" },
            Buyer = new FacturXParty { Name = "Esempio", VatId = "IT09876543210", Country = "IT" },
            Lines =
            [
                new FacturXLine
                {
                    Name = "Formazione esente",
                    Quantity = 1m,
                    UnitPrice = 100m,
                    LineNetAmount = 100m,
                    TaxCategory = "E",
                    TaxRate = 0m,
                },
            ],
            TaxBreakdown = [new FacturXTaxBreakdown { Category = "E", Rate = 0m, Basis = 100m, Amount = 0m }],
            Totals = new FacturXTotals
            {
                LineTotal = 100m,
                TaxBasisTotal = 100m,
                GrandTotal = 100m,
                Due = 100m,
            },
        };

        var xml = FatturaPaWriter.Write(invoice);

        Assert.Contains("<Natura>N1</Natura>", xml, StringComparison.Ordinal);
        Assert.Equal("E", FacturXReader.Read(xml).TaxBreakdown[0].Category);
    }

    [Fact]
    public void The_root_is_in_the_namespace_the_schema_declares_and_states_the_private_format()
    {
        var document = XDocument.Parse(FatturaPaWriter.Write(FacturXReader.Read(Fixture())));

        Assert.Equal("FatturaElettronica", document.Root!.Name.LocalName);
        Assert.Equal(
            "http://ivaservizi.agenziaentrate.gov.it/docs/xsd/fatture/v1.2",
            document.Root.Name.NamespaceName);
        Assert.Equal("FPR12", document.Root.Attribute("versione")?.Value);
        Assert.Contains("<FormatoTrasmissione>FPR12</FormatoTrasmissione>", document.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_elements_follow_the_order_the_official_schema_imposes()
    {
        var xml = FatturaPaWriter.Write(FacturXReader.Read(Fixture()));

        AssertInOrder(
            xml,
            "<FatturaElettronicaHeader>",
            "<DatiTrasmissione>",
            "<IdTrasmittente>",
            "<ProgressivoInvio>",
            "<FormatoTrasmissione>",
            "<CodiceDestinatario>",
            "<CedentePrestatore>",
            "<CessionarioCommittente>",
            "<FatturaElettronicaBody>",
            "<DatiGenerali>",
            "<DatiGeneraliDocumento>",
            "<TipoDocumento>",
            "<Divisa>",
            "<Data>",
            "<Numero>",
            "<ScontoMaggiorazione>",
            "<ImportoTotaleDocumento>",
            "<Causale>",
            "<DatiFattureCollegate>",
            "<DatiBeniServizi>",
            "<DettaglioLinee>",
            "<NumeroLinea>",
            "<CodiceArticolo>",
            "<Descrizione>",
            "<Quantita>",
            "<UnitaMisura>",
            "<PrezzoUnitario>",
            "<PrezzoTotale>",
            "<AliquotaIVA>",
            "<DatiRiepilogo>",
            "<DatiPagamento>",
            "<CondizioniPagamento>",
            "<DettaglioPagamento>",
            "<ModalitaPagamento>",
            "<DataScadenzaPagamento>",
            "<ImportoPagamento>",
            "<IBAN>",
            "<BIC>",
            "<CodicePagamento>");
    }

    [Fact]
    public void A_document_without_a_breakdown_gets_one_derived_from_its_lines()
    {
        var invoice = new FacturXInvoice
        {
            Number = "FA-2026-0102",
            Currency = "EUR",
            IssueDate = new DateOnly(2026, 4, 8),
            Seller = new FacturXParty { Name = "Atelier", VatId = "IT01234567890", Country = "IT" },
            Buyer = new FacturXParty { Name = "Esempio", VatId = "IT09876543210", Country = "IT" },
            Lines =
            [
                new FacturXLine { Name = "A", Quantity = 1m, UnitPrice = 100m, LineNetAmount = 100m, TaxCategory = "S", TaxRate = 22m },
                new FacturXLine { Name = "B", Quantity = 1m, UnitPrice = 50m, LineNetAmount = 50m, TaxCategory = "S", TaxRate = 10m },
            ],
            Totals = new FacturXTotals { LineTotal = 150m, TaxBasisTotal = 150m, TaxTotal = 27m, GrandTotal = 177m, Due = 177m },
        };

        var read = FacturXReader.Read(FatturaPaWriter.Write(invoice));

        Assert.Equal(2, read.TaxBreakdown.Count);
        Assert.Equal(22m, read.TaxBreakdown[0].Rate);
        Assert.Equal(22m, read.TaxBreakdown[0].Amount);
        Assert.Equal(10m, read.TaxBreakdown[1].Rate);
        Assert.Equal(5m, read.TaxBreakdown[1].Amount);
    }

    [Fact]
    public void The_written_bytes_declare_the_encoding_they_use()
    {
        var bytes = FatturaPaWriter.WriteBytes(FacturXReader.Read(Fixture()));
        var prolog = System.Text.Encoding.UTF8.GetString(bytes, 0, 60);

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", prolog, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("utf-16", prolog, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_unknown_document_is_refused_by_the_dedicated_reader_too()
    {
        var exception = Assert.Throws<FacturXFormatException>(() => FatturaPaReader.Read("<Order/>"));

        Assert.Contains("FatturaElettronicaHeader", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_number_is_written_with_the_two_decimals_the_schema_patterns_demand()
    {
        var xml = FatturaPaWriter.Write(FacturXReader.Read(Fixture()));

        // The schema states each of these with a pattern such as `[0-9]{1,3}\.[0-9]{2}`, so `22`
        // is refused where `22.00` is accepted.
        Assert.Contains("<AliquotaIVA>22.00</AliquotaIVA>", xml, StringComparison.Ordinal);
        Assert.Contains("<Quantita>2.00</Quantita>", xml, StringComparison.Ordinal);
        Assert.Contains("<PrezzoUnitario>150.00</PrezzoUnitario>", xml, StringComparison.Ordinal);
        Assert.Contains("<Percentuale>2.50</Percentuale>", xml, StringComparison.Ordinal);
        Assert.Contains("<ImportoTotaleDocumento>463.80</ImportoTotaleDocumento>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void A_credit_note_states_the_absolute_quantity_the_schema_allows()
    {
        var credit = new FacturXInvoice
        {
            Number = "NC-2026-0002",
            TypeCode = "381",
            Currency = "EUR",
            IssueDate = new DateOnly(2026, 4, 6),
            Seller = new FacturXParty { Name = "Atelier", VatId = "IT01234567890", Country = "IT" },
            Buyer = new FacturXParty { Name = "Esempio", VatId = "IT09876543210", ElectronicAddress = "ABC1234", Country = "IT" },
            Lines =
            [
                new FacturXLine
                {
                    Name = "Avoir",
                    Quantity = -2m,
                    UnitPrice = 100m,
                    LineNetAmount = -200m,
                    TaxCategory = "S",
                    TaxRate = 22m,
                },
            ],
            TaxBreakdown = [new FacturXTaxBreakdown { Category = "S", Rate = 22m, Basis = -200m, Amount = -44m }],
            Totals = new FacturXTotals
            {
                LineTotal = -200m,
                TaxBasisTotal = -200m,
                TaxTotal = -44m,
                GrandTotal = -244m,
                Due = -244m,
            },
        };

        var xml = FatturaPaWriter.Write(credit);

        Assert.Contains("<Quantita>2.00</Quantita>", xml, StringComparison.Ordinal);
        Assert.Contains("<PrezzoTotale>-200.00</PrezzoTotale>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pec_recipient_fills_the_recipient_code_with_zeros()
    {
        var invoice = FacturXReader.Read(Fixture());
        var withPec = new FacturXInvoice
        {
            Number = invoice.Number,
            Currency = invoice.Currency,
            IssueDate = invoice.IssueDate,
            Seller = invoice.Seller,
            Buyer = new FacturXParty
            {
                Name = invoice.Buyer.Name,
                VatId = invoice.Buyer.VatId,
                ElectronicAddress = "esempio@pec.it",
            },
            Lines = invoice.Lines,
            Totals = invoice.Totals,
        };

        var xml = FatturaPaWriter.Write(withPec);

        Assert.Contains("<CodiceDestinatario>0000000</CodiceDestinatario>", xml, StringComparison.Ordinal);
        Assert.Contains("<PECDestinatario>esempio@pec.it</PECDestinatario>", xml, StringComparison.Ordinal);
        Assert.Equal("EM", FacturXReader.Read(xml).Buyer.ElectronicAddressScheme);
    }

    private static void AssertInOrder(string xml, params string[] fragments)
    {
        var previous = -1;

        foreach (var fragment in fragments)
        {
            var index = xml.IndexOf(fragment, previous + 1, StringComparison.Ordinal);

            Assert.True(index >= 0, $"`{fragment}` is missing from the document or out of the order the schema imposes");
            previous = index;
        }
    }
}
